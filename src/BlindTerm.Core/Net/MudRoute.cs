namespace BlindTerm.Core.Net;

/// <summary>
/// The compass a MUD is walked with, as the short words a MUD accepts.
///
/// Directions have three spellings that mean the same thing: the letter a speedwalk is
/// written in ("n"), the word a player types ("north"), and the name said out loud when a
/// route is described ("north"). This is the one place that knows all three, so a route, the
/// breadcrumb trail and a typed command can never disagree about what "northeast" was.
/// </summary>
public static class Compass
{
    private static readonly Dictionary<string, string> ByWord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "n", ["north"] = "n",
        ["s"] = "s", ["south"] = "s",
        ["e"] = "e", ["east"] = "e",
        ["w"] = "w", ["west"] = "w",
        ["ne"] = "ne", ["northeast"] = "ne",
        ["nw"] = "nw", ["northwest"] = "nw",
        ["se"] = "se", ["southeast"] = "se",
        ["sw"] = "sw", ["southwest"] = "sw",
        ["u"] = "u", ["up"] = "u",
        ["d"] = "d", ["down"] = "d",
    };

    /// <summary>
    /// Reads a word as a direction, in any of its spellings. "North", "NORTH" and "n" are
    /// the one step; anything that is not a direction at all is nothing.
    /// </summary>
    public static bool TryParse(string? word, out string direction)
    {
        direction = string.Empty;
        if (string.IsNullOrWhiteSpace(word)) return false;
        if (!ByWord.TryGetValue(word.Trim(), out string? canonical)) return false;
        direction = canonical;
        return true;
    }

    /// <summary>The step that undoes a step: north for south, up for down.</summary>
    public static string Opposite(string direction) => direction switch
    {
        "n" => "s",
        "s" => "n",
        "e" => "w",
        "w" => "e",
        "ne" => "sw",
        "sw" => "ne",
        "nw" => "se",
        "se" => "nw",
        "u" => "d",
        "d" => "u",
        _ => throw new ArgumentException($"Not a direction: {direction}", nameof(direction)),
    };

    /// <summary>The word said out loud for a step: "northeast", not "ne".</summary>
    public static string Name(string direction) => direction switch
    {
        "n" => "north",
        "s" => "south",
        "e" => "east",
        "w" => "west",
        "ne" => "northeast",
        "nw" => "northwest",
        "se" => "southeast",
        "sw" => "southwest",
        "u" => "up",
        "d" => "down",
        _ => throw new ArgumentException($"Not a direction: {direction}", nameof(direction)),
    };
}

/// <summary>
/// A run of walking, written the way MUD players have always written one: a dot, then
/// directions with counts in front of them. ".3n2e" is three north and two east.
///
/// This is the arithmetic half of walking, and no more than that: whether the steps are
/// fired off at once or walked one room at a time is <see cref="MudWalker"/>'s business,
/// and where the steps go is the window's.
///
/// A line that is not exactly a route is not a route at all, and goes to the MUD as the
/// ordinary command it is. ".3north" is prose, ".n,n,e" has separators routes do not have,
/// and a zero count (".3n0e") is a typo rather than a leg to skip quietly.
/// </summary>
public sealed class MudRoute
{
    /// <summary>
    /// The longest walk there can be. A slipped keypress like ".999999999n" is refused
    /// rather than flooding the MUD with a billion moves.
    /// </summary>
    public const int MaximumSteps = 1000;

    private MudRoute(bool careful, IReadOnlyList<string> steps)
    {
        Careful = careful;
        Steps = steps;
    }

    /// <summary>
    /// Whether the route was written with two dots. A one-dot route is sent as fast as the
    /// connection carries it; a two-dot route is walked one room at a time, stopping if
    /// something blocks the way.
    /// </summary>
    public bool Careful { get; }

    /// <summary>The steps, expanded, as the short words a MUD accepts: "n", "se", "u".</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>The route as it would be said: "3 north, 2 east".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        int i = 0;
        while (i < Steps.Count)
        {
            int run = 1;
            while (i + run < Steps.Count && Steps[i + run] == Steps[i]) run++;
            parts.Add(run > 1 ? $"{run} {Compass.Name(Steps[i])}" : Compass.Name(Steps[i]));
            i += run;
        }
        return string.Join(", ", parts);
    }

    /// <summary>Reads a typed line as a route, or says it is not one.</summary>
    public static bool TryParse(string? line, out MudRoute? route)
    {
        route = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        string text = line.Trim();
        if (text[0] != '.') return false;

        bool careful = text.Length > 1 && text[1] == '.';
        int i = careful ? 2 : 1;
        if (i >= text.Length) return false;

        var steps = new List<string>();
        while (i < text.Length)
        {
            int count = 1;
            if (char.IsAsciiDigit(text[i]))
            {
                long value = 0;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    value = value * 10 + (text[i] - '0');
                    if (value > MaximumSteps) return false;
                    i++;
                }
                // A leg counted zero times is a typo, not one that was meant to be skipped.
                if (value == 0) return false;
                count = (int)value;
                if (i >= text.Length) return false;
            }

            string step;
            if (i + 1 < text.Length && IsDiagonal(text[i], text[i + 1]))
            {
                step = new string([char.ToLowerInvariant(text[i]), char.ToLowerInvariant(text[i + 1])]);
                i += 2;
            }
            else if (IsSingle(text[i]))
            {
                step = char.ToLowerInvariant(text[i]).ToString();
                i += 1;
            }
            else
            {
                return false;
            }

            if (steps.Count + count > MaximumSteps) return false;
            for (int k = 0; k < count; k++) steps.Add(step);
        }

        if (steps.Count == 0) return false;
        route = new MudRoute(careful, steps);
        return true;
    }

    // The diagonals are read before the single letters they begin with, because that is
    // what "se" means to everyone who writes one: southeast, not south then east.
    private static bool IsDiagonal(char first, char second)
        => (char.ToLowerInvariant(first) is 'n' or 's')
           && (char.ToLowerInvariant(second) is 'e' or 'w');

    private static bool IsSingle(char letter)
        => char.ToLowerInvariant(letter) is 'n' or 's' or 'e' or 'w' or 'u' or 'd';
}
