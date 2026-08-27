using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MspTriggerTests
{
    private static MspTrigger Parse(string body, MspKind kind = MspKind.Sound)
    {
        Assert.True(MspTrigger.TryParse(kind, body, out MspTrigger? trigger));
        return trigger!;
    }

    [Fact]
    public void ANameOnItsOwnGetsTheProtocolDefaults()
    {
        MspTrigger trigger = Parse("sword.wav");

        Assert.Equal("sword.wav", trigger.FileName);
        Assert.Equal(MspTrigger.DefaultVolume, trigger.Volume);
        Assert.Equal(1, trigger.Loops);
        Assert.Equal(MspTrigger.DefaultPriority, trigger.Priority);
        Assert.True(trigger.Continue);
        Assert.Null(trigger.Type);
        Assert.Null(trigger.Url);
    }

    [Fact]
    public void EveryParameterIsRead()
    {
        MspTrigger trigger = Parse("theme.mid V=70 L=-1 P=90 C=0 T=music U=http://mud.example/snd",
                                   MspKind.Music);

        Assert.Equal("theme.mid", trigger.FileName);
        Assert.Equal(70, trigger.Volume);
        Assert.Equal(MspTrigger.Forever, trigger.Loops);
        Assert.Equal(90, trigger.Priority);
        Assert.False(trigger.Continue);
        Assert.Equal("music", trigger.Type);
        Assert.Equal("http://mud.example/snd", trigger.Url);
    }

    [Fact]
    public void ParametersMayComeInAnyOrderAndInEitherCase()
    {
        MspTrigger trigger = Parse("hit.wav t=combat v=25 l=3");

        Assert.Equal("combat", trigger.Type);
        Assert.Equal(25, trigger.Volume);
        Assert.Equal(3, trigger.Loops);
    }

    [Fact]
    public void AParameterNobodyHasHeardOfIsIgnoredRatherThanFatal()
    {
        // This comes from a server. A MUD adding a parameter of its own must not stop the
        // sound it asked for from playing.
        MspTrigger trigger = Parse("hit.wav V=40 Z=nonsense X=1");

        Assert.Equal("hit.wav", trigger.FileName);
        Assert.Equal(40, trigger.Volume);
    }

    [Theory]
    [InlineData("hit.wav V=900", 100)]
    [InlineData("hit.wav V=-5", 0)]
    [InlineData("hit.wav V=loud", MspTrigger.DefaultVolume)]
    public void AVolumeOutsideTheRangeIsBroughtBackIntoIt(string body, int expected)
        => Assert.Equal(expected, Parse(body).Volume);

    [Fact]
    public void ALoopCountBelowMinusOneMeansForEver()
    {
        // -1 is the only negative with a meaning; anything further down is a count that could
        // never be reached, and silence is not what the MUD asked for.
        Assert.Equal(MspTrigger.Forever, Parse("drums.wav L=-9").Loops);
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("off")]
    [InlineData("OFF")]
    public void OffIsRecognisedHoweverItIsWritten(string body)
        => Assert.True(Parse(body).IsOff);

    [Fact]
    public void ARealNameIsNotMistakenForOff()
        => Assert.False(Parse("offer.wav").IsOff);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V=50")]
    public void SomethingWithNoNameIsNotATrigger(string body)
        => Assert.False(MspTrigger.TryParse(MspKind.Sound, body, out _));
}
