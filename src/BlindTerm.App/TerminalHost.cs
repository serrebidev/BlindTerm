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
    /// The far end. It is chosen when the window opens -- a shell, a console Windows handed
    /// over, or a host on the network -- and nothing above this line can tell which.
    /// </summary>
    private ITerminalSession? _session;

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

    public event Action<string>? TitleChanged;
    public event Action<int?>? Exited;

    public bool IsRunning => _session?.IsRunning ?? false;

    /// <summary>What kind of far end this window is showing.</summary>
    public TerminalSessionKind Kind => _session?.Kind ?? TerminalSessionKind.Shell;

    /// <summary>
    /// Whether something other than an idle shell prompt is reading what is typed. The
    /// keyboard follows this: a running program gets the arrow keys and the Ctrl chords, and
    /// a prompt keeps the ordinary editing keys Windows has always provided.
    /// </summary>
    public bool ProgramOwnsInput => _session?.ProgramOwnsInput ?? false;

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
        _core.Engine.Respond += bytes => _session?.Write(bytes);
    }

    /// <summary>Takes ownership of a far end and starts feeding its bytes to the engine.</summary>
    private T Attach<T>(T session) where T : ITerminalSession
    {
        if (_session is not null) throw new InvalidOperationException("This window already has a session.");
        _session = session;
        session.Output += memory => _core.Feed(memory.Span);
        if (session is TelnetSession remote)
            remote.SoundRequested += trigger => Post(() => SoundRequested?.Invoke(trigger));
        session.Exited += code =>
        {
            _core.Flush();
            Post(() => Exited?.Invoke(code));
        };
        return session;
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Start(string commandLine, string? workingDirectory = null)
        => Attach(new PtySession()).Start(commandLine, Engine.Columns, Engine.Rows,
                                          TerminalEnvironment.ForChild(), workingDirectory);

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
    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        => Attach(new TelnetSession()).ConnectAsync(host, port, Engine.Columns, Engine.Rows,
                                                    cancellationToken);

    /// <summary>Starts reading a connection that <see cref="ConnectAsync"/> opened.</summary>
    public void Begin()
    {
        if (_session is TelnetSession telnet) telnet.Begin();
    }

    /// <summary>Whether this window is showing a console Windows handed over.</summary>
    public bool IsHandoff => Kind == TerminalSessionKind.Handoff;

    /// <summary>
    /// Adds lines the app writes itself rather than the shell: the ready message at launch,
    /// the exit message at the end.
    ///
    /// They go through the same update the shell's output does, so the window mirrors them
    /// and the reader announces them without anything having to know where they came from.
    /// Appending to the document alone is not enough -- that is the transcript, not the box
    /// the user is reading.
    /// </summary>
    public void AppendExternal(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;

        var update = new TerminalUpdate { FirstNewLine = Transcript.Count };
        _core.Builder.AppendExternal(lines);
        update.NewLines.AddRange(lines);
        update.LiveText = string.Empty;

        Post(() => Updated?.Invoke(update));
    }

    /// <summary>Sends a typed line, with the Return as a separate write. See PtySession.</summary>
    public void SendLine(string text)
    {
        if (_session is null) return;
        _ = _session.WriteLineSplit(text, _session.LineTerminator, 20);
    }

    public void Send(ReadOnlySpan<byte> bytes) => _session?.Write(bytes);

    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        // Resize the child first. If ConPTY rejects it, the parser remains at its old size and
        // can continue consuming output consistently.
        _session?.Resize(size.Columns, size.Rows);
        Engine.Resize(size.Columns, size.Rows);
    }

    public void Dispose()
    {
        Announcer.Dispose();
        _session?.Dispose();
    }
}
