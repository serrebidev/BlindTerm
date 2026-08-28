using System.Net.Http.Headers;
using System.Text.Json;

namespace BlindTerm.Core.Mud;

/// <summary>
/// Grapevine, read from the JSON its own games page serves.
///
/// Grapevine documents a WebSocket API that needs an account and a client secret, and says
/// nothing about HTTP. But its games page answers to <c>Accept: application/json</c> with a
/// clean, paginated list -- no key, no account, no negotiation -- and that list is the best
/// connection data of any of these directories: a hundred and fifty games, properly
/// paginated, the whole thing in about four seconds, and it is the only source here that
/// states an encrypted port as a first-class thing rather than leaving it to be guessed.
///
/// It is a small list and it has no player counts, so it is not a directory to browse on its
/// own. What it is for is filling in what MUDVerse cannot be paged through to reach and what
/// MUDStats does not carry: addresses, encrypted ports, descriptions and homepages.
/// </summary>
public sealed class GrapevineDirectory : IMudDirectory, IDisposable
{
    public const string Site = "https://grapevine.haus";

    /// <summary>
    /// A bound on the paging. Grapevine returns twenty-five to a page and has a hundred and
    /// fifty games; forty pages is far past the end and stops a broken "next" link looping.
    /// </summary>
    private const int MaximumPages = 40;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _site;
    private IReadOnlyList<MudGame>? _games;

    public GrapevineDirectory(string? site = null, HttpClient? http = null)
    {
        _site = (string.IsNullOrWhiteSpace(site) ? Site : site).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
        // This is the whole trick: the same address that serves a web page serves JSON when
        // asked for JSON.
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Name => "Grapevine";

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        // Grapevine publishes no genres or game types at all, so there is nothing here to
        // narrow a list by. Saying so plainly beats inventing a taxonomy out of taglines.
        await Task.CompletedTask.ConfigureAwait(false);
        return MudDirectoryFilters.None;
    }

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<MudGame> games = await GamesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<MudGame> matching = games;
        if (query.OnlyConnectable) matching = matching.Where(game => game.CanConnect);
        foreach (string word in query.Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string needle = word;
            matching = matching.Where(game =>
                game.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                game.Intro.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        List<MudGame> found = [.. MudSorting.Apply(matching, query.Sort)];
        return MudSorting.Page(found, query.Page, query.PerPage);
    }

    /// <summary>Every game Grapevine lists, following its own "next" links to the end.</summary>
    public async Task<IReadOnlyList<MudGame>> GamesAsync(CancellationToken cancellationToken = default)
    {
        if (_games is not null) return _games;

        var games = new List<MudGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? url = _site + "/games";

        for (int page = 0; page < MaximumPages && url is not null; page++)
        {
            using JsonDocument document = await GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("items", out JsonElement items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    MudGame? game = Read(item);
                    if (game is not null && seen.Add(game.SourceId)) games.Add(game);
                }
            }

            url = Next(document);
            // A next link that points back where it came from would page forever.
            if (url is not null && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = null;
        }

        _games = games;
        return _games;
    }

    private static string? Next(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("links", out JsonElement links) ||
            links.ValueKind != JsonValueKind.Array) return null;

        foreach (JsonElement link in links.EnumerateArray())
        {
            if (link.ValueKind != JsonValueKind.Object) continue;
            if (link.TryGetProperty("rel", out JsonElement rel) && rel.GetString() == "next" &&
                link.TryGetProperty("href", out JsonElement href))
                return href.GetString();
        }
        return null;
    }

    private static MudGame? Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        string name = Text(item, "name");
        string shortName = Text(item, "short_name");
        if (name.Length == 0) name = shortName;
        if (name.Length == 0) return null;

        // A game can publish a plain port, an encrypted one and a web client. The first two
        // are what a terminal can use, and Grapevine is the only directory here that names
        // the encrypted one outright instead of leaving it to be discovered.
        string host = string.Empty;
        int port = 0, tls = 0;
        if (item.TryGetProperty("connections", out JsonElement connections) &&
            connections.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement connection in connections.EnumerateArray())
            {
                string kind = Text(connection, "type");
                string where = Text(connection, "host");
                int which = Number(connection, "port") ?? 0;
                if (where.Length == 0 || which is < 1 or > 65535) continue;

                if (kind.Contains("secure", StringComparison.OrdinalIgnoreCase))
                {
                    tls = which;
                    if (host.Length == 0) host = where;
                }
                else if (kind.Equals("telnet", StringComparison.OrdinalIgnoreCase))
                {
                    host = where;
                    port = which;
                }
            }
        }

        // A game that only offers an encrypted port is still a game a terminal can open.
        if (port == 0 && tls != 0) port = tls;

        return new MudGame
        {
            Source = "Grapevine",
            SourceId = shortName.Length > 0 ? shortName : name,
            Name = name,
            // The tagline is one line and the description is several paragraphs. A list that
            // is read aloud wants the line.
            Intro = Text(item, "tagline") is { Length: > 0 } tagline ? tagline : Trim(Text(item, "description")),
            Host = host,
            Port = port,
            TlsPort = tls == 0 || tls == port ? null : tls,
            Website = Text(item, "homepage_url"),
            ListingUrl = Text(item, "discord_invite_url"),
        };
    }

    /// <summary>
    /// A description standing in for a missing tagline, cut to one line.
    ///
    /// The first sentence, when there is one. A list entry is spoken in full every time the
    /// arrow keys move onto it, so three paragraphs there is three paragraphs read out before
    /// the next game can be reached. The whole description is not lost -- the sources that
    /// have one put it in the details.
    /// </summary>
    private static string Trim(string description)
    {
        if (description.Length == 0) return description;

        int stop = description.IndexOf(". ", StringComparison.Ordinal);
        if (stop > 0 && stop < 240) return description[..(stop + 1)];
        if (description.EndsWith('.') && description.Length <= 240) return description;
        return description.Length <= 240 ? description : description[..240].TrimEnd() + "...";
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"Grapevine answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                    worthRetrying: (int)response.StatusCode >= 500);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not reach Grapevine. " + ex.Message,
                worthRetrying: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException("Grapevine did not answer in time.", worthRetrying: true, inner: ex);
        }
        catch (JsonException ex)
        {
            // The documented API is a WebSocket one; this JSON is a convenience of the web
            // site. If it ever stops being JSON, that is the shape the failure takes.
            throw new MudDirectoryException("Grapevine sent something BlindTerm could not read.", inner: ex);
        }
    }

    private static string Text(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int? Number(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
