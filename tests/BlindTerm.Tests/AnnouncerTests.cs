using System.Diagnostics;
using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

/// <summary>
/// When output gets spoken, which is the difference between a terminal that answers and one
/// that feels slow.
///
/// Both readers drop whatever they were saying when a new utterance arrives, so a program
/// printing forty lines quickly has to become one utterance rather than forty interruptions
/// and one audible line. The obvious way to do that -- always wait a fixed while -- puts that
/// same wait between pressing Return and hearing the answer, every time. So the wait tracks
/// the output instead: it ends as soon as output stops, and is capped so a program that keeps
/// printing is still described as it goes.
/// </summary>
public class AnnouncerTests
{
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

    private static (Announcer Announcer, Collector Collected) Make(int idleMs = 50, int maxMs = 250)
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

    private sealed class NullScreenReader : IScreenReader
    {
        public string Name => "none";
        public bool IsRunning => false;
        public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal) => false;
        public bool Braille(string text) => false;
        public bool Silence() => false;
    }
}
