using BlindTerm.App;

namespace BlindTerm.Tests;

public class ForegroundProgramStateTests
{
    /// <summary>A clock the test moves by hand, so nothing waits on real time.</summary>
    private sealed class Clock
    {
        public TimeSpan Now { get; private set; }
        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void AnIdleShellPromptOwnsItsOwnKeys()
    {
        var clock = new Clock();
        var state = new ForegroundProgramState(() => false, () => clock.Now);

        Assert.False(state.Active);
    }

    [Fact]
    public void AProgramTheShellStartedOwnsTheKeyboard()
    {
        var clock = new Clock();
        var state = new ForegroundProgramState(() => true, () => clock.Now);

        Assert.True(state.Active);
    }

    [Fact]
    public void AJustSubmittedCommandCountsBeforeItsProcessExists()
    {
        var clock = new Clock();
        int probes = 0;
        var state = new ForegroundProgramState(() => { probes++; return false; }, () => clock.Now);

        state.Submitted("codex");

        Assert.True(state.Active);
        Assert.Equal(0, probes);
    }

    [Fact]
    public void ACommandThatStartedNothingStopsCountingOnceItCouldHave()
    {
        var clock = new Clock();
        var state = new ForegroundProgramState(() => false, () => clock.Now);
        state.Submitted("echo hello");

        clock.Advance(ForegroundProgramState.StartupGrace);

        Assert.False(state.Active);
    }

    [Fact]
    public void EmptyShellInputDoesNotClaimTheKeyboard()
    {
        var clock = new Clock();
        var state = new ForegroundProgramState(() => false, () => clock.Now);

        state.Submitted(string.Empty);

        Assert.False(state.Active);
    }

    [Fact]
    public void TheProgramExitingHandsTheKeysBack()
    {
        var clock = new Clock();
        bool running = true;
        var state = new ForegroundProgramState(() => running, () => clock.Now);
        Assert.True(state.Active);

        running = false;
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(state.Active);
    }

    [Fact]
    public void TheProcessListIsNotConsultedOnEveryKeyPress()
    {
        var clock = new Clock();
        int probes = 0;
        var state = new ForegroundProgramState(() => { probes++; return true; }, () => clock.Now);

        for (int i = 0; i < 20; i++) Assert.True(state.Active);

        Assert.Equal(1, probes);
    }

    [Fact]
    public void SubmittingACommandDiscardsTheStaleAnswer()
    {
        var clock = new Clock();
        int probes = 0;
        var state = new ForegroundProgramState(() => { probes++; return false; }, () => clock.Now);
        Assert.False(state.Active);

        state.Submitted("telnet example.org 4000");
        clock.Advance(ForegroundProgramState.StartupGrace);

        Assert.False(state.Active);
        Assert.Equal(2, probes);
    }

    [Fact]
    public void TerminalExitClearsForegroundState()
    {
        var clock = new Clock();
        var state = new ForegroundProgramState(() => true, () => clock.Now);

        state.Exited();

        Assert.False(state.Active);
    }

    [Fact]
    public void ReturningToTheShellRestoresForegroundTracking()
    {
        var clock = new Clock();
        bool running = false;
        var state = new ForegroundProgramState(() => running, () => clock.Now);
        state.Exited();

        state.Resumed();
        Assert.False(state.Active);

        running = true;
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(state.Active);
    }
}
