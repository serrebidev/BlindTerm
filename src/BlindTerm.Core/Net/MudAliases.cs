namespace BlindTerm.Core.Net;

/// <summary>
/// One shortcut for a line typed at a MUD: the pattern that is typed, and what is sent
/// instead.
///
/// The pattern is the whole line, exactly. It may end in a single "*" that stands for
/// whatever follows: "sh *" matched against "sh goblin" makes "goblin" the first capture.
/// The replacement is filled in before it is sent: "$0" is the whole line that was typed,
/// "$*" is everything the "*" stood for, and "$1" onwards are its words, one by one -- the
/// same spelling a trigger's fields use, so there is only one convention to learn.
/// </summary>
public sealed class MudAlias
{
    /// <summary>What is typed: "sh *" or "recall". Case does not have to match.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>What is sent instead, with "$1" and friends filled in.</summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>The longest shortcut that can be stored.</summary>
    public const int MaximumPatternLength = 200;

    /// <summary>The longest expansion that can be stored, the same bound a trigger lives under.</summary>
    public const int MaximumReplacementLength = 2000;

    public MudAlias Copy() => new() { Pattern = Pattern, Replacement = Replacement };

    /// <summary>
    /// Brings an alias back within its limits. This runs over a file that may have been
    /// edited by hand, like everything else in the settings: an alias too long in it is
    /// one to trim, not a reason to lose the file.
    /// </summary>
    public void Clamp()
    {
        Pattern = Trim(Pattern, MaximumPatternLength);
        Replacement = Trim(Replacement, MaximumReplacementLength);
    }

    private static string Trim(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string trimmed = text.Trim();
        return trimmed.Length > limit ? trimmed[..limit] : trimmed;
    }
}

/// <summary>
/// The aliases in force, in the order they were written, and the filling-in of one typed
/// line against them.
///
/// The book wraps the settings' own list rather than copying it: what is added or removed
/// here is what gets saved, with no second list to fall out of step.
/// </summary>
public sealed class MudAliasBook
{
    private readonly List<MudAlias> _aliases;

