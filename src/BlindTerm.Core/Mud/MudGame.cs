using System.Text.Json.Serialization;

namespace BlindTerm.Core.Mud;

/// <summary>What a directory last knew about whether the host was answering.</summary>
public enum MudAvailability
{
    /// <summary>Nobody said. Not the same as "no".</summary>
    Unknown,

    Online,

    /// <summary>Not answering now, but recently enough that it is expected back.</summary>
    Offline,

    /// <summary>Not answering for so long that it is not coming back.</summary>
    Dead,
}

/// <summary>
/// One game in a MUD directory, in BlindTerm's own words rather than any one directory's.
///
/// Every directory names these things differently and half of them are missing from any given
/// listing, so the shape here is what BlindTerm needs to connect and what a person needs to
/// choose. Anything a source does not say is left empty rather than guessed, and the sentences
/// below leave out what is empty instead of reading "unknown" over and over.
///
/// Two sources fill this in and they are good at different halves. MUDVerse knows what a game
/// is -- its address, its blurb, its genre, what players think of it. MUDStats has watched
/// them for twenty years and knows how busy they actually are, which is the half nobody else
/// publishes. A merged entry carries both.
/// </summary>
public sealed record MudGame
{
    /// <summary>Which directory this came from, so two sources can be told apart in a list.</summary>
    public required string Source { get; init; }

    /// <summary>That directory's own identifier, for fetching the rest of it later.</summary>
    public required string SourceId { get; init; }

    public required string Name { get; init; }

