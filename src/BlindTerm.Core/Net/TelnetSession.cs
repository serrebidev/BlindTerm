using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace BlindTerm.Core.Net;

/// <summary>
/// A telnet connection, spoken directly rather than through a telnet program.
///
/// This exists because of a limitation that cannot be worked around from the terminal's side.
/// Windows' telnet.exe draws into its console window through the console API instead of
/// writing a stream of lines, and a Windows pseudo console can only report what is on that
/// window when it next redraws. Anything that scrolls past in between -- a MUD's help file, a
/// long who list -- is overwritten before it can be reported, and those lines reach no
/// terminal at all. Feeding the socket straight into the VT engine loses nothing, and lets
/// BlindTerm answer the option negotiation itself, which is where it gets to say that a
/// screen reader is on this end.
/// </summary>
public sealed class TelnetSession : ITerminalSession
{
    private const int ReadBuffer = 16 * 1024;

    private readonly BlockingCollection<byte[]> _writes = new(new ConcurrentQueue<byte[]>());
    private readonly CancellationTokenSource _stopping = new();
    private readonly TelnetProtocol _protocol;
    private readonly MspScanner _sounds = new();
    private readonly List<MspTrigger> _triggers = new();
    private readonly List<string> _outOfBandSounds = new();
    private readonly object _sizeLock = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Thread? _readThread;
    private Thread? _writeThread;
    private int _disposed;

    public event Action<ReadOnlyMemory<byte>>? Output;
    public event Action<int?>? Exited;

    /// <summary>
    /// The host asked for a sound. Raised on the reading thread, like <see cref="Output"/>,
    /// and always after the text around it has been handed on, so a sound never arrives
    /// before the line that explains it.
    /// </summary>
    public event Action<MspTrigger>? SoundRequested;

    public TerminalSessionKind Kind => TerminalSessionKind.Remote;
    public bool IsRunning { get; private set; }

    /// <summary>A remote host is always the program: there is no local prompt behind it.</summary>
    public bool ProgramOwnsInput => IsRunning;

    /// <summary>
    /// Telnet's network virtual terminal ends a line with both characters. A bare Return is
    /// how you send a carriage return and nothing else, which is not what pressing Enter on a
    /// command line means.
    /// </summary>
    public string LineTerminator => "\r\n";

    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <param name="clientName">The name reported as the terminal type, before ANSI and MTTS.</param>
    public TelnetSession(string clientName = "BLINDTERM")
        => _protocol = new TelnetProtocol(clientName);

    /// <summary>
    /// Opens the connection, and no more than that. Reading starts at <see cref="Begin"/>, so
    /// a window has time to subscribe before a login banner that arrives in the first
    /// millisecond is delivered to nobody.
    /// </summary>
    public async Task ConnectAsync(string host, int port, int columns, int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (_client is not null) throw new InvalidOperationException("Session is already connected.");

        TerminalSize size = TerminalSize.Validate(columns, rows);
        Columns = size.Columns;
        Rows = size.Rows;

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _stream = client.GetStream();
        Host = host;
        Port = port;
    }

    /// <summary>
    /// Starts reading. Nothing is written: the far end speaks first, and a client that
    /// announces itself would put three telnet commands in front of a plain TCP service's
    /// first line.
    /// </summary>
    public void Begin()
    {
        if (_stream is null) throw new InvalidOperationException("Session is not connected.");
        if (IsRunning) return;

        IsRunning = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "BlindTerm telnet read" };
        _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "BlindTerm telnet write" };
        _readThread.Start();
        _writeThread.Start();
    }

    private void ReadLoop()
    {
        var received = new byte[ReadBuffer];
        var text = new byte[ReadBuffer];
        // Longer than a read: the scanner can hand back bytes it withheld from an earlier one.
        var sounds = new byte[ReadBuffer + MspScanner.Headroom];
        var reply = new List<byte>();

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                int read = _stream!.Read(received, 0, received.Length);
                if (read <= 0) break;

                reply.Clear();
                _triggers.Clear();
                int written = _protocol.Receive(received.AsSpan(0, read), text, reply);

                // A trigger sent out of band came and went with the telnet commands, and is
                // already whole. One sent in the text has to be lifted out of it, after the
                // commands have gone and before anything sees the line.
                _outOfBandSounds.Clear();
                _protocol.DrainMudSoundRequests(_outOfBandSounds);
                foreach (string request in _outOfBandSounds)
                {
                    if (MspTrigger.TryParseLine(request, out MspTrigger? outOfBand))
                        _triggers.Add(outOfBand);
                }

                written = _sounds.Scan(text.AsSpan(0, written), sounds, _triggers);

                if (_protocol.TakeWindowSizeRequest())
                {
                    lock (_sizeLock) TelnetProtocol.AppendWindowSize(reply, Columns, Rows);
                }
                if (reply.Count > 0) Send(reply);

                // A read can be nothing but negotiation, and an empty update is noise the
                // transcript assembler should never have to think about.
                if (written > 0) Output?.Invoke(new ReadOnlyMemory<byte>(sounds, 0, written));
                foreach (MspTrigger trigger in _triggers) SoundRequested?.Invoke(trigger);
            }
        }
        catch (Exception) when (_stopping.IsCancellationRequested || _disposed != 0)
        {
            // Shutting down; the socket closing under us is expected.
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The connection dropped, which is an ordinary way for a session to end.
        }

        // Anything held back waiting to become a sound trigger never will now, and is text.
        int trailing = _sounds.Flush(sounds);
        if (trailing > 0) Output?.Invoke(new ReadOnlyMemory<byte>(sounds, 0, trailing));

        IsRunning = false;
        // A closed connection has no exit code. Nothing here can invent one.
        Exited?.Invoke(null);
    }

    private void WriteLoop()
    {
        try
        {
            foreach (byte[] chunk in _writes.GetConsumingEnumerable(_stopping.Token))
            {
                _stream!.Write(chunk, 0, chunk.Length);
                _stream.Flush();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
    }

    /// <summary>Queues typed bytes, in call order, with any literal 255 escaped for the wire.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || _writes.IsAddingCompleted) return;
        _writes.Add(TelnetProtocol.Escape(bytes));
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    /// <summary>Bytes that are already protocol, and must not be escaped again.</summary>
    private void Send(List<byte> protocol)
    {
        if (protocol.Count == 0 || _writes.IsAddingCompleted) return;
        _writes.Add([.. protocol]);
    }

    public async Task WriteLineSplit(string text, string terminator, int gapMs)
    {
        if (text.Length > 0) Write(text);
        if (gapMs > 0) await Task.Delay(gapMs).ConfigureAwait(false);
        Write(terminator);
    }

    /// <summary>
    /// Tells the far end how wide the terminal is, so a MUD wraps its own text to the width
    /// being read rather than to a guess.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        lock (_sizeLock)
        {
            if (size.Columns == Columns && size.Rows == Rows) return;
            Columns = size.Columns;
            Rows = size.Rows;
        }

        if (!IsRunning || !_protocol.WindowSizeAgreed) return;
        var reply = new List<byte>();
        lock (_sizeLock) TelnetProtocol.AppendWindowSize(reply, Columns, Rows);
        Send(reply);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stopping.Cancel();
        _writes.CompleteAdding();

        _stream?.Dispose();
        _client?.Dispose();

        _stopping.Dispose();
        _writes.Dispose();
        IsRunning = false;
    }
}
