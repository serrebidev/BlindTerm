using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using BlindTerm.Core.Net;
using BlindTerm.Core.Pty;
using BlindTerm.Core.Speech;
using BlindTerm.Core.Vt;

namespace BlindTerm.App;

/// <summary>
/// The terminal, the shell and the speech, joined up and handed to the window on its own
/// thread.
///
/// Everything below this reads bytes on a background thread; everything above it touches
/// controls and must not. This is the seam: updates are marshalled once, here, so the window
/// never has to think about it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TerminalHost : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly TerminalCore _core;

    /// <summary>
    /// Serializes session changes with bytes arriving from their reader threads.
    ///
    /// <see cref="TerminalCore"/> is deliberately a synchronous state machine: two feeds at
    /// once would corrupt its carried escape sequence, screen and transcript. The same lock
    /// therefore protects both the session stack and every mutation of the core.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The far end. It is chosen when the window opens -- a shell, a console Windows handed
    /// over, or a host on the network -- and nothing above this line can tell which.
    /// </summary>
    private ITerminalSession? _session;

    /// <summary>
    /// The shell a connection was laid over, kept running so the window can go back to it.
    ///
    /// This is what "telnet" typed at a prompt has always meant: the shell is still there,
    /// still at its prompt, and comes back when the connection ends. It never received the
    /// command, so there is nothing to return it from.
    /// </summary>
    private ITerminalSession? _underneath;

    /// <summary>Whether that shell died while the connection was in front of it.</summary>
    private bool _underneathExited;

    public TerminalCore Core => _core;
    public TerminalEngine Engine => _core.Engine;
    public Transcript Transcript => _core.Transcript;

    public ScreenReaderRouter Reader { get; }
    public Announcer Announcer { get; }

    /// <summary>A batch of changes, already on the UI thread.</summary>
    public event Action<TerminalUpdate>? Updated;

    /// <summary>The program rang the bell, already on the UI thread.</summary>
    public event Action? Bell;

    /// <summary>A remote host asked for a sound, already on the UI thread.</summary>
    public event Action<MspTrigger>? SoundRequested;

    /// <summary>
    /// A remote host said something about the room or the character over GMCP, already on the
    /// UI thread.
    /// </summary>
    public event Action<GmcpMessage>? StatusReceived;

    /// <summary>Structured MSDP room or character data, already on the UI thread.</summary>
    public event Action<MsdpMessage>? MsdpStatusReceived;

    public event Action<string>? TitleChanged;
    public event Action<int?>? Exited;

    public bool IsRunning
    {
        get { lock (_gate) return _session?.IsRunning ?? false; }
    }

    /// <summary>What kind of far end this window is showing.</summary>
    public TerminalSessionKind Kind
    {
        get { lock (_gate) return _session?.Kind ?? TerminalSessionKind.Shell; }
    }

    /// <summary>
    /// Whether something other than an idle shell prompt is reading what is typed. The
    /// keyboard follows this: a running program gets the arrow keys and the Ctrl chords, and
    /// a prompt keeps the ordinary editing keys Windows has always provided.
    /// </summary>
    public bool ProgramOwnsInput
    {
        get { lock (_gate) return _session?.ProgramOwnsInput ?? false; }
    }

    public TerminalHost(int columns, int rows, SynchronizationContext ui)
    {
        _ui = ui;
        _core = new TerminalCore(columns, rows);
        Reader = new ScreenReaderRouter();
        Announcer = new Announcer(Reader);

        _core.Updated += update => Post(() => Updated?.Invoke(update));
        _core.Engine.Bell += () => Post(() => Bell?.Invoke());
        _core.Engine.TitleChanged += title => Post(() => TitleChanged?.Invoke(title));

        // Replies the terminal owes the program -- cursor position reports and the like --
        // go straight back without touching the UI thread.
        _core.Engine.Respond += bytes =>
        {
            lock (_gate) _session?.Write(bytes);
        };
    }

    /// <summary>Takes ownership of a far end and starts feeding its bytes to the engine.</summary>
    private T Attach<T>(T session) where T : ITerminalSession
    {
        lock (_gate)
        {
            if (_session is not null)
                throw new InvalidOperationException("This window already has a session.");
            Wire(session);
            _session = session;
        }
        return session;
    }

    /// <summary>
    /// Subscribes to a far end, ignoring anything it says while it is not the one on top.
    ///
    /// The gate is what lets a connection be laid over a shell. An idle shell says nothing,
    /// so in practice it drops nothing -- but a shell that does print while a MUD is in front
    /// of it must not have its output spliced into the middle of the conversation being read.
    /// </summary>
    private void Wire(ITerminalSession session)
    {
        session.Output += memory =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_session, session)) _core.Feed(memory.Span);
            }
        };
        if (session is TelnetSession remote)
        {
            remote.SoundRequested += trigger =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session))
                        Post(() => SoundRequested?.Invoke(trigger));
                }
            };
            remote.StatusReceived += message =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session))
                        Post(() => StatusReceived?.Invoke(message));
                }
            };
            remote.MsdpStatusReceived += message =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session))
                        Post(() => MsdpStatusReceived?.Invoke(message));
                }
            };
        }
        session.Exited += code =>
        {
            lock (_gate)
            {
                // A shell that dies underneath a connection is not the end of the window: the
                // connection is still up and being read. It is remembered instead, because it
                // is no longer somewhere to go back to when the connection ends.
                if (!ReferenceEquals(_session, session))
                {
                    if (ReferenceEquals(_underneath, session)) _underneathExited = true;
                    return;
                }
                _core.Flush();
            }
            Post(() => Exited?.Invoke(code));
        };
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Start(string commandLine, string? workingDirectory = null)
        => Attach(new PtySession()).Start(commandLine, Engine.Columns, Engine.Rows,
                                          TerminalEnvironment.ForChild(), workingDirectory);

    /// <summary>Starts Windows OpenSSH inside BlindTerm's pseudo console.</summary>
    public void StartSsh(SshTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Attach(new PtySession(TerminalSessionKind.Ssh, alwaysOwnsInput: true)).Start(
            target.CommandLine, Engine.Columns, Engine.Rows, TerminalEnvironment.ForChild());
    }

    /// <summary>
    /// Takes over a console Windows has already created for a program, because BlindTerm is
    /// the default terminal. Nothing above this line can tell the difference.
    /// </summary>
    public void Adopt(ConsoleHandoff handoff)
        => Attach(new PtySession()).Adopt(handoff, Engine.Columns, Engine.Rows);

    /// <summary>
    /// Opens a telnet connection, without reading from it yet.
    ///
    /// Connecting and reading are separate so that a failure is reported before a window
    /// exists to fail in, and so that a login banner arriving in the first millisecond is not
    /// delivered before the window has subscribed. Call <see cref="Begin"/> once it has.
    /// </summary>
    public Task ConnectAsync(TelnetTarget target, CancellationToken cancellationToken = default)
        => Attach(new TelnetSession()).ConnectAsync(target, Engine.Columns, Engine.Rows,
                                                    cancellationToken);

    /// <summary>
    /// How the connection is encrypted, as words: "TLS 1.3". Empty when it is not, and on
    /// anything that is not a connection at all.
    /// </summary>
    public string Security
    {
        get { lock (_gate) return _session is TelnetSession telnet ? telnet.Security : string.Empty; }
    }

    /// <summary>Starts reading a connection that <see cref="ConnectAsync"/> opened.</summary>
    public void Begin()
    {
        if (_session is TelnetSession telnet) telnet.Begin();
    }

    /// <summary>
    /// Whether a connection could be laid over what this window is showing. A window that is
    /// already showing a connection has nothing to lay another over.
    /// </summary>
    public bool CanConnectOver
    {
        get
        {
            lock (_gate)
                return _session is { IsRunning: true, Kind: not TerminalSessionKind.Remote }
                       && _underneath is null;
        }
    }

    /// <summary>
    /// Connects to a host and puts it in front of the shell this window is already running,
    /// rather than opening a second window for it.
    ///
    /// The transcript carries straight on, so what the shell printed stays above the
    /// conversation and remains readable. Nothing is sent to the shell and nothing is taken
    /// from it: it is left at the prompt it was already at, and <see cref="ReturnToShell"/>
    /// brings it back when the connection ends.
    ///
    /// Throws exactly what <see cref="ConnectAsync"/> throws when the host cannot be reached,
    /// and leaves the window showing the shell untouched when it does.
    /// </summary>
    public async Task ConnectOverAsync(TelnetTarget target, CancellationToken cancellationToken = default)
    {
        if (!CanConnectOver) throw new InvalidOperationException("This window has nothing to connect over.");

        var telnet = new TelnetSession();
        Wire(telnet);
        try
        {
            await telnet.ConnectAsync(target, Engine.Columns, Engine.Rows, cancellationToken);
        }
        catch
        {
            telnet.Dispose();
            throw;
        }

        lock (_gate)
        {
            // The shell may have ended during the network connection attempt. Do not replace
            // a dead window with a connection that can never return to what the user asked to
            // keep underneath it.
            if (_session is not { IsRunning: true, Kind: not TerminalSessionKind.Remote }
                || _underneath is not null)
            {
                telnet.Dispose();
                throw new InvalidOperationException("The shell ended while BlindTerm was connecting.");
            }

            // The prompt the command was typed at is an unfinished line, and the cursor is
            // still sitting in the middle of it. Without this the host's first line would be
            // printed onto the end of the prompt and read as part of it.
            _core.Feed("\r\n"u8);

            _underneathExited = false;
            _underneath = _session;
            _session = telnet;
            // Start while the switch is still locked. A form closing on another thread must
            // not be able to dispose the new current session in the gap before its reader is
            // started.
            telnet.Begin();
        }
    }

    /// <summary>
    /// Puts the shell back in front after a connection laid over it has ended, and says
    /// whether there was one still alive to go back to.
    /// </summary>
    public bool ReturnToShell()
    {
        ITerminalSession? connection;
        bool returned;
        lock (_gate)
        {
            if (_underneath is null) return false;

            connection = _session;
            _session = _underneath;
            _underneath = null;
            returned = !_underneathExited && _session.IsRunning;
            _underneathExited = false;
        }

        connection?.Dispose();
        return returned;
    }

    /// <summary>Whether this window is showing a console Windows handed over.</summary>
    public bool IsHandoff => Kind == TerminalSessionKind.Handoff;

    /// <summary>What the connected host said about itself over MSSP. Empty for anything else.</summary>
    public IReadOnlyDictionary<string, string> ServerStatus
    {
        get
        {
            lock (_gate)
                return _session is TelnetSession remote
                    ? remote.ServerStatus
                    : new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Adds lines the app writes itself rather than the shell: the ready message at launch,
    /// the exit message at the end.
    ///
    /// They go through the same update the shell's output does, so the window mirrors them
    /// and the reader announces them without anything having to know where they came from.
    /// Appending to the document alone is not enough -- that is the transcript, not the box
    /// the user is reading.
    /// </summary>
    /// <param name="quiet">Whether the lines go in without being announced.</param>
    public void AppendExternal(IReadOnlyList<string> lines, bool quiet = false)
    {
        if (lines.Count == 0) return;

        TerminalUpdate update;
        lock (_gate) update = _core.AppendExternal(lines, quiet);

        Post(() => Updated?.Invoke(update));
    }

    /// <summary>Sends a typed line, with the Return as a separate write. See PtySession.</summary>
    public void SendLine(string text)
    {
        ITerminalSession? session;
        lock (_gate) session = _session;
        if (session is null) return;
        _ = session.WriteLineSplit(text, session.LineTerminator, 20);
    }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        lock (_gate) _session?.Write(bytes);
    }

    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        lock (_gate)
        {
            // Resize the child first. If ConPTY rejects it, the parser remains at its old size
            // and can continue consuming output consistently.
            _session?.Resize(size.Columns, size.Rows);
            // A shell waiting behind a connection is resized too, so that the window it comes
            // back to is the size the window actually is.
            _underneath?.Resize(size.Columns, size.Rows);
            Engine.Resize(size.Columns, size.Rows);
        }
    }

    public void Dispose()
    {
        Announcer.Dispose();
        ITerminalSession? session;
        ITerminalSession? underneath;
        lock (_gate)
        {
            session = _session;
            underneath = _underneath;
            _session = null;
            _underneath = null;
        }
        session?.Dispose();
        if (!ReferenceEquals(underneath, session)) underneath?.Dispose();
    }
}
