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
