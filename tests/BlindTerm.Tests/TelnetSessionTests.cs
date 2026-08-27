using System.Net;
using System.Net.Sockets;
using System.Text;
using BlindTerm.Core;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

/// <summary>
/// The session against a server on the loopback, so the whole path is exercised: the socket,
/// the option layer, and the transcript the window would show.
/// </summary>
public class TelnetSessionTests : IDisposable
{
    private const byte Iac = 255, Do = 253, Will = 251, Sb = 250, Se = 240;
    private const byte OptEcho = 1, OptTerminalType = 24, OptNaws = 31, OptCompress2 = 86;

    private readonly TcpListener _listener;
    private readonly List<TelnetSession> _sessions = new();

    public TelnetSessionTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private TelnetSession Connect()
    {
        var session = new TelnetSession();
        _sessions.Add(session);
        // Connecting to the loopback completes at once; the test is not waiting on a network.
        session.ConnectAsync("127.0.0.1", Port, 120, 30).GetAwaiter().GetResult();
        return session;
    }

    private static bool Eventually(Func<bool> condition, int seconds = 10)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    public void Dispose()
    {
        foreach (var session in _sessions) session.Dispose();
        _listener.Stop();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryLineOfALongBurstReachesTheTranscript()
    {
        // This is the whole reason for dialling the socket directly. The same 200 lines
        // through Windows' telnet.exe and a pseudo console arrive as whatever happened to be
        // on its console window when it last redrew -- about one screenful.
        const int Lines = 200;
        var core = new TerminalCore(120, 30);
        var session = Connect();
        session.Output += memory => core.Feed(memory.Span);

        using (TcpClient server = _listener.AcceptTcpClient())
        {
            session.Begin();
            var text = new StringBuilder();
            for (int i = 1; i <= Lines; i++) text.Append($"row {i:d3}\r\n");
            byte[] payload = Encoding.ASCII.GetBytes(text.ToString());
            server.GetStream().Write(payload, 0, payload.Length);
            server.GetStream().Flush();

            Assert.True(Eventually(() => core.Transcript.Count >= Lines),
                $"only {core.Transcript.Count} of {Lines} lines arrived.");
        }

        core.Flush();
        Assert.Equal("row 001", core.Transcript.Lines[0]);
        Assert.Equal("row 200", core.Transcript.Lines[Lines - 1]);
    }

    [Fact]
    public void NegotiationIsAnsweredAndNeverAppearsInTheTranscript()
    {
        var core = new TerminalCore(120, 30);
        var session = Connect();
        session.Output += memory => core.Feed(memory.Span);

        using TcpClient server = _listener.AcceptTcpClient();
        NetworkStream stream = server.GetStream();
        session.Begin();

        byte[] opening =
        [
            Iac, Do, OptTerminalType,
            Iac, Do, OptNaws,
            Iac, Will, OptEcho,
            Iac, Will, OptCompress2,
            .. Encoding.ASCII.GetBytes("By what name is your character known?\r\n"),
        ];
        stream.Write(opening, 0, opening.Length);
        stream.Flush();

        Assert.True(Eventually(() => core.Transcript.Count >= 1));
        core.Flush();
        Assert.Equal("By what name is your character known?", core.Transcript.Lines[0]);

        byte[] answer = ReadAvailable(stream);
        Assert.True(Find(answer, [Iac, Will, OptTerminalType]));
        Assert.True(Find(answer, [Iac, Will, OptNaws]));
        // The size goes out with the agreement, unasked, because nothing asks twice.
        Assert.True(Find(answer, [Iac, Sb, OptNaws, 0, 120, 0, 30, Iac, Se]));
        // Compression would turn the rest of the stream into deflate.
        Assert.True(Find(answer, [Iac, 254, OptCompress2]));
    }

    [Fact]
    public void TheTerminalTypeAnsweredLastDeclaresAScreenReader()
    {
        var session = Connect();
        using TcpClient server = _listener.AcceptTcpClient();
        NetworkStream stream = server.GetStream();
        session.Begin();

        byte[] ask = [Iac, Do, OptTerminalType, Iac, Sb, OptTerminalType, 1, Iac, Se,
                      Iac, Sb, OptTerminalType, 1, Iac, Se,
                      Iac, Sb, OptTerminalType, 1, Iac, Se];
        stream.Write(ask, 0, ask.Length);
        stream.Flush();

        byte[] answer = ReadAvailable(stream);
        string wire = Encoding.ASCII.GetString(answer);
        Assert.Contains("BLINDTERM", wire);
        Assert.Contains("ANSI", wire);
        // MTTS bit 64 is SCREEN READER: a MUD that honours it leaves out its maps and art.
        Assert.Contains(TelnetProtocol.MttsAnswer, wire);
    }

    [Fact]
    public async Task ASubmittedLineArrivesWithBothLineEndingCharacters()
    {
        var session = Connect();
        using TcpClient server = _listener.AcceptTcpClient();
        NetworkStream stream = server.GetStream();
        session.Begin();

        await session.WriteLineSplit("look", session.LineTerminator, 0);

        Assert.Equal("look\r\n", Encoding.ASCII.GetString(ReadAvailable(stream)));
    }

    [Fact]
    public void ADroppedConnectionEndsTheSession()
    {
        var session = Connect();
        int? code = -1;
        bool ended = false;
        session.Exited += value => { code = value; ended = true; };

        TcpClient server = _listener.AcceptTcpClient();
        session.Begin();
        server.Close();

        Assert.True(Eventually(() => ended));
        // A closed connection has no exit code, and inventing a zero would read as success.
        Assert.Null(code);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task AHostThatIsNotListeningIsReportedRatherThanHung()
    {
        int port = Port;
        _listener.Stop();
        using var session = new TelnetSession();

        await Assert.ThrowsAnyAsync<SocketException>(
            () => session.ConnectAsync("127.0.0.1", port, 120, 30));
    }

    private static byte[] ReadAvailable(NetworkStream stream, int seconds = 5)
    {
        var all = new List<byte>();
        var buffer = new byte[4096];
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        stream.ReadTimeout = 250;
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            try
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                all.AddRange(buffer[..read]);
            }
            catch (IOException)
            {
                // A read timeout means the other end has said all it is going to say.
                if (all.Count > 0) break;
            }
        }
        return [.. all];
    }

    private static bool Find(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }
}
