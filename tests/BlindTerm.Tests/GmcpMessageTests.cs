using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class GmcpMessageTests
{
    [Fact]
    public void APackageAndItsPayloadAreSplitAtTheFirstSpace()
    {
        Assert.True(GmcpMessage.TryParse("""Char.Vitals {"hp":280,"maxhp":280}""",
                                         out GmcpMessage? message));

        Assert.Equal("Char.Vitals", message!.Package);
        Assert.Equal("""{"hp":280,"maxhp":280}""", message.Payload);
    }

    [Fact]
    public void APackageOnItsOwnIsAWholeMessage()
    {
        Assert.True(GmcpMessage.TryParse("Core.Ping", out GmcpMessage? message));

        Assert.Equal("Core.Ping", message!.Package);
        Assert.Equal(string.Empty, message.Payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".Leading")]
    [InlineData("Trailing.")]
    [InlineData("Two..Dots")]
    [InlineData("Char/Vitals {}")]
    [InlineData("Char:Vitals {}")]
    [InlineData("{\"just\":\"json\"}")]
    public void SomethingThatIsNotAPackageNameIsNotAMessage(string text)
        => Assert.False(GmcpMessage.TryParse(text, out _));

    [Fact]
    public void AMessageTooLargeToBeAFactIsRefused()
    {
        // GMCP carries small statements. Anything of this size is a server misbehaving, and
        // parsing it would only cost the reading thread time.
        string huge = "Room " + new string('x', GmcpMessage.MaximumLength);

        Assert.False(GmcpMessage.TryParse(huge, out _));
    }

    [Theory]
    [InlineData("Room", "Room", true)]
    [InlineData("Room.Info", "Room", true)]
    [InlineData("room.info", "Room", true)]
    [InlineData("Rooms", "Room", false)]
    [InlineData("Char.Vitals", "Room", false)]
    public void APackageKnowsWhichFamilyItIsIn(string package, string family, bool inside)
    {
        Assert.True(GmcpMessage.TryParse(package, out GmcpMessage? message));

        Assert.Equal(inside, message!.IsIn(family));
    }
}
