using BlindTerm.Core.Mud;

namespace BlindTerm.Tests;

/// <summary>
/// Reading MUDStats' browse table, and putting its figures onto MUDVerse's listings.
///
/// The rows below are real ones, kept verbatim from mudstats.com/WorldList. That matters
/// more here than anywhere else in this codebase: MUDStats publishes no API and makes no
/// promises, so this is a scrape, and the only way a scrape stays honest is if the shape it
/// expects is written down where a change to it fails loudly.
///
/// The cases are chosen to be the awkward ones -- an estimated count, a dead world, a game
/// that charges, a database size that is the word "Unknown", a trend that is missing, a
/// downward trend -- because the ordinary row was never going to be the one that broke.
/// </summary>
public class MudStatsTests
{
    /// <summary>A busy MUSH: a real count, a full set of figures, an upward trend, no year.</summary>
    private const string Busy = """
    ["<span class=\"name\"><a href=\"/World/PenultimateDestination\">Penultimate Destination</a></span>",
     "<span class=\"genre\"><a href=\"/Genre/Adult\">Adult</a></span>",
     "<span class=\"status\"><strong class=green>UP</strong></span>",
     "<span class=\"servertype\"><a href=\"/ServerType/MUSH\">MUSH</a> (PennMUSH 1.8.8p0)</span>",
     "<span class=\"playersconnected\">648</span>",
     "<span class=\"max\">773</span>","<span class=\"min\">536</span>","<span class=\"avg\">640</span>",
     "<span class=\"monthtrend\"><div class=\"green\">▲1&#37;</div></span>",
     "<span class=\"dbsize\">12962</span>","<span class=\"created\"></span>"]
    """;

    /// <summary>Pay to play, an estimated count, no database size, and older than the web.</summary>
    private const string Estimated = """
    ["<span class=\"name\"><a href=\"/World/GemstoneIV\">Gemstone IV</a></span>",
     "<span class=\"genre\"><a href=\"/Genre/Fantasy\">Fantasy</a> (Pay-To-Play)</span>",
     "<span class=\"status\"><strong class=green>UP</strong></span>",
     "<span class=\"servertype\"><a href=\"/ServerType/Other\">Other</a> (MUD)</span>",
     "<span class=\"playersconnected\"><span class=\"estimate\">~</span>628</span>",
     "<span class=\"max\">774</span>","<span class=\"min\">222</span>","<span class=\"avg\">511</span>",
     "<span class=\"monthtrend\"></span>",
     "<span class=\"dbsize\">Unknown</span>","<span class=\"created\">1989</span>"]
    """;

    /// <summary>Gone for twelve years. The count is the last one anybody saw.</summary>
    private const string Dead = """
    ["<span class=\"name\"><a href=\"/World/TheLandofDrogon\">The Land of Drogon</a></span>",
     "<span class=\"genre\"><a href=\"/Genre/Fantasy\">Fantasy</a></span>",
     "<span class=\"status\"><div class=red>DEAD (12 years)</div></span>",
     "<span class=\"servertype\"><a href=\"/ServerType/Other\">Other</a> (UberMUD)</span>",
     "<span class=\"playersconnected\"><span class=\"down\">1</span></span>",
     "<span class=\"max\">1</span>","<span class=\"min\">1</span>","<span class=\"avg\">1</span>",
     "<span class=\"monthtrend\"></span>",
     "<span class=\"dbsize\">Unknown</span>","<span class=\"created\">1992</span>"]
    """;

    /// <summary>Down right now rather than gone, and losing players.</summary>
    private const string Falling = """
    ["<span class=\"name\"><a href=\"/World/TheLandsofDraknor\">The Lands of Draknor</a></span>",
     "<span class=\"genre\"><a href=\"/Genre/WheelofTime\">Wheel of Time</a></span>",
     "<span class=\"status\"><div class=red>DOWN</div></span>",
     "<span class=\"servertype\"><a href=\"/ServerType/Other\">Other</a> (Diku)</span>",
     "<span class=\"playersconnected\"><span class=\"down\">3</span></span>",
     "<span class=\"max\">4</span>","<span class=\"min\">0</span>","<span class=\"avg\">1</span>",
     "<span class=\"monthtrend\"><div class=\"red\">▼17&#37;</div></span>",
     "<span class=\"dbsize\">31629</span>","<span class=\"created\">2001</span>"]
    """;

    private static string Table(params string[] rows)
        => $$"""{"sEcho":1,"iTotalRecords":4,"iTotalDisplayRecords":4,"aaData":[{{string.Join(",", rows)}}]}""";

