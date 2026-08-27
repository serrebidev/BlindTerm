namespace BlindTerm.App;

/// <summary>
/// Whether a program other than the shell owns the terminal.
///
/// This decides who a keystroke belongs to. At an idle prompt the command line is an ordinary
/// edit box and arrows, Home, End and the Ctrl chords are the ones Windows has always
/// provided. While Codex, Claude Code, OpenCode, Freebuff or telnet is running, those keys are
/// how the program is driven -- its model picker moves with Up and Down, its effort level with
/// Left and Right -- and a terminal that swallows them leaves the program unusable.
///
/// The answer comes from the process list rather than from the shell's own shell-integration
/// markers. A stock PowerShell 7 prompt emits no OSC 133 markers at all, so a session that
/// waited for a completed-command marker would treat the very first command as still running
/// forever after. A child process is a fact every shell reports identically.
/// </summary>
internal sealed class ForegroundProgramState
{
    /// <summary>
    /// How long after a submitted command line the program is assumed to be running before
    /// the process list is believed. Creating a process takes a moment, and without this the
    /// first keys after "codex" would be delivered to the shell that is busy starting it.
    /// </summary>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a process-list answer is reused. The probe runs on the UI thread from a key
    /// press, so it is kept off the path of a held-down arrow key while staying current
    /// enough that the shell prompt is not still "busy" once a program has exited.
    /// </summary>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(150);

    private readonly Func<bool> _shellHasChild;
    private readonly Func<TimeSpan> _now;

    private TimeSpan? _submittedAt;
    private TimeSpan? _probedAt;
    private bool _probed;
    private bool _exited;

    /// <param name="shellHasChild">Whether the session's process currently has a child.</param>
    /// <param name="now">A monotonic clock, for tests.</param>
    public ForegroundProgramState(Func<bool> shellHasChild, Func<TimeSpan>? now = null)
    {
        ArgumentNullException.ThrowIfNull(shellHasChild);
        _shellHasChild = shellHasChild;
        _now = now ?? (() => TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    public bool Active
    {
        get
        {
            if (_exited) return false;
            if (Probe()) return true;
            return _submittedAt is TimeSpan at && _now() - at < StartupGrace;
        }
    }

    /// <summary>
    /// A command line was sent to the shell. An empty submission is a bare Return, which
    /// starts nothing and must not claim the keyboard from the shell's own line editor.
    /// </summary>
    public void Submitted(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _submittedAt = _now();
        // The world just changed, so the previous answer is worthless.
        _probedAt = null;
    }

    /// <summary>The session ended. Nothing is in the foreground of a terminal that is gone.</summary>
    public void Exited() => _exited = true;

    private bool Probe()
    {
        TimeSpan now = _now();
        if (_probedAt is TimeSpan at && now - at < ProbeInterval) return _probed;
        _probed = _shellHasChild();
        _probedAt = now;
        return _probed;
    }
}
