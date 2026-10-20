using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace BlindTerm.Core.Mud;

/// <summary>
/// GMUD, read from its browse page and each server's own profile.
///
/// GMUD is a young directory with no API, but its pages are plain server-rendered HTML and
/// listings appear the moment somebody submits one, with no approval queue -- so it is where
/// a brand-new MUD shows up first. The browse page carries the name, genre, description and
/// this month's votes for every server in one request. The host and port are only on each
/// server's profile page, so those cost one request each, paced.
///
/// Every submission must give a hostname and port, and GMUD only lists telnet, so every
/// listing that reads cleanly is one a terminal can open.
/// </summary>
public sealed partial class GmudDirectory : IMudDirectory, IDisposable
{
    public const string Site = "https://gmud.org";

    /// <summary>
    /// Between profile requests. GMUD is one small site, and this job is the only visitor
    /// from BlindTerm it ever sees, so it should look like a person reading, not a crawler.
    /// </summary>
    private static readonly TimeSpan BetweenRequests = TimeSpan.FromSeconds(1);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _site;
    private readonly TimeSpan _pace;
    private IReadOnlyList<MudGame>? _games;

    public GmudDirectory(string? site = null, HttpClient? http = null, TimeSpan? pace = null)
    {
        _site = (string.IsNullOrWhiteSpace(site) ? Site : site).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _ownsClient = http is null;
        _pace = pace ?? BetweenRequests;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
    }

    public string Name => "GMUD";

    /// <summary>The whole list is in hand once it has been read, so there is no page to fetch.</summary>
    public int PageSizeLimit => 5000;

