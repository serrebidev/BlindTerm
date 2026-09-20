using System.Net;
using System.Net.Http.Headers;

namespace BlindTerm.Core.Mud;

/// <summary>
/// The directory read from BlindTerm's published list rather than from MUDVerse directly.
///
/// This is the path everybody takes. Nobody is asked for a key, because the key lives in the
/// job that builds the list, not in the program that reads it. See <see cref="MudFeed"/>.
///
/// The whole list arrives in one download and everything after that is local, so sorting by
/// players, narrowing to a genre and searching are all instant and none of them costs a
/// request. The copy is kept on disk, so opening the browser a second time -- or on a train
/// -- reads what is already there.
/// </summary>
public sealed class MudFeedDirectory : IMudDirectory, IDisposable
{
    /// <summary>
    /// Where the published list lives.
    ///
    /// The generated data has a repository of its own, so rewriting the file every half hour
    /// never touches BlindTerm's history or its project notifications.
    /// </summary>
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/serrebidev/BlindTerm-directory/directory/mud-directory.json";

    /// <summary>
    /// How old the copy on disk may be before BlindTerm asks for a newer one.
    ///
    /// Shorter than it sounds: the ask is conditional, so an unchanged list costs one 304 and
    /// no download. This is only how long the browser will open without going to the network
    /// at all.
    /// </summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _url;
    private readonly string _cachePath;
    private MudFeed? _feed;

    public MudFeedDirectory(string? url = null, string? cachePath = null, HttpClient? http = null)
    {
        _url = string.IsNullOrWhiteSpace(url) ? DefaultFeedUrl : url.Trim();
        _cachePath = string.IsNullOrWhiteSpace(cachePath) ? DefaultCachePath : cachePath;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
    }

    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlindTerm", "mud-directory.json");

    public string Name => _feed?.Source ?? "MUDVerse";

    /// <summary>
    /// The whole list at once.
    ///
    /// The download already happened and everything after it is a sort of a list in memory, so
    /// there is no page to fetch and nothing to save by asking for one. Bounded rather than
    /// unbounded only so that a feed that grows absurdly cannot hand a list box a hundred
    /// thousand rows in one go.
    /// </summary>
    public int PageSizeLimit => 5000;

    /// <summary>When the list was built, once one has been read. Null before that.</summary>
    public DateTimeOffset? Generated => _feed?.Generated;

    /// <summary>How many games the list holds, once one has been read.</summary>
    public int Count => _feed?.Games.Count ?? 0;

    /// <summary>
    /// Which directories the list was built from, once one has been read.
    ///
    /// Named out loud in the browser, because the figures come from more than one place and
    /// whoever collected them is owed the credit.
    /// </summary>
    public IReadOnlyList<string> Sources => _feed?.Sources ?? [];

