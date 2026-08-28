namespace BlindTerm.Core.Mud;

/// <summary>
/// Putting a list of games in an order, and cutting a page out of it.
///
/// Shared, because every source that hands over its whole list -- the one BlindTerm publishes
/// and MUDStats' own table -- has to answer the same six or nine orderings from the same
/// data, and two copies of "what does most players mean when nobody counted" would drift.
/// </summary>
public static class MudSorting
{
    public static IEnumerable<MudGame> Apply(IEnumerable<MudGame> games, MudDirectorySort sort)
    {
        ArgumentNullException.ThrowIfNull(games);
        return sort switch
        {
            // A game nobody has counted is not a game with nobody on it, so it sorts below
            // zero rather than alongside it.
            MudDirectorySort.MostPlayers => games
                .OrderByDescending(game => game.PlayersOnline ?? -1)
                .ThenByDescending(game => game.ConfirmedOnline)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            // The honest answer to "is anybody playing this". A count taken now says whether
            // a game is busy at this hour in this timezone; a month's average says whether it
            // is a game with people in it.
            MudDirectorySort.BusiestAverage => games
                .OrderByDescending(game => game.AveragePlayers ?? -1)
                .ThenByDescending(game => game.PlayersOnline ?? -1)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            MudDirectorySort.HighestPeak => games
                .OrderByDescending(game => game.PeakPlayers ?? -1)
                .ThenByDescending(game => game.AveragePlayers ?? -1)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            // Rank 1 is the top of the month's list; anything unranked follows on its votes.
            MudDirectorySort.TopVoted => games
                .OrderBy(game => game.Rank ?? int.MaxValue)
                .ThenByDescending(game => game.MonthlyVotes)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            MudDirectorySort.MostReviewed => games
                .OrderByDescending(game => game.ReviewCount)
                .ThenByDescending(game => game.Rating ?? 0)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            MudDirectorySort.RecentlyOnline => games
                .OrderByDescending(game => game.ConfirmedOnline)
                .ThenByDescending(game => game.LastSeen ?? DateTimeOffset.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            MudDirectorySort.Newest => games
                .OrderByDescending(game => game.Listed ?? DateTimeOffset.MinValue)
                .ThenByDescending(game => game.YearOpened ?? 0)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            // A year of zero would sort the unknowns to the top of "oldest first", which is
            // the wrong end: not knowing when something opened is not the same as it being
            // ancient.
            MudDirectorySort.Oldest => games
                .OrderBy(game => game.YearOpened ?? int.MaxValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),

            _ => games
                .OrderByDescending(game => game.Updated ?? DateTimeOffset.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),
        };
    }

    /// <summary>One page out of a list already in hand.</summary>
    public static MudDirectoryPage Page(IReadOnlyList<MudGame> games, int page, int perPage)
    {
        ArgumentNullException.ThrowIfNull(games);
        perPage = Math.Max(1, perPage);
        page = Math.Max(1, page);

        int start = (page - 1) * perPage;
        if (start >= games.Count)
            return new MudDirectoryPage { Games = [], Page = page, PerPage = perPage, Total = games.Count };

        int length = Math.Min(perPage, games.Count - start);
        return new MudDirectoryPage
        {
            Games = [.. games.Skip(start).Take(length)],
            Page = page,
            PerPage = perPage,
            Total = games.Count,
            HasMore = start + length < games.Count,
        };
    }
}
