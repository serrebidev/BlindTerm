using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

/// <summary>
/// What a MUD says about itself over GMCP, turned into the sentence a player wants read.
///
/// The payloads here are Core MUD's own, taken off the wire.
/// </summary>
public class MudStatusTests
{
    private static GmcpMessage Message(string text)
    {
        Assert.True(GmcpMessage.TryParse(text, out GmcpMessage? message));
        return message!;
    }

    private const string Apartment = """
        Room {"coords":{"z":0,"y":0,"x":0},"z":0,"y":0,"area":"South Dome",
        "Environment":"unknown","x":0,"exits":{"north":"3bae953b"},"id":"331a46bd",
        "long":"This is your apartment.\n\nExits: north\n","short":"Apartment of Karia"}
        """;

    private const string Vitals = """
        Char.Vitals {"sp":154,"bloat":"","sp_percent":100,"damaged_limb":"","maxhp":280,
        "limb_health":100,"poison":"","hp":280,"intox":"","stuffed":"","hp_percent":100,
        "maxsp":154}
        """;

    [Fact]
    public void ARoomBecomesItsNameAreaAndExits()
    {
        var status = new MudStatus();

        Assert.Equal("Apartment of Karia, South Dome. Exits: north.",
                     status.News(Message(Apartment)));
        Assert.Equal("Apartment of Karia, South Dome. Exits: north.", status.Room);
        Assert.Equal(["north"], status.Exits);
    }

    [Fact]
    public void TheSameRoomAgainIsNotNews()
    {
        // Core MUD sends the room after every command, whether or not anyone has moved. A
        // transcript line per "look" would be a transcript of nothing.
        var status = new MudStatus();
        Assert.NotNull(status.News(Message(Apartment)));
        Assert.Null(status.News(Message(Apartment)));
        Assert.Null(status.News(Message(Apartment)));
    }

    [Fact]
    public void MovingBetweenRoomsThatReadAlikeIsStillMoving()
    {
        // One corridor is much like the next. Where the MUD gives a room an identity, that is
        // what says whether the character moved -- not whether the words changed.
        var status = new MudStatus();
        status.News(Message("""Room {"short":"Corridor","exits":{"north":"b"},"id":"a"}"""));

        Assert.Equal("Corridor. Exits: north.",
                     status.News(Message("""Room {"short":"Corridor","exits":{"north":"c"},"id":"b"}""")));
    }

    [Fact]
    public void ARoomWithNoWayOutSaysSo()
        => Assert.Equal("The Vault. No obvious exits.",
                        new MudStatus().News(Message("""Room {"short":"The Vault","exits":{}}""")));

    [Fact]
    public void ExitsMayArriveAsAListOrAsAString()
    {
        Assert.Equal("Crossroads. Exits: north, east.",
                     new MudStatus().News(Message("""Room {"short":"Crossroads","exits":["north","east"]}""")));
        Assert.Equal("Crossroads. Exits: north, east.",
                     new MudStatus().News(Message("""Room {"short":"Crossroads","exits":"north east"}""")));
    }

    [Fact]
    public void VitalsBecomeThePoolsAndNothingElse()
    {
        var status = new MudStatus();

        // The empty conditions are how this MUD says "not poisoned", and saying them would
        // bury the two numbers that matter.
        Assert.Equal("HP 280 of 280. SP 154 of 154.", status.News(Message(Vitals)));
        Assert.Equal("HP 280 of 280. SP 154 of 154.", status.Vitals);
    }

    [Fact]
    public void AConditionIsSaidOnlyWhileItApplies()
    {
        var status = new MudStatus();
        status.News(Message(Vitals));

        Assert.Equal("HP 240 of 280. SP 154 of 154. Poison venom. Damaged limb left arm.",
                     status.News(Message("""
                        Char.Vitals {"hp":240,"maxhp":280,"sp":154,"maxsp":154,
                        "poison":"venom","damaged_limb":"left arm","intox":""}
                        """)));
    }

    [Fact]
    public void UnchangedVitalsAfterEveryCommandAreNotNews()
    {
        var status = new MudStatus();
        Assert.NotNull(status.News(Message(Vitals)));
        Assert.Null(status.News(Message(Vitals)));
    }

    [Fact]
    public void ANumberSentAsTextIsStillANumber()
        => Assert.Equal("HP 40 of 100.",
                        new MudStatus().News(Message("""Char.Vitals {"hp":"40","maxhp":"100"}""")));

    [Fact]
    public void TheCharactersNameIsRememberedWithoutBeingAnnounced()
    {
        var status = new MudStatus();

        Assert.Null(status.News(Message("""Char.Status {"name":"karia"}""")));
        Assert.Equal("karia", status.CharacterName);
    }

    [Theory]
    [InlineData("""Room {not json at all}""")]
    [InlineData("""Room "a string, not an object" """)]
    [InlineData("Room")]
    [InlineData("""Char.Vitals {"nothing":"recognised"}""")]
    public void AMessageThisDoesNotUnderstandChangesNothing(string text)
    {
        var status = new MudStatus();

        Assert.Null(status.News(Message(text)));
        Assert.Null(status.Room);
        Assert.Null(status.Vitals);
    }

    [Fact]
    public void ANewConnectionIsSomewhereElseEntirely()
    {
        var status = new MudStatus();
        status.News(Message(Apartment));
        status.News(Message(Vitals));

        status.Reset();

        Assert.Null(status.Room);
        Assert.Null(status.Vitals);
        Assert.Empty(status.Exits);
        // And the room that was current is news again, because nothing is known any more.
        Assert.NotNull(status.News(Message(Apartment)));
    }

    [Fact]
    public void PackagesOutsideTheOnesThatMatterAreIgnored()
    {
        var status = new MudStatus();

        Assert.Null(status.News(Message("""Client.Map {"url":"https://coremud.org/coremud.dat"}""")));
        Assert.Null(status.News(Message("""External.Discord.Info {"inviteurl":"https://x"}""")));
    }
}
