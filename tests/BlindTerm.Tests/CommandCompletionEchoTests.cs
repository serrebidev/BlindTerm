using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandCompletionEchoTests
{
    [Fact]
    public void TheCompletedCommandIsWhatTheShellAddedAfterThePrompt()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("PS C:\\Users\\admin> ");

        Assert.Equal("git status", echo.Completed("PS C:\\Users\\admin> git status"));
    }

    [Fact]
    public void NothingIsWaitingUntilTabIsPressed()
    {
        var echo = new CommandCompletionEcho();

        Assert.False(echo.Pending);
        Assert.Null(echo.Completed("PS C:\\Users\\admin> git status"));
    }

    [Fact]
    public void OnlyTheFirstReadBackAnswers()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("$ ");
        Assert.True(echo.Pending);

        Assert.Equal("cd Documents/", echo.Completed("$ cd Documents/"));

        // Everything after this is the user typing, which the screen reader echoes itself.
        Assert.False(echo.Pending);
        Assert.Null(echo.Completed("$ cd Documents/reports"));
    }

    [Fact]
    public void TabPressedAgainCyclesAndEachCandidateIsReadBack()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("$ ");
        Assert.Equal("vi notes.md", echo.Completed("$ vi notes.md"));

        echo.ExpectAnother();

        Assert.Equal("vi notes.txt", echo.Completed("$ vi notes.txt"));
    }

    [Fact]
    public void ThereIsNothingToCycleBeforeTheFirstTab()
    {
        var echo = new CommandCompletionEcho();

        echo.ExpectAnother();

        Assert.False(echo.Pending);
    }

    /// <summary>
    /// The prompt is the anchor. When it is no longer in front of the line, the screen has
    /// moved on -- a program started, the window rewrapped -- and a substring of whatever is
    /// there now would put someone else's text into the command box as if it had been typed.
    /// </summary>
    [Fact]
    public void ALineThatNoLongerStartsWithThePromptIsNotGuessedAt()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("PS C:\\Users\\admin> ");

        Assert.Null(echo.Completed("Loading model list..."));
    }

    [Fact]
    public void ACompletionThatChangedNothingSaysNothing()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("$ ");

        Assert.Null(echo.Completed("$ "));
    }

    [Fact]
    public void ASubmittedOrAbandonedLineIsNotReadBackLater()
    {
        var echo = new CommandCompletionEcho();
        echo.Expect("$ ");

        echo.Cancel();

        Assert.False(echo.Pending);
        Assert.Null(echo.Completed("$ git status"));
    }
}