    [Fact]
    public void AnOrdinaryRowGivesUpEverythingItHas()
    {
        MudGame world = Assert.Single(MudStatsDirectory.Parse(Table(Busy)));

        Assert.Equal("Penultimate Destination", world.Name);
        Assert.Equal("PenultimateDestination", world.SourceId);
        Assert.Equal("Adult", world.Genre);
        Assert.Equal("MUSH", world.GameType);
        Assert.Equal("PennMUSH 1.8.8p0", world.Codebase);
        Assert.Equal(MudAvailability.Online, world.Availability);
        Assert.Equal(648, world.PlayersOnline);
        Assert.False(world.PlayersEstimated);
        Assert.Equal(773, world.PeakPlayers);
        Assert.Equal(536, world.MinimumPlayers);
        Assert.Equal(640, world.AveragePlayers);
        Assert.Equal(1, world.TrendPercent);
        Assert.Equal(12962, world.DatabaseSize);
        Assert.Null(world.YearOpened);
        Assert.Equal("https://mudstats.com/World/PenultimateDestination", world.StatisticsUrl);
    }

    [Fact]
    public void AnEstimatedCountIsMarkedAsOne()
    {
        MudGame world = Assert.Single(MudStatsDirectory.Parse(Table(Estimated)));

        Assert.Equal(628, world.PlayersOnline);
        // "About six hundred" and "six hundred" are different claims, and the second one is
        // not MUDStats'. It says so with a tilde, and so does BlindTerm.
        Assert.True(world.PlayersEstimated);
        Assert.Contains("about 628 players", world.Summary);
        Assert.True(world.PayToPlay);
        Assert.Contains("pay to play", world.Summary);
        Assert.Equal(1989, world.YearOpened);
        // "Unknown" is not a number, and must not be read as one.
        Assert.Null(world.DatabaseSize);
        // "(Pay-To-Play)" sits where a codebase would, and is not a codebase.
        Assert.Equal("MUD", world.Codebase);
    }

    [Fact]
    public void DownAndDeadAreDifferentThings()
    {
        MudGame gone = Assert.Single(MudStatsDirectory.Parse(Table(Dead)));
        MudGame down = Assert.Single(MudStatsDirectory.Parse(Table(Falling)));

        Assert.Equal(MudAvailability.Dead, gone.Availability);
        Assert.False(gone.ConfirmedOnline);
        Assert.Contains("years ago", gone.Details);

        Assert.Equal(MudAvailability.Offline, down.Availability);
        Assert.Contains("down just now", down.Summary);
        // A falling trend is a negative number, not a number with an arrow in front of it.
        Assert.Equal(-17, down.TrendPercent);
        Assert.Contains("down 17 percent this month", down.Details);
    }

    [Fact]
    public void TheGenreListIsBuiltFromGamesThatExist()
    {
        IReadOnlyList<MudGame> worlds = MudStatsDirectory.Parse(Table(Busy, Estimated, Dead, Falling));

        Assert.Equal(4, worlds.Count);
        Assert.Equal(["Adult", "Fantasy", "Wheel of Time"],
            worlds.Select(world => world.Genre).Distinct().Order().ToArray());
    }

    [Fact]
    public void ARowWithNoNameIsSkippedRatherThanThrown()
    {
        // The shape of a scrape going wrong: something changed, and this row is now junk.
        const string broken = """["<span class=\"name\">no link at all</span>","","","","","","","","","",""]""";
        IReadOnlyList<MudGame> worlds = MudStatsDirectory.Parse(Table(broken, Busy));

        Assert.Equal("Penultimate Destination", Assert.Single(worlds).Name);
    }

    [Fact]
    public void SomethingThatIsNotTheTableIsReportedRatherThanReturnedEmpty()
    {
        // An empty list and a changed endpoint look identical to a caller. They must not.
        MudDirectoryException failure = Assert.Throws<MudDirectoryException>(
            () => MudStatsDirectory.Parse("""{"error":"nope"}"""));
        Assert.Contains("no world list", failure.Message);
    }

    [Fact]
    public void TheFiguresLandOnTheListingThatHasTheAddress()
    {
        MudGame described = new()
        {
            Source = "MUDVerse",
            SourceId = "79",
            Name = "The Lands of Draknor",
            Host = "draknor.example.com",
            Port = 4000,
            Genre = "Fantasy",
            Rating = 4.5,
            ReviewCount = 8,
        };
        IReadOnlyList<MudGame> measured = MudStatsDirectory.Parse(Table(Falling));

        (IReadOnlyList<MudGame> merged, IReadOnlyList<MudGame> unmatched) =
            MudMerge.Combine([described], measured);

        MudGame game = Assert.Single(merged);
        Assert.Empty(unmatched);

        // MUDVerse's half is untouched.
        Assert.Equal("draknor.example.com", game.Host);
        Assert.Equal(4.5, game.Rating);
        Assert.Equal("Fantasy", game.Genre);
        // MUDStats' half is the part nobody else publishes.
        Assert.Equal(1, game.AveragePlayers);
        Assert.Equal(4, game.PeakPlayers);
        Assert.Equal(-17, game.TrendPercent);
        Assert.Equal(2001, game.YearOpened);
        Assert.Equal("Diku", game.Codebase);
        Assert.Equal("MUDStats", game.StatisticsSource);
    }

