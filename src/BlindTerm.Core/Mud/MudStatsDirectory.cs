using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BlindTerm.Core.Mud;

/// <summary>
/// MUDStats, read from the table its own Browse page reads.
///
/// MUDStats publishes no API. What it has instead is one endpoint, <c>/WorldList</c>, that
/// its browse table fetches, and which will hand over all two thousand two hundred worlds in
/// a single request. That is worth having: MUDStats has been sampling player counts for
/// twenty years, and a thirty-day average is the one number that answers "is anybody
/// actually playing this", which no other directory publishes at all.
///
/// It is also undocumented, which is a real cost and is handled rather than ignored:
///
/// - Nothing here is on the path of somebody opening the browser. This runs in the scheduled
///   job that builds BlindTerm's published list, so MUDStats sees one visitor twice an hour
///   rather than one per user per keystroke.
/// - Every field is optional. A column that moves, a span that gets renamed, a number that
///   becomes a word -- each of those loses one field on some listings, and never throws.
/// - The generator treats a total failure here as "no statistics this time" and still
///   publishes the rest. MUDStats going away degrades the list; it does not break it.
/// </summary>
public sealed partial class MudStatsDirectory : IMudDirectory, IDisposable
{
    public const string Site = "https://mudstats.com";

    /// <summary>The table's own source, as named in mudstats.com/Scripts/Pages/WorldList.js.</summary>
    private const string ListPath = "/WorldList";

    /// <summary>
    /// The table has eleven columns and the endpoint refuses a request that does not describe
    /// every one of them. This is the legacy DataTables 1.9 protocol, which is what the site
    /// is built on.
    /// </summary>
    private const int Columns = 11;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _site;
    private IReadOnlyList<MudGame>? _worlds;

    public MudStatsDirectory(string? site = null, HttpClient? http = null)
    {
        _site = (string.IsNullOrWhiteSpace(site) ? Site : site).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
    }

    public string Name => "MUDStats";

    /// <summary>The whole list is in hand once it has been read, so there is no page to fetch.</summary>
    public int PageSizeLimit => 5000;

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MudGame> worlds = await WorldsAsync(cancellationToken).ConfigureAwait(false);

        // Taken from the worlds rather than from the site's own select boxes, so a genre that
        // no longer has a game in it does not sit in the list leading nowhere. MUDStats lists
        // over two hundred genres and a good few of them are empty.
        static IReadOnlyList<MudTag> Distinct(IEnumerable<string> names)
            => [.. names.Where(name => name.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                        .Select(name => new MudTag(name, name))];

        return new MudDirectoryFilters
        {
            Themes = Distinct(worlds.Select(world => world.Genre)),
            GameTypes = Distinct(worlds.Select(world => world.GameType)),
        };
    }

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<MudGame> worlds = await WorldsAsync(cancellationToken).ConfigureAwait(false);

        // The whole table is already here, so everything below is local. Asking the endpoint
        // to filter would be another request for an answer that is in hand.
        IEnumerable<MudGame> matching = worlds;
        if (query.OnlyConnectable) matching = matching.Where(world => world.CanConnect);
        if (query.OnlyAnswering) matching = matching.Where(world => world.IsAnswering);
        if (!string.IsNullOrWhiteSpace(query.ThemeTagId))
            matching = matching.Where(world => Same(world.Genre, query.ThemeTagId));
        if (!string.IsNullOrWhiteSpace(query.TypeTagId))
            matching = matching.Where(world => Same(world.GameType, query.TypeTagId));
        foreach (string word in query.Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string needle = word;
            matching = matching.Where(world =>
                world.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                world.Genre.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        List<MudGame> found = [.. MudSorting.Apply(matching, query.Sort)];
        return MudSorting.Page(found, query.Page, query.PerPage);
    }

    /// <summary>
    /// Every world MUDStats knows about, statistics and all, from one request.
    ///
    /// Deliberately one request rather than paging: the endpoint will return the lot, and two
    /// megabytes once is kinder to it than forty-five requests of fifty.
    /// </summary>
    public async Task<IReadOnlyList<MudGame>> WorldsAsync(CancellationToken cancellationToken = default)
    {
        if (_worlds is not null) return _worlds;

        string url = _site + ListPath + "?" + ListQuery();
        string json;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The endpoint exists to serve the browse table, and says so.
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Referrer = new Uri(_site + "/Browse");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"MUDStats answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not reach MUDStats. " + ex.Message, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException("MUDStats did not answer in time.", inner: ex);
        }

        _worlds = Parse(json, _site);
        return _worlds;
    }

