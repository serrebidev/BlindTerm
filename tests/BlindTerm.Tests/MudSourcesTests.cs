using System.Net;
using System.Text;
using BlindTerm.Core.Mud;

namespace BlindTerm.Tests;

/// <summary>
/// Grapevine and The Mud Connector, and folding four directories into one list.
///
/// Both samples below are real, kept verbatim from the live sites. Grapevine's JSON is an
/// undocumented convenience of its web site -- its published API is a WebSocket one needing
/// an account -- and The Mud Connector's Big List is a table with no API at all, so both are
/// read on sufferance and the shape they are read with belongs written down.
/// </summary>
public class MudSourcesTests
{
    /// <summary>
    /// One page of Grapevine, with the cases that matter: a game offering both a plain and an
    /// encrypted port, one offering only a web client, and one with no tagline.
    /// </summary>
    private const string GrapevinePage = """
    {
      "items": [
        {
          "name": "ChatMUD",
          "short_name": "ChatMUD",
          "tagline": "A modern social MOO.",
          "description": "A long description that nobody wants read to them in a list.",
          "homepage_url": "https://www.chatmud.com/",
          "discord_invite_url": "https://discord.gg/example",
          "connections": [
            { "host": "chatmud.com", "port": 7777, "type": "telnet" },
            { "host": "chatmud.com", "port": 7443, "type": "secure telnet" }
          ]
        },
        {
          "name": "Apotheosis",
          "short_name": "Apotheosis",
          "tagline": "Web only, nothing to dial.",
          "connections": [ { "host": "example.com", "port": 443, "type": "web" } ]
        },
        {
          "name": "Silent One",
          "short_name": "Silent",
          "description": "First sentence survives. The rest of it does not.",
          "connections": [ { "host": "silent.example.com", "port": 4000, "type": "telnet" } ]
        }
      ],
      "links": [ { "href": "https://grapevine.haus/games?page=2", "rel": "next" } ]
    }
    """;

    /// <summary>Two rows of The Mud Connector's Big List, one reachable and one refused.</summary>
    private const string ConnectorPage = """
    <table id='biglist-table'><tbody>
    <tr> <td>38</td>
    <td><a href='https://www.mudconnect.com/cgi-bin/search.cgi?mode=mud_listing&mud=3-Kingdoms' data-tooltip='View'>3-Kingdoms</a></td>
    <td><a href='https://www.mudconnect.com/cgi-bin/telnet.cgi?mud=3-Kingdoms&url=telnet://3k.org:3000' data-tooltip='Connect'>3k.org 3000</a></td>
    <td><a href='http://www.mudportal.com/play?host=3k.org&port=3000' target='MudPortal'><i class='play icon'></i></a></td>
    <td><a href='https://www.mudconnect.com/cgi-bin/redirect.cgi?mud=3-Kingdoms&url=http://www.3k.org/' data-tooltip='Website'>http://www.3k.org/</a></td>
    <td>Connected</td> </tr>
    <tr> <td>643</td>
    <td><a href='https://www.mudconnect.com/cgi-bin/search.cgi?mode=mud_listing&mud=SneezyMUD' data-tooltip='View'>SneezyMUD</a></td>
    <td><a href='https://www.mudconnect.com/cgi-bin/telnet.cgi?mud=SneezyMUD&url=telnet://165.227.109.118:7900' data-tooltip='Connect'>165.227.109.118 7900</a></td>
    <td><a href='http://www.mudportal.com/play?host=165.227.109.118&port=7900' target='MudPortal'><i class='play icon'></i></a></td>
    <td><a href='https://www.mudconnect.com/cgi-bin/redirect.cgi?mud=SneezyMUD&url=http://sneezy.example.org/'>http://sneezy.example.org/</a></td>
    <td>Connect Refused</td> </tr>
    </tbody></table>
    """;

    [Fact]
    public async Task GrapevineIsTheOneSourceThatNamesAnEncryptedPortOutright()
    {
        using var directory = new GrapevineDirectory("https://grapevine.example",
            new HttpClient(new Once(GrapevinePage)));

        IReadOnlyList<MudGame> games = await directory.GamesAsync();
        MudGame chat = games.Single(game => game.Name == "ChatMUD");

        Assert.Equal("chatmud.com", chat.Host);
        Assert.Equal(7777, chat.Port);
        // The whole reason Grapevine is worth reading: it states this rather than leaving it
        // to be guessed from a port number.
        Assert.Equal(7443, chat.TlsPort);
        Assert.Equal("A modern social MOO.", chat.Intro);
        Assert.Equal("https://www.chatmud.com/", chat.Website);
    }

    [Fact]
    public async Task AGameWithOnlyAWebClientIsNotSomethingToDial()
    {
        using var directory = new GrapevineDirectory("https://grapevine.example",
            new HttpClient(new Once(GrapevinePage)));

        MudGame web = (await directory.GamesAsync()).Single(game => game.Name == "Apotheosis");

        Assert.False(web.CanConnect);
        // It is still read, so the generator can count it and say so, but nothing that filters
        // for connectable games will offer it.
        Assert.DoesNotContain((await directory.SearchAsync(new MudDirectoryQuery())).Games,
            game => game.Name == "Apotheosis");
    }

