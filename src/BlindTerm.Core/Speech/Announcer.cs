using System.Diagnostics;
using System.Runtime.Versioning;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Sends output to the screen reader, batched so that a burst of lines becomes one utterance
/// instead of dozens of interruptions.
///
/// Batching matters more here than it did on the Mac: both NVDA and JAWS drop whatever they
/// were saying when a new utterance arrives, so a compiler writing forty lines in a tenth of
/// a second would otherwise be forty interruptions and one audible line -- the last one.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Announcer : IDisposable
{
    private readonly IScreenReader _reader;
    private readonly Lock _gate = new();
    private readonly List<string> _pending = new();
    private Timer? _flushTimer;
    private long _batchStarted;
    private bool _disposed;

    /// <summary>
    /// How long after output stops before it is spoken.
    ///
    /// Short, because most of the time this is the whole delay: press Return, the shell
    /// answers in one go, output stops, and speech starts. A fixed window instead of this put
    /// a quarter of a second between every keystroke and its answer, which is small enough to
    /// look reasonable written down and large enough to feel broken to use.
    /// </summary>
    public TimeSpan IdleWindow { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The longest output can keep arriving before it is spoken anyway.
    ///
    /// Without a cap, a program that prints steadily would defer speech forever. With one, a
    /// build that runs for a minute is still described as it goes.
    /// </summary>
    public TimeSpan MaxWindow { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Past this many lines in one batch, the text is summarised rather than read out whole.
    /// Nobody wants a thousand-line build read to them, and the tail is the useful part.
    /// </summary>
    public int MaxLinesPerAnnouncement { get; set; } = 30;

    /// <summary>When off, streamed output is silent. Bells and explicit reads still speak.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where announcements go instead of the screen reader. Set by tests to collect them;
    /// null in normal use.
    /// </summary>
    public Action<string, SpeechPriority>? Sink { get; set; }

    public Announcer(IScreenReader reader) => _reader = reader;

    /// <summary>Queues lines of streamed output.</summary>
    public void Enqueue(IEnumerable<string> lines)
    {
        if (!Enabled) return;

        var useful = lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (useful.Count == 0) return;

        lock (_gate)
        {
            if (_disposed) return;

            bool starting = _pending.Count == 0;
            _pending.AddRange(useful);
            if (starting) _batchStarted = Stopwatch.GetTimestamp();

            // Wait for output to stop -- but never past the cap measured from the first line,
            // so that a burst still becomes one utterance rather than an unbroken postponement.
            TimeSpan elapsed = Stopwatch.GetElapsedTime(_batchStarted);
            TimeSpan delay = IdleWindow;
            TimeSpan remaining = MaxWindow - elapsed;
            if (remaining < delay) delay = remaining;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            if (_flushTimer is null) _flushTimer = new Timer(_ => Flush(), null, delay, Timeout.InfiniteTimeSpan);
            else _flushTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Says something immediately, ahead of anything queued: a bell, or the line the caret
    /// has just been put on. Not subject to <see cref="Enabled"/>, which is about streamed
    /// output only -- someone who has turned output off still asked for this.
    /// </summary>
    public void AnnounceNow(string text, SpeechPriority priority = SpeechPriority.Now)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Speak(text.Trim(), priority);
    }

    /// <summary>
    /// Drops queued line-mode output without cancelling speech that the reader is already
    /// speaking. A full-screen program has taken over, so a delayed shell utterance would be
    /// stale and would compete with the program's own screen speech.
    /// </summary>
    public void DiscardPending()
    {
        lock (_gate)
        {
            _pending.Clear();
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
    }

    private void Flush()
    {
        string text;
        lock (_gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (_pending.Count == 0 || _disposed) return;

            if (_pending.Count > MaxLinesPerAnnouncement)
            {
                var tail = _pending.Skip(_pending.Count - MaxLinesPerAnnouncement);
                text = $"{_pending.Count} lines of output. Last {MaxLinesPerAnnouncement}: "
                     + string.Join("\n", tail);
            }
            else
            {
                text = string.Join("\n", _pending);
            }
            _pending.Clear();
        }

        Speak(text, SpeechPriority.Normal);
    }

    private void Speak(string text, SpeechPriority priority)
    {
        if (Sink is { } sink) sink(text, priority);
        else _reader.Speak(text, priority);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _flushTimer?.Dispose();
            _flushTimer = null;
            _pending.Clear();
        }
    }
}