    public async Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default)
    {
        // GMUD's genre is whatever the submitter typed, so the genres are the ones its
        // listings actually use, not a taxonomy of its own.
        IReadOnlyList<MudGame> games = await GamesAsync(cancellationToken).ConfigureAwait(false);
        return new MudDirectoryFilters
        {
            Themes =
            [
                .. games.Select(game => game.Genre)
                        .Where(genre => genre.Length > 0)
                        .Distinct(StringComparer.CurrentCultureIgnoreCase)
                        .OrderBy(genre => genre, StringComparer.CurrentCultureIgnoreCase)
                        .Select(genre => new MudTag(genre, genre))
            ],
        };
    }

    public async Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<MudGame> games = await GamesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<MudGame> matching = games;
        if (query.OnlyConnectable) matching = matching.Where(game => game.CanConnect);
        if (query.OnlyAnswering) matching = matching.Where(game => game.IsAnswering);
        if (!string.IsNullOrWhiteSpace(query.ThemeTagId))
            matching = matching.Where(game =>
                string.Equals(game.Genre, query.ThemeTagId, StringComparison.CurrentCultureIgnoreCase));
        foreach (string word in query.Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string needle = word;
            matching = matching.Where(game =>
                game.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                game.Genre.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ||
                game.Intro.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        }

        List<MudGame> found = [.. MudSorting.Apply(matching, query.Sort)];
        return MudSorting.Page(found, query.Page, query.PerPage);
    }

    /// <summary>Every server GMUD lists, each with the address from its profile.</summary>
    public async Task<IReadOnlyList<MudGame>> GamesAsync(CancellationToken cancellationToken = default)
    {
        if (_games is not null) return _games;

        IReadOnlyList<MudGame> listed = ParseList(
            await GetAsync("/servers", cancellationToken).ConfigureAwait(false), _site);

        // ponytail: one profile request per listing, every run. Fine while GMUD lists dozens;
        // carry addresses over from --previous, as MUDStats does, if it grows to hundreds.
        var games = new List<MudGame>(listed.Count);
        foreach (MudGame game in listed)
        {
            if (games.Count > 0) await Task.Delay(_pace, cancellationToken).ConfigureAwait(false);
            try
            {
                string profile = await GetAsync("/servers/" + Uri.EscapeDataString(game.SourceId),
                    cancellationToken).ConfigureAwait(false);
                games.Add(WithProfile(game, profile));
            }
            catch (MudDirectoryException)
            {
                // One profile failing costs one address, not the list. The listing goes
                // through without it and is dropped with the other unconnectable ones.
                games.Add(game);
            }
        }

        _games = games;
        return _games;
    }

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(_site + path, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MudDirectoryException(
                    $"GMUD answered {(int)response.StatusCode} {response.ReasonPhrase}.",
                    worthRetrying: (int)response.StatusCode >= 500);
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MudDirectoryException("BlindTerm could not reach GMUD. " + ex.Message,
                worthRetrying: true, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MudDirectoryException("GMUD did not answer in time.", worthRetrying: true, inner: ex);
        }
    }

    /// <summary>
    /// Reads the browse page: one card per server, with everything but the address.
    ///
    /// One pattern per card rather than a parse of the document, the way The Mud Connector's
    /// table is read: a card that does not match is a card this does not understand and
    /// skips, which beats inventing a listing.
    /// </summary>
    internal static IReadOnlyList<MudGame> ParseList(string html, string site = Site)
    {
        ArgumentNullException.ThrowIfNull(html);
        var games = new List<MudGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match card in Card().Matches(html))
        {
            string slug = WebUtility.HtmlDecode(card.Groups["slug"].Value);
            string name = WebUtility.HtmlDecode(card.Groups["name"].Value).Trim();
            if (name.Length == 0 || slug.Length == 0 || !seen.Add(slug)) continue;

            games.Add(new MudGame
            {
                Source = "GMUD",
                SourceId = slug,
                Name = name,
                Genre = WebUtility.HtmlDecode(card.Groups["genre"].Value).Trim(),
                // Submissions run to three thousand characters, and a list entry is spoken in
                // full every time the arrow keys land on it.
                Intro = GrapevineDirectory.Trim(WebUtility.HtmlDecode(card.Groups["intro"].Value).Trim()),
                // GMUD ranks by the month, so these are this month's votes.
                MonthlyVotes = int.TryParse(card.Groups["votes"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int votes) ? votes : 0,
                ListingUrl = $"{site}/servers/{slug}",
            });
        }

        if (games.Count == 0)
            throw new MudDirectoryException("GMUD's list could not be read.");
        return games;
    }

    /// <summary>Adds the host, port, status and website from a server's profile page.</summary>
    internal static MudGame WithProfile(MudGame game, string html)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(html);

        string host = Field(html, "HOST");
        string protocol = Field(html, "PROTOCOL");
        // Only telnet is something a terminal can dial. GMUD lists nothing else today; if it
        // ever does, those listings keep no address and are dropped with the web-only games.
        if (host.Length == 0
            || (protocol.Length > 0 && !protocol.Equals("TELNET", StringComparison.OrdinalIgnoreCase))
            || !int.TryParse(Field(html, "PORT"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
            return game;

        string status = Field(html, "STATUS");
        Match website = Website().Match(html);
        return game with
        {
            Host = host,
            Port = port,
            // GMUD checks its servers itself; until it has, the status reads "unknown", and
            // that is passed on as unknown rather than guessed at.
            Availability = status.ToLowerInvariant() switch
            {
                "online" or "up" => MudAvailability.Online,
                "offline" or "down" => MudAvailability.Offline,
                _ => MudAvailability.Unknown,
            },
            ConfirmedOnline = status.Equals("online", StringComparison.OrdinalIgnoreCase)
                || status.Equals("up", StringComparison.OrdinalIgnoreCase),
            Website = website.Success ? WebUtility.HtmlDecode(website.Groups["url"].Value) : string.Empty,
        };
    }

    private static string Field(string html, string label)
    {
        Match match = Regex.Match(html, $@"<span>{label}</span>\s*<strong>(?<value>[^<]*)</strong>",
            RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value).Trim() : string.Empty;
    }

    [GeneratedRegex(
        """<article class="server[^"]*">(?:(?!</article>).)*?<a href="/servers/(?<slug>[^"/?#]+)">(?<name>[^<]*)</a>(?:(?!</article>).)*?\[(?<votes>\d+) VOTES?\](?:(?!</article>).)*?GENRE:(?<genre>[^<]*)</div>\s*<p>(?<intro>[^<]*)</p>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Card();

    [GeneratedRegex("""<a href="(?<url>https?://[^"]+)"[^>]*>\s*\[ VISIT WEBSITE \]""", RegexOptions.IgnoreCase)]
    private static partial Regex Website();

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
