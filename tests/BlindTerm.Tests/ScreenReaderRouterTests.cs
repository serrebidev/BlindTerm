using BlindTerm.Core.Speech;

namespace BlindTerm.Tests;

/// <summary>
/// Choosing which screen reader to speak through, and how often that choice is re-made.
///
/// The cost of asking is the whole point. "Is NVDA running?" is a remote procedure call, and
/// "is JAWS running?" is a COM activation, so asking once per line of output is not free --
/// and the moment it hurts most is when the answer is no, because that is when every
/// candidate gets asked. A reader that has just crashed and is restarting is exactly that
/// case, and it is the case where making things worse is least forgivable.
/// </summary>
public class ScreenReaderRouterTests
{
    private sealed class FakeReader(string name) : IScreenReader
    {
        public string Name { get; } = name;
        public bool Running { get; set; }
        public bool SpeakSucceeds { get; set; } = true;

        public int RunningChecks { get; private set; }
        public int SpeakCalls { get; private set; }
        public List<string> Spoken { get; } = [];

        public bool IsRunning
        {
            get { RunningChecks++; return Running; }
        }

        public bool Speak(string text, SpeechPriority priority = SpeechPriority.Normal)
        {
            SpeakCalls++;
            if (!SpeakSucceeds) return false;
            Spoken.Add(text);
            return true;
        }

        public bool Braille(string text) => Running;
        public bool Silence() => Running;
    }

    private static ScreenReaderRouter Router(params IScreenReader[] readers)
        => new(readers) { RespectSecureDesktop = false };

    [Fact]
    public void WithNoReaderRunningTheCandidatesAreNotAskedEveryTime()
    {
        var nvda = new FakeReader("NVDA") { Running = false };
        var jaws = new FakeReader("JAWS") { Running = false };
        var router = Router(nvda, jaws);

        for (int i = 0; i < 50; i++) router.Speak($"line {i}");

        // One probe, not fifty. The probe interval is two seconds and this loop is immediate.
        Assert.Equal(1, nvda.RunningChecks);
        Assert.Equal(1, jaws.RunningChecks);
    }

    [Fact]
    public void WithAReaderRunningTheCandidatesAreNotAskedEveryTimeEither()
    {
        var nvda = new FakeReader("NVDA") { Running = true };
        var router = Router(nvda);

        for (int i = 0; i < 50; i++) router.Speak($"line {i}");

        Assert.Equal(1, nvda.RunningChecks);
        Assert.Equal(50, nvda.SpeakCalls);
    }

    [Fact]
    public void ARefusedUtteranceStopsTheNextOneBeingShoutedIntoTheVoid()
    {
        // NVDA answers "running" from the moment its endpoint exists, which is before it can
        // say anything -- and it restarts itself whenever its settings change.
        var nvda = new FakeReader("NVDA") { Running = true, SpeakSucceeds = false };
        var router = Router(nvda);

        Assert.False(router.Speak("first"));
        for (int i = 0; i < 20; i++) router.Speak($"line {i}");

        Assert.Equal(1, nvda.SpeakCalls);
    }

    [Fact]
    public void TheFirstReaderRunningIsTheOneUsed()
    {
        var nvda = new FakeReader("NVDA") { Running = false };
        var jaws = new FakeReader("JAWS") { Running = true };
        var router = Router(nvda, jaws);

        Assert.True(router.Speak("hello"));

        Assert.Equal("JAWS", router.Name);
        Assert.Equal(["hello"], jaws.Spoken);
        Assert.Empty(nvda.Spoken);
    }

    [Fact]
    public void WithNothingRunningSpeakingFailsQuietlyRatherThanThrowing()
    {
        var router = Router(new FakeReader("NVDA"), new FakeReader("JAWS"));

        Assert.False(router.Speak("nobody is listening"));
        Assert.False(router.IsRunning);
        Assert.Equal("none", router.Name);
    }

    [Fact]
    public void EmptyTextIsNotSentToTheReaderAtAll()
    {
        var nvda = new FakeReader("NVDA") { Running = true };
        var router = Router(nvda);

        router.Speak("");
        router.Speak(null!);

        Assert.Equal(0, nvda.SpeakCalls);
    }
}