    /// <summary>
    /// What the list can actually be narrowed by, counted from the listings that are in it.
    ///
    /// Not the taxonomy the file carries. That is MUDVerse's whole vocabulary, and the list is
    /// built from four directories of which three publish no genre at all: offering thirty-one
    /// genres over eight hundred listings where seven hundred of them have no genre gives a
    /// filter where most choices return one game and one returns none. Counting first means
    /// every entry left in the box matches something, the number is beside the name, and a
    /// category nobody uses -- the roleplaying policy, which no source in this list fills in --
    /// disappears rather than sitting there as a way to empty the window.
    /// </summary>
    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        MudFeed feed = await FeedAsync(cancellationToken).ConfigureAwait(false);
        return new MudDirectoryFilters
        {
            Themes = Present(feed.Games, game => game.Genre),
            GameTypes = Present(feed.Games, game => game.GameType),
            Roleplaying = Present(feed.Games, game => game.Roleplaying),
        };
    }

    /// <summary>
    /// The distinct values one field takes across the list, with how many listings take each.
    ///
    /// The value is its own identifier. A tag number would have to be looked back up in the
    /// file's taxonomy to find the word the listings are actually labelled with, and a value
    /// the taxonomy has never heard of -- which happens the moment a source spells something
    /// its own way -- would then be unreachable.
    /// </summary>
    private static IReadOnlyList<MudTag> Present(List<MudGame> games, Func<MudGame, string> field)
    {
        var counts = new Dictionary<string, (string Name, int Count)>(StringComparer.CurrentCultureIgnoreCase);
        foreach (MudGame game in games)
        {
            string value = field(game).Trim();
            if (value.Length == 0) continue;
            counts[value] = counts.TryGetValue(value, out var seen)
                ? (seen.Name, seen.Count + 1)
                : (value, 1);
        }

        return [.. counts.Values
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => new MudTag(entry.Name, entry.Name, entry.Count))];
    }

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        MudFeed feed = await FeedAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<MudGame> matching = feed.Games;
        if (query.OnlyConnectable) matching = matching.Where(game => game.CanConnect);
        if (query.OnlyAnswering) matching = matching.Where(game => game.IsAnswering);

        // A tag identifier from FiltersAsync is the value itself, so there is nothing to look
        // up. A tag number from an older window, or from the file's own taxonomy, still lands
        // here as the name it stands for.
        if (Value(feed.Themes, query.ThemeTagId) is string theme)
            matching = matching.Where(game => Same(game.Genre, theme));
        if (Value(feed.Types, query.TypeTagId) is string type)
            matching = matching.Where(game => Same(game.GameType, type));
        if (Value(feed.Roleplaying, query.RoleplayingTagId) is string roleplaying)
            matching = matching.Where(game => Same(game.Roleplaying, roleplaying));

        foreach (string word in Words(query.Search))
        {
            string needle = word;
            matching = matching.Where(game => Has(game, needle));
        }

        List<MudGame> found = [.. MudSorting.Apply(matching, query.Sort)];
        return MudSorting.Page(found, query.Page, query.PerPage);
    }

    private static IEnumerable<string> Words(string search)
        => string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Every word has to appear somewhere in the entry, which is what makes typing two words
    /// narrow the list rather than widen it.
    /// </summary>
    private static bool Has(MudGame game, string word)
        => game.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
           || game.Intro.Contains(word, StringComparison.CurrentCultureIgnoreCase)
           || game.Genre.Contains(word, StringComparison.CurrentCultureIgnoreCase)
           || game.GameType.Contains(word, StringComparison.CurrentCultureIgnoreCase)
           // Searchable because it is the one thing people ask for by name that is not a name:
           // somebody who wants an LP or a CoffeeMud is asking about the codebase.
           || game.Codebase.Contains(word, StringComparison.CurrentCultureIgnoreCase)
           || game.Host.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The word a chosen filter stands for. Identifiers are the values themselves now, so this
    /// only has anything to do when one is a number out of the file's own taxonomy.
    /// </summary>
    private static string? Value(List<MudTag> tags, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return tags.FirstOrDefault(tag => tag.Id == id)?.Name ?? id;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// The list, from memory, then from disk, then from the network.
    ///
    /// A copy on disk that is out of date still beats no list at all, so a failed download
    /// falls back to it and only reports a failure when there is nothing to fall back on.
    /// </summary>
    private async Task<MudFeed> FeedAsync(CancellationToken cancellationToken)
    {
        if (_feed is not null) return _feed;

        MudFeed? saved = ReadCache();
        if (saved is not null && DateTimeOffset.UtcNow - saved.Generated < Fresh)
        {
            _feed = saved;
            return _feed;
        }

        try
        {
            _feed = await DownloadAsync(saved, cancellationToken).ConfigureAwait(false);
            return _feed;
        }
        catch (MudDirectoryException) when (saved is not null)
        {
            _feed = saved;
            return _feed;
        }
    }

    private async Task<MudFeed> DownloadAsync(MudFeed? saved, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        // The copy on disk was kept together with the tag the server gave it, so the ordinary
        // case -- the list has not been rebuilt since this window last looked -- costs one 304
        // and no download rather than a second copy of four hundred kilobytes. The note on
        // Fresh has always said this is what happens. Nothing implemented it, so every refresh
        // of a list that had not changed pulled the whole thing down again, on a schedule of
        // twice an hour upstream and every six hours in every copy of the program.
        if (saved is not null && ReadTag() is string tag)
            request.Headers.TryAddWithoutValidation("If-None-Match", tag);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not fetch the list of MUDs. " + ex.Message,
                inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException("The list of MUDs did not arrive in time.", inner: ex);
        }

        using (response)
        {
            // Answered before anything asks whether the status is a success, because it is
            // not: 304 is outside that range, so left to the check below it would be reported
            // as a failure to fetch a list that is sitting on the disk already.
            if (response.StatusCode == HttpStatusCode.NotModified && saved is not null) return saved;
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new MudDirectoryException(
                    "BlindTerm's list of MUDs is not published at " + _url + ". Enter a MUDVerse "
                    + "key to read the directory directly instead.", isAuthentication: true);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"The list of MUDs could not be fetched: {(int)response.StatusCode} {response.ReasonPhrase}.");

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            MudFeed feed = MudFeed.FromJson(json);
            // The tag is kept only alongside a copy that was written. A tag from this response
            // paired with the previous copy would answer "nothing has changed" to a request for
            // content that is not what the server just called unchanged.
            if (WriteCache(json) && response.Headers.ETag is { } etag) WriteTag(etag.Tag);
            return feed;
        }
    }

    private MudFeed? ReadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            return MudFeed.FromJson(File.ReadAllText(_cachePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or MudDirectoryException or NotSupportedException)
        {
            // A cache is a convenience. One that cannot be read is one to go past, never one
            // to report: the download that follows is the real answer.
            return null;
        }
    }

    /// <summary>Whether the copy was kept, which is also whether its tag may be.</summary>
    private bool WriteCache(string json)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            string temporary = $"{_cachePath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _cachePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            // Failing to keep a copy is not a failure to browse.
            return false;
        }
    }

    /// <summary>Where the server's tag for the copy on disk is kept, beside the copy.</summary>
    private string TagPath => _cachePath + ".etag";

    /// <summary>
    /// The tag the server gave the copy on disk, or nothing.
    ///
    /// Nothing is the safe answer: without a tag the next request is an ordinary one, which
    /// costs a download, and a download is what happened before any of this existed.
    /// </summary>
    private string? ReadTag()
    {
        try
        {
            if (!File.Exists(TagPath)) return null;
            string tag = File.ReadAllText(TagPath).Trim();
            return tag.Length > 0 ? tag : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void WriteTag(string tag)
    {
        try
        {
            File.WriteAllText(TagPath, tag);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            // Not keeping the tag costs the next reader a download, and nothing else. It is
            // never worth failing a browse over, and never worth reporting.
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
