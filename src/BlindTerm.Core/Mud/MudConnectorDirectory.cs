using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BlindTerm.Core.Mud;

/// <summary>
/// The Mud Connector, read from its Big List.
///
/// TMC has been listing MUDs since 1994 and its Big List is the largest single source of
/// addresses anywhere: six hundred and sixty-odd host-and-port pairs, with a website and a
/// live connect status beside each, in one request. There is no API, but there does not need
/// to be one -- the list is a single ordinary table, and it is one page rather than six
/// hundred.
///
/// That is exactly the gap in the other sources. MUDVerse cannot be paged through far enough
/// to reach most of what it holds; MUDStats knows two thousand worlds and their player
/// counts but keeps each address on its own page, one request at a time. TMC hands over the
/// addresses in bulk, so a MUDStats world with twenty years of statistics and no address can
/// be given one without fetching anything else.
///
/// Its ranking has been inactive since 2021, so the rank is read and kept but nothing is
/// ordered by it.
/// </summary>
public sealed partial class MudConnectorDirectory : IMudDirectory, IDisposable
{
    public const string Site = "https://www.mudconnect.com";

    /// <summary>The whole list, as one page. There is no paging to do.</summary>
    private const string ListPath = "/cgi-bin/search.cgi?mode=tmc_biglist";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _site;
    private IReadOnlyList<MudGame>? _games;

    public MudConnectorDirectory(string? site = null, HttpClient? http = null)
    {
        _site = (string.IsNullOrWhiteSpace(site) ? Site : site).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
    }

    public string Name => "The Mud Connector";

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        // The Big List carries no genre. TMC has genres on each game's own page, which is six
        // hundred requests for something two other directories already publish in one.
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
                game.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        List<MudGame> found = [.. MudSorting.Apply(matching, query.Sort)];
        return MudSorting.Page(found, query.Page, query.PerPage);
    }

    public async Task<IReadOnlyList<MudGame>> GamesAsync(CancellationToken cancellationToken = default)
    {
        if (_games is not null) return _games;

        string html;
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(_site + ListPath, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"The Mud Connector answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                    worthRetrying: (int)response.StatusCode >= 500);
            html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not reach The Mud Connector. " + ex.Message,
                worthRetrying: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException("The Mud Connector did not answer in time.",
                worthRetrying: true, inner: ex);
        }

        _games = Parse(html, _site);
        return _games;
    }

    /// <summary>
    /// Reads the Big List table.
    ///
    /// One pattern over the whole row rather than a parse of the document, because the row is
    /// rigid -- rank, name, telnet link, play link, website, status -- and a row that does not
    /// match it is a row this does not understand and skips. That is the right failure: losing
    /// one listing beats inventing one.
    /// </summary>
    internal static IReadOnlyList<MudGame> Parse(string html, string site = Site)
    {
        ArgumentNullException.ThrowIfNull(html);
        var games = new List<MudGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match row in Row().Matches(html))
        {
            string name = WebUtility.HtmlDecode(row.Groups["name"].Value).Trim();
            string host = row.Groups["host"].Value.Trim();
            if (name.Length == 0 || host.Length == 0) continue;
            if (!int.TryParse(row.Groups["port"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int port) || port is < 1 or > 65535) continue;

            string slug = WebUtility.HtmlDecode(row.Groups["slug"].Value);
            if (!seen.Add(slug.Length > 0 ? slug : name)) continue;

            string status = WebUtility.HtmlDecode(row.Groups["status"].Value).Trim();
            int? rank = int.TryParse(row.Groups["rank"].Value, out int placed) && placed > 0 ? placed : null;

            games.Add(new MudGame
            {
                Source = "The Mud Connector",
                SourceId = slug.Length > 0 ? slug : name,
                Name = name,
                Host = host,
                Port = port,
                // TMC tries each host as it builds the page, so this is a real answer about
                // right now rather than a field somebody filled in years ago.
                Availability = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
                    ? MudAvailability.Online
                    : status.Length > 0 ? MudAvailability.Offline : MudAvailability.Unknown,
                ConfirmedOnline = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase),
                Website = Website(row.Groups["website"].Value),
                // Kept, but nothing sorts by it: TMC's ranking stopped moving in 2021 and an
                // ordering that has not changed in five years is not a popularity measure.
                Rank = rank,
                ListingUrl = $"{site}/cgi-bin/search.cgi?mode=mud_listing&mud={slug}",
            });
        }

        if (games.Count == 0)
            throw new MudDirectoryException("The Mud Connector's list could not be read.");
        return games;
    }

    /// <summary>
    /// The real address out of TMC's redirect wrapper, which carries it in a url parameter.
    /// </summary>
    private static string Website(string href)
    {
        if (href.Length == 0) return string.Empty;
        string decoded = WebUtility.HtmlDecode(href);
        int at = decoded.IndexOf("url=", StringComparison.OrdinalIgnoreCase);
        string url = at >= 0 ? decoded[(at + 4)..] : decoded;
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : string.Empty;
    }

    [GeneratedRegex(
        """<tr>\s*<td>(?<rank>\d*)</td>.*?mode=mud_listing&mud=(?<slug>[^']*)'[^>]*>(?<name>[^<]*)</a>.*?url=telnet://(?<host>[^:']+):(?<port>\d+)'.*?(?:<td><a href='(?<website>[^']*)'[^>]*>[^<]*</a></td>\s*)?<td>(?<status>[^<]*)</td>\s*</tr>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Row();

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
