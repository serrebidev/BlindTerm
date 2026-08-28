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
    /// The same repository the updates come from, on a branch of its own so that rewriting a
    /// file every half hour never touches the history anybody reads.
    /// </summary>
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/serrebidev/BlindTerm/directory/mud-directory.json";

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

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
        => (await FeedAsync(cancellationToken).ConfigureAwait(false)).Filters;

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        MudFeed feed = await FeedAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<MudGame> matching = feed.Games;
        // The combo boxes hold the feed's own tags, so a chosen identifier can be turned back
        // into the name each listing carries.
        if (Named(feed.Themes, query.ThemeTagId) is string theme)
            matching = matching.Where(game => Same(game.Genre, theme));
        if (Named(feed.Types, query.TypeTagId) is string type)
            matching = matching.Where(game => Same(game.GameType, type));
        if (Named(feed.Roleplaying, query.RoleplayingTagId) is string roleplaying)
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
           || game.Host.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static string? Named(List<MudTag> tags, string? id)
        => string.IsNullOrWhiteSpace(id) ? null : tags.FirstOrDefault(tag => tag.Id == id)?.Name;

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
            _feed = await DownloadAsync(cancellationToken).ConfigureAwait(false);
            return _feed;
        }
        catch (MudDirectoryException) when (saved is not null)
        {
            _feed = saved;
            return _feed;
        }
    }

    private async Task<MudFeed> DownloadAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(_url, cancellationToken).ConfigureAwait(false);
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
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new MudDirectoryException(
                    "BlindTerm's list of MUDs is not published at " + _url + ". Enter a MUDVerse "
                    + "key to read the directory directly instead.", isAuthentication: true);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"The list of MUDs could not be fetched: {(int)response.StatusCode} {response.ReasonPhrase}.");

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            MudFeed feed = MudFeed.FromJson(json);
            WriteCache(json);
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

    private void WriteCache(string json)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            string temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            // Failing to keep a copy is not a failure to browse.
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
