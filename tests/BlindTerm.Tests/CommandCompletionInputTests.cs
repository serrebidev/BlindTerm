using System.Text;
using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandCompletionInputTests
{
    [Fact]
    public void FirstTabFlushesBufferedTextBeforeTheCompletionByte()
    {
        var input = new CommandCompletionInput();

        Assert.Equal("/model\t", Encoding.UTF8.GetString(input.Begin("/model")));
        Assert.True(input.Active);
    }

    [Fact]
    public void LaterTabsDoNotResendTheOriginalText()
    {
        var input = new CommandCompletionInput();
        input.Begin("/m");

        Assert.Equal(new byte[] { 0x09 }, input.Begin("ignored local mirror"));
    }

    [Fact]
    public void TypingStreamsOnlyAfterCompletionStarts()
    {
        var input = new CommandCompletionInput();

        Assert.Null(input.Character('x'));
        input.Begin("@");
        Assert.Equal("é", Encoding.UTF8.GetString(input.Character('é')!));
        Assert.Null(input.Character('\r'));
    }

    /// <summary>
    /// A line the terminal's own editor completed can start a program, and BlindTerm no
    /// longer holds the text to tell. What it does know is whether the line is bare.
    /// </summary>
    [Fact]
    public void ALineHandedOverWithSomethingOnItCountsAsHavingText()
    {
        var input = new CommandCompletionInput();

        Assert.False(input.HasText);
        input.Begin("cod");
        Assert.True(input.HasText);
        input.FinishLine();
        Assert.False(input.HasText);
    }

    [Fact]
    public void CompletionPutsTextOnALineThatWasBareWhenTabWasPressed()
    {
        var input = new CommandCompletionInput();
        input.Begin(string.Empty);
        Assert.False(input.HasText);

        input.Completed("Documents/");

        Assert.True(input.HasText);
    }

    [Fact]
    public void TypingAfterCompletionPutsTextOnABareLineToo()
    {
        var input = new CommandCompletionInput();
        input.Begin(string.Empty);

        input.Character('x');

        Assert.True(input.HasText);
    }

    [Fact]
    public void EnterEndsStreamingAndTheNextPromptIsBufferedAgain()
    {
        var input = new CommandCompletionInput();
        input.Begin("/m");

        Assert.True(input.FinishLine());
        Assert.False(input.Active);
        Assert.False(input.FinishLine());
        Assert.Null(input.Character('x'));
    }
}
