using System.Net;
using System.Text;
using BlindTerm.Core.Mud;

namespace BlindTerm.Tests;

/// <summary>
/// The list BlindTerm publishes, and reading it without a key.
///
/// This is the path everybody takes, so it is the path that has to work when the network is
/// down, when the file is a version too new, and when somebody asks for a genre by a tag
/// identifier that only means anything inside the file itself.
/// </summary>
public class MudFeedTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "blindterm-feed-" + Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_folder, "mud-directory.json");

    [Fact]
    public void AFeedSurvivesBeingWrittenDownAndReadBack()
    {
        var feed = new MudFeed
        {
            Generated = new DateTimeOffset(2026, 8, 28, 18, 0, 0, TimeSpan.Zero),
            Themes = [new MudTag("34", "Fantasy")],
            Games = [Game("Alter Aeon", players: 41, genre: "Fantasy", tls: 3011)],
        };

        MudFeed read = MudFeed.FromJson(feed.ToJson());

        MudGame game = Assert.Single(read.Games);
        Assert.Equal("Alter Aeon", game.Name);
        Assert.Equal(41, game.PlayersOnline);
        Assert.Equal(3011, game.TlsPort);
        Assert.Equal("Fantasy", game.Genre);
        Assert.Equal(feed.Generated, read.Generated);
        Assert.Equal("Fantasy", Assert.Single(read.Themes).Name);
    }

    [Fact]
    public void TheSentencesReadAloudAreNotWrittenIntoTheFile()
    {
        string json = new MudFeed { Games = [Game("Alter Aeon", players: 41)] }.ToJson();

        // Everybody who opens the browser downloads this. The summary and the details are
        // built from the fields beside them; shipping them too would be paying twice.
        Assert.DoesNotContain("Summary", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Details", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CanConnect", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileFromALaterBlindTermIsRefusedRatherThanHalfRead()
    {
        string json = new MudFeed { Version = MudFeed.CurrentVersion + 1 }.ToJson();

        MudDirectoryException failure = Assert.Throws<MudDirectoryException>(() => MudFeed.FromJson(json));
        Assert.Contains("newer format", failure.Message);
    }

    [Fact]
    public async Task EverySortIsAnsweredFromTheOneDownload()
    {
        var handler = new Stub(Sample().ToJson());
        using var directory = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(handler));

        Assert.Equal(["Busy", "Quiet", "Ancient"],
            await Names(directory, MudDirectorySort.MostPlayers));
        Assert.Equal(["Quiet", "Busy", "Ancient"],
            await Names(directory, MudDirectorySort.TopVoted));
        Assert.Equal(["Ancient", "Busy", "Quiet"],
            await Names(directory, MudDirectorySort.MostReviewed));
        Assert.Equal(["Ancient", "Busy", "Quiet"],
            await Names(directory, MudDirectorySort.Newest));

        // Six sorts, one download. That is the whole point of shipping the list rather than
        // proxying the queries.
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AGenreIsMatchedThroughTheFeedsOwnTagIdentifiers()
    {
        var handler = new Stub(Sample().ToJson());
        using var directory = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(handler));

        MudDirectoryFilters filters = await directory.FiltersAsync();
        string fantasy = filters.Themes.Single(tag => tag.Name == "Fantasy").Id;

        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery
        {
            Sort = MudDirectorySort.MostPlayers,
            ThemeTagId = fantasy,
        });

        Assert.Equal(["Busy", "Ancient"], page.Games.Select(game => game.Name));
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task EveryTypedWordHasToMatch()
    {
        var handler = new Stub(Sample().ToJson());
        using var directory = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(handler));

        MudDirectoryPage both = await directory.SearchAsync(new MudDirectoryQuery { Search = "ancient dragons" });
        MudDirectoryPage neither = await directory.SearchAsync(new MudDirectoryQuery { Search = "ancient spaceships" });

        Assert.Equal(["Ancient"], both.Games.Select(game => game.Name));
        Assert.Empty(neither.Games);
    }

    [Fact]
    public async Task ACopyOnDiskIsUsedWhenTheNetworkIsGone()
    {
        // First run: fetched and kept.
        var working = new Stub(Sample().ToJson());
        using (var first = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(working)))
        {
            await first.SearchAsync(new MudDirectoryQuery());
        }
        Assert.True(File.Exists(CachePath));

        // Second run, six hours later as far as the freshness rule is concerned, with nothing
        // answering. An old list beats no list: this is what browsing on a train looks like.
        Stale();
        var offline = new Stub("", HttpStatusCode.ServiceUnavailable);
        using var second = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(offline));

        MudDirectoryPage page = await second.SearchAsync(new MudDirectoryQuery());

        Assert.Equal(3, page.Total);
        Assert.Equal(1, offline.Calls);
    }

    [Fact]
    public async Task AFreshCopyOnDiskIsNotFetchedAgainAtAll()
    {
        var working = new Stub(Sample().ToJson());
        using (var first = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(working)))
        {
            await first.SearchAsync(new MudDirectoryQuery());
        }

        var second = new Stub(Sample().ToJson());
        using var reopened = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(second));
        await reopened.SearchAsync(new MudDirectoryQuery());

        // Opening the browser again inside the window costs nothing.
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task AListThatHasNeverBeenPublishedSaysWhatToDoInstead()
    {
        var missing = new Stub("Not Found", HttpStatusCode.NotFound);
        using var directory = new MudFeedDirectory("https://example.com/list.json", CachePath,
            new HttpClient(missing));

        MudDirectoryException failure = await Assert.ThrowsAsync<MudDirectoryException>(
            () => directory.SearchAsync(new MudDirectoryQuery()));

        // Flagged as something a key would fix, so the window offers the key dialog rather
        // than leaving somebody at a dead end.
        Assert.True(failure.IsAuthentication);
        Assert.Contains("MUDVerse key", failure.Message);
    }

    [Fact]
    public void NothingIsAskedOfSomebodyWhoHasConfiguredNothing()
    {
        using IMudDirectory plain = MudDirectories.Open(null, null);
        Assert.True(MudDirectories.IsPublishedList(plain));

        // A key is a deliberate act, and only then is MUDVerse talked to directly.
        using IMudDirectory keyed = MudDirectories.Open("mv_live_something", null);
        Assert.False(MudDirectories.IsPublishedList(keyed));
    }

    private static async Task<string[]> Names(IMudDirectory directory, MudDirectorySort sort)
    {
        MudDirectoryPage page = await directory.SearchAsync(new MudDirectoryQuery { Sort = sort });
        return [.. page.Games.Select(game => game.Name)];
    }

    private static MudFeed Sample() => new()
    {
        Generated = DateTimeOffset.UtcNow,
        Themes = [new MudTag("34", "Fantasy"), new MudTag("12", "Cyberpunk")],
        Games =
        [
            Game("Busy", players: 90, genre: "Fantasy", rank: 2, votes: 40, reviews: 3,
                 listed: new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Game("Quiet", players: 4, genre: "Cyberpunk", rank: 1, votes: 200, reviews: 1,
                 listed: new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Game("Ancient", players: null, genre: "Fantasy", rank: null, votes: 0, reviews: 12,
                 listed: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                 intro: "Ancient dragons and older grudges."),
        ],
    };

    private static MudGame Game(string name, int? players = null, string genre = "", int? tls = null,
        int? rank = null, int votes = 0, int reviews = 0, DateTimeOffset? listed = null,
        string intro = "") => new()
        {
            Source = "MUDVerse",
            SourceId = name,
            Name = name,
            Intro = intro,
            Host = name.ToLowerInvariant() + ".example.com",
            Port = 4000,
            TlsPort = tls,
            Genre = genre,
            PlayersOnline = players,
            ConfirmedOnline = players is not null,
            Rank = rank,
            MonthlyVotes = votes,
            ReviewCount = reviews,
            Listed = listed,
        };

    /// <summary>Ages the cache past the point where it is used without asking.</summary>
    private void Stale()
    {
        string json = File.ReadAllText(CachePath);
        MudFeed feed = MudFeed.FromJson(json);
        feed.Generated = DateTimeOffset.UtcNow.AddDays(-2);
        File.WriteAllText(CachePath, feed.ToJson());
    }

    public void Dispose()
    {
        try { if (System.IO.Directory.Exists(_folder)) System.IO.Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class Stub(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
