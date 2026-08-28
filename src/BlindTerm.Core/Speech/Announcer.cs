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
    /// <summary>
    /// Lines a trigger asked to have said at once. Kept apart from the rest so that they go
    /// first and are never the part a long batch summarises away.
    /// </summary>
    private readonly List<string> _urgent = new();
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
    /// Whether the window this speaks for is the one the user is actually in.
    ///
    /// A screen reader has one voice for the whole desktop, so a terminal that keeps talking
    /// after you have gone somewhere else is not providing information, it is talking over
    /// whatever you left to go and read. That is worse than useless when BlindTerm is the
    /// default terminal and a long-running program -- a chat client, a build, a MUD -- is
    /// sitting in a window nobody is looking at.
    /// </summary>
    public bool Attended { get; set; } = true;

    /// <summary>
    /// Whether to speak output anyway while the window is in the background.
    ///
    /// Off by default, because the useful case is narrow -- waiting on a build in another
    /// workspace -- and the harmful one is every other minute of the day. On, this is exactly
    /// the old behaviour.
    /// </summary>
    public bool SpeakInBackground { get; set; }

    /// <summary>Whether anything the program says on its own should be spoken at all.</summary>
    private bool Listening => Enabled && (Attended || SpeakInBackground);

    /// <summary>
    /// Where announcements go instead of the screen reader. Set by tests to collect them;
    /// null in normal use.
    /// </summary>
    public Action<string, SpeechPriority>? Sink { get; set; }

    public Announcer(IScreenReader reader) => _reader = reader;

    /// <summary>Queues lines of streamed output.</summary>
    /// <param name="attendedOnly">
    /// Whether this is the program talking, which a window nobody is in should keep to
    /// itself, or something the user asked for by writing a trigger, which they asked for
    /// wherever they happen to be looking.
    /// </param>
    public void Enqueue(IEnumerable<string> lines, bool attendedOnly = true)
    {
        if (attendedOnly ? !Listening : !Enabled) return;

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
    /// Says something the program did rather than something the user asked for: a full-screen
    /// program's cursor moving, a new file opening, a prompt appearing.
    ///
    /// Immediate like <see cref="AnnounceNow"/>, but only while somebody is actually in this
    /// window. A repaint in a terminal nobody is looking at is not news.
    /// </summary>
    public void AnnounceIfAttended(string text, SpeechPriority priority = SpeechPriority.Now)
    {
        if (!Listening) return;
        AnnounceNow(text, priority);
    }

    /// <summary>
    /// Puts something at the front of the batch that is waiting, and speaks the batch now.
    ///
    /// This is what a trigger means by "say this immediately". Saying it with
    /// <see cref="AnnounceNow"/> instead would have it spoken and then cut off a twentieth of
    /// a second later by the very output it was about -- both readers drop what they are
    /// saying when the next utterance arrives. Going out at the head of that batch is the
    /// only arrangement where the urgent line is heard first and heard whole.
    ///
    /// Not subject to <see cref="Enabled"/>: someone who has turned streamed output off has
    /// still asked for this one line.
    /// </summary>
    public void Interject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_gate)
        {
            if (_disposed) return;
            _urgent.Add(text.Trim());
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        Flush(SpeechPriority.Now);
    }

    /// <summary>
    /// Drops output that was waiting to be spoken, keeping anything a trigger called urgent.
    ///
    /// For the moment the window stops being the one the user is in. Without this, leaving a
    /// busy terminal is followed a fifth of a second later by it saying one last thing into
    /// whatever you switched to -- which is the exact interruption this is all meant to stop.
    /// A trigger's line is kept because the user asked for that one by name.
    /// </summary>
    public void DiscardStreamed()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            _pending.Clear();
            if (_urgent.Count > 0) return;
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
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
            _urgent.Clear();
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
    }

    private void Flush(SpeechPriority priority = SpeechPriority.Normal)
    {
        string text;
        lock (_gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (_disposed || _pending.Count + _urgent.Count == 0) return;

            string body;
            if (_pending.Count > MaxLinesPerAnnouncement)
            {
                var tail = _pending.Skip(_pending.Count - MaxLinesPerAnnouncement);
                body = $"{_pending.Count} lines of output. Last {MaxLinesPerAnnouncement}: "
                     + string.Join("\n", tail);
            }
            else
            {
                body = string.Join("\n", _pending);
            }

            // What a trigger called urgent leads, whatever else is waiting behind it -- and
            // is never the part a long batch summarises away.
            text = _urgent.Count == 0
                ? body
                : string.Join("\n", _urgent) + (body.Length > 0 ? "\n" + body : string.Empty);
            _pending.Clear();
            _urgent.Clear();
        }

        Speak(text, priority);
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
            _urgent.Clear();
        }
    }
}