    /// <summary>The short blurb a directory prints under the name.</summary>
    public string Intro { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public int? TlsPort { get; init; }

    public string Genre { get; init; } = string.Empty;
    public string GameType { get; init; } = string.Empty;
    public string Roleplaying { get; init; } = string.Empty;

    /// <summary>The server software, when a source names it: "PennMUSH 1.8.8p0", "Diku".</summary>
    public string Codebase { get; init; } = string.Empty;

    /// <summary>Players seen the last time the directory looked. Null when it has never seen.</summary>
    public int? PlayersOnline { get; init; }

    /// <summary>
    /// Whether that count is the directory's estimate rather than a number the game reported.
    /// Said out loud, because "about six hundred" and "six hundred" are different claims.
    /// </summary>
    public bool PlayersEstimated { get; init; }

    /// <summary>Whether the directory has actually reached the host recently.</summary>
    public bool ConfirmedOnline { get; init; }

    public MudAvailability Availability { get; init; }

    /// <summary>
    /// Players averaged over the last thirty days, where a source watches for that.
    ///
    /// The most useful number here and the one hardest to come by. A count taken now says
    /// whether a game is busy at this hour in this timezone; an average over a month says
    /// whether it is a game with people in it.
    /// </summary>
    public int? AveragePlayers { get; init; }

    /// <summary>The busiest and quietest that month got.</summary>
    public int? PeakPlayers { get; init; }
    public int? MinimumPlayers { get; init; }

    /// <summary>Which way the month went, as a percentage. Negative is downwards.</summary>
    public int? TrendPercent { get; init; }

    /// <summary>The year it opened, where a source has kept that.</summary>
    public int? YearOpened { get; init; }

    /// <summary>How many rooms and objects the world holds, where a source counts them.</summary>
    public int? DatabaseSize { get; init; }

    /// <summary>Whether playing it costs money. Worth knowing before connecting, not after.</summary>
    public bool PayToPlay { get; init; }

    public double? Rating { get; init; }
    public int ReviewCount { get; init; }
    public int MonthlyVotes { get; init; }
    public int? Rank { get; init; }

    public string Website { get; init; } = string.Empty;

    /// <summary>The directory's own page for this game.</summary>
    public string ListingUrl { get; init; } = string.Empty;

    /// <summary>Where the activity figures came from, when that is not <see cref="Source"/>.</summary>
    public string StatisticsSource { get; init; } = string.Empty;

    /// <summary>That source's page for this game.</summary>
    public string StatisticsUrl { get; init; } = string.Empty;

    public DateTimeOffset? Updated { get; init; }

    /// <summary>When the directory first listed it. What "newest" means.</summary>
    public DateTimeOffset? Listed { get; init; }

    /// <summary>The last time the directory actually reached the host.</summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>
    /// Whether BlindTerm could open this. A web-only listing has no host, and offering to
    /// connect to one would fail in a way that looks like BlindTerm's fault.
    /// </summary>
    [JsonIgnore]
    public bool CanConnect => Host.Length > 0 && Port is >= 1 and <= 65535;

    /// <summary>"host:port", the form the connect dialog and the recent list both use.</summary>
    [JsonIgnore]
    public string Address => CanConnect ? Net.TelnetAddress.Format(Host, Port) : string.Empty;

    /// <summary>
    /// The single line a results list reads out.
    ///
    /// A list is arrowed through, one item spoken per keypress, so this has to carry enough to
    /// pass over a game without opening it and no more than that. Name first, because that is
    /// what is being looked for; players next, because that is what "is this worth joining"
    /// mostly comes down to.
    ///
    /// The thirty-day average is only said when there is no live count to say, which is
    /// exactly when it is the only thing worth hearing. Adding it to every row would double
    /// the length of every row to repeat what the live count already said.
    /// </summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string> { Name };

            if (PlayersOnline is int players)
                parts.Add(PlayersEstimated
                    ? $"about {players} {(players == 1 ? "player" : "players")}"
                    : players == 1 ? "1 player" : $"{players} players");
            else if (AveragePlayers is int average)
                parts.Add($"usually {average}");
            else if (Availability == MudAvailability.Dead) parts.Add("long gone");
            else if (!ConfirmedOnline) parts.Add("not answering");

            if (Availability == MudAvailability.Offline) parts.Add("down just now");
            if (Genre.Length > 0) parts.Add(Genre);
            if (PayToPlay) parts.Add("pay to play");
            if (Rating is double rating && ReviewCount > 0)
                parts.Add($"rated {rating:0.#} from {Count(ReviewCount, "review")}");
            if (!CanConnect) parts.Add("web only");
            return string.Join(". ", parts) + ".";
        }
    }

    /// <summary>
    /// The whole entry, as lines to arrow down through.
    ///
    /// Separate from <see cref="Summary"/> because these are different jobs: one is skimmed at
    /// speed, the other is read once the skimming has stopped.
    /// </summary>
    [JsonIgnore]
    public string Details
    {
        get
        {
            var lines = new List<string> { Name };
            lines.Add(CanConnect
                ? TlsPort is int tls
                    ? $"{Host}, port {Port}, or port {tls} with TLS."
                    : $"{Host}, port {Port}."
                : "No telnet address. This one is played in a web browser.");

            lines.Add(Now());
            if (Activity() is string activity) lines.Add(activity);

            var what = new List<string>();
            if (Genre.Length > 0) what.Add(Genre);
            if (GameType.Length > 0) what.Add(GameType);
            if (Codebase.Length > 0) what.Add("running " + Codebase);
            if (Roleplaying.Length > 0) what.Add(Roleplaying);
            if (PayToPlay) what.Add("Pay to play");
            if (what.Count > 0) lines.Add(string.Join(". ", what) + ".");

            var history = new List<string>();
            if (YearOpened is int year) history.Add($"Opened in {year}");
            if (DatabaseSize is int size) history.Add($"{size:N0} rooms and objects");
            if (history.Count > 0) lines.Add(string.Join(". ", history) + ".");

            if (Rating is double rating && ReviewCount > 0)
                lines.Add($"Rated {rating:0.#} out of 5 from {Count(ReviewCount, "review")}.");
            if (Rank is int rank)
                lines.Add($"Ranked {rank} this month, on {Count(MonthlyVotes, "vote")}.");
            else if (MonthlyVotes > 0) lines.Add($"{Count(MonthlyVotes, "vote")} this month.");

            if (Website.Length > 0) lines.Add("Website: " + Website);
            if (ListingUrl.Length > 0) lines.Add($"Listed at {Source}: {ListingUrl}");
            if (StatisticsUrl.Length > 0 && StatisticsSource.Length > 0)
                lines.Add($"Statistics from {StatisticsSource}: {StatisticsUrl}");
            if (Updated is DateTimeOffset updated)
                lines.Add("Listing updated " + updated.ToLocalTime().ToString("d MMMM yyyy"));

            if (Intro.Length > 0)
            {
                lines.Add(string.Empty);
                lines.Add(Intro);
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    private string Now()
    {
        if (PlayersOnline is not int players)
        {
            return Availability switch
            {
                MudAvailability.Dead => "Long gone. Nothing has answered here in years.",
                MudAvailability.Offline => "Not answering just now.",
                _ when ConfirmedOnline => "Online. The player count is not published.",
                _ => "The directory has not reached this host recently.",
            };
        }

        string counted = PlayersEstimated
            ? $"About {players} {(players == 1 ? "player" : "players")}"
            : players == 1 ? "1 player" : $"{players} players";
        return Availability switch
        {
            MudAvailability.Offline => $"{counted} when it was last reachable; it is down just now.",
            MudAvailability.Dead => $"{counted} the last time anything answered, years ago.",
            _ => counted + " online.",
        };
    }

    /// <summary>
    /// The month in one sentence, or nothing when no source has been watching.
    ///
    /// This is what MUDStats brings that nothing else does. A game with four people on it
    /// right now reads very differently once you know whether that is a quiet Tuesday or the
    /// whole population.
    /// </summary>
    private string? Activity()
    {
        if (AveragePlayers is not int average && PeakPlayers is null) return null;

        var said = new List<string>();
        if (AveragePlayers is int mean) said.Add($"{mean} on average over thirty days");
        if (PeakPlayers is int peak)
            said.Add(MinimumPlayers is int least ? $"between {least} and {peak}" : $"peaking at {peak}");
        if (TrendPercent is int trend && trend != 0)
            said.Add(trend > 0 ? $"up {trend} percent this month" : $"down {-trend} percent this month");

        return char.ToUpperInvariant(said[0][0]) + string.Join(", ", said)[1..] + ".";
    }

    private static string Count(int howMany, string noun)
        => howMany == 1 ? $"1 {noun}" : $"{howMany} {noun}s";
}