    public MudAliasBook(List<MudAlias> aliases)
        => _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));

    /// <summary>Every alias, in the order they are tried.</summary>
    public IReadOnlyList<MudAlias> All => _aliases;

    /// <summary>
    /// Writes an alias, replacing the one with the same pattern if there is one. An alias
    /// whose pattern is already there is not added twice, because the first match is the
    /// one that wins and the second could never be reached.
    /// </summary>
    public void Set(string pattern, string replacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentNullException.ThrowIfNull(replacement);
        var alias = new MudAlias { Pattern = pattern.Trim(), Replacement = replacement.Trim() };
        alias.Clamp();
        int existing = _aliases.FindIndex(other =>
            string.Equals(other.Pattern, alias.Pattern, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _aliases[existing] = alias;
        else _aliases.Add(alias);
    }

    /// <summary>Takes an alias away. Says whether there was one to take.</summary>
    public bool Remove(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return _aliases.RemoveAll(other =>
            string.Equals(other.Pattern, pattern.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>
    /// Fills a typed line in against the first alias that matches it, or leaves the line
    /// alone. One pass only: an expansion that happens to look like another alias is sent
    /// as written, because it is what the alias's author wrote, not what the player typed.
    /// </summary>
    public bool TryExpand(string line, out string expanded)
    {
        expanded = line;
        if (string.IsNullOrWhiteSpace(line)) return false;
        string typed = line.Trim();

        foreach (MudAlias alias in _aliases)
        {
            string pattern = alias.Pattern;
            int star = pattern.IndexOf('*');
            if (star < 0)
            {
                if (!string.Equals(typed, pattern, StringComparison.OrdinalIgnoreCase)) continue;
                expanded = Fill(alias.Replacement, typed, string.Empty, []);
                return true;
            }

            string before = pattern[..star];
            string after = pattern[(star + 1)..];
            if (typed.Length < before.Length + after.Length) continue;
            if (!typed.StartsWith(before, StringComparison.OrdinalIgnoreCase)
                || !typed.EndsWith(after, StringComparison.OrdinalIgnoreCase)) continue;

            string rest = typed[before.Length..(typed.Length - after.Length)];
            string[] words = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            expanded = Fill(alias.Replacement, typed, rest.Trim(), words);
            return true;
        }
        return false;
    }

    private static string Fill(string replacement, string wholeLine, string rest, string[] words)
    {
        var filled = new System.Text.StringBuilder(replacement);
        filled.Replace("$*", rest);
        filled.Replace("$0", wholeLine);
        // Highest first. Captures stop at nine words, so there is no "$12": a replacement
        // that writes one gets "$1" filled and the "2" left standing, which is the honest
        // reading of what was typed.
        for (int i = Math.Min(words.Length, 9); i >= 1; i--)
            filled.Replace($"${i}", words[i - 1]);
        return filled.ToString();
    }
}

/// <summary>Which of the typed commands for managing aliases and triggers a line is, if it is one.</summary>
public enum MudClientCommand
{
    /// <summary>Not one of them: an ordinary line for the MUD.</summary>
    None,

    /// <summary>"/alias pattern = replacement": write an alias.</summary>
    DefineAlias,

    /// <summary>"/unalias pattern": take one away.</summary>
    RemoveAlias,

    /// <summary>"/aliases": read back what has been made.</summary>
    ListAliases,

    /// <summary>"/trigger pattern = command": watch for a line, and send a command back.</summary>
    DefineTrigger,

    /// <summary>"/untrigger pattern": stop watching for one.</summary>
    RemoveTrigger,

    /// <summary>"/triggers": read back the ones written this way.</summary>
    ListTriggers,
}

/// <summary>
/// The commands a player types to manage aliases and triggers instead of opening a
/// dialog, read off the line.
///
/// These belong to the client rather than the MUD, so a line that is one of them is never
/// sent onwards. Only the exact words count: "/alias" with nothing after it is not a
/// definition, and it goes to the MUD like any other line it might have a meaning for.
/// </summary>
public static class MudClientCommands
{
    /// <summary>
    /// Reads a line as one of the alias commands. On a definition, <paramref name="pattern"/>
    /// and <paramref name="replacement"/> are its two halves; on a removal,
    /// <paramref name="pattern"/> is the one named; on a listing, both are empty.
    /// </summary>
    public static MudClientCommand Parse(string? line, out string pattern, out string replacement)
    {
        pattern = string.Empty;
        replacement = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return MudClientCommand.None;
        string text = line.Trim();

        if (text.Equals("/aliases", StringComparison.OrdinalIgnoreCase))
            return MudClientCommand.ListAliases;

        if (text.Equals("/triggers", StringComparison.OrdinalIgnoreCase))
            return MudClientCommand.ListTriggers;

        if (StartsWithWord(text, "/unalias"))
        {
            string named = text["/unalias".Length..].Trim();
            if (named.Length == 0) return MudClientCommand.None;
            pattern = named;
            return MudClientCommand.RemoveAlias;
        }

        if (StartsWithWord(text, "/untrigger"))
        {
            string named = text["/untrigger".Length..].Trim();
            if (named.Length == 0) return MudClientCommand.None;
            pattern = named;
            return MudClientCommand.RemoveTrigger;
        }

        if (StartsWithWord(text, "/alias"))
        {
            return ParseDefinition(text["/alias".Length..], MudClientCommand.DefineAlias,
                out pattern, out replacement);
        }

        if (StartsWithWord(text, "/trigger"))
        {
            return ParseDefinition(text["/trigger".Length..], MudClientCommand.DefineTrigger,
                out pattern, out replacement);
        }

        return MudClientCommand.None;
    }

    /// <summary>
    /// Reads "pattern = what happens" after the command word. Both halves must be there:
    /// half a definition is a line for the MUD, not a broken command of BlindTerm's.
    /// </summary>
    private static MudClientCommand ParseDefinition(string rest, MudClientCommand kind,
        out string pattern, out string replacement)
    {
        pattern = string.Empty;
        replacement = string.Empty;
        string body = rest.Trim();
        int equals = body.IndexOf('=');
        if (equals <= 0) return MudClientCommand.None;
        string left = body[..equals].Trim();
        string right = body[(equals + 1)..].Trim();
        if (left.Length == 0 || right.Length == 0) return MudClientCommand.None;
        pattern = left;
        replacement = right;
        return kind;
    }

    /// <summary>
    /// Whether a line begins with a word. "/aliases" begins with the word "/alias" in
    /// letters alone, which is why the listing is checked before the definition.
    /// </summary>
    private static bool StartsWithWord(string text, string word)
        => text.Length > word.Length
           && text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
           && char.IsWhiteSpace(text[word.Length]);
}
