using BlindTerm.Core.Mud;

namespace BlindTerm.Cli;

/// <summary>
/// Builds the list of MUDs that BlindTerm ships to everybody.
///
/// This is the half of the arrangement that holds the API key. It runs on a schedule,
/// somewhere the key can be a secret -- a CI job, a machine of your own -- reads both
/// directories, merges them, and writes one file. BlindTerm downloads that file and asks
/// nobody for anything. See <see cref="MudFeed"/> for why it is a whole file rather than a
/// proxy, and <see cref="MudMerge"/> for why there are two directories.
///
/// The key is read from the environment, never from an argument, because an argument ends up
/// in a shell history, a process list and a CI log.
/// </summary>
internal static class Directory
{
    /// <summary>Where the key comes from.</summary>
    public const string KeyVariable = "MUDVERSE_API_KEY";

    /// <summary>
    /// How far into any one ordering this will go before giving up on it.
    ///
    /// Three pages of twenty, so the deepest offset asked of MUDVerse is sixty. Past roughly
    /// there its answers stop arriving at all -- see <see cref="Harvest"/>, where the reason
    /// this number is small is written down.
    /// </summary>
    private const int ShallowPages = 3;

    /// <summary>
    /// How many MUDStats worlds get their address looked up in one run.
    ///
    /// MUDStats' table has everything except the host and port, which live on each world's
    /// own page -- one request each, on somebody else's server. So they are fetched a few at
    /// a time, and every address already found is carried over from the last run. The set
    /// fills in over a day of runs and after that only genuinely new worlds cost anything.
    /// </summary>
    private const int AddressesPerRun = 60;

    /// <summary>
    /// Paced to stay inside MUDVerse's sixty-a-minute allowance with room to spare, and to be
    /// an unremarkable visitor to MUDStats rather than a load on it.
    /// </summary>
    private static readonly TimeSpan BetweenRequests = TimeSpan.FromSeconds(1.2);

