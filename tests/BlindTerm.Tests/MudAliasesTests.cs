using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MudAliasesTests
{
    [Fact]
    public void AnExactAliasReplacesTheWholeLine()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("gg", "get all from corpse");

        Assert.True(book.TryExpand("gg", out string expanded));
        Assert.Equal("get all from corpse", expanded);
    }

    [Fact]
    public void MatchingDoesNotCareAboutCapitals()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("gg", "get gold");

        Assert.True(book.TryExpand("GG", out string expanded));
        Assert.Equal("get gold", expanded);
    }

    [Fact]
    public void ALineWithNoAliasStaysItself()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("gg", "get gold");

        Assert.False(book.TryExpand("look", out string expanded));
        Assert.Equal("look", expanded);
    }

    [Fact]
    public void AStarStandsForTheWordsThatFollow()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("sh *", "shoot $1");
        book.Set("say *", "emote says: $*");
        book.Set("give *", "give $2 to $1");

        Assert.True(book.TryExpand("sh goblin", out string shot));
        Assert.Equal("shoot goblin", shot);
        Assert.True(book.TryExpand("say hello there", out string said));
        Assert.Equal("emote says: hello there", said);
        Assert.True(book.TryExpand("give sword goblin", out string given));
        Assert.Equal("give goblin to sword", given);
    }

    [Fact]
    public void DollarZeroIsTheWholeLineTyped()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("rep", "score; say $0 done");

        Assert.True(book.TryExpand("rep", out string expanded));
        Assert.Equal("score; say rep done", expanded);
    }

    [Fact]
    public void AnExpansionIsNeverExpandedAgain()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("a", "b");
        book.Set("b", "look");

        Assert.True(book.TryExpand("a", out string expanded));
        Assert.Equal("b", expanded);
    }

    [Fact]
    public void WritingAnAliasTwiceReplacesIt()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("x", "one");
        book.Set("X", "two");

        Assert.Single(book.All);
        Assert.True(book.TryExpand("x", out string expanded));
        Assert.Equal("two", expanded);
    }

    [Fact]
    public void RemovingAnAliasSaysWhetherThereWasOne()
    {
        var book = new MudAliasBook(new List<MudAlias>());
        book.Set("x", "one");

        Assert.True(book.Remove("x"));
        Assert.False(book.Remove("x"));
        Assert.Empty(book.All);
    }

    [Fact]
    public void TheTypedCommandsAreReadOffTheLine()
    {
        Assert.Equal(MudClientCommand.DefineAlias,
            MudClientCommands.Parse("/alias sh * = shoot $1", out string pattern, out string replacement));
        Assert.Equal("sh *", pattern);
        Assert.Equal("shoot $1", replacement);

        Assert.Equal(MudClientCommand.RemoveAlias,
            MudClientCommands.Parse("/unalias sh", out pattern, out _));
        Assert.Equal("sh", pattern);

        Assert.Equal(MudClientCommand.ListAliases,
            MudClientCommands.Parse("/aliases", out _, out _));

        Assert.Equal(MudClientCommand.DefineTrigger,
            MudClientCommands.Parse("/trigger You are hungry = eat bread", out pattern, out replacement));
        Assert.Equal("You are hungry", pattern);
        Assert.Equal("eat bread", replacement);

        Assert.Equal(MudClientCommand.RemoveTrigger,
            MudClientCommands.Parse("/untrigger You are hungry", out pattern, out _));
        Assert.Equal("You are hungry", pattern);

        Assert.Equal(MudClientCommand.ListTriggers,
            MudClientCommands.Parse("/triggers", out _, out _));
    }

    [Theory]
    [InlineData("/alias")]
    [InlineData("/alias nothingfollows")]
    [InlineData("/alias = send")]
    [InlineData("/unalias")]
    [InlineData("alias x = y")]
    [InlineData("/say hello")]
    [InlineData("look")]
    public void HalfCommandsAndOrdinaryLinesAreNotCommands(string line)
    {
        Assert.Equal(MudClientCommand.None, MudClientCommands.Parse(line, out _, out _));
    }
}
