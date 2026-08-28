namespace BlindTerm.Core.Mud;

/// <summary>
/// How a list of games is ordered.
///
/// <see cref="MostPlayers"/> is deliberately not the same thing as <see cref="TopVoted"/>.
/// Votes measure who campaigned; players measure who is there now, which is the question
/// someone looking for a game to log into is actually asking. Directories mostly publish the
/// first, so BlindTerm works out the second itself.
/// </summary>
public enum MudDirectorySort
{
    MostPlayers,

    /// <summary>
    /// By players averaged over the last thirty days.
    ///
    /// The one ordering that answers "is anybody actually playing this", and it only exists
    /// because MUDStats has been sampling player counts for twenty years. A count taken now
    /// says whether a game is busy at this hour in this timezone.
    /// </summary>
    BusiestAverage,

    /// <summary>By the busiest that month got.</summary>
    HighestPeak,

    TopVoted,
    MostReviewed,
    RecentlyUpdated,
    RecentlyOnline,
    Newest,

    /// <summary>By the year it opened, earliest first. Some of these predate the web.</summary>
    Oldest,
}

/// <summary>
/// One request for part of a directory.
///
/// Tag identifiers are the directory's own, taken from <see cref="MudDirectoryFilters"/> at
/// run time rather than written down here: a taxonomy that is fetched cannot go stale, and a
/// list of genres compiled into BlindTerm would.
/// </summary>
public sealed record MudDirectoryQuery
{
    public MudDirectorySort Sort { get; init; } = MudDirectorySort.MostPlayers;

    /// <summary>Free text. Empty means browse rather than search.</summary>
    public string Search { get; init; } = string.Empty;

    public string? ThemeTagId { get; init; }
    public string? TypeTagId { get; init; }
    public string? RoleplayingTagId { get; init; }

    /// <summary>
    /// Whether to leave out games that have no host and port.
    ///
    /// On, always, from BlindTerm: it is a terminal. A listing that can only be played on a
    /// web page is not a thing this program can open, and offering it would be a dead end.
    /// </summary>
    public bool OnlyConnectable { get; init; } = true;

    /// <summary>One-based.</summary>
    public int Page { get; init; } = 1;

    public int PerPage { get; init; } = 25;

    /// <summary>
    /// What identifies these results apart from which page of them is wanted. Two queries with
    /// the same filters can share one fetch of the directory.
    /// </summary>
    public string FilterKey => string.Join('|',
        Search.Trim().ToLowerInvariant(), ThemeTagId, TypeTagId, RoleplayingTagId, OnlyConnectable);
}

/// <summary>One page of results, and enough to know whether to offer another.</summary>
public sealed record MudDirectoryPage
{
    public required IReadOnlyList<MudGame> Games { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 25;

    /// <summary>How many match altogether, when the source says. Zero when it does not.</summary>
    public int Total { get; init; }

    public bool HasMore { get; init; }

    public static MudDirectoryPage Empty { get; } = new() { Games = [] };
}
