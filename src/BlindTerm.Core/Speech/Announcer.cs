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
    private bool _disposed;

    /// <summary>
    /// How long lines are gathered before being spoken. Long enough that a burst becomes one
    /// utterance, short enough that a prompt does not feel late.
    /// </summary>
    public TimeSpan BatchWindow { get; set; } = TimeSpan.FromMilliseconds(250);

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
            _pending.AddRange(useful);
            _flushTimer ??= new Timer(_ => Flush(), null, BatchWindow, Timeout.InfiniteTimeSpan);
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
