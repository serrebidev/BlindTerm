using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
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
    private readonly PtySession _session = new();
    private readonly SynchronizationContext _ui;
    private readonly TerminalCore _core;

    public TerminalCore Core => _core;
    public TerminalEngine Engine => _core.Engine;
    public Transcript Transcript => _core.Transcript;
    public PtySession Session => _session;

    public ScreenReaderRouter Reader { get; }
    public Announcer Announcer { get; }

    /// <summary>A batch of changes, already on the UI thread.</summary>
    public event Action<TerminalUpdate>? Updated;

    /// <summary>The program rang the bell, already on the UI thread.</summary>
    public event Action? Bell;

    public event Action<string>? TitleChanged;
    public event Action<int?>? Exited;

    public bool IsRunning => _session.IsRunning;

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
        _core.Engine.Respond += bytes => _session.Write(bytes);

        _session.Output += memory => _core.Feed(memory.Span);
        _session.Exited += code =>
        {
            _core.Flush();
            Post(() => Exited?.Invoke(code));
        };
    }

    private void Post(Action action) => _ui.Post(_ => action(), null);

    public void Start(string commandLine, string? workingDirectory = null)
        => _session.Start(commandLine, Engine.Columns, Engine.Rows,
                          TerminalEnvironment.ForChild(), workingDirectory);

    /// <summary>
    /// Takes over a console Windows has already created for a program, because BlindTerm is
    /// the default terminal. Nothing above this line can tell the difference.
    /// </summary>
    public void Adopt(ConsoleHandoff handoff)
        => _session.Adopt(handoff, Engine.Columns, Engine.Rows);

    /// <summary>Whether this window is showing a console Windows handed over.</summary>
    public bool IsHandoff => _session.IsHandoff;

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
    public void SendLine(string text) => _ = _session.WriteLineSplit(text);

    public void Send(ReadOnlySpan<byte> bytes) => _session.Write(bytes);

    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        // Resize the child first. If ConPTY rejects it, the parser remains at its old size and
        // can continue consuming output consistently.
        _session.Resize(size.Columns, size.Rows);
        Engine.Resize(size.Columns, size.Rows);
    }

    public void Dispose()
    {
        Announcer.Dispose();
        _session.Dispose();
    }
}