    public static int Run(string[] args)
    {
        string output = "mud-directory.json";
        string? endpoint = null, previous = null, mudstats = null;
        bool quiet = false, skipStats = false, statsOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": output = Next(args, ref i); break;
                case "--endpoint": endpoint = Next(args, ref i); break;
                case "--previous": previous = Next(args, ref i); break;
                case "--mudstats": mudstats = Next(args, ref i); break;
                case "--no-mudstats": skipStats = true; break;
                case "--mudstats-only": statsOnly = true; break;
                case "--quiet": quiet = true; break;
                default:
                    Console.Error.WriteLine($"directory: unexpected argument '{args[i]}'");
                    return 2;
            }
        }

        try
        {
            // A check that MUDStats is still readable, which needs no key and changes nothing.
            // Worth having on its own: when that scrape breaks, this says so in one line.
            if (statsOnly) return Report(mudstats).GetAwaiter().GetResult();

            string? key = Environment.GetEnvironmentVariable(KeyVariable);
            if (string.IsNullOrWhiteSpace(key))
            {
                Console.Error.WriteLine($"directory: set {KeyVariable} to a MUDVerse API key.");
                Console.Error.WriteLine("directory: get one at " + MudVerseDirectory.ApiKeyPage);
                return 2;
            }

            MudFeed feed = Build(key, endpoint, mudstats, previous, skipStats, quiet)
                .GetAwaiter().GetResult();
            if (feed.Games.Count == 0)
            {
                // Publishing an empty list would replace a working one with nothing, and every
                // BlindTerm would then show "no MUDs matched" until the next run.
                Console.Error.WriteLine("directory: no games; refusing to write an empty list.");
                return 1;
            }

            string json = feed.ToJson();
            Write(output, json);
            Console.Error.WriteLine(
                $"directory: {feed.Games.Count} games "
                + $"({feed.Games.Count(game => game.AveragePlayers is not null)} with activity figures), "
                + $"{feed.Themes.Count} genres, {json.Length / 1024} KB -> {output}");
            return 0;
        }
        catch (MudDirectoryException ex)
        {
            Console.Error.WriteLine("directory: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> Report(string? site)
    {
        using var stats = new MudStatsDirectory(site);
        IReadOnlyList<MudGame> worlds = await stats.WorldsAsync();

        Console.Error.WriteLine($"directory: MUDStats returned {worlds.Count} worlds");
        Console.Error.WriteLine($"directory:   {worlds.Count(w => w.Availability == MudAvailability.Online)} up, "
            + $"{worlds.Count(w => w.Availability == MudAvailability.Offline)} down, "
            + $"{worlds.Count(w => w.Availability == MudAvailability.Dead)} dead");
        Console.Error.WriteLine($"directory:   {worlds.Count(w => w.AveragePlayers is not null)} with a 30-day average, "
            + $"{worlds.Count(w => w.YearOpened is not null)} with a year, "
            + $"{worlds.Count(w => w.Genre.Length > 0)} with a genre");

        // Deliberately not MudGame.Summary: a world here has no address yet, because the
        // table does not carry one, and Summary would call every one of them "web only".
        foreach (MudGame world in MudSorting.Apply(worlds, MudDirectorySort.BusiestAverage).Take(10))
            Console.WriteLine(
                $"{world.Name}: {world.AveragePlayers} on average, "
                + $"{world.PlayersOnline?.ToString() ?? "no"} now, peak {world.PeakPlayers}, "
                + $"{world.Genre}{(world.YearOpened is int year ? ", opened " + year : "")}");

        // Three addresses, to prove the other half of the scrape still works. The table has
        // no host or port; those live on each world's own page.
        foreach (MudGame world in worlds.Where(w => w.Availability == MudAvailability.Online).Take(3))
        {
            var found = await stats.AddressAsync(world.SourceId);
            Console.WriteLine(found is (string host, int port, _)
                ? $"{world.Name}: {host} port {port}"
                : $"{world.Name}: no address on its page");
            await Task.Delay(BetweenRequests);
        }

        // Every count coming back zero is what a quietly broken scrape looks like: the request
        // still succeeds and every row parses to nothing.
        return worlds.Count > 0 && worlds.Any(world => world.AveragePlayers is not null) ? 0 : 1;
    }

    private static async Task<MudFeed> Build(string key, string? endpoint, string? mudstatsSite,
        string? previous, bool skipStats, bool quiet)
    {
        using var mudverse = new MudVerseDirectory(key, endpoint);
        // Always reported, even under --quiet: a run that is retrying is a run in trouble, and
        // the log should say so while it is happening rather than only if it finally fails.
        mudverse.Retrying += trouble => Console.Error.WriteLine("directory: " + trouble);

        MudDirectoryFilters filters = await mudverse.FiltersAsync();
        var feed = new MudFeed
        {
            Generated = DateTimeOffset.UtcNow,
            Themes = [.. filters.Themes],
            Types = [.. filters.GameTypes],
            Roleplaying = [.. filters.Roleplaying],
        };

        List<MudGame> games = [.. (await Harvest(mudverse, quiet)).Values];
        if (!skipStats)
        {
            try
            {
                games = await AddStatistics(games, feed, mudstatsSite, previous, quiet);
            }
            catch (MudDirectoryException ex)
            {
                // MUDStats is the second opinion, not the list. Losing it costs the activity
                // figures for one run; it must never cost the run.
                Console.Error.WriteLine("directory: MUDStats unavailable, publishing without activity "
                    + "figures: " + ex.Message);
            }
        }

        feed.Games = [.. games.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)];
        feed.Sources = [.. feed.Games.Select(game => game.Source).Distinct().OrderBy(name => name)];
        return feed;
    }

    /// <summary>
    /// Everything MUDVerse will actually serve, gathered without ever paging deep.
    ///
    /// MUDVerse cannot be enumerated by walking its pages. Its cost grows with the offset,
    /// not the page size: measured against the live API, page 1 came back in about two
    /// seconds, page 3 in seven, and every page past that timed out and kept timing out. So
    /// walking to the end of two hundred listings is not slow, it is impossible.
    ///
    /// What works is asking the same question several different ways and never going far into
    /// any of the answers. A game near the top of "most reviewed" is somewhere else entirely
    /// in "recently online", so the union of a few shallow pages across several orderings
    /// covers most of the directory while every single request stays in the fast range. What
    /// it misses is the long tail -- games nobody voted for, reviewed or updated recently --
    /// and MUDStats has those, with their statistics.
    /// </summary>
    private static async Task<Dictionary<string, MudGame>> Harvest(MudVerseDirectory mudverse, bool quiet)
    {
        // Deliberately different questions, so their shallow pages overlap as little as
        // possible. Twenty to a page keeps the deepest offset at sixty.
        MudDirectorySort[] askedFor =
        [
            MudDirectorySort.TopVoted,
            MudDirectorySort.RecentlyOnline,
            MudDirectorySort.MostReviewed,
            MudDirectorySort.Newest,
            MudDirectorySort.RecentlyUpdated,
        ];

        var described = new Dictionary<string, MudGame>(StringComparer.Ordinal);
        int total = 0;
        foreach (MudDirectorySort sort in askedFor)
        {
            for (int page = 1; page <= ShallowPages; page++)
            {
                MudDirectoryPage batch;
                try
                {
                    batch = await mudverse.SearchAsync(new MudDirectoryQuery
                    {
                        Sort = sort,
                        Page = page,
                        PerPage = 20,
                    });
                }
                catch (MudDirectoryException ex)
                {
                    // The expected way for this to end, not an error. This ordering has gone
                    // as deep as MUDVerse will serve it; the next ordering starts from the top
                    // again, where it is fast.
                    if (!quiet) Console.Error.WriteLine($"directory: MUDVerse {sort} stopped at page {page}: {ex.Message}");
                    break;
                }

                foreach (MudGame game in batch.Games) described[game.SourceId] = game;
                total = Math.Max(total, batch.Total);
                if (!batch.HasMore || batch.Games.Count == 0) break;
                await Task.Delay(BetweenRequests);
            }
            if (!quiet) Console.Error.WriteLine($"directory: MUDVerse after {sort}: {described.Count} games");
        }

        if (total > 0)
            Console.Error.WriteLine($"directory: MUDVerse gave {described.Count} of the {total} it says it has");
        return described;
    }

    private static async Task<List<MudGame>> AddStatistics(List<MudGame> games, MudFeed feed,
        string? site, string? previous, bool quiet)
    {
        using var stats = new MudStatsDirectory(site);
        IReadOnlyList<MudGame> worlds = await stats.WorldsAsync();
        if (!quiet) Console.Error.WriteLine($"directory: MUDStats returned {worlds.Count} worlds");

        (IReadOnlyList<MudGame> merged, IReadOnlyList<MudGame> unmatched) = MudMerge.Combine(games, worlds);
        var result = new List<MudGame>(merged);

        // Addresses already found, so a world is only ever looked up once however many times
        // this job runs.
        Dictionary<string, MudGame> known = await Known(previous);

        // Only worlds that are actually up. Chasing an address for something that has not
        // answered in twelve years spends a request on a listing nobody can connect to.
        List<MudGame> wanted =
        [
            .. unmatched
                .Where(world => world.Availability == MudAvailability.Online)
                .OrderByDescending(world => world.AveragePlayers ?? 0)
        ];

        int carried = 0, fetched = 0;
        foreach (MudGame world in wanted)
        {
            if (known.TryGetValue(world.SourceId, out MudGame? already) && already.CanConnect)
            {
                result.Add(world with
                {
                    Host = already.Host,
                    Port = already.Port,
                    TlsPort = already.TlsPort,
                    Website = already.Website,
                    Intro = already.Intro,
                });
                carried++;
                continue;
            }

            if (fetched >= AddressesPerRun) continue;
            fetched++;
            var found = await stats.AddressAsync(world.SourceId);
            await Task.Delay(BetweenRequests);
            if (found is not (string host, int port, string website)) continue;

            result.Add(world with { Host = host, Port = port, Website = website });
        }

        if (!quiet)
            Console.Error.WriteLine(
                $"directory: {merged.Count(game => game.AveragePlayers is not null)} games gained activity "
                + $"figures; {unmatched.Count} MUDStats-only worlds, {carried} addresses carried over, "
                + $"{fetched} looked up this run");

        // MUDStats' genres and server types are its own, and there are two hundred of them.
        // Folding them in is what makes "Dresden Files" or "MUCK" something you can pick.
        feed.Themes = Fold(feed.Themes, result.Select(game => game.Genre));
        feed.Types = Fold(feed.Types, result.Select(game => game.GameType));
        return result;
    }

    /// <summary>
    /// Adds names nothing had a tag for yet, keeping the identifiers a source already gave.
    ///
    /// A tag that came from MUDVerse keeps its numeric identifier, because that is what its
    /// API filters on. One that exists only in the merged list is keyed by its own name,
    /// which is all the published list ever needs: everything reading that filters locally.
    /// </summary>
    private static List<MudTag> Fold(List<MudTag> existing, IEnumerable<string> names)
    {
        var byName = new Dictionary<string, MudTag>(StringComparer.CurrentCultureIgnoreCase);
        foreach (MudTag tag in existing) byName.TryAdd(tag.Name, tag);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            byName.TryAdd(name, new MudTag(name, name));
        }
        return [.. byName.Values.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The previously published list, by MUDStats identifier, or nothing.</summary>
    private static async Task<Dictionary<string, MudGame>> Known(string? previous)
    {
        var known = new Dictionary<string, MudGame>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(previous)) return known;

        string json;
        try
        {
            if (previous.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                json = await http.GetStringAsync(previous);
            }
            else if (File.Exists(previous))
            {
                json = await File.ReadAllTextAsync(previous);
            }
            else
            {
                return known;
            }

            foreach (MudGame game in MudFeed.FromJson(json).Games)
                if (game.Source == "MUDStats") known[game.SourceId] = game;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or IOException or MudDirectoryException)
        {
            // No previous list is the first run, and a first run just does more work.
            Console.Error.WriteLine("directory: no usable previous list (" + ex.Message + ")");
        }
        return known;
    }

    /// <summary>
    /// Written whole, then moved into place, so a job that dies halfway never leaves a
    /// truncated list where a good one was.
    /// </summary>
    private static void Write(string path, string json)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) System.IO.Directory.CreateDirectory(folder);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"directory: {args[i]} needs a value");
        return args[++i];
    }
}
