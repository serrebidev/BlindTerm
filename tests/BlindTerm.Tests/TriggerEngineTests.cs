using BlindTerm.Core;
using BlindTerm.Core.Triggers;

namespace BlindTerm.Tests;

public class TriggerEngineTests
{
    /// <summary>A clock the test moves by hand, so a cooldown can be tested without waiting.</summary>
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static Trigger Watch(string pattern, TriggerMatch match = TriggerMatch.Contains)
        => new() { Pattern = pattern, Match = match, Speak = "noticed" };

    private static TriggerEngine Loaded(params Trigger[] triggers)
    {
        var engine = new TriggerEngine();
        engine.Load(triggers);
        return engine;
    }

    [Fact]
    public void AMatchingLineSaysWhatTheTriggerAsked()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "* has connected.",
            Match = TriggerMatch.Wildcard,
            Speak = "$1 is online",
        });

        TriggerOutcome outcome = engine.Run(["Fred has connected."], TriggerWhere.Mud);

        Assert.Equal([new TriggerSpeech("Fred is online", false)], outcome.Speech);
    }

    [Fact]
    public void ALineNothingMatchesAsksForNothing()
        => Assert.True(Loaded(Watch("dragon")).Run(["a quiet afternoon"], TriggerWhere.Mud).IsEmpty);

    [Fact]
    public void ATriggerSeesAMudEventThatRewritesTheCurrentPromptLine()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "Karia chips away at the wall.",
            Send = "drill south wall",
            Where = TriggerWhere.Mud,
        });
        var update = new TerminalUpdate { LiveLine = 21 };
        update.Edits.Add(new Transcript.Edit(
            Line: 21, Start: 0, OldLength: 1, Text: "> Karia chips away at the wall."));

        TriggerOutcome outcome = engine.Run(update, TriggerWhere.Mud);

        Assert.Equal(["drill south wall"], outcome.Sends);
    }

    [Fact]
    public void ALineAppendedAndRewrittenInOneBatchIsCheckedOnceAtItsFinalText()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "Karia chips away at the wall.",
            Send = "drill south wall",
        });
        var update = new TerminalUpdate { FirstNewLine = 21, LiveLine = 21 };
        update.NewLines.Add(">");
        update.Edits.Add(new Transcript.Edit(
            Line: 21, Start: 0, OldLength: 1, Text: "> Karia chips away at the wall."));

        TriggerOutcome outcome = engine.Run(update, TriggerWhere.Mud);

        Assert.Equal(["drill south wall"], outcome.Sends);
    }

    [Fact]
    public void SoundsBeepsAndSendsAreAllGatheredFromOneLine()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "*attacks you*",
            Match = TriggerMatch.Wildcard,
            Sound = @"C:\sounds\alarm.wav",
            Beep = true,
            Send = "flee",
        });

        TriggerOutcome outcome = engine.Run(["A troll attacks you!"], TriggerWhere.Mud);

        Assert.Equal([@"C:\sounds\alarm.wav"], outcome.Sounds);
        Assert.True(outcome.Beep);
        Assert.Equal(["flee"], outcome.Sends);
    }

    [Fact]
    public void WhatIsSentHasTheWildcardsFilledIn()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "* asks you for help",
            Match = TriggerMatch.Wildcard,
            Send = "tell $1 on my way",
        });

        Assert.Equal(["tell Fred on my way"],
            engine.Run(["Fred asks you for help"], TriggerWhere.Mud).Sends);
    }

    /// <summary>
    /// A line break in what is sent would be two commands where the user wrote one. The
    /// dialog cannot put one there, but a settings file edited by hand can, and what reaches
    /// the far end is still one line.
    /// </summary>
    [Fact]
    public void ASentLineIsOneLineWhateverWasWrittenIntoTheSettingsFile()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "attacks you",
            Send = "flee\r\nnorth",
        });

        Assert.Equal(["flee north"],
            engine.Run(["A troll attacks you!"], TriggerWhere.Mud).Sends);
    }

    [Fact]
    public void ASilencedLineIsRecognisedWhateverSpaceIsAroundIt()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "gossip",
            Silence = true,
        });

        TriggerOutcome outcome = engine.Run(["  Fred gossips about the weather  "], TriggerWhere.Mud);

        Assert.True(outcome.AnySilenced);
        Assert.True(outcome.IsSilenced("Fred gossips about the weather"));
        Assert.False(outcome.IsSilenced("Fred tells you hello"));
    }

    [Fact]
    public void AnUrgentTriggerSaysSoInTheOutcome()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "critical",
            Speak = "warning",
            SpeakNow = true,
        });

        Assert.Equal([new TriggerSpeech("warning", true)],
            engine.Run(["health critical"], TriggerWhere.Mud).Speech);
    }

    [Fact]
    public void ATriggerThatIsOffDoesNothing()
    {
        Trigger trigger = Watch("dragon");
        trigger.Enabled = false;
        Assert.True(Loaded(trigger).Run(["a dragon appears"], TriggerWhere.Mud).IsEmpty);
    }

    [Fact]
    public void TheMasterSwitchStopsEveryTrigger()
    {
        TriggerEngine engine = Loaded(Watch("dragon"));
        engine.Enabled = false;
        Assert.True(engine.Run(["a dragon appears"], TriggerWhere.Mud).IsEmpty);
    }

    [Theory]
    [InlineData(TriggerWhere.Mud, TriggerWhere.Mud, true)]
    [InlineData(TriggerWhere.Mud, TriggerWhere.Shell, false)]
    [InlineData(TriggerWhere.Shell, TriggerWhere.Shell, true)]
    [InlineData(TriggerWhere.Shell, TriggerWhere.Mud, false)]
    [InlineData(TriggerWhere.Anywhere, TriggerWhere.Shell, true)]
    [InlineData(TriggerWhere.Anywhere, TriggerWhere.Mud, true)]
    public void ATriggerRunsOnlyInTheKindOfSessionItWasWrittenFor(
        TriggerWhere written, TriggerWhere session, bool expected)
    {
        Trigger trigger = Watch("dragon");
        trigger.Where = written;

        bool fired = !Loaded(trigger).Run(["a dragon appears"], session).IsEmpty;
        Assert.Equal(expected, fired);
    }

    /// <summary>
    /// The way to write "everything from this channel, except when it is talking to me" is a
    /// trigger for your own name above one that silences the channel.
    /// </summary>
    [Fact]
    public void StopProcessingKeepsALineFromTheTriggersBelowIt()
    {
        var mine = new Trigger
        {
            Pattern = "*Serrebi*",
            Match = TriggerMatch.Wildcard,
            Beep = true,
            StopProcessing = true,
        };
        var channel = new Trigger { Pattern = "[gossip]", Silence = true };
        TriggerEngine engine = Loaded(mine, channel);

        TriggerOutcome about = engine.Run(["[gossip] Fred: has anyone seen Serrebi?"], TriggerWhere.Mud);
        Assert.True(about.Beep);
        Assert.False(about.AnySilenced);

        TriggerOutcome other = engine.Run(["[gossip] Fred: nice weather"], TriggerWhere.Mud);
        Assert.False(other.Beep);
        Assert.True(other.AnySilenced);
    }

    [Fact]
    public void EveryLineInABatchIsChecked()
    {
        TriggerEngine engine = Loaded(new Trigger { Pattern = "warning", Speak = "warning" });

        TriggerOutcome outcome = engine.Run(
            ["all fine", "warning: one", "also fine", "warning: two"], TriggerWhere.Shell);

        Assert.Equal(2, outcome.Speech.Count);
    }

    [Fact]
    public void ATriggerAskedToWaitDoesNotFireAgainUntilItHas()
    {
        var clock = new StoppedClock();
        var engine = new TriggerEngine(clock);
        engine.Load([new Trigger { Pattern = "hit", Beep = true, RepeatAfterMilliseconds = 5000 }]);

        Assert.True(engine.Run(["a hit"], TriggerWhere.Mud).Beep);
        Assert.False(engine.Run(["a hit"], TriggerWhere.Mud).Beep);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True(engine.Run(["a hit"], TriggerWhere.Mud).Beep);
    }

    [Fact]
    public void WithNoWaitAskedForATriggerFiresOnEveryMatchingLine()
    {
        var engine = new TriggerEngine(new StoppedClock());
        engine.Load([new Trigger { Pattern = "hit", Speak = "hit" }]);

        Assert.Single(engine.Run(["a hit"], TriggerWhere.Mud).Speech);
        Assert.Single(engine.Run(["a hit"], TriggerWhere.Mud).Speech);
    }

    /// <summary>
    /// The loop this exists for: a trigger sends a line, the MUD echoes it, the echo matches
    /// the same pattern, and the two ends shout at each other until someone pulls the plug.
    /// </summary>
    [Fact]
    public void ATriggerThatRunsAwayIsSwitchedOffAndSaidSo()
    {
        var clock = new StoppedClock();
        var engine = new TriggerEngine(clock);
        var runaway = new Trigger { Pattern = "ping", Send = "ping" };
        engine.Load([runaway]);

        string[] flood = [.. Enumerable.Repeat("ping", TriggerEngine.BurstLimit + 5)];
        TriggerOutcome outcome = engine.Run(flood, TriggerWhere.Mud);

        Assert.Equal(TriggerEngine.BurstLimit, outcome.Sends.Count);
        Assert.Single(outcome.Notes);
        Assert.Contains("switched off", outcome.Notes[0]);
        Assert.False(runaway.Enabled);

        // And it stays off, rather than starting again once the burst window has passed.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(engine.Run(["ping"], TriggerWhere.Mud).IsEmpty);
    }

    [Fact]
    public void AnOrdinaryBurstSpreadOverTimeIsNotMistakenForALoop()
    {
        var clock = new StoppedClock();
        var engine = new TriggerEngine(clock);
        engine.Load([new Trigger { Pattern = "hit", Beep = true }]);

        for (int i = 0; i < TriggerEngine.BurstLimit * 3; i++)
        {
            Assert.True(engine.Run(["a hit"], TriggerWhere.Mud).Beep);
            clock.Advance(TriggerEngine.BurstWindow + TimeSpan.FromMilliseconds(1));
        }
    }

    /// <summary>
    /// A trigger nobody can hear is worse than one that never ran, so a pattern that will not
    /// compile is named rather than dropped, and the ones around it carry on working.
    /// </summary>
    [Fact]
    public void APatternThatWillNotCompileIsNamedAndTheRestStillRun()
    {
        TriggerEngine engine = Loaded(
            new Trigger { Name = "broken", Pattern = "(unclosed", Match = TriggerMatch.Regex, Beep = true },
            new Trigger { Pattern = "dragon", Speak = "dragon" });

        Assert.Single(engine.Problems);
        Assert.Contains("broken", engine.Problems[0]);
        Assert.Equal(2, engine.Count);
        Assert.Single(engine.Run(["a dragon appears"], TriggerWhere.Mud).Speech);
    }

    [Fact]
    public void LoadingReplacesTheWholeSet()
    {
        var engine = new TriggerEngine();
        engine.Load([Watch("dragon")]);
        engine.Load([Watch("troll")]);

        Assert.Equal(1, engine.Count);
        Assert.True(engine.Run(["a dragon appears"], TriggerWhere.Mud).IsEmpty);
        Assert.False(engine.Run(["a troll appears"], TriggerWhere.Mud).IsEmpty);
    }

    [Fact]
    public void SpeechThatExpandsToNothingIsNotSpoken()
    {
        TriggerEngine engine = Loaded(new Trigger
        {
            Pattern = "*says*",
            Match = TriggerMatch.Wildcard,
            Speak = "$3",
        });

        Assert.Empty(engine.Run(["Fred says hello"], TriggerWhere.Mud).Speech);
    }
}
