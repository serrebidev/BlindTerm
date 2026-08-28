using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BlindTerm.Core.Mud;

/// <summary>
/// MUDVerse, read through its v1 API.
///
/// Two things about that API shape this class. It has no "sort by players", so
/// <see cref="MudDirectorySort.MostPlayers"/> is done here: the matching listings are fetched
/// once, kept for a few minutes and ordered locally. Fifty to a page against an allowance of
/// sixty requests a minute makes that a handful of calls, not a scrape.
///
/// The second is that its keys are meant for a server, and MUDVerse says plainly not to put
/// one in a public repository. BlindTerm is a public repository, so there is no key in it.
/// The key comes from whoever is running the program, or the endpoint is pointed at something
/// that holds one on their behalf -- which is why the base address is a parameter and the key
/// is allowed to be absent.
/// </summary>
public sealed class MudVerseDirectory : IMudDirectory, IDisposable
{
    /// <summary>MUDVerse itself. Talking to it directly needs a key of your own.</summary>
    public const string MudVerseEndpoint = "https://www.mudverse.com/api/v1";

    /// <summary>Where to send someone who has not got a key.</summary>
    public const string ApiKeyPage = "https://www.mudverse.com/api";

    /// <summary>The most MUDVerse will return at once.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// How much of the directory a local sort will pull. Four hundred listings is well past
    /// the point where anything still has players on it, and it bounds the work at eight
    /// requests however large MUDVerse grows.
    /// </summary>
    private const int SweepPages = 8;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _endpoint;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Fetched, IReadOnlyList<MudGame> Games)> _sweeps = new();
    private MudDirectoryFilters? _filters;

    public MudVerseDirectory(string? apiKey = null, string? endpoint = null, HttpClient? http = null)
    {
        _endpoint = (string.IsNullOrWhiteSpace(endpoint) ? MudVerseEndpoint : endpoint).TrimEnd('/');
        // Short on purpose, and this was measured rather than guessed. MUDVerse answers a
        // first page in about two seconds and gets slower the deeper the offset goes: at
        // twenty to a page, page 3 took seven seconds and everything past it never came back
        // at all. Waiting a minute on a request like that buys nothing -- it was never going
        // to arrive -- so the caller is better off being told quickly and moving on.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    public string Name => "MUDVerse";

    /// <summary>Whether this will be talking to MUDVerse itself, where a key is required.</summary>
    public bool NeedsKey => _http.DefaultRequestHeaders.Authorization is null
                            && _endpoint.StartsWith(MudVerseEndpoint, StringComparison.OrdinalIgnoreCase);

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        // The taxonomy changes about as often as MUDVerse ships a release, so it is fetched
        // once per window rather than per search.
        if (_filters is not null) return _filters;

        using JsonDocument document = await GetAsync("/tags", cancellationToken).ConfigureAwait(false);
        var themes = new List<MudTag>();
        var types = new List<MudTag>();
        var roleplaying = new List<MudTag>();

        if (document.RootElement.TryGetProperty("data", out JsonElement categories) &&
            categories.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement category in categories.EnumerateArray())
            {
                string name = Text(category, "name");
                List<MudTag>? into =
                    Mentions(name, "theme") || Mentions(name, "genre") ? themes
                    : Mentions(name, "type") ? types
                    : Mentions(name, "roleplay") || Mentions(name, "rp") ? roleplaying
                    : null;
                if (into is null) continue;

                if (!category.TryGetProperty("values", out JsonElement values) ||
                    values.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement value in values.EnumerateArray())
                {
                    string id = Text(value, "id");
                    string label = Text(value, "name");
                    if (id.Length > 0 && label.Length > 0) into.Add(new MudTag(id, label));
                }
            }
        }

        _filters = new MudDirectoryFilters
        {
            Themes = Ordered(themes),
            GameTypes = Ordered(types),
            Roleplaying = Ordered(roleplaying),
        };
        return _filters;
    }

    private static IReadOnlyList<MudTag> Ordered(List<MudTag> tags)
        => [.. tags.DistinctBy(tag => tag.Id).OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)];

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        int perPage = Math.Clamp(query.PerPage, 1, PageSize);

        if (query.Sort == MudDirectorySort.MostPlayers)
            return Slice(await SweepAsync(query, cancellationToken).ConfigureAwait(false), query.Page, perPage);

        // MUDVerse pages what it sorts, so anything it sorts itself is one request.
        var parameters = new List<string>
        {
            "sort=" + ServerSort(query.Sort),
            "per_page=" + perPage,
            "page=" + Math.Max(1, query.Page),
        };
        Filters(query, parameters);
        if (query.Sort == MudDirectorySort.MostReviewed) parameters.Add("has_reviews=1");
        if (query.Sort == MudDirectorySort.RecentlyOnline) parameters.Add("online_recently=1");
        if (query.Sort == MudDirectorySort.RecentlyUpdated) parameters.Add("recently_updated=1");

        using JsonDocument document = await GetAsync("/games?" + Join(parameters), cancellationToken)
            .ConfigureAwait(false);
        List<MudGame> games = ReadGames(document, query.OnlyConnectable);

        int total = 0;
        if (document.RootElement.TryGetProperty("meta", out JsonElement meta)) total = Number(meta, "total") ?? 0;

        return new MudDirectoryPage
        {
            Games = games,
            Page = Math.Max(1, query.Page),
            PerPage = perPage,
            Total = total,
            // A page that dropped its web-only listings can come back short of a full page and
            // still have more behind it, so the server's own "next" link decides, not the count.
            HasMore = HasNext(document),
        };
    }

    /// <summary>
    /// Pulls the matching listings so they can be ordered by who is actually playing.
    ///
    /// Kept for a quarter of an hour, because MUDVerse crawls on its own schedule and asking
    /// again inside that window buys nothing but requests. The key is the filters, not the
    /// page, so arrowing on to a second page of the same genre fetches nothing at all.
    /// </summary>
    private async Task<IReadOnlyList<MudGame>> SweepAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken)
    {
        string key = query.FilterKey;
        if (_sweeps.TryGetValue(key, out var cached) &&
            DateTimeOffset.UtcNow - cached.Fetched < CacheLifetime)
            return cached.Games;

        var all = new List<MudGame>();
        for (int page = 1; page <= SweepPages; page++)
        {
            var parameters = new List<string> { "sort=recently_online", "per_page=" + PageSize, "page=" + page };
            Filters(query, parameters);
            using JsonDocument document = await GetAsync("/games?" + Join(parameters), cancellationToken)
                .ConfigureAwait(false);

            int before = all.Count;
            all.AddRange(ReadGames(document, query.OnlyConnectable));
            // A page with no next link, or one that added nothing at all, is the end of the list.
            if (!HasNext(document) || all.Count == before) break;
        }

        IReadOnlyList<MudGame> ordered =
        [
            .. all.DistinctBy(game => game.SourceId)
                  .OrderByDescending(game => game.PlayersOnline ?? -1)
                  .ThenByDescending(game => game.ConfirmedOnline)
                  .ThenByDescending(game => game.MonthlyVotes)
                  .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
        _sweeps[key] = (DateTimeOffset.UtcNow, ordered);
        return ordered;
    }

    private static MudDirectoryPage Slice(IReadOnlyList<MudGame> games, int page, int perPage)
    {
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

    private static string Join(List<string> parameters) => string.Join("&", parameters);

    private static bool HasNext(JsonDocument document)
        => document.RootElement.TryGetProperty("links", out JsonElement links) &&
           links.ValueKind == JsonValueKind.Object &&
           links.TryGetProperty("next", out JsonElement next) &&
           next.ValueKind == JsonValueKind.String;

    private static void Filters(MudDirectoryQuery query, List<string> parameters)
    {
        if (query.OnlyConnectable) parameters.Add("connection_type=mud_client");
        if (!string.IsNullOrWhiteSpace(query.Search))
            parameters.Add("q=" + Uri.EscapeDataString(query.Search.Trim()));
        Tag(parameters, "theme_tag_id", query.ThemeTagId);
        Tag(parameters, "type_tag_id", query.TypeTagId);
        Tag(parameters, "rp_status_tag_id", query.RoleplayingTagId);
    }

    private static void Tag(List<string> parameters, string name, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        parameters.Add(Uri.EscapeDataString(name + "[]") + "=" + Uri.EscapeDataString(id.Trim()));
    }

    private static string ServerSort(MudDirectorySort sort) => sort switch
    {
        MudDirectorySort.TopVoted => "top_voted",
        MudDirectorySort.MostReviewed => "most_reviewed",
        MudDirectorySort.RecentlyOnline => "recently_online",
        MudDirectorySort.Newest => "newest",
        _ => "last_updated",
    };

    private static List<MudGame> ReadGames(JsonDocument document, bool onlyConnectable)
    {
        var games = new List<MudGame>();
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array) return games;

        foreach (JsonElement entry in data.EnumerateArray())
        {
            MudGame? game = ReadGame(entry);
            if (game is null) continue;
            // MUDVerse is asked for connectable listings, but an archived or web-only one still
            // slips through with a blank host, and a dead entry in the list is worse than a
            // shorter list.
            if (onlyConnectable && !game.CanConnect) continue;
            games.Add(game);
        }
        return games;
    }

    private static MudGame? ReadGame(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        string id = Text(entry, "id");
        string name = Text(entry, "name");
        if (id.Length == 0 || name.Length == 0) return null;

        JsonElement connection = Child(entry, "connection");
        JsonElement urls = Child(entry, "urls");
        JsonElement ranking = Child(entry, "ranking");
        JsonElement reviews = Child(entry, "reviews");
        JsonElement status = Child(entry, "status");
        JsonElement categories = Child(Child(entry, "tags"), "categories");

        return new MudGame
        {
            Source = "MUDVerse",
            SourceId = id,
            Name = name,
            Intro = Text(entry, "intro"),
            Host = Text(connection, "host"),
            Port = Number(connection, "port") ?? 0,
            TlsPort = Number(connection, "tls_port"),
            Genre = Category(categories, "theme"),
            GameType = Category(categories, "type"),
            Roleplaying = Category(categories, "rp_status"),
            PlayersOnline = Number(status, "latest_players"),
            ConfirmedOnline = Flag(status, "confirmed_online"),
            Rating = Fraction(reviews, "average_rating"),
            ReviewCount = Number(reviews, "count") ?? 0,
            MonthlyVotes = Number(ranking, "monthly_votes") ?? 0,
            Rank = Number(ranking, "rank"),
            Website = Text(urls, "website"),
            ListingUrl = Text(urls, "mudverse"),
            Updated = Moment(Child(entry, "dates"), "updated"),
            Listed = Moment(Child(entry, "dates"), "listed"),
            LastSeen = Moment(status, "last_successful_connect"),
        };
    }

    /// <summary>
    /// How many times a request that failed for a reason that might pass is tried again.
    ///
    /// Two, not more. A dropped connection or a server having a moment is worth one more ask;
    /// a request that timed out because the offset is too deep for MUDVerse to serve will
    /// time out again in exactly the same way, and a third attempt only spends another
    /// twenty-five seconds of a scheduled run proving it. A rejected key is not retried at all.
    /// </summary>
    private const int Attempts = 2;

    /// <summary>Raised when a request is being tried again, so a slow run says so rather than just being slow.</summary>
    public event Action<string>? Retrying;

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        MudDirectoryException? last = null;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                return await SendAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (MudDirectoryException ex) when (ex.IsWorthRetrying && attempt < Attempts)
            {
                last = ex;
                Retrying?.Invoke($"{path}: {ex.Message} Trying again ({attempt} of {Attempts}).");
                // Backing off rather than hammering: whatever was wrong is given time to stop
                // being wrong, and MUDVerse is not asked the same question three times in a row.
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        throw last ?? new MudDirectoryException("MUDVerse could not be reached.");
    }

    private async Task<JsonDocument> SendAsync(string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(_endpoint + path, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not reach MUDVerse. " + ex.Message,
                worthRetrying: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException($"MUDVerse did not answer {path} within {_http.Timeout.TotalSeconds:0} seconds.",
                worthRetrying: true, inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw Failure(response);
            try
            {
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new MudDirectoryException("MUDVerse sent something BlindTerm could not read.", inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                // The headers arrived and the body did not. Worth another go.
                throw new MudDirectoryException("MUDVerse stopped part way through answering " + path + ".",
                    worthRetrying: true, inner: ex);
            }
        }
    }

    private static MudDirectoryException Failure(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.Unauthorized => new MudDirectoryException(
            "MUDVerse did not accept the API key. Choose MUDVerse key to enter another, or "
            + "generate a new one at " + ApiKeyPage + ".", isAuthentication: true),
        HttpStatusCode.TooManyRequests => new MudDirectoryException(
            "MUDVerse is rate limiting this key. " + RetryIn(response), worthRetrying: true),
        HttpStatusCode.NotFound => new MudDirectoryException("MUDVerse has no such listing any more."),
        // A server having a moment is worth asking again; anything it means on purpose is not.
        _ => new MudDirectoryException($"MUDVerse answered {(int)response.StatusCode} {response.ReasonPhrase}.",
            worthRetrying: (int)response.StatusCode >= 500),
    };

    private static string RetryIn(HttpResponseMessage response)
    {
        double? delta = response.Headers.RetryAfter?.Delta?.TotalSeconds;
        return delta is > 0 ? $"Try again in {(int)delta.Value} seconds." : "Try again shortly.";
    }

    private static bool Mentions(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static JsonElement Child(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement child)
            ? child : default;

    /// <summary>
    /// A tag category, whether MUDVerse keyed it by slug or gave it a name. Its own reference
    /// shows "theme"; a sibling category could as easily be titled "Game Type", so both the
    /// key and any key containing it are tried before giving up.
    /// </summary>
    private static string Category(JsonElement categories, string key)
    {
        if (categories.ValueKind != JsonValueKind.Object) return string.Empty;
        if (categories.TryGetProperty(key, out JsonElement direct)) return Text(direct, "name");
        foreach (JsonProperty property in categories.EnumerateObject())
            if (Mentions(property.Name, key)) return Text(property.Value, "name");
        return string.Empty;
    }

    private static string Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty,
        };
    }

    private static int? Number(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;
        return null;
    }

    private static double? Fraction(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return parsed;
        return null;
    }

    private static bool Flag(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object &&
           parent.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Moment(JsonElement parent, string name)
    {
        string text = Text(parent, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset moment)
            ? moment : null;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