    [Fact]
    public async Task ADescriptionIsCutToSomethingAListCanRead()
    {
        using var directory = new GrapevineDirectory("https://grapevine.example",
            new HttpClient(new Once(GrapevinePage)));

        MudGame silent = (await directory.GamesAsync()).Single(game => game.Name == "Silent One");

        // No tagline, so the first sentence of the description stands in for one. A list item
        // is spoken in full on every arrow press; the paragraph belongs in the details.
        Assert.Equal("First sentence survives.", silent.Intro);
    }

    [Fact]
    public void TheBigListGivesUpItsAddressesAndItsConnectStatus()
    {
        IReadOnlyList<MudGame> games = MudConnectorDirectory.Parse(ConnectorPage);

        Assert.Equal(2, games.Count);
        MudGame kingdoms = games[0];
        Assert.Equal("3-Kingdoms", kingdoms.Name);
        Assert.Equal("3k.org", kingdoms.Host);
        Assert.Equal(3000, kingdoms.Port);
        Assert.Equal(38, kingdoms.Rank);
        Assert.Equal(MudAvailability.Online, kingdoms.Availability);
        // The website is behind TMC's redirect wrapper, which carries the real one in "url=".
        Assert.Equal("http://www.3k.org/", kingdoms.Website);

        MudGame sneezy = games[1];
        Assert.Equal("165.227.109.118", sneezy.Host);
        // TMC tries each host while building the page, so this is an answer about right now.
        Assert.Equal(MudAvailability.Offline, sneezy.Availability);
        Assert.False(sneezy.ConfirmedOnline);
    }

    [Fact]
    public void AListThatCannotBeReadIsReportedRatherThanReturnedEmpty()
    {
        // A table that has been redesigned and an outage look identical to a caller unless
        // one of them says so.
        MudDirectoryException failure = Assert.Throws<MudDirectoryException>(
            () => MudConnectorDirectory.Parse("<html><body>Nothing like a list.</body></html>"));
        Assert.Contains("could not be read", failure.Message);
    }

    [Fact]
    public void EachDirectoryFillsInWhatTheOnesBeforeItLeftBlank()
    {
        // What the generator actually does: MUDVerse has the genre and the rating, Grapevine
        // has the encrypted port and the tagline, TMC has the address.
        MudGame fromMudVerse = new()
        {
            Source = "MUDVerse", SourceId = "1", Name = "Core MUD",
            Genre = "Science Fiction", Rating = 4.5, ReviewCount = 3,
        };
        MudGame fromGrapevine = new()
        {
            Source = "Grapevine", SourceId = "CoreMUD", Name = "CoreMUD",
            Host = "coremud.org", Port = 4000, TlsPort = 4022,
            Intro = "Company mining colony.",
        };
        MudGame fromConnector = new()
        {
            Source = "The Mud Connector", SourceId = "CoreMUD", Name = "Core MUD",
            Host = "coremud.org", Port = 4000, Website = "https://coremud.org",
            Availability = MudAvailability.Online, ConfirmedOnline = true,
        };

        MudGame merged = Assert.Single(
            MudMerge.Describe([fromMudVerse], [fromGrapevine], [fromConnector]));

        // Nobody overwrote anybody, and the listing that had no address ended up with one.
        Assert.Equal("MUDVerse", merged.Source);
        Assert.Equal("Science Fiction", merged.Genre);
        Assert.Equal(4.5, merged.Rating);
        Assert.Equal("coremud.org", merged.Host);
        Assert.Equal(4000, merged.Port);
        Assert.Equal(4022, merged.TlsPort);
        Assert.Equal("Company mining colony.", merged.Intro);
        Assert.Equal("https://coremud.org", merged.Website);
        Assert.True(merged.ConfirmedOnline);
    }

    [Fact]
    public void AGameOnlyTheLastDirectoryKnowsAboutIsStillListed()
    {
        MudGame known = new()
        {
            Source = "MUDVerse", SourceId = "1", Name = "Known",
            Host = "known.example.com", Port = 4000,
        };
        MudGame onlyThere = new()
        {
            Source = "The Mud Connector", SourceId = "Obscure", Name = "Obscure MUD",
            Host = "obscure.example.com", Port = 5000,
        };

        IReadOnlyList<MudGame> merged = MudMerge.Describe([known], [], [onlyThere]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, game => game.Name == "Obscure MUD");
        // Order follows the order the sources were given, so the richest source leads.
        Assert.Equal("Known", merged[0].Name);
    }

    /// <summary>Answers the first request with a body, and every later one with an empty page.</summary>
    private sealed class Once(string json) : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = _calls++ == 0 ? json : """{"items":[],"links":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
