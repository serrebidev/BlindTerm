namespace BlindTerm.App;

/// <summary>
/// Jumping through a long list by typing the start of a name.
///
/// A list box does have a keyboard search of its own, and it searches the line it displays.
/// That is the wrong line here: the results list reads out "Alter Aeon. 41 players. Fantasy."
/// because those are the things worth hearing while arrowing past, and a search over that
/// string quietly matches player counts and genres as well as names. Eight hundred listings is
/// also well past the point where arrowing is a way to get anywhere, so the one thing that has
/// to work is pressing A and landing on the As.
///
/// The rules are the ones every list in Windows has, because they are the ones already in
/// people's hands: a letter goes to the next name starting with it, the same letter again goes
/// to the one after that, and letters typed in quick succession build a longer prefix instead.
/// A pause of a second starts again.
///
/// Two things are added, both because MUD names are not tidy. A name beginning with punctuation
/// is still reachable by its first letter, and a name beginning with "The" is reachable by the
/// word after it -- otherwise a fifth of any MUD list lives under T.
/// </summary>
internal sealed class ListTypeahead
{
    /// <summary>
    /// How long a typed prefix stands before the next letter counts as a fresh start.
    ///
    /// A second, which is Windows' own figure. Longer would make a mistyped letter stick
    /// around and swallow the correction; shorter and nobody using a screen reader, who is
    /// waiting to hear each name before deciding, could ever type two letters in a row.
    /// </summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(1);

    private static readonly string[] Articles = ["the ", "an ", "a "];

    private string _typed = string.Empty;
    private DateTimeOffset _at;

    /// <summary>What has been typed so far and is still standing. Empty once it has lapsed.</summary>
    public string Typed => _typed;

    /// <summary>Forgets the prefix. Called when the list underneath is replaced.</summary>
    public void Reset() => _typed = string.Empty;

    /// <summary>
    /// Where a typed character should move the selection, or null to leave it alone.
    /// </summary>
    /// <param name="names">The names, in the order the list shows them.</param>
    /// <param name="from">The selected index, or -1 for nothing selected.</param>
    /// <param name="typed">The character typed.</param>
    /// <param name="now">Passed in rather than read, so the lapsing rule can be tested.</param>
    public int? Next(IReadOnlyList<string> names, int from, char typed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0) return null;

        if (now - _at > Gap) _typed = string.Empty;
        _at = now;

        char letter = char.ToLowerInvariant(typed);
        // The same letter over and over walks through the names beginning with it, rather than
        // looking for a name that starts "aaa".
        bool cycling = _typed.Length > 0 && _typed.All(already => already == letter);
        string prefix = cycling ? letter.ToString() : _typed + letter;
        _typed = prefix;

        // A single letter always moves on, so pressing it twice never sits still. A longer
        // prefix starts where it is, so refining a search does not skip the name it is on.
        int? found = Find(names, prefix, prefix.Length == 1 ? from + 1 : Math.Max(0, from));
        if (found is not null) return found;

        // Nothing starts with what has been typed. Rather than leaving somebody stuck typing
        // into a dead prefix, the last letter is taken as a fresh start -- which is what they
        // will do themselves a second later anyway.
        if (prefix.Length == 1) return null;
        _typed = letter.ToString();
        return Find(names, _typed, from + 1);
    }

    private static int? Find(IReadOnlyList<string> names, string prefix, int start)
    {
        // Names as they read first, then names with the punctuation and the article taken off.
        // In that order, because "The Inquisition" under T is what somebody who can see the
        // list expects, and under I is what somebody who was told the name expects.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int step = 0; step < names.Count; step++)
            {
                int at = (start + step) % names.Count;
                if (at < 0) at += names.Count;
                string name = names[at] ?? string.Empty;
                string key = pass == 0 ? name : Simplify(name);
                if (key.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) return at;
            }
        }
        return null;
    }

    /// <summary>A name without its leading punctuation or its leading article.</summary>
    private static string Simplify(string name)
    {
        int at = 0;
        while (at < name.Length && !char.IsLetterOrDigit(name[at])) at++;
        string trimmed = name[at..];

        foreach (string article in Articles)
        {
            if (trimmed.StartsWith(article, StringComparison.CurrentCultureIgnoreCase))
                return trimmed[article.Length..].TrimStart();
        }
        return trimmed;
    }
}
