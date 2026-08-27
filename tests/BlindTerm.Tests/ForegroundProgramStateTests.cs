using BlindTerm.App;

namespace BlindTerm.Tests;

public class ForegroundProgramStateTests
{
    [Fact]
    public void NonEmptySubmissionStaysActiveUntilItsCommandBlockCompletes()
    {
        var state = new ForegroundProgramState();

        state.Submitted("codex", completedBlocks: 4);
        state.Updated(completedBlocks: 4);

        Assert.True(state.Active);

        state.Updated(completedBlocks: 5);

        Assert.False(state.Active);
    }

    [Fact]
    public void InputSentToTheForegroundProgramDoesNotMoveItsCompletionBoundary()
    {
        var state = new ForegroundProgramState();

        state.Submitted("any-program", completedBlocks: 2);
        state.Submitted("a prompt for that program", completedBlocks: 2);
        state.Updated(completedBlocks: 3);

        Assert.False(state.Active);
    }

    [Fact]
    public void EmptyShellInputDoesNotClaimControlShortcuts()
    {
        var state = new ForegroundProgramState();

        state.Submitted(string.Empty, completedBlocks: 0);

        Assert.False(state.Active);
    }

    [Fact]
    public void TerminalExitClearsForegroundState()
    {
        var state = new ForegroundProgramState();
        state.Submitted("any-program", completedBlocks: 0);

        state.Exited();

        Assert.False(state.Active);
    }
}
