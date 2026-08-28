using System.Text;

namespace BlindTerm.Core.Mud;

/// <summary>
/// Putting two directories' idea of the same game together.
///
/// MUDVerse and MUDStats are good at different halves and neither is good at both. MUDVerse
/// knows what a game <em>is</em> -- its address, its encrypted port, its blurb, its genre,
/// what players thought of it. MUDStats has been sampling player counts since before most of
/// those games had websites and knows how busy they actually <em>are</em>. Merged, an entry
/// answers both "what is this" and "is anybody there", which neither source answers alone.
///
/// The join is on the name, because that is the only field both publish and mean the same
/// thing by. That is imperfect, so it is done conservatively: names are compared with the
/// punctuation and the articles taken out, and an ambiguous name -- two games that normalise
/// to the same thing -- is left unmatched rather than guessed at. A missing statistic is a
/// line the browser does not read out; a wrong one is a lie about a different game.
/// </summary>
public static class MudMerge
{
    /// <summary>
    /// Folds several directories' listings of the same games into one set, keyed by name.
    ///
    /// The sources are passed richest first, and each one after fills in only what is still
    /// blank. Nobody overwrites anybody: MUDVerse has the genre and the ratings, Grapevine
    /// has the encrypted ports and the taglines, The Mud Connector has the addresses and a
    /// connect status it checked while building the page. A game listed in all three ends up
    /// with all three halves, and one listed in only the last still ends up connectable.
    /// </summary>
    public static IReadOnlyList<MudGame> Describe(params IEnumerable<MudGame>[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var byName = new Dictionary<string, MudGame>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (IEnumerable<MudGame> source in sources)
        {
            foreach (MudGame game in source)
            {
                string key = Key(game.Name);
                if (key.Length == 0) continue;
                if (byName.TryGetValue(key, out MudGame? already))
                {
                    byName[key] = Fill(already, game);
                }
                else
                {
                    byName[key] = game;
                    order.Add(key);
                }
            }
        }
        return [.. order.Select(key => byName[key])];
    }

    /// <summary>
    /// The first listing, with the second's answers used only where the first had none.
    ///
    /// An address is the exception worth spelling out: a listing that cannot be connected to
    /// takes the other's host and port outright, because a genre and a rating attached to no
    /// address is a row in a list that cannot be chosen.
    /// </summary>
    public static MudGame Fill(MudGame first, MudGame second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return first with
        {
            Host = first.CanConnect ? first.Host : second.Host,
            Port = first.CanConnect ? first.Port : second.Port,
            TlsPort = first.TlsPort ?? (first.CanConnect || second.TlsPort is null
                ? first.TlsPort ?? second.TlsPort
                : second.TlsPort),
            Intro = first.Intro.Length > 0 ? first.Intro : second.Intro,
            Genre = first.Genre.Length > 0 ? first.Genre : second.Genre,
            GameType = first.GameType.Length > 0 ? first.GameType : second.GameType,
            Roleplaying = first.Roleplaying.Length > 0 ? first.Roleplaying : second.Roleplaying,
            Codebase = first.Codebase.Length > 0 ? first.Codebase : second.Codebase,
            Website = first.Website.Length > 0 ? first.Website : second.Website,
            ListingUrl = first.ListingUrl.Length > 0 ? first.ListingUrl : second.ListingUrl,
            PlayersOnline = first.PlayersOnline ?? second.PlayersOnline,
            Rating = first.Rating ?? second.Rating,
            ReviewCount = first.ReviewCount > 0 ? first.ReviewCount : second.ReviewCount,
            MonthlyVotes = first.MonthlyVotes > 0 ? first.MonthlyVotes : second.MonthlyVotes,
            Rank = first.Rank ?? second.Rank,
            YearOpened = first.YearOpened ?? second.YearOpened,
            // Either directory having reached the host is enough to say somebody reached it.
            ConfirmedOnline = first.ConfirmedOnline || second.ConfirmedOnline,
            Availability = first.Availability != MudAvailability.Unknown
                ? first.Availability
                : second.Availability,
            Updated = first.Updated ?? second.Updated,
            Listed = first.Listed ?? second.Listed,
            LastSeen = first.LastSeen ?? second.LastSeen,
        };
    }

    /// <summary>
    /// Copies MUDStats' activity figures onto the games that MUDVerse described.
    ///
    /// Returns the merged games and the MUDStats worlds that matched nothing, which the
    /// caller may then go and find addresses for.
    /// </summary>
    public static (IReadOnlyList<MudGame> Games, IReadOnlyList<MudGame> Unmatched) Combine(
        IEnumerable<MudGame> described, IEnumerable<MudGame> measured)
    {
        ArgumentNullException.ThrowIfNull(described);
        ArgumentNullException.ThrowIfNull(measured);

        List<MudGame> statistics = [.. measured];
        Dictionary<string, MudGame?> byName = Index(statistics);

        var merged = new List<MudGame>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (MudGame game in described)
        {
            string key = Key(game.Name);
            if (key.Length > 0 && byName.TryGetValue(key, out MudGame? match) && match is not null)
            {
                used.Add(key);
                merged.Add(Enrich(game, match));
            }
            else
            {
                merged.Add(game);
            }
        }

        // Worlds nobody described. An ambiguous name was recorded as null above and is not
        // offered here either: it cannot be matched and it cannot be trusted on its own.
        List<MudGame> unmatched =
        [
            .. statistics.Where(world =>
            {
                string key = Key(world.Name);
                return key.Length > 0 && !used.Contains(key)
                       && byName.TryGetValue(key, out MudGame? only) && only is not null;
            })
        ];

        return (merged, unmatched);
    }

    /// <summary>
    /// The described game, wearing the measured one's figures.
    ///
    /// MUDVerse's own live count is kept when it has one, because it was taken from the game
    /// itself; MUDStats fills that in only when MUDVerse never saw a number. Everything under
    /// "how busy has it been" is MUDStats' alone -- nothing else publishes it.
    /// </summary>
    public static MudGame Enrich(MudGame described, MudGame measured)
    {
        ArgumentNullException.ThrowIfNull(described);
        ArgumentNullException.ThrowIfNull(measured);

        return described with
        {
            PlayersOnline = described.PlayersOnline ?? measured.PlayersOnline,
            PlayersEstimated = described.PlayersOnline is null && measured.PlayersEstimated,
            AveragePlayers = measured.AveragePlayers,
            PeakPlayers = measured.PeakPlayers,
            MinimumPlayers = measured.MinimumPlayers,
            TrendPercent = measured.TrendPercent,
            YearOpened = described.YearOpened ?? measured.YearOpened,
            DatabaseSize = described.DatabaseSize ?? measured.DatabaseSize,
            Codebase = described.Codebase.Length > 0 ? described.Codebase : measured.Codebase,
            PayToPlay = described.PayToPlay || measured.PayToPlay,
            // Availability comes from whichever of them last managed to reach the host. A
            // directory saying "up" outranks one saying nothing.
            Availability = measured.Availability != MudAvailability.Unknown
                ? measured.Availability
                : described.Availability,
            ConfirmedOnline = described.ConfirmedOnline || measured.ConfirmedOnline,
            GameType = described.GameType.Length > 0 ? described.GameType : measured.GameType,
            StatisticsSource = measured.StatisticsSource.Length > 0 ? measured.StatisticsSource : measured.Source,
            StatisticsUrl = measured.StatisticsUrl,
        };
    }

    /// <summary>
    /// Names to worlds, with a null recorded wherever two worlds share a name.
    ///
    /// A null is the point: it says "this name is known and cannot be resolved", which stops
    /// a later lookup matching the first of two games that happen to be called the same
    /// thing. Silently taking one of them would attach one game's population to another's.
    /// </summary>
    private static Dictionary<string, MudGame?> Index(IEnumerable<MudGame> worlds)
    {
        var index = new Dictionary<string, MudGame?>(StringComparer.Ordinal);
        foreach (MudGame world in worlds)
        {
            string key = Key(world.Name);
            if (key.Length == 0) continue;
            if (index.TryGetValue(key, out MudGame? existing))
            {
                // Two worlds, one name. Neither can be matched on it after this.
                if (existing is not null && existing.SourceId != world.SourceId) index[key] = null;
                continue;
            }
            index[key] = world;
        }
        return index;
    }

    /// <summary>
    /// A name reduced to what two directories are likely to agree on.
    ///
    /// Letters and digits only, folded to lower case, with a leading article dropped: "The
    /// Land of Drogon" and "Land of Drogon", "Gemstone IV" and "GemStone IV", "Alter Aeon"
    /// and "Alter-Aeon" all come out the same. Anything more clever than this starts matching
    /// games that merely sound alike, which is the failure worth avoiding.
    /// </summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var letters = new StringBuilder(name.Length);
        foreach (char character in name)
            if (char.IsLetterOrDigit(character)) letters.Append(char.ToLowerInvariant(character));

        string key = letters.ToString();
        if (key.StartsWith("the", StringComparison.Ordinal) && key.Length > 3) key = key[3..];
        return key;
    }
}
