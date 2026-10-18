using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MudTimersTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATimerIsWrittenWithANameAnIntervalAndALine()
    {
        MudTimerCommand kind = MudClientCommands.ParseTimer(
            "/timer sip 45 = drink water", out string name, out int seconds,
            out bool repeat, out string command);

        Assert.Equal(MudTimerCommand.Define, kind);
        Assert.Equal("sip", name);
        Assert.Equal(45, seconds);
        Assert.True(repeat);
        Assert.Equal("drink water", command);
    }

    [Fact]
    public void AnIntervalMayBeWrittenInMinutesOrHours()
    {
        MudClientCommands.ParseTimer("/timer sip 2m = drink water",
            out _, out int minutes, out _, out _);
        MudClientCommands.ParseTimer("/timer save 1h = save",
            out _, out int hours, out _, out _);

        Assert.Equal(120, minutes);
        Assert.Equal(3600, hours);
    }

    [Fact]
    public void TheWordOnceMakesAOneShot()
    {
        MudTimerCommand kind = MudClientCommands.ParseTimer(
            "/timer greet 10s once = say hello to the room", out string name,
            out int seconds, out bool repeat, out string command);

        Assert.Equal(MudTimerCommand.Define, kind);
        Assert.Equal("greet", name);
        Assert.Equal(10, seconds);
        Assert.False(repeat);
        Assert.Equal("say hello to the room", command);
    }

    [Fact]
    public void TheListingAndTheRemovalReadAsThemselves()
    {
        Assert.Equal(MudTimerCommand.List,
            MudClientCommands.ParseTimer("/timers", out _, out _, out _, out _));
        Assert.Equal(MudTimerCommand.Remove,
            MudClientCommands.ParseTimer("/untimer sip", out string name, out _, out _, out _));
        Assert.Equal("sip", name);
    }

    [Theory]
    [InlineData("/timer sip = drink water")]        // no interval
    [InlineData("/timer sip 45x = drink water")]    // an interval that does not read
    [InlineData("/timer sip 0 = drink water")]      // no time at all
    [InlineData("/timer sip 25h = drink water")]    // more than a day
    [InlineData("/timer sip 45")]                   // nothing to send
    [InlineData("/timer sip 45 = ")]                // nothing after the sign either
    [InlineData("/timer sip 45 twice = drink")]     // a third word that is not "once"
    [InlineData("/timerx sip 45 = drink water")]    // a different word entirely
    [InlineData("/untimer")]                        // nothing named to remove
    public void AHalfWrittenTimerIsALineForTheMud(string line)
        => Assert.Equal(MudTimerCommand.None,
            MudClientCommands.ParseTimer(line, out _, out _, out _, out _));

    [Fact]
    public void WritingATimerAgainUnderItsNameReplacesIt()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("sip", 45, "drink water", true);
        book.Set("SIP", 30, "drink tea", false);

        MudTimer timer = Assert.Single(book.All);
        Assert.Equal(30, timer.Seconds);
        Assert.Equal("drink tea", timer.Command);
        Assert.False(timer.Repeat);
    }

    [Fact]
    public void RemovingATimerSaysWhetherThereWasOne()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("sip", 45, "drink water", true);

        Assert.True(book.Remove("SIP"));
        Assert.False(book.Remove("sip"));
        Assert.Empty(book.All);
    }

    [Fact]
    public void ATimerIsBroughtWithinItsLimitsWhenWritten()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("fast", 0, "look", true);
        book.Set("slow", int.MaxValue, "look", true);

        Assert.Equal(MudTimer.MinimumSeconds, book.All[0].Seconds);
        Assert.Equal(MudTimer.MaximumSeconds, book.All[1].Seconds);
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(90, "90 seconds")]
    [InlineData(120, "2 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    public void AnIntervalReadsBackInWords(int seconds, string words)
        => Assert.Equal(words, MudTimer.DescribeInterval(seconds));

    [Fact]
    public void ATimerFiresWhenItsIntervalIsUpAndARepeatingOneCountsAgainFromThen()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("sip", 45, "drink water", true);
        var schedule = new MudTimerSchedule(book);
        schedule.Restart(Noon);

        Assert.Empty(schedule.CollectDue(Noon.AddSeconds(44)));
        Assert.Equal(["drink water"], schedule.CollectDue(Noon.AddSeconds(45)));
        Assert.Empty(schedule.CollectDue(Noon.AddSeconds(89)));
        Assert.Equal(["drink water"], schedule.CollectDue(Noon.AddSeconds(90)));
    }

    [Fact]
    public void AOneShotFiresOnceAndStartingAgainArmsItAgain()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("greet", 10, "say hello", false);
        var schedule = new MudTimerSchedule(book);
        schedule.Restart(Noon);

        Assert.Equal(["say hello"], schedule.CollectDue(Noon.AddSeconds(10)));
        Assert.Empty(schedule.CollectDue(Noon.AddSeconds(20)));
        Assert.Equal(0, schedule.ArmedCount);

        // A new session is a new start: what a one-shot is for is "when I arrive".
        schedule.Restart(Noon.AddHours(1));
        Assert.Equal(["say hello"], schedule.CollectDue(Noon.AddHours(1).AddSeconds(10)));
    }

    [Fact]
    public void TimeMissedIsNotOwed()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("sip", 10, "drink water", true);
        var schedule = new MudTimerSchedule(book);
        schedule.Restart(Noon);

        // The machine slept through nine firings. One is sent, not nine.
        Assert.Equal(["drink water"], schedule.CollectDue(Noon.AddSeconds(95)));
        Assert.Empty(schedule.CollectDue(Noon.AddSeconds(100)));
        Assert.Equal(["drink water"], schedule.CollectDue(Noon.AddSeconds(105)));
    }

    [Fact]
    public void TimersDueTogetherComeOutInTheOrderTheyWereWritten()
    {
        var book = new MudTimerBook(new List<MudTimer>());
        book.Set("first", 30, "one", true);
        book.Set("second", 30, "two", true);
        var schedule = new MudTimerSchedule(book);
        schedule.Restart(Noon);

        Assert.Equal(["one", "two"], schedule.CollectDue(Noon.AddSeconds(30)));
    }
}
