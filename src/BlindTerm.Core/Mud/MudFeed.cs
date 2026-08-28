using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlindTerm.Core.Mud;

/// <summary>
/// The whole directory as one file: every game, and the tags to narrow them by.
///
/// This exists so that browsing MUDs needs nothing from the person doing it. MUDVerse issues
/// API keys for servers and asks that they are not published, which leaves an open-source
/// desktop program two honest choices: make every user go and get their own key, or have one
/// machine hold a key and publish what it fetched. This is the second. A scheduled job runs
/// <c>blindterm directory</c> with the key, writes this file, and commits it; BlindTerm
/// downloads the file and needs no key at all.
///
/// Publishing the whole list rather than proxying each query also turns out to be the better
/// shape for a screen reader. Every sort and every filter is then instant and local, the same
/// list can be read on a train, and "most players online" -- which MUDVerse does not sort by
/// -- costs nothing instead of costing eight requests.
/// </summary>
public sealed class MudFeed
{
    /// <summary>
    /// The shape of this file. A reader that does not recognise the number refuses it rather
    /// than guessing, so a later format cannot be half-read by an older BlindTerm.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>When the job fetched this. Shown, because a player count has a shelf life.</summary>
    public DateTimeOffset Generated { get; set; }

    /// <summary>Who the data belongs to. Said out loud in the browser.</summary>
    public string Source { get; set; } = "MUDVerse";

    public string Attribution { get; set; } = "https://www.mudverse.com";

    /// <summary>Which directories this list was built from. Said out loud in the browser.</summary>
    public List<string> Sources { get; set; } = [];

    public List<MudTag> Themes { get; set; } = [];
    public List<MudTag> Types { get; set; } = [];
    public List<MudTag> Roleplaying { get; set; } = [];
    public List<MudGame> Games { get; set; } = [];

    [JsonIgnore]
    public MudDirectoryFilters Filters => new()
    {
        Themes = Themes,
        GameTypes = Types,
        Roleplaying = Roleplaying,
    };

    /// <summary>
    /// Compact rather than indented, and nulls left out. This is downloaded by everybody who
    /// opens the browser; a thousand listings of pretty-printed JSON is a megabyte of spaces.
    /// </summary>
    public static JsonSerializerOptions Format { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Format);

    /// <summary>Reads a feed, or throws <see cref="MudDirectoryException"/> saying why not.</summary>
    public static MudFeed FromJson(string json)
    {
        MudFeed? feed;
        try
        {
            feed = JsonSerializer.Deserialize<MudFeed>(json, Format);
        }
        catch (JsonException ex)
        {
            throw new MudDirectoryException("The MUD list could not be read.", inner: ex);
        }

        if (feed is null) throw new MudDirectoryException("The MUD list was empty.");
        if (feed.Version > CurrentVersion)
            throw new MudDirectoryException(
                "The MUD list is in a newer format than this version of BlindTerm understands. "
                + "Update BlindTerm, or enter a MUDVerse key to read the directory directly.");

        feed.Sources ??= [];
        feed.Themes ??= [];
        feed.Types ??= [];
        feed.Roleplaying ??= [];
        feed.Games ??= [];
        // A listing with no address is nothing a terminal can open, and one that arrived
        // without a name cannot be chosen from a list that is read out.
        feed.Games.RemoveAll(game => game is null || string.IsNullOrWhiteSpace(game.Name)
                                     || !game.CanConnect);
        return feed;
    }
}
