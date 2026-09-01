using System.Diagnostics;
using System.Runtime.Versioning;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Sends output to the screen reader, batched so that a burst of lines becomes one utterance
/// instead of hundreds of tiny ones.
///
/// Batching is not what stops output cutting itself off. Streamed lines go out at
/// <see cref="SpeechPriority.Normal"/>, which queues behind whatever is speaking in both
/// readers -- NVDA through speakSsml's priority, JAWS through SayString's interrupt flag --
/// so forty lines arriving in a tenth of a second are forty queued utterances, not one
/// audible line. Only <see cref="SpeechPriority.Next"/> and above interrupt.
///
/// What batching is actually for is keeping the reader's queue and the listener's patience
/// in proportion to the output: one utterance per burst rather than one per pty read, and a
/// summary rather than the whole of a build log. Because it is not protecting against
/// interruption, the wait before speaking can be short, and it should be -- it is paid in
/// full on every command a person runs.
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

    /// <summary>Whether this announcer is holding Windows to a one-millisecond timer.</summary>
    private bool _holdingTimerResolution;

    /// <summary>
    /// When the current unbroken run of output began, and whether it has been going on long
    /// enough to have outrun speech. See <see cref="FloodAfter"/>.
    /// </summary>
    private long _streamingSince;
    private long _lastEnqueued;
    private bool _flooding;

    /// <summary>
    /// How long after output stops before it is spoken.
    ///
    /// Short, because most of the time this is the whole delay: press Return, the shell
    /// answers in one go, output stops, and speech starts. A fixed window instead of this put
    /// a quarter of a second between every keystroke and its answer, which is small enough to
    /// look reasonable written down and large enough to feel broken to use.
    ///
    /// It only has to outlast the gaps inside one burst, because a batch that splits in two
    /// is heard as the same words in the same order -- streamed speech queues rather than
    /// interrupting. A pseudo console hands over a single command's output in several reads
    /// a handful of milliseconds apart, and this covers that without waiting on the listener's
    /// behalf for output that has already finished arriving.
    /// </summary>
    public TimeSpan IdleWindow { get; set; } = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// The longest output can keep arriving before it is spoken anyway.
    ///
    /// Without a cap, a program that prints steadily would defer speech forever. With one, a
    /// build that runs for a minute is still described as it goes.
    /// </summary>
    public TimeSpan MaxWindow { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long output has to keep arriving before the terminal counts as flooding.
    ///
    /// Past this, the program is printing faster than anyone can be read to, and every batch
    /// spoken is one more on a queue that will still be draining long after the program has
    /// finished. Neither reader discards queued speech to make room -- a higher priority
    /// interrupts, then the backlog resumes -- so the queue only ever grows.
    /// </summary>
    public TimeSpan FloodAfter { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often a flooding terminal is spoken.
    ///
    /// Long enough for each report to be worth hearing. At the ordinary cap a flood would be
    /// four utterances a second, each of them dropped on the floor by the next -- which is
    /// not a description of anything, just noise.
    /// </summary>
    public TimeSpan FloodWindow { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Past this many lines in one batch, the text is summarised rather than read out whole.
    /// Nobody wants a thousand-line build read to them, and the tail is the useful part.
    ///
    /// High enough that ordinary commands are read rather than described. Thirty lines is
    /// less than a screenful -- a directory listing, a git log, a short test run -- and
    /// hearing "41 lines of output" instead of the output is the tool declining to do the one
    /// thing it is for. A batch is at most <see cref="MaxWindow"/> of output, so this bites
    /// only when a program is genuinely flooding the terminal.
    /// </summary>
    public int MaxLinesPerAnnouncement { get; set; } = 100;

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

    /// <summary>
    /// Whether anyone is actually in this window, or background speech has been switched on.
    ///
    /// Nothing is spoken into an empty seat: a screen reader has one voice for the whole
    /// desktop, so a background window that talks is interrupting whatever the user went
    /// to read -- which is worse than useless when BlindTerm is the default terminal and a
    /// program sits in a window nobody is looking at.
    /// </summary>
    private bool SomeoneHome => Attended || SpeakInBackground;

    /// <summary>Whether anything the program says on its own should be spoken at all.</summary>
    private bool Listening => Enabled && SomeoneHome;

    /// <summary>
    /// Where announcements go instead of the screen reader. Set by tests to collect them;
    /// null in normal use.
    /// </summary>
    public Action<string, SpeechPriority>? Sink { get; set; }

    public Announcer(IScreenReader reader) => _reader = reader;

    /// <summary>Queues lines of streamed output.</summary>
    public void Enqueue(IEnumerable<string> lines)
    {
        // Everything waits on the same two gates: output switched on, and a window somebody
        // is in (or background speech turned on). A trigger is not the exception it used to
        // be -- the user only wants to hear the app while they are in it.
        if (!Listening) return;

        var useful = lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (useful.Count == 0) return;

        lock (_gate)
        {
            if (_disposed) return;

            long now = Stopwatch.GetTimestamp();

            // A gap longer than the wait for output to stop means the last run finished and
            // was spoken. What arrives now is a new run, however long the previous one was.
            if (_lastEnqueued == 0 || Stopwatch.GetElapsedTime(_lastEnqueued) > IdleWindow)
            {
                _streamingSince = now;
                _flooding = false;
            }
            _lastEnqueued = now;
            if (!_flooding && Stopwatch.GetElapsedTime(_streamingSince) >= FloodAfter)
                _flooding = true;

            bool starting = _pending.Count == 0;
            _pending.AddRange(useful);
            if (starting) _batchStarted = now;

            // Wait for output to stop -- but never past the cap measured from the first line,
            // so that a burst still becomes one utterance rather than an unbroken postponement.
            // A flood is capped further out, because its batches are spoken over the top of
            // each other and there is no point producing them faster than they can be heard.
            TimeSpan elapsed = Stopwatch.GetElapsedTime(_batchStarted);
            TimeSpan delay = IdleWindow;
            TimeSpan remaining = (_flooding ? FloodWindow : MaxWindow) - elapsed;
            if (remaining < delay) delay = remaining;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            ArmFlushTimer(delay);
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
        // Not subject to Enabled, which is about streamed output only -- someone who has
        // turned output off still asked for this. But a window nobody is in is told
        // nothing, not even an explicit read or a bell the program rang on its own.
        if (!SomeoneHome) return;
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
        // Not subject to Enabled, but only spoken into an attended window: a trigger the
        // user wrote is still a line they only asked to hear while they are in the app.
        if (!SomeoneHome) return;

        lock (_gate)
        {
            if (_disposed) return;
            _urgent.Add(text.Trim());
            DisarmFlushTimer();
        }

        Flush(SpeechPriority.Now);
    }

    /// <summary>
    /// Drops output that was waiting to be spoken, keeping anything already marked urgent.
    ///
    /// For the moment the window stops being the one the user is in. Without this, leaving a
    /// busy terminal is followed a fifth of a second later by it saying one last thing into
    /// whatever you switched to -- which is the exact interruption this is all meant to stop.
    /// An urgent line interjected before the window was left is kept, because it was spoken
    /// into an attended window at the moment the user asked for it.
    /// </summary>
    public void DiscardStreamed()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            _pending.Clear();
            if (_urgent.Count > 0) return;
            DisarmFlushTimer();
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
            DisarmFlushTimer();
        }
    }

    /// <summary>
    /// Sets the flush timer, holding the system clock to a millisecond while it runs.
    ///
    /// Without the hold, Windows' 15.6 ms default resolution turns every wait here into the
    /// next multiple of 15.6 -- the delay the source says is 25 ms is measured at 31, and the
    /// 50 ms it used to say was measured at 62. Called under <see cref="_gate"/>.
    /// </summary>
    private void ArmFlushTimer(TimeSpan delay)
    {
        if (!_holdingTimerResolution)
        {
            SpeechTimerResolution.Acquire();
            _holdingTimerResolution = true;
        }

        if (_flushTimer is null) _flushTimer = new Timer(_ => Flush(), null, delay, Timeout.InfiniteTimeSpan);
        else _flushTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Drops the flush timer and the clock resolution it was holding. Called under
    /// <see cref="_gate"/>, and safe when there is no timer to drop.
    /// </summary>
    private void DisarmFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;

        if (!_holdingTimerResolution) return;
        _holdingTimerResolution = false;
        SpeechTimerResolution.Release();
    }

    private void Flush(SpeechPriority priority = SpeechPriority.Normal)
    {
        string text;
        bool dropBacklog;
        lock (_gate)
        {
            DisarmFlushTimer();
            if (_disposed || _pending.Count + _urgent.Count == 0) return;

            // While the terminal is flooding, what is already queued is out of date: it
            // describes output the program has since printed past. Speaking this batch behind
            // it would put the listener further behind still, so the queue goes and this
            // batch -- the current one -- is what gets said.
            dropBacklog = _flooding && priority == SpeechPriority.Normal;

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

        // Outside the lock: cancelling speech is a call into the reader, and the reader must
        // never be called with this held.
        if (dropBacklog) _reader.Silence();
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
            DisarmFlushTimer();
            _pending.Clear();
            _urgent.Clear();
        }
    }
}
