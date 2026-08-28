using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlindTerm.Core.Triggers;

/// <summary>How a trigger's pattern is read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerMatch
{
    /// <summary>The pattern is plain text, and matches anywhere in the line.</summary>
    Contains,

    /// <summary>
    /// The pattern is a wildcard: <c>*</c> stands for any run of characters and <c>?</c> for
    /// exactly one, and the whole line has to match. Each wildcard is remembered, so a
    /// pattern can say what part of the line it wants back.
    /// </summary>
    Wildcard,

    /// <summary>The pattern is a .NET regular expression.</summary>
    Regex,
}

/// <summary>
/// A trigger's pattern, compiled once and asked about a line many times.
///
/// All three kinds end up as the same thing -- a regular expression -- so that matching a
/// line is one decision, and the wildcards a pattern captured are available the same way
/// whichever kind wrote them. Compiling is separate from matching because a pattern typed
/// into a dialog has to be able to say what is wrong with it while the dialog is still open,
/// rather than failing silently the first time a line arrives.
/// </summary>
public sealed class TriggerPattern
{
    /// <summary>
    /// How long a single line may be matched against before the pattern is given up on.
    ///
    /// A regular expression is typed by the user, and a badly shaped one can take longer than
    /// the age of the universe on a line that nearly matches. This runs on the thread that
    /// draws the window, so "nearly matches" must not come to mean "the terminal has hung".
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>Longer than any pattern anyone needs, and short enough to compile quickly.</summary>
    public const int MaximumLength = 1000;

    private readonly Regex _regex;

    private TriggerPattern(Regex regex) => _regex = regex;

    /// <summary>
    /// Turns a written pattern into one that can be matched, or says why it cannot be.
    /// </summary>
    /// <param name="problem">
    /// A sentence to read out or show when the pattern was refused. It names what is wrong
    /// rather than quoting the regular-expression engine at someone who wrote a wildcard.
    /// </param>
    public static bool TryCompile(
        string? pattern,
        TriggerMatch match,
        bool caseSensitive,
        [NotNullWhen(true)] out TriggerPattern? compiled,
        [NotNullWhen(false)] out string? problem)
    {
        compiled = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            problem = "A trigger needs something to match.";
            return false;
        }
        if (pattern.Length > MaximumLength)
        {
            problem = $"A pattern can be at most {MaximumLength} characters.";
            return false;
        }

        string expression = match switch
        {
            TriggerMatch.Contains => Regex.Escape(pattern),
            TriggerMatch.Wildcard => FromWildcard(pattern),
            _ => pattern,
        };

        var options = RegexOptions.CultureInvariant;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;

        try
        {
            compiled = new TriggerPattern(new Regex(expression, options, MatchTimeout));
            return true;
        }
        catch (ArgumentException ex)
        {
            problem = match == TriggerMatch.Regex
                ? $"That is not a regular expression this can use. {ex.Message}"
                : "That pattern could not be used.";
            return false;
        }
    }

    /// <summary>
    /// What the line matched, or null when it did not.
    ///
    /// A pattern that takes too long is treated as not matching. There is nothing better to
    /// do with it on the line it was given, and refusing it is what keeps one awkward line
    /// from stopping everything after it from being read.
    /// </summary>
    public TriggerCapture? Match(string? line)
    {
        if (line is null) return null;
        try
        {
            Match found = _regex.Match(line);
            return found.Success ? new TriggerCapture(line, found) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// A wildcard pattern as a regular expression: the whole line has to match, and each
    /// wildcard is a group, so that what it stood for can be put into what the trigger says
    /// or sends.
    ///
    /// The whole line rather than part of it, because that is what a wildcard has meant
    /// everywhere else it has ever been written -- and because a star at each end is a
    /// clearer way to say "anywhere in the line" than a rule nobody can see.
    /// </summary>
    private static string FromWildcard(string pattern)
    {
        var builder = new StringBuilder("^");
        foreach (char c in pattern)
        {
            switch (c)
            {
                case '*': builder.Append("(.*)"); break;
                case '?': builder.Append("(.)"); break;
                default: builder.Append(Regex.Escape(c.ToString())); break;
            }
        }
        return builder.Append('$').ToString();
    }
}

/// <summary>
/// What one line matching a pattern left behind: the line itself, and whatever its wildcards
/// or capturing groups stood for.
/// </summary>
public sealed class TriggerCapture
{
    private readonly string[] _groups;

    internal TriggerCapture(string line, Match found)
    {
        Line = line;
        Matched = found.Value;
        _groups = new string[Math.Max(0, found.Groups.Count - 1)];
        for (int i = 1; i < found.Groups.Count; i++)
            _groups[i - 1] = found.Groups[i].Success ? found.Groups[i].Value : string.Empty;
    }

    /// <summary>The whole line, as it arrived.</summary>
    public string Line { get; }

    /// <summary>The part of the line the pattern itself covered.</summary>
    public string Matched { get; }

    /// <summary>What each wildcard, or each group of a regular expression, stood for.</summary>
    public IReadOnlyList<string> Groups => _groups;

    /// <summary>
    /// Fills in what a trigger was told to say or send.
    ///
    /// <c>$0</c> is the whole line, <c>$1</c> to <c>$9</c> are the wildcards in the order
    /// they were written, and <c>$$</c> is a dollar sign. A number with nothing behind it
    /// becomes nothing, which is what lets one trigger serve a line that sometimes has a
    /// second half and sometimes does not.
    /// </summary>
    public string Expand(string? template)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (!template.Contains('$')) return template;

        var builder = new StringBuilder(template.Length);
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '$' || i + 1 >= template.Length)
            {
                builder.Append(template[i]);
                continue;
            }

            char next = template[i + 1];
            if (next == '$')
            {
                builder.Append('$');
                i++;
            }
            else if (next is >= '0' and <= '9')
            {
                int index = next - '0';
                builder.Append(index == 0
                    ? Line
                    : index <= _groups.Length ? _groups[index - 1] : string.Empty);
                i++;
            }
            else
            {
                builder.Append('$');
            }
        }
        return builder.ToString();
    }
}
