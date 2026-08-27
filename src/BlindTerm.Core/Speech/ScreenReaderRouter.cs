using System.Diagnostics;
using System.Runtime.Versioning;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Speaks through whichever screen reader is running, and notices when that changes.
///
/// A reader can be started, stopped or swapped in the middle of a session -- NVDA restarts
/// itself on a settings change, and a user may move between the two -- so the choice is
/// re-made periodically rather than once at startup. Between probes the last known good
/// reader is used, which is the common case and costs nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenReaderRouter : IScreenReader
{
    private readonly IScreenReader[] _candidates;
    private readonly Stopwatch _sinceProbe = Stopwatch.StartNew();
    private IScreenReader? _current;

    /// <summary>How stale the choice of reader is allowed to get.</summary>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether to stay silent while the lock screen or another secure desktop is in front.
    /// On by default: a terminal read aloud to a locked machine is a leak.
    /// </summary>
    public bool RespectSecureDesktop { get; set; } = true;

    /// <summary>Raised when the reader being spoken through changes, including to none.</summary>
    public event Action<string?>? ReaderChanged;

    public ScreenReaderRouter() : this(new NvdaScreenReader(), new JawsScreenReader()) { }

    public ScreenReaderRouter(params IScreenReader[] candidates) => _candidates = candidates;

    /// <summary>The reader currently being spoken through, or null when none is running.</summary>
    public IScreenReader? Current => Resolve();

    public string Name => Resolve()?.Name ?? "none";

    public bool IsRunning => Resolve() is not null;

    private IScreenReader? Resolve()
    {
        if (_current is not null && _sinceProbe.Elapsed < ProbeInterval) return _current;

        _sinceProbe.Restart();
        IScreenReader? found = _candidates.FirstOrDefault(c => c.IsRunning);

        if (!ReferenceEquals(found, _current))
        {
            _current = found;
            ReaderChanged?.Invoke(found?.Name);
        }
        return _current;
    }

    private bool Muted => RespectSecureDesktop && SecureDesktop.IsActive();

    public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
    {
        if (string.IsNullOrEmpty(text) || Muted) return false;
        return Resolve()?.Speak(text, priority) ?? false;
    }

    public bool Braille(string text)
    {
        if (string.IsNullOrEmpty(text) || Muted) return false;
        return Resolve()?.Braille(text) ?? false;
    }

    public bool Silence() => Resolve()?.Silence() ?? false;
}
