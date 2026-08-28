using System.Net;
using System.Text;
using BlindTerm.Core.Mud;

namespace BlindTerm.Tests;

/// <summary>
/// Reading somebody else's directory, and turning it into something that can be read aloud.
///
/// The parsing is the part that fails silently: a renamed field comes back as a blank rather
/// than an error, and a list of MUDs with no player counts on it looks like a quiet week.
/// </summary>
public class MudDirectoryTests
{
    private const string OneGame = """
    {
      "data": [{
        "id": 79,
        "name": "Example MUD",
        "intro": "A persistent text-based world.",
        "urls": { "website": "https://example.com", "mudverse": "https://www.mudverse.com/game/79" },
        "connection": { "host": "mud.example.com", "port": 4000, "tls_port": 4001 },
        "ranking": { "rank": 4, "monthly_votes": 125 },
        "reviews": { "count": 8, "average_rating": 4.5 },
        "tags": { "categories": { "theme": { "id": 34, "name": "Fantasy" },
                                  "type": { "id": 2, "name": "MUD" } } },
        "dates": { "updated": "2026-08-20T14:10:00Z" },
        "status": { "confirmed_online": true, "latest_players": 42 }
      }],
      "meta": { "page": 1, "per_page": 25, "total": 278 },
      "links": { "next": "https://www.mudverse.com/api/v1/games?page=2" }
    }
    """;

