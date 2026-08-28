namespace BlindTerm.Core.Speech;

/// <summary>
/// Picks what in a batch of transcript changes is worth speaking: a line that is not blank,
/// and that does not already say, for that line, what was last announced for it.
///
/// A program repainting its frame rewrites the same lines with the same words several times a
/// second, and none of that is news. A line that fills in, or changes, is spoken once.
/// </summary>
public sealed class LineNews
{
    private readonly Dictionary<int, string> _announced = new();
    private string? _pendingCommandEcho;
    private string? _pendingPromptEcho;

    /// <summary>Suppresses the shell's one-line echo of a command already spoken while typing.</summary>
    public void SuppressCommandEcho(string command)
        => _pendingCommandEcho = string.IsNullOrWhiteSpace(command) ? null : command.Trim();

    /// <summary>
    /// Suppresses the transcript copy of a prompt that has already been spoken as the current
    /// line.
    ///
    /// A prompt does not end in a newline, so it is read while the cursor is still sitting on
    /// it. It becomes a transcript line only later, when something moves the cursor past that
    /// row -- another program starting, or a connection taking the window over. Those are the
    /// same words a second time, not news.
    /// </summary>
    public void SuppressPromptEcho(string prompt)
        => _pendingPromptEcho = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();

    /// <summary>
    /// How many lines of announcement history to keep. The transcript can run to a hundred
    /// thousand lines and only the recent end is ever rewritten, so remembering all of it
    /// would be a leak for no benefit.
    /// </summary>
    private const int Remembered = 4096;

    public IReadOnlyList<string> News(TerminalUpdate update)
    {
        // A batch can both append a line and rewrite it. Collapse by line first, so each one
        // is considered once, in transcript order, holding the text it ended the batch with.
        // Appends are laid down before edits because an edit is the later change: the
        // assembler produces a line and only then patches it, and speaking the text it was
        // built with rather than the text it was left with would announce a half-drawn line.
        var byLine = new SortedDictionary<int, string>();
        for (int i = 0; i < update.NewLines.Count; i++) byLine[update.FirstNewLine + i] = update.NewLines[i];
        foreach (var edit in update.Edits) byLine[edit.Line] = edit.Text;

        var spoken = new List<string>();
        foreach (var (line, text) in byLine)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (_pendingCommandEcho is { } command && IsCommandEcho(text, command))
            {
                _pendingCommandEcho = null;
                _announced[line] = text;
                continue;
            }
            if (_pendingPromptEcho is { } prompt
                && text.Trim().Equals(prompt, StringComparison.Ordinal))
            {
                _pendingPromptEcho = null;
                _announced[line] = text;
                continue;
            }
            if (_announced.TryGetValue(line, out string? was) && was == text) continue;

            _announced[line] = text;
            spoken.Add(text);
        }

        Forget(byLine.Count > 0 ? byLine.Keys.Max() : 0);
        return spoken;
    }

    private static bool IsCommandEcho(string line, string command)
    {
        string value = line.TrimEnd();
        return value.Equals(command, StringComparison.Ordinal)
               || value.EndsWith(" " + command, StringComparison.Ordinal);
    }

    private void Forget(int newest)
    {
        if (_announced.Count <= Remembered * 2) return;
        int floor = newest - Remembered;
        foreach (int line in _announced.Keys.Where(l => l < floor).ToList()) _announced.Remove(line);
    }

    /// <summary>
    /// Forgets everything. The screen has been wiped or the scrollback thrown away, so line
    /// numbers no longer mean what they meant and what was said about them is not a guide to
    /// what is worth saying now.
    /// </summary>
    public void Reset() => _announced.Clear();
}
