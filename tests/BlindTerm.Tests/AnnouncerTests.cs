using System.Diagnostics;
using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

/// <summary>
/// When output gets spoken, which is the difference between a terminal that answers and one
/// that feels slow.
///
/// Streamed output queues in both readers rather than interrupting them, so batching is not
/// what stops a program's output cutting itself off -- it is what keeps forty lines from
/// becoming forty separate utterances. The obvious way to do that -- always wait a fixed
/// while -- puts that same wait between pressing Return and hearing the answer, every time.
/// So the wait tracks the output instead: it ends as soon as output stops, and is capped so
/// a program that keeps printing is still described as it goes.
/// </summary>
public class AnnouncerTests
{
    /// <summary>
    /// The reported bug: with BlindTerm as the default terminal, a chat client left running
    /// in a window nobody was looking at kept reading its status updates out over whatever
    /// the user had gone to do. A screen reader has one voice for the whole desktop, so a
    /// background window that talks is not informing anyone, it is interrupting them.
    /// </summary>
    [Fact]
    public async Task AWindowNobodyIsInKeepsTheProgramsOutputToItself()
    {
        (Announcer announcer, Collector collected) = Make();
        using (announcer)
        {
            announcer.Attended = false;
            announcer.Enqueue(["Status: 3 users online"]);
            await Task.Delay(200);
            Assert.Empty(collected.Spoken);

            // Coming back to the window makes it talk again; nothing is permanently muted.
            announcer.Attended = true;
            announcer.Enqueue(["Status: 4 users online"]);
            await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["Status: 4 users online"], collected.Spoken);
        }
    }

    [Fact]
    public async Task LeavingABusyTerminalDoesNotGetOneLastSentenceOverTheTop()
    {
        (Announcer announcer, Collector collected) = Make(idleMs: 400, maxMs: 800);
        using (announcer)
        {
            announcer.Enqueue(["a line that was still waiting to be spoken"]);
            // The window loses focus while that batch is still queued.
            announcer.Attended = false;
            announcer.DiscardStreamed();

            await Task.Delay(600);
            Assert.Empty(collected.Spoken);
        }
    }

    /// <summary>
    /// A trigger is only heard while someone is in the window, same as any other speech: the
    /// user asked to hear the app while they are in it, not to be talked over in another
    /// window.
    /// </summary>
    [Fact]
    public async Task ATriggerIsNotHeardInAWindowNobodyIsIn()
    {
        (Announcer announcer, Collector collected) = Make();
        using (announcer)
        {
            announcer.Attended = false;

            announcer.Enqueue(["Someone said your name"]);
            await Task.Delay(200);
            Assert.Empty(collected.Spoken);
        }
    }

    [Fact]
    public async Task AnUrgentInterjectIsNotHeardInAWindowNobodyIsIn()
    {
        (Announcer announcer, Collector collected) = Make(idleMs: 400, maxMs: 800);
        using (announcer)
        {
            announcer.Attended = false;
            announcer.Interject("Someone is attacking you");

            await Task.Delay(600);
            Assert.Empty(collected.Spoken);
        }
    }

    [Fact]
    public async Task TurningBackgroundSpeechOnPutsTheOldBehaviourBack()
    {
        (Announcer announcer, Collector collected) = Make();
        using (announcer)
        {
            announcer.Attended = false;
            announcer.SpeakInBackground = true;

            announcer.Enqueue(["still talking"]);
            announcer.Interject("an urgent trigger");
            await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(collected.Spoken, said => said.Contains("still talking"));
        }
    }

    [Fact]
    public async Task SpeakOutputOffStillWinsOverEverythingAutomatic()
    {
        (Announcer announcer, Collector collected) = Make();
        using (announcer)
        {
            announcer.Enabled = false;

            announcer.Enqueue(["output"]);
            announcer.Enqueue(["trigger"]);
            announcer.AnnounceIfAttended("a repaint");
            await Task.Delay(200);
            Assert.Empty(collected.Spoken);

            // What the user asked for by hand is still said, as it always was.
            announcer.AnnounceNow("Speak output on");
            await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["Speak output on"], collected.Spoken);
        }
    }

    [Fact]
    public async Task ARepaintInAWindowNobodyIsWatchingIsNotNews()
    {
        (Announcer announcer, Collector collected) = Make();
        using (announcer)
        {
            announcer.Attended = false;
            announcer.AnnounceIfAttended("line 4 of 40");
            await Task.Delay(150);
            Assert.Empty(collected.Spoken);
        }
    }

    private sealed class Collector
    {
        public List<string> Spoken { get; } = [];
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task First => _first.Task;

        public void Take(string text, SpeechPriority priority)
        {
            lock (Spoken) Spoken.Add(text);
            _first.TrySetResult();
        }
    }

    /// <summary>
    /// The delay a person actually waits after their command has finished printing. It is
    /// paid on every command, so it is pinned here rather than left to drift: the reported
    /// complaint was output that lagged, and half of this wait was once invisible overshoot
    /// from Windows' 15.6 ms timer.
    /// </summary>
    [Fact]
    public void TheWaitAfterOutputStopsIsShortByDefault()
    {
        using var announcer = new Announcer(new NullScreenReader());
        Assert.True(
            announcer.IdleWindow <= TimeSpan.FromMilliseconds(25),
            $"The idle window is {announcer.IdleWindow.TotalMilliseconds} ms; every command waits it out.");
    }

    /// <summary>
    /// The complaint this pins: an ordinary command was described instead of read. Thirty
    /// lines is less than a screenful -- a directory listing, a git log, a short test run --
    /// and hearing "41 lines of output" instead of the output is the tool declining to do the
    /// one thing it is for.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryCommandsOutputIsReadRatherThanDescribed()
    {
        var (announcer, collected) = Make(idleMs: 20, maxMs: 200);
        using var _ = announcer;

        announcer.Enqueue(Enumerable.Range(1, 40).Select(i => $"line {i}"));
        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));

        string only = Assert.Single(collected.Spoken);
        Assert.DoesNotContain("lines of output", only, StringComparison.Ordinal);
        Assert.Contains("line 1", only, StringComparison.Ordinal);
        Assert.Contains("line 40", only, StringComparison.Ordinal);
    }

    /// <summary>
    /// A genuine flood is still summarised. The tail is the useful part of a build log, and
    /// nobody asked to be read five thousand lines.
    /// </summary>
    [Fact]
    public async Task AFloodIsStillSummarised()
    {
        var (announcer, collected) = Make(idleMs: 20, maxMs: 200);
        using var _ = announcer;

        int flood = announcer.MaxLinesPerAnnouncement * 3;
        announcer.Enqueue(Enumerable.Range(1, flood).Select(i => $"line {i}"));
        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));

        string only = Assert.Single(collected.Spoken);
        Assert.StartsWith($"{flood} lines of output.", only, StringComparison.Ordinal);
        Assert.Contains($"line {flood}", only, StringComparison.Ordinal);
    }

    /// <summary>
    /// Windows' default 15.6 ms timer does not fire early, so a 25 ms wait is measured at 31
    /// and the 50 ms this used to ask for was measured at 62 -- a quarter of the whole delay
    /// between a program printing and a reader speaking, invisible in the source. The
    /// resolution is raised only while a flush is pending, and must come back down: holding
    /// it keeps the scheduler awake for a terminal that is saying nothing.
    /// </summary>
    [Fact]
    public async Task TheClockIsHeldFineOnlyWhileOutputIsWaitingToBeSpoken()
    {
        var (announcer, collected) = Make(idleMs: 400, maxMs: 2000);
        using var _ = announcer;

        int before = SpeechTimerResolution.Holders;

        announcer.Enqueue(["something to say"]);
        Assert.True(
            SpeechTimerResolution.Holders > before,
            "A pending flush should hold the clock to a millisecond.");

        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(before, SpeechTimerResolution.Holders);
    }

    /// <summary>Dropping a batch releases the clock as surely as speaking it does.</summary>
    [Fact]
    public void DiscardingAWaitingBatchGivesTheClockBack()
    {
        var (announcer, _) = Make(idleMs: 2000, maxMs: 4000);
        using var __ = announcer;

        int before = SpeechTimerResolution.Holders;
        announcer.Enqueue(["a line nobody will hear"]);
        Assert.True(SpeechTimerResolution.Holders > before);

        announcer.DiscardPending();
        Assert.Equal(before, SpeechTimerResolution.Holders);
    }

    private static (Announcer Announcer, Collector Collected) Make(int idleMs = 25, int maxMs = 250)
    {
        var collected = new Collector();
        var announcer = new Announcer(new NullScreenReader())
        {
            Sink = collected.Take,
            IdleWindow = TimeSpan.FromMilliseconds(idleMs),
            MaxWindow = TimeSpan.FromMilliseconds(maxMs),
        };
        return (announcer, collected);
    }

    [Fact]
    public async Task AShortAnswerIsSpokenAsSoonAsOutputStops()
    {
        var (announcer, collected) = Make(idleMs: 40, maxMs: 2000);
        using var _ = announcer;

        var clock = Stopwatch.StartNew();
        announcer.Enqueue(["C:\\Users\\admin>"]);
        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Stop();

        Assert.Equal(["C:\\Users\\admin>"], collected.Spoken);

        // The cap is two seconds here, so waiting for it would be obvious. What is being
        // asserted is that the answer does not wait for the cap when nothing more is coming.
        Assert.True(clock.ElapsedMilliseconds < 1000,
            $"A single line took {clock.ElapsedMilliseconds}ms to reach the reader.");
    }

    [Fact]
    public async Task ABurstBecomesOneUtterance()
    {
        var (announcer, collected) = Make(idleMs: 60, maxMs: 5000);
        using var _ = announcer;

        for (int i = 1; i <= 8; i++)
        {
            announcer.Enqueue([$"line {i}"]);
            await Task.Delay(10);
        }

        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);

        string only = Assert.Single(collected.Spoken);
        Assert.Contains("line 1", only, StringComparison.Ordinal);
        Assert.Contains("line 8", only, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutputThatNeverStopsIsStillSpoken()
    {
        // The idle window alone would defer this forever: something arrives every 10ms and
        // the wait restarts each time. The cap is what makes a long-running build audible.
        var (announcer, collected) = Make(idleMs: 500, maxMs: 120);
        using var _ = announcer;

        using var stop = new CancellationTokenSource();
        Task printing = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                announcer.Enqueue(["still going"]);
                await Task.Delay(10, CancellationToken.None);
            }
        });

        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await printing;

        Assert.NotEmpty(collected.Spoken);
    }

    [Fact]
    public async Task BlankLinesAreNotAnnounced()
    {
        var (announcer, collected) = Make(idleMs: 20, maxMs: 100);
        using var _ = announcer;

        announcer.Enqueue(["", "   ", "\t"]);
        await Task.Delay(200);

        Assert.Empty(collected.Spoken);
    }

    [Fact]
    public async Task ALongBuildIsSummarisedRatherThanReadOutWhole()
    {
        var (announcer, collected) = Make(idleMs: 20, maxMs: 100);
        using var _ = announcer;
        announcer.MaxLinesPerAnnouncement = 5;

        announcer.Enqueue(Enumerable.Range(1, 40).Select(i => $"line {i}"));
        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));

        string only = Assert.Single(collected.Spoken);
        Assert.StartsWith("40 lines of output. Last 5:", only, StringComparison.Ordinal);
        Assert.Contains("line 40", only, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1\n", only, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurningOutputSpeechOffSilencesStreamedOutputOnly()
    {
        var (announcer, collected) = Make(idleMs: 20, maxMs: 100);
        using var _ = announcer;
        announcer.Enabled = false;

        announcer.Enqueue(["this should not be spoken"]);
        announcer.AnnounceNow("but this was asked for");
        await Task.Delay(200);

        Assert.Equal(["but this was asked for"], collected.Spoken);
    }

    [Fact]
    public async Task ScreenModeDropsOutputThatWouldArriveStale()
    {
        var (announcer, collected) = Make(idleMs: 500, maxMs: 500);
        using var _ = announcer;

        announcer.Enqueue(["a line the shell printed"]);
        announcer.DiscardPending();
        await Task.Delay(250);

        Assert.Empty(collected.Spoken);
    }

    /// <summary>
    /// What a trigger means by "say this at once".
    ///
    /// Saying it and letting the batch follow a twentieth of a second later would have the
    /// reader drop it mid-word: the urgent line has to lead the batch, not precede it.
    /// </summary>
    [Fact]
    public void AnUrgentLineLeadsTheBatchItInterruptsAndIsSpokenStraightAway()
    {
        var spoken = new List<(string Text, SpeechPriority Priority)>();
        using var announcer = new Announcer(new NullScreenReader())
        {
            Sink = (text, priority) => spoken.Add((text, priority)),
            IdleWindow = TimeSpan.FromSeconds(30),
            MaxWindow = TimeSpan.FromSeconds(30),
        };

        announcer.Enqueue(["ordinary output"]);
        announcer.Interject("your health is low");

        (string text, SpeechPriority priority) = Assert.Single(spoken);
        Assert.Equal("your health is low\nordinary output", text);
        Assert.Equal(SpeechPriority.Now, priority);
    }

    /// <summary>
    /// The batch a trigger fires in is often exactly the huge one that gets summarised. The
    /// urgent line is the one part of it that must not be summarised away.
    /// </summary>
    [Fact]
    public void AnUrgentLineSurvivesABatchLongEnoughToBeSummarised()
    {
        var spoken = new List<string>();
        using var announcer = new Announcer(new NullScreenReader())
        {
            Sink = (text, _) => spoken.Add(text),
            IdleWindow = TimeSpan.FromSeconds(30),
            MaxWindow = TimeSpan.FromSeconds(30),
            MaxLinesPerAnnouncement = 5,
        };

        announcer.Enqueue(Enumerable.Range(0, 40).Select(i => $"line {i}"));
        announcer.Interject("your health is low");

        Assert.StartsWith("your health is low", Assert.Single(spoken));
        Assert.Contains("40 lines of output", spoken[0]);
    }

    [Fact]
    public void AnUrgentLineIsSaidEvenWhenStreamedOutputHasBeenTurnedOff()
    {
        var spoken = new List<string>();
        using var announcer = new Announcer(new NullScreenReader())
        {
            Sink = (text, _) => spoken.Add(text),
            Enabled = false,
        };

        announcer.Enqueue(["ordinary output"]);
        announcer.Interject("your health is low");

        Assert.Equal(["your health is low"], spoken);
    }

    /// <summary>A reader that counts what was asked of it, so the tests can see a cancel.</summary>
    private sealed class CountingScreenReader : IScreenReader
    {
        public int Silences;
        public string Name => "counting";
        public bool IsRunning => true;
        public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal) => true;
        public bool Braille(string text) => true;
        public bool Silence() { Interlocked.Increment(ref Silences); return true; }
    }

    /// <summary>
    /// The reported bug: with a program printing steadily, speech ran further and further
    /// behind it -- still reading the start of a build after it had finished. Neither reader
    /// discards queued speech to make room, so every batch spoken during a flood is one more
    /// on a queue that only grows.
    /// </summary>
    [Fact]
    public async Task AFloodingTerminalIsHeardAsItIsNowRatherThanAsABacklog()
    {
        var reader = new CountingScreenReader();
        var collected = new Collector();
        using var announcer = new Announcer(reader)
        {
            Sink = collected.Take,
            IdleWindow = TimeSpan.FromMilliseconds(15),
            MaxWindow = TimeSpan.FromMilliseconds(50),
            FloodAfter = TimeSpan.FromMilliseconds(100),
            FloodWindow = TimeSpan.FromMilliseconds(200),
        };

        // A program printing steadily for well past the point speech could keep up.
        for (int i = 0; i < 80; i++)
        {
            announcer.Enqueue([$"building object {i}"]);
            await Task.Delay(10);
        }
        await Task.Delay(300);

        Assert.True(reader.Silences > 0, "A flood should drop the backlog rather than queue behind it.");

        // The last thing printed is in the last thing said: the listener is current, not behind.
        string last = collected.Spoken[^1];
        Assert.Contains("building object 79", last, StringComparison.Ordinal);
    }

    /// <summary>
    /// A flood is spoken at a cadence worth listening to. At the ordinary cap it would be
    /// several utterances a second, each cancelled by the next before a word of it was out.
    /// </summary>
    [Fact]
    public async Task AFloodIsNotSpokenFasterThanItCanBeHeard()
    {
        var collected = new Collector();
        using var announcer = new Announcer(new CountingScreenReader())
        {
            Sink = collected.Take,
            IdleWindow = TimeSpan.FromMilliseconds(15),
            MaxWindow = TimeSpan.FromMilliseconds(50),
            FloodAfter = TimeSpan.FromMilliseconds(100),
            FloodWindow = TimeSpan.FromMilliseconds(250),
        };

        for (int i = 0; i < 100; i++)
        {
            announcer.Enqueue([$"line {i}"]);
            await Task.Delay(10);
        }
        await Task.Delay(400);

        // A second of flooding at the 50 ms cap would be twenty utterances; at the flood
        // cadence it is a handful.
        Assert.InRange(collected.Spoken.Count, 1, 10);
    }

    /// <summary>
    /// An ordinary command is not a flood and keeps every word. Nothing is cancelled, so a
    /// line spoken just before it -- a trigger, a prompt -- is not cut off either.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryCommandNeverCancelsAnything()
    {
        var reader = new CountingScreenReader();
        var collected = new Collector();
        using var announcer = new Announcer(reader)
        {
            Sink = collected.Take,
            IdleWindow = TimeSpan.FromMilliseconds(20),
            MaxWindow = TimeSpan.FromMilliseconds(250),
            FloodAfter = TimeSpan.FromMilliseconds(1000),
        };

        announcer.Enqueue(["'d' is not recognized as an internal or external command,"]);
        announcer.Enqueue(["operable program or batch file."]);
        await collected.First.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(0, reader.Silences);
        Assert.Contains("not recognized", string.Join(Environment.NewLine, collected.Spoken), StringComparison.Ordinal);
        Assert.Contains("operable program", string.Join(Environment.NewLine, collected.Spoken), StringComparison.Ordinal);
    }

    /// <summary>
    /// When the flood stops, the tail is spoken at once rather than after another flood-length
    /// wait. The end of a build is the part worth hearing.
    /// </summary>
    [Fact]
    public async Task TheEndOfAFloodIsSpokenPromptly()
    {
        var collected = new Collector();
        using var announcer = new Announcer(new CountingScreenReader())
        {
            Sink = collected.Take,
            IdleWindow = TimeSpan.FromMilliseconds(20),
            MaxWindow = TimeSpan.FromMilliseconds(50),
            FloodAfter = TimeSpan.FromMilliseconds(100),
            FloodWindow = TimeSpan.FromSeconds(5),
        };

        for (int i = 0; i < 40; i++)
        {
            announcer.Enqueue([$"line {i}"]);
            await Task.Delay(10);
        }

        // Output stops. The idle window, not the five-second flood cap, decides.
        var clock = Stopwatch.StartNew();
        int before = collected.Spoken.Count;
        while (collected.Spoken.Count == before && clock.ElapsedMilliseconds < 2000)
            await Task.Delay(5);
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 500,
            $"The tail waited {clock.ElapsedMilliseconds} ms after output stopped.");
        Assert.Contains("line 39", string.Join(Environment.NewLine, collected.Spoken), StringComparison.Ordinal);
    }

    private sealed class NullScreenReader : IScreenReader
    {
        public string Name => "none";
        public bool IsRunning => false;
        public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal) => false;
        public bool Braille(string text) => false;
        public bool Silence() => false;
    }
}
