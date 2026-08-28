using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandHistoryTests
{
    [Fact]
    public void UpRecallsNewestFirstAndDownReturnsToABlankLine()
    {
        var history = new CommandHistory();
        history.Remember("look");
        history.Remember("north");

        Assert.Equal("north", history.Step(-1));
        Assert.Equal("look", history.Step(-1));
        Assert.Equal("look", history.Step(-1));
        Assert.Equal("north", history.Step(1));
        Assert.Equal(string.Empty, history.Step(1));
        Assert.Equal(string.Empty, history.Step(1));
    }

    [Fact]
    public void PressingEnterOnARecalledLineMakesItNewestAgain()
    {
        var history = new CommandHistory();
        history.Remember("look");
        history.Remember("north");
        string recalled = history.Step(-2)!;

        history.Remember(recalled);

        Assert.Equal("look", history.Step(-1));
    }

    [Fact]
    public void ConsecutiveDuplicatesAndBlankLinesDoNotClutterHistory()
    {
        var history = new CommandHistory();
        history.Remember("say hello");
        history.Remember("say hello");
        history.Remember(string.Empty);

        Assert.Equal(1, history.Count);
        Assert.Equal("say hello", history.Step(-1));
    }

    [Fact]
    public void ClearingAConnectionRemovesItsHistory()
    {
        var history = new CommandHistory();
        history.Remember("inventory");

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Null(history.Step(-1));
    }
}
