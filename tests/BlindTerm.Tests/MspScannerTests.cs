using System.Text;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MspScannerTests
{
    private static (string Text, List<MspTrigger> Triggers) Scan(MspScanner scanner, params string[] reads)
    {
        var text = new StringBuilder();
        var triggers = new List<MspTrigger>();
        foreach (string read in reads)
        {
            byte[] received = Encoding.UTF8.GetBytes(read);
            var buffer = new byte[received.Length + MspScanner.Headroom];
            int written = scanner.Scan(received, buffer, triggers);
            text.Append(Encoding.UTF8.GetString(buffer, 0, written));
        }
        return (text.ToString(), triggers);
    }

    private static (string Text, List<MspTrigger> Triggers) Scan(params string[] reads)
        => Scan(new MspScanner(), reads);

    [Fact]
    public void OrdinaryTextIsUntouched()
    {
        var (text, triggers) = Scan("You are in a dusty room.\r\n");

        Assert.Equal("You are in a dusty room.\r\n", text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void ATriggerNeverReachesTheText()
    {
        // Left in, this is a line read aloud as "exclamation exclamation SOUND left paren
        // sword dot wav" in the middle of a fight.
        var (text, triggers) = Scan("You hit the orc.\r\n!!SOUND(sword.wav)\r\nIt staggers.\r\n");

        Assert.Equal("You hit the orc.\r\nIt staggers.\r\n", text);
        MspTrigger trigger = Assert.Single(triggers);
        Assert.Equal(MspKind.Sound, trigger.Kind);
        Assert.Equal("sword.wav", trigger.FileName);
    }

    [Fact]
    public void ATriggerAtTheVeryStartOfAConnectionCounts()
    {
        var (text, triggers) = Scan("!!MUSIC(theme.mid L=-1)\r\nWelcome.\r\n");

        Assert.Equal("Welcome.\r\n", text);
        Assert.Equal(MspKind.Music, Assert.Single(triggers).Kind);
    }

    [Fact]
    public void TextAfterATriggerOnTheSameLineSurvives()
    {
        var (text, triggers) = Scan("!!SOUND(hit.wav)You hit the orc.\r\n");

        Assert.Equal("You hit the orc.\r\n", text);
        Assert.Single(triggers);
    }

    [Fact]
    public void SomethingThatOnlyLooksLikeATriggerIsLeftAlone()
    {
        // What a player types into a chat channel comes back after a name and a colon, in the
        // middle of a line. That is ordinary text and must stay ordinary text.
        var (text, triggers) = Scan("Grubnak says: !!SOUND(scream.wav)\r\n");

        Assert.Equal("Grubnak says: !!SOUND(scream.wav)\r\n", text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void ATriggerSplitBetweenTwoReadsIsStillOneTrigger()
    {
        var (text, triggers) = Scan("Ouch!\r\n!!SOU", "ND(hit.wav V=80)\r\nYou bleed.\r\n");

        Assert.Equal("Ouch!\r\nYou bleed.\r\n", text);
        Assert.Equal(80, Assert.Single(triggers).Volume);
    }

    [Fact]
    public void ATriggerSplitOneByteAtATimeIsStillOneTrigger()
    {
        var scanner = new MspScanner();
        string[] reads = [.. "!!SOUND(drip.wav)\r\ndrip\r\n".Select(c => c.ToString())];

        var (text, triggers) = Scan(scanner, reads);

        Assert.Equal("drip\r\n", text);
        Assert.Equal("drip.wav", Assert.Single(triggers).FileName);
    }

    [Fact]
    public void AnExclamationThatGoesNowhereIsGivenBack()
    {
        var (text, triggers) = Scan("!! Attention !!\r\n");

        Assert.Equal("!! Attention !!\r\n", text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void AnExclamationHeldAtTheEndOfAReadIsGivenBackByTheNext()
    {
        var (text, triggers) = Scan("!", "! nope\r\n");

        Assert.Equal("!! nope\r\n", text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void AnythingStillHeldWhenTheConnectionEndsIsGivenBack()
    {
        var scanner = new MspScanner();
        var (text, _) = Scan(scanner, "!!SOU");
        Assert.Equal(string.Empty, text);
        Assert.True(scanner.HasPartialTrigger);

        var buffer = new byte[MspScanner.Headroom];
        int written = scanner.Flush(buffer);

        Assert.Equal("!!SOU", Encoding.UTF8.GetString(buffer, 0, written));
        Assert.False(scanner.HasPartialTrigger);
    }

    [Fact]
    public void SomethingTooLongToBeATriggerStopsBeingHeld()
    {
        string runaway = "!!SOUND(" + new string('x', MspScanner.MaximumTriggerLength + 20);

        var (text, triggers) = Scan(runaway);

        Assert.Equal(runaway, text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void AnUnfinishedTriggerInterruptedByANewLineIsText()
    {
        var (text, triggers) = Scan("!!SOUND(oops\r\nnext line\r\n");

        Assert.Equal("!!SOUND(oops\r\nnext line\r\n", text);
        Assert.Empty(triggers);
    }

    [Fact]
    public void TwoTriggersInOneReadAreBothFound()
    {
        var (text, triggers) = Scan("!!SOUND(a.wav)\r\n!!MUSIC(b.mid)\r\nHello.\r\n");

        Assert.Equal("Hello.\r\n", text);
        Assert.Equal(2, triggers.Count);
        Assert.Equal(MspKind.Sound, triggers[0].Kind);
        Assert.Equal(MspKind.Music, triggers[1].Kind);
    }

    [Fact]
    public void ATriggerWithNoLineEndingAfterItLeavesThePromptAlone()
    {
        // A MUD prompt has no line ending, and swallowing the wrong byte would eat it.
        var (text, triggers) = Scan("!!SOUND(ping.wav)> ");

        Assert.Equal("> ", text);
        Assert.Single(triggers);
    }

    [Fact]
    public void ALineEndingSplitFromItsTriggerIsStillSwallowed()
    {
        var (text, triggers) = Scan("!!SOUND(ping.wav)\r", "\nYou wake up.\r\n");

        Assert.Equal("You wake up.\r\n", text);
        Assert.Single(triggers);
    }

    [Fact]
    public void LowerCaseTriggersAreAccepted()
    {
        var (text, triggers) = Scan("!!sound(quiet.wav)\r\n");

        Assert.Equal(string.Empty, text);
        Assert.Equal("quiet.wav", Assert.Single(triggers).FileName);
    }

    [Fact]
    public void AnOffTriggerIsRecognised()
    {
        var (_, triggers) = Scan("!!SOUND(Off)\r\n!!MUSIC(Off)\r\n");

        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, trigger => Assert.True(trigger.IsOff));
    }
}