    /// <summary>
    /// The DataTables 1.9 request the table makes. Every column has to be described or the
    /// endpoint returns a 500.
    /// </summary>
    private static string ListQuery()
    {
        var parameters = new List<string>
        {
            "sEcho=1",
            "iColumns=" + Columns,
            "sColumns=",
            "iDisplayStart=0",
            // Everything. The endpoint honours this, and the alternative is forty-five requests.
            "iDisplayLength=5000",
            "sSearch=",
            "bRegex=false",
            "iSortingCols=1",
            // Column 7 is the thirty-day average, which is the ordering worth having if the
            // list ever comes back truncated.
            "iSortCol_0=7",
            "sSortDir_0=desc",
        };
        for (int column = 0; column < Columns; column++)
        {
            parameters.Add($"mDataProp_{column}={column}");
            parameters.Add($"bSearchable_{column}=true");
            parameters.Add($"sSearch_{column}=");
            parameters.Add($"bRegex_{column}=false");
            parameters.Add($"bSortable_{column}=true");
        }
        return string.Join("&", parameters);
    }

    /// <summary>
    /// Turns the table's rows of HTML into games.
    ///
    /// Every cell is a small piece of markup rather than a value -- this is a rendered table
    /// being shipped as JSON -- so each is read with a pattern that fails to nothing. A row
    /// with no name is skipped; a row with an unreadable number keeps the rest of itself.
    /// </summary>
    internal static IReadOnlyList<MudGame> Parse(string json, string site = Site)
    {
        var worlds = new List<MudGame>();
        System.Text.Json.JsonDocument document;
        try
        {
            document = System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MudDirectoryException("MUDStats sent something BlindTerm could not read.", inner: ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("aaData", out var rows) ||
                rows.ValueKind != System.Text.Json.JsonValueKind.Array)
                throw new MudDirectoryException("MUDStats sent no world list.");

            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                string[] cells = [.. row.EnumerateArray().Select(cell => cell.GetString() ?? string.Empty)];
                if (cells.Length < Columns) continue;

                MudGame? world = Read(cells, site);
                if (world is not null) worlds.Add(world);
            }
        }
        return worlds;
    }

    private static MudGame? Read(string[] cells, string site)
    {
        Match named = NameCell().Match(cells[0]);
        if (!named.Success) return null;
        string slug = named.Groups["slug"].Value;
        string name = Plain(named.Groups["name"].Value);
        if (name.Length == 0) return null;

        Match genre = LinkCell().Match(cells[1]);
        Match type = LinkCell().Match(cells[3]);
        (MudAvailability availability, _) = Status(cells[2]);
        (int? players, bool estimated) = Players(cells[4]);

        return new MudGame
        {
            Source = "MUDStats",
            SourceId = slug.Length > 0 ? slug : name,
            Name = name,
            Genre = genre.Success ? Plain(genre.Groups["text"].Value) : string.Empty,
            // MUDStats marks the games that charge, right beside the genre.
            PayToPlay = cells[1].Contains("Pay-To-Play", StringComparison.OrdinalIgnoreCase),
            GameType = type.Success ? Plain(type.Groups["text"].Value) : string.Empty,
            Codebase = Parenthesised(cells[3]),
            Availability = availability,
            ConfirmedOnline = availability == MudAvailability.Online,
            PlayersOnline = players,
            PlayersEstimated = estimated,
            PeakPlayers = Number(cells[5]),
            MinimumPlayers = Number(cells[6]),
            AveragePlayers = Number(cells[7]),
            TrendPercent = Trend(cells[8]),
            DatabaseSize = Number(cells[9]),
            YearOpened = Number(cells[10]),
            StatisticsSource = "MUDStats",
            StatisticsUrl = slug.Length > 0 ? $"{site}/World/{slug}" : string.Empty,
            ListingUrl = slug.Length > 0 ? $"{site}/World/{slug}" : string.Empty,
        };
    }

    /// <summary>
    /// The host and port, which the table does not carry and the world's own page does.
    ///
    /// One request per world, so this is only ever called for a world that is not already
    /// known from somewhere else, and the caller is expected to space them out. Returns null
    /// when the page has no address rather than throwing: plenty of dead worlds have none.
    /// </summary>
    public async Task<(string Host, int Port, string Website)?> AddressAsync(string slug,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        string html;
        try
        {
            using HttpResponseMessage response = await _http.GetAsync($"{_site}/World/{Uri.EscapeDataString(slug)}",
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) return null;
            html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        // The page prints it as a telnet:// link, which is the one place on the page where
        // the host and the port are already separated for us.
        Match address = AddressLink().Match(html);
        if (!address.Success) return null;
        if (!int.TryParse(address.Groups["port"].Value, out int port) || port is < 1 or > 65535) return null;

        Match website = WebsiteLink().Match(html);
        return (address.Groups["host"].Value, port, website.Success ? website.Groups["url"].Value : string.Empty);
    }

    private static (MudAvailability, string) Status(string cell)
    {
        string text = Plain(cell).Trim();
        if (text.StartsWith("UP", StringComparison.OrdinalIgnoreCase)) return (MudAvailability.Online, text);
        if (text.StartsWith("DEAD", StringComparison.OrdinalIgnoreCase)) return (MudAvailability.Dead, text);
        if (text.StartsWith("DOWN", StringComparison.OrdinalIgnoreCase)) return (MudAvailability.Offline, text);
        return (MudAvailability.Unknown, text);
    }

    /// <summary>
    /// The count, and whether MUDStats was estimating it. A tilde in the cell means the game
    /// does not report a number and MUDStats worked one out.
    /// </summary>
    private static (int?, bool) Players(string cell)
        => (Number(cell), cell.Contains("class=\"estimate\"", StringComparison.OrdinalIgnoreCase));

    /// <summary>A signed percentage out of an up or down arrow, or nothing.</summary>
    private static int? Trend(string cell)
    {
        string text = Plain(cell).Trim();
        if (text.Length == 0) return null;
        bool down = text.Contains('▼');
        int? size = Number(text);
        return size is null ? null : down ? -size : size;
    }

    /// <summary>The number in a cell, ignoring "Unknown", the markup and any thousands commas.</summary>
    private static int? Number(string cell)
    {
        Match digits = Digits().Match(Plain(cell).Replace(",", string.Empty));
        return digits.Success && int.TryParse(digits.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    /// <summary>The bracketed aside a cell carries after its link: the codebase, usually.</summary>
    private static string Parenthesised(string cell)
    {
        Match match = Bracketed().Match(Plain(cell));
        string inside = match.Success ? match.Groups["inside"].Value.Trim() : string.Empty;
        // "Pay-To-Play" sits in the same brackets on the genre cell and is not a codebase.
        return inside.Equals("Pay-To-Play", StringComparison.OrdinalIgnoreCase) ? string.Empty : inside;
    }

    /// <summary>Markup out, entities decoded, whitespace collapsed.</summary>
    private static string Plain(string markup)
        => WhiteSpace().Replace(WebUtility.HtmlDecode(Tags().Replace(markup, " ")), " ").Trim();

    private static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    [GeneratedRegex(@"href=""/World/(?<slug>[^""]*)""[^>]*>(?<name>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex NameCell();

    [GeneratedRegex(@"<a[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex LinkCell();

    [GeneratedRegex(@"telnet://(?<host>[^:""/]+):(?<port>\d+)")]
    private static partial Regex AddressLink();

    [GeneratedRegex(@"<div id=""links"">\s*<a href=""(?<url>[^""]+)""", RegexOptions.Singleline)]
    private static partial Regex WebsiteLink();

    [GeneratedRegex(@"\((?<inside>[^)]*)\)")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
