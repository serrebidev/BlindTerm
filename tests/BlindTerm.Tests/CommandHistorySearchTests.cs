using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandHistorySearchTests
{
    private static CommandHistory Filled()
    {
        var history = new CommandHistory();
        history.Remember("north");
        history.Remember("look");
        history.Remember("northeast");
        history.Remember("south");
        return history;
    }

    [Fact]
    public void TypingFirstTurnsTheArrowsIntoASearch()
    {
        CommandHistory history = Filled();

        Assert.Equal("northeast", history.Step(-1, "n"));
        Assert.Equal("north", history.Step(-1, "northeast"));
        Assert.Null(history.Step(-1, "north"));
    }

    [Fact]
    public void DownWalksBackAndGivesWhatWasTypedAgain()
    {
        CommandHistory history = Filled();
        history.Step(-1, "n");
        history.Step(-1, "northeast");

        Assert.Equal("northeast", history.Step(1, "north"));
        Assert.Equal("n", history.Step(1, "northeast"));

        // The search is over: an empty line steps the whole history again.
        Assert.Equal("south", history.Step(-1, string.Empty));
    }

    [Fact]
    public void ASearchMatchesWithoutRegardToCase()
    {
        CommandHistory history = Filled();

        Assert.Equal("northeast", history.Step(-1, "N"));
    }

    [Fact]
    public void ASearchThatMatchesNothingLeavesTheLineAsItIs()
    {
        CommandHistory history = Filled();

        Assert.Null(history.Step(-1, "xyzzy"));
        Assert.Equal("south", history.Step(-1, string.Empty));
    }

    [Fact]
    public void OnlyTheWordTypedNarrowsTheSearch()
    {
        CommandHistory history = Filled();

        // "o" appears inside several lines but begins none of them.
        Assert.Null(history.Step(-1, "o"));
    }

    [Fact]
    public void EditingWhatWasRecalledStartsTheSearchAgainFromWhatIsTyped()
    {
        CommandHistory history = Filled();
        Assert.Equal("northeast", history.Step(-1, "n"));

        // The line no longer holds what the search put there: "ne" typed afresh finds
        // the same match from the bottom, rather than stepping on to "north".
        Assert.Equal("northeast", history.Step(-1, "ne"));
    }

    [Fact]
    public void SendingALineEndsAnySearch()
    {
        CommandHistory history = Filled();
        Assert.Equal("northeast", history.Step(-1, "n"));

        history.Remember("northeast");

        Assert.Equal("northeast", history.Step(-1, string.Empty));
        Assert.Equal("south", history.Step(-1, "northeast"));
    }
}