    [Fact]
    public void ALiveCountFromTheGameItselfIsNotOverwrittenByAnEstimate()
    {
        MudGame described = new()
        {
            Source = "MUDVerse",
            SourceId = "1",
            Name = "Gemstone IV",
            Host = "gs4.example.com",
            Port = 4000,
            PlayersOnline = 12,
        };

        MudGame game = Assert.Single(MudMerge.Combine([described], MudStatsDirectory.Parse(Table(Estimated))).Games);

        // MUDVerse asked the game; MUDStats guessed. The game wins.
        Assert.Equal(12, game.PlayersOnline);
        Assert.False(game.PlayersEstimated);
        // The month's history is still worth having, and only MUDStats has it.
        Assert.Equal(511, game.AveragePlayers);
    }

    [Theory]
    [InlineData("The Land of Drogon", "Land of Drogon")]
    [InlineData("Alter Aeon", "Alter-Aeon")]
    [InlineData("GemStone IV", "Gemstone IV")]
    [InlineData("Threshold RPG", "threshold rpg")]
    public void NamesAreMatchedPastPunctuationAndArticles(string one, string other)
        => Assert.Equal(MudMerge.Key(one), MudMerge.Key(other));

    [Theory]
    [InlineData("Achaea", "Aardwolf")]
    [InlineData("Dark Wizardry", "Dark Wizardy")]
    public void NamesThatMerelyLookAlikeAreNotMatched(string one, string other)
        => Assert.NotEqual(MudMerge.Key(one), MudMerge.Key(other));

    [Fact]
    public void TwoWorldsWithOneNameAreLeftAloneRatherThanGuessedAt()
    {
        const string first = """
        ["<span class=\"name\"><a href=\"/World/AvalonA\">Avalon</a></span>",
         "<span class=\"genre\"><a href=\"/Genre/Fantasy\">Fantasy</a></span>",
         "<span class=\"status\"><strong class=green>UP</strong></span>","<span></span>",
         "<span class=\"playersconnected\">5</span>","<span class=\"max\">9</span>",
         "<span class=\"min\">1</span>","<span class=\"avg\">4</span>","<span></span>",
         "<span></span>","<span></span>"]
        """;
        const string second = """
        ["<span class=\"name\"><a href=\"/World/AvalonB\">Avalon</a></span>",
         "<span class=\"genre\"><a href=\"/Genre/Fantasy\">Fantasy</a></span>",
         "<span class=\"status\"><strong class=green>UP</strong></span>","<span></span>",
         "<span class=\"playersconnected\">300</span>","<span class=\"max\">400</span>",
         "<span class=\"min\">200</span>","<span class=\"avg\">350</span>","<span></span>",
         "<span></span>","<span></span>"]
        """;

        MudGame described = new()
        {
            Source = "MUDVerse", SourceId = "1", Name = "Avalon",
            Host = "avalon.example.com", Port = 4000,
        };

        MudGame game = Assert.Single(
            MudMerge.Combine([described], MudStatsDirectory.Parse(Table(first, second))).Games);

        // Attaching one Avalon's population to the other would be a lie about a real game,
        // and there is no way to tell which is which from a name. So: no figures.
        Assert.Null(game.AveragePlayers);
        Assert.Equal("avalon.example.com", game.Host);
    }

    [Fact]
    public void AWorldNobodyElseListsIsHandedBackToBeChasedUp()
    {
        MudGame described = new()
        {
            Source = "MUDVerse", SourceId = "1", Name = "Penultimate Destination",
            Host = "penultimatemush.com", Port = 9500,
        };

        (_, IReadOnlyList<MudGame> unmatched) =
            MudMerge.Combine([described], MudStatsDirectory.Parse(Table(Busy, Estimated)));

        // Gemstone IV is on MUDStats and not in the described list, so it is a candidate for
        // having its address looked up and being added.
        Assert.Equal("Gemstone IV", Assert.Single(unmatched).Name);
    }

    [Fact]
    public void TheBusiestOrderingIsTheOneMudStatsMakesPossible()
    {
        IReadOnlyList<MudGame> worlds = MudStatsDirectory.Parse(Table(Falling, Estimated, Busy, Dead));

        Assert.Equal(["Penultimate Destination", "Gemstone IV", "The Lands of Draknor", "The Land of Drogon"],
            MudSorting.Apply(worlds, MudDirectorySort.BusiestAverage).Select(world => world.Name).ToArray());

        // Oldest first, and a world with no year recorded is not thereby ancient.
        Assert.Equal(["Gemstone IV", "The Land of Drogon", "The Lands of Draknor", "Penultimate Destination"],
            MudSorting.Apply(worlds, MudDirectorySort.Oldest).Select(world => world.Name).ToArray());
    }
}
