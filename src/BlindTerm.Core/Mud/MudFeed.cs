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

    /// <summary>
    /// The directory a listing came from, before anything was merged into it.
    ///
    /// A name for the list rather than a credit: most of these listings are carrying a genre,
    /// a player count or an encrypted port that a different directory supplied. <see cref="Sources"/>
    /// is the list that says who those were.
    /// </summary>
    public string Source { get; set; } = "MUDVerse";

    /// <summary>
    /// The home page of the directory the taxonomies and the ratios of this format come from.
    ///
    /// Not the credit for the data, and not read out anywhere: the browser says
    /// <see cref="Sources"/> out loud, which is the list of everybody who contributed.
    /// </summary>
    public string Attribution { get; set; } = "https://www.mudverse.com";

    /// <summary>
    /// Which directories this list was built out of, said out loud in the browser.
    ///
    /// Every one that contributed a listing or the figures on one, so that a player count
    /// measured by somebody other than the listing's own directory is credited to whoever
    /// measured it.
    /// </summary>
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
        // A listing is kept only if it can be connected to and can be chosen from a list that
        // is read out, and a listing that is kept has every null word filled in first. JSON
        // writes a null where a key is explicitly null -- the initializers on the record do
        // nothing about that, they only cover a key that is absent -- and every one of these
        // fields is read with .Length or handed to something that is. The file is published
        // by a scheduled job in this repository, so this is not a defence against anybody; it
        // is what stops one hand-edited listing from throwing somewhere the message would not
        // say what had happened.
        for (int i = 0; i < feed.Games.Count; i++)
        {
            MudGame? game = feed.Games[i];
            if (game is null || !game.CanConnect || string.IsNullOrWhiteSpace(game.Name))
            {
                feed.Games.RemoveAt(i--);
                continue;
            }
            feed.Games[i] = game.Tidy();
        }
        return feed;
    }
}
