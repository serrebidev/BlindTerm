using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandHistoryLastTests
{
    [Fact]
    public void NothingSentYetMeansNothingToSendAgain()
    {
        var history = new CommandHistory();
        Assert.Null(history.Last);
    }

    [Fact]
    public void TheLastLineSentIsTheNewestOne()
    {
        var history = new CommandHistory();
        history.Remember("look");
        history.Remember("north");

        Assert.Equal("north", history.Last);
    }
}