    [Fact]
    public async Task AListingBecomesSomethingThatCanBeReadOut()
    {
        using var directory = Reading(OneGame);
        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.TopVoted,
        });

        MudGame game = Assert.Single(page.Games);
        Assert.Equal("Example MUD", game.Name);
        Assert.Equal("mud.example.com", game.Host);
        Assert.Equal(4000, game.Port);
        Assert.Equal(4001, game.TlsPort);
        Assert.Equal("Fantasy", game.Genre);
        Assert.Equal("MUD", game.GameType);
        Assert.Equal(42, game.PlayersOnline);
        Assert.True(game.ConfirmedOnline);
        Assert.Equal(4.5, game.Rating);
        Assert.Equal(8, game.ReviewCount);
        Assert.Equal(125, game.MonthlyVotes);
        Assert.Equal(4, game.Rank);
        Assert.Equal(278, page.Total);
        Assert.True(page.HasMore);

        Assert.Equal("Example MUD. 42 players. Fantasy. rated 4.5 from 8 reviews.", game.Summary);
        Assert.Contains("mud.example.com, port 4000, or port 4001 with TLS.", game.Details);
    }

    [Fact]
    public async Task AListingWithNoAddressIsNotOfferedAsSomethingToConnectTo()
    {
        // A web-only game reaches the list even when mud_client was asked for, and a dead
        // entry is worse than a shorter list.
        using var directory = Reading("""
        { "data": [
            { "id": 1, "name": "Web Only", "connection": {}, "status": {} },
            { "id": 2, "name": "Dialable", "connection": { "host": "a.example.com", "port": 4000 },
              "status": { "latest_players": 3 } }
        ], "meta": { "total": 2 } }
        """);

        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.Newest,
        });

        MudGame game = Assert.Single(page.Games);
        Assert.Equal("Dialable", game.Name);
    }

    [Fact]
    public async Task MostPlayersIsSortedHereBecauseTheDirectoryWillNotSortItThatWay()
    {
        var handler = new Stub("""
        { "data": [
            { "id": 1, "name": "Quiet", "connection": { "host": "a", "port": 1 },
              "status": { "latest_players": 2, "confirmed_online": true } },
            { "id": 2, "name": "Busy", "connection": { "host": "b", "port": 2 },
              "status": { "latest_players": 90, "confirmed_online": true } },
            { "id": 3, "name": "Unknown", "connection": { "host": "c", "port": 3 }, "status": {} }
        ], "meta": { "total": 3 } }
        """);
        using var directory = new MudVerseDirectory("key", http: new HttpClient(handler));

        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.MostPlayers,
        });

        Assert.Equal(["Busy", "Quiet", "Unknown"], page.Games.Select(game => game.Name));
        // No next link, so the sweep stopped after one page rather than asking eight times.
        Assert.Equal(1, handler.Calls);
        Assert.Contains("connection_type=mud_client", handler.LastUrl);
    }

    [Fact]
    public async Task TheSweepIsFetchedOnceAndPagedFromMemory()
    {
        var handler = new Stub("""
        { "data": [
            { "id": 1, "name": "One", "connection": { "host": "a", "port": 1 }, "status": { "latest_players": 9 } },
            { "id": 2, "name": "Two", "connection": { "host": "b", "port": 2 }, "status": { "latest_players": 8 } },
            { "id": 3, "name": "Three", "connection": { "host": "c", "port": 3 }, "status": { "latest_players": 7 } }
        ], "meta": { "total": 3 } }
        """);
        using var directory = new MudVerseDirectory("key", http: new HttpClient(handler));

        var query = new MudDirectoryQuery { Sort = MudDirectorySort.MostPlayers, PerPage = 2 };
        MudDirectoryPage first = await directory.SearchAsync(query);
        MudDirectoryPage second = await directory.SearchAsync(query with { Page = 2 });

        Assert.Equal(["One", "Two"], first.Games.Select(game => game.Name));
        Assert.True(first.HasMore);
        Assert.Equal(["Three"], second.Games.Select(game => game.Name));
        Assert.False(second.HasMore);
        // Arrowing on to the next page must not spend a request on a list already in hand.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AGenreNarrowsTheRequestRatherThanTheAnswer()
    {
        var handler = new Stub("""{ "data": [], "meta": { "total": 0 } }""");
        using var directory = new MudVerseDirectory("key", http: new HttpClient(handler));

        await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.TopVoted,
            ThemeTagId = "34",
            Search = "dragon ball",
        });

        Assert.Contains("sort=top_voted", handler.LastUrl);
        Assert.Contains("theme_tag_id%5B%5D=34", handler.LastUrl);
        Assert.Contains("q=dragon%20ball", handler.LastUrl);
    }

    [Fact]
    public async Task TheTagListIsReadWhateverTheCategoriesAreCalled()
    {
        using var directory = Reading("""
        { "data": [
            { "id": 8, "name": "Theme", "values": [ { "id": 34, "name": "Fantasy" },
                                                    { "id": 12, "name": "Cyberpunk" } ] },
            { "id": 9, "name": "Game Type", "values": [ { "id": 2, "name": "MUD" } ] },
            { "id": 10, "name": "Roleplaying", "values": [ { "id": 5, "name": "Required" } ] },
            { "id": 11, "name": "Codebase", "values": [ { "id": 7, "name": "LPMud" } ] }
        ] }
        """);

        MudDirectoryFilters filters = await directory.FiltersAsync();

        // Alphabetical, because a list that is arrowed through has to be predictable.
        Assert.Equal(["Cyberpunk", "Fantasy"], filters.Themes.Select(tag => tag.Name));
        Assert.Equal("34", filters.Themes.Single(tag => tag.Name == "Fantasy").Id);
        Assert.Equal(["MUD"], filters.GameTypes.Select(tag => tag.Name));
        Assert.Equal(["Required"], filters.Roleplaying.Select(tag => tag.Name));
    }

    [Fact]
    public async Task ARefusedKeySaysWhichThingToGoAndFix()
    {
        var handler = new Stub("""{ "error": { "code": "invalid_api_key" } }""", HttpStatusCode.Unauthorized);
        using var directory = new MudVerseDirectory("wrong", http: new HttpClient(handler));

        MudDirectoryException failure = await Assert.ThrowsAsync<MudDirectoryException>(
            () => directory.SearchAsync(new MudDirectoryQuery { Sort = MudDirectorySort.Newest }));

        Assert.True(failure.IsAuthentication);
        Assert.Contains("did not accept the API key", failure.Message);
    }

    [Fact]
    public void AKeyIsOnlyRequiredWhenTalkingToMudVerseItself()
    {
        using var direct = new MudVerseDirectory();
        Assert.True(direct.NeedsKey);

        using var keyed = new MudVerseDirectory("key");
        Assert.False(keyed.NeedsKey);

        // A service holding the key on everybody's behalf needs nothing from this end.
        using var relayed = new MudVerseDirectory(endpoint: "https://directory.example.com/v1");
        Assert.False(relayed.NeedsKey);
    }

    [Fact]
    public void WhatIsMissingIsLeftOutRatherThanReadAsUnknown()
    {
        var bare = new MudGame { Source = "MUDVerse", SourceId = "1", Name = "Bare MUD" };

        Assert.Equal("Bare MUD. not answering. web only.", bare.Summary);
        Assert.Contains("No telnet address.", bare.Details);
        Assert.DoesNotContain("rated", bare.Summary);
        Assert.False(bare.CanConnect);
    }

    [Fact]
    public async Task AFailureThatMightPassIsTriedAgain()
    {
        // A server having a moment, which is worth exactly one more ask.
        var handler = new Flaky(HttpStatusCode.ServiceUnavailable, failures: 1, OneGame);
        using var directory = new MudVerseDirectory("key", http: new HttpClient(handler));

        var retries = new List<string>();
        directory.Retrying += trouble => retries.Add(trouble);

        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.Newest,
        });

        Assert.Equal("Example MUD", Assert.Single(page.Games).Name);
        Assert.Equal(2, handler.Calls);
        // Said out loud, so a run that is limping says so while it is happening.
        Assert.Single(retries);
    }

    [Fact]
    public async Task ASecondFailureIsNotChasedForever()
    {
        // A request that timed out because MUDVerse cannot serve that offset will time out
        // again in exactly the same way. Two attempts, then the caller is told.
        var handler = new Flaky(HttpStatusCode.ServiceUnavailable, failures: 99, OneGame);
        using var directory = new MudVerseDirectory("key", http: new HttpClient(handler));

        await Assert.ThrowsAsync<MudDirectoryException>(
            () => directory.SearchAsync(new MudDirectoryQuery { Sort = MudDirectorySort.Newest }));

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ARejectedKeyIsNotAskedTwice()
    {
        var handler = new Flaky(HttpStatusCode.Unauthorized, failures: 5, OneGame);
        using var directory = new MudVerseDirectory("wrong", http: new HttpClient(handler));

        await Assert.ThrowsAsync<MudDirectoryException>(
            () => directory.SearchAsync(new MudDirectoryQuery { Sort = MudDirectorySort.Newest }));

        // Retrying a key MUDVerse has already refused just spends the rate limit on the
        // same answer.
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>Fails the first few times with one status, then answers properly.</summary>
    private sealed class Flaky(HttpStatusCode status, int failures, string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            bool failing = Calls <= failures;
            return Task.FromResult(new HttpResponseMessage(failing ? status : HttpStatusCode.OK)
            {
                Content = new StringContent(failing ? "{}" : json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static MudVerseDirectory Reading(string json)
        => new("key", http: new HttpClient(new Stub(json)));

    /// <summary>Answers every request with the same body, and remembers what was asked.</summary>
    private sealed class Stub(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastUrl { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.AbsoluteUri ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
