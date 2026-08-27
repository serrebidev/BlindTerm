namespace BlindTerm.Core.Speech;

/// <summary>What screen mode decided to say about one frame.</summary>
public readonly record struct ScreenAnnouncement(string Text, SpeechPriority Priority)
{
    public bool IsEmpty => string.IsNullOrEmpty(Text);
    public static readonly ScreenAnnouncement None = new(string.Empty, SpeechPriority.Normal);
}

/// <summary>
/// Decides what to speak while a full-screen program owns the screen.
///
/// The rule is: follow the cursor, not the screen. That distinction is the whole difference
/// between a usable editor and an unusable one. Arrowing down a document in nano changes the
/// cursor row, the status bar and often the shortcut bar as well; the only one of those the
/// user asked to hear is the line they moved onto. Speaking everything that changed reads the
/// furniture out on every keystroke, and speaking the first few changed rows -- which is what
/// the macOS original does -- reads the wrong thing in the wrong order.
///
/// So: a cursor that moves to another row speaks that row. A cursor that moves along a row
/// speaks what it crossed, as an edit field would. A row that changes underneath the cursor
/// is the text being typed or deleted, and speaks. Everything else -- status bars, clocks,
/// other panes, htop's meters -- changes silently and is read on demand.
/// </summary>
public sealed class ScreenNews
{
    private string[] _previous = [];
    private int _row = -1;
    private int _column = -1;
    private bool _started;

    /// <summary>
    /// Rows away from the cursor are silent by default. Turning this on speaks them too,
    /// which is occasionally what someone watching a build wants and is intolerable in an
    /// editor.
    /// </summary>
    public bool SpeakOffCursorChanges { get; set; }

    /// <summary>Forgets the screen, so the next frame is announced as a fresh arrival.</summary>
    public void Reset()
    {
        _previous = [];
        _row = _column = -1;
        _started = false;
    }

    public ScreenAnnouncement News(string[] screen, int cursorRow, int cursorColumn)
    {
        var previous = _previous;
        int wasRow = _row;

        _previous = screen;
        _row = cursorRow;
        _column = cursorColumn;

        string current = RowAt(screen, cursorRow);

        // The program has just taken the screen. Reading the whole thing out would be a wall
        // of text; the line the cursor is on is where the user is.
        if (!_started)
        {
            _started = true;
            return current.Trim().Length == 0 ? ScreenAnnouncement.None : Speak(current, SpeechPriority.Now);
        }

        // Moved to another row: that row is the answer, and it interrupts, because the user
        // pressed a key and is waiting to hear where they landed.
        if (cursorRow != wasRow)
            return current.Trim().Length == 0
                ? new ScreenAnnouncement($"blank, line {cursorRow + 1}", SpeechPriority.Now)
                : Speak(current, SpeechPriority.Now);

        string before = RowAt(previous, cursorRow);

        // Same row, and it changed under the cursor: something was typed or deleted. Do not
        // speak the inserted character here. NVDA and JAWS own keyboard echo and know whether
        // the user enabled character, word, or no echo; announcing it ourselves would always
        // override that preference and commonly produce duplicate characters in nano.
        if (before != current)
        {
            return ScreenAnnouncement.None;
        }

        // Horizontal movement is left to NVDA or JAWS as well. A custom live surface has no
        // reader-managed caret, so synthesizing character or word echo would override settings.

        if (SpeakOffCursorChanges)
        {
            var changed = new List<string>();
            for (int i = 0; i < screen.Length; i++)
            {
                if (i == cursorRow) continue;
                if (RowAt(previous, i) != screen[i] && screen[i].Trim().Length > 0)
                    changed.Add(screen[i].Trim());
            }
            if (changed.Count > 0) return Speak(string.Join("\n", changed), SpeechPriority.Normal);
        }

        return ScreenAnnouncement.None;
    }

    /// <summary>
    /// Everything on the screen, for the review key. Blank rows are dropped: a screen is
    /// mostly padding and reading it out is not.
    /// </summary>
    public static string Whole(string[] screen)
        => string.Join("\n", screen.Where(r => r.Trim().Length > 0));

    public static string? NanoFileName(IEnumerable<string> rows)
    {
        string? title = rows.FirstOrDefault(row => row.TrimStart().StartsWith("GNU nano ", StringComparison.OrdinalIgnoreCase));
        if (title is null) return null;
        string text = title.Trim();
        int versionStart = "GNU nano ".Length;
        int versionEnd = text.IndexOf(' ', versionStart);
        if (versionEnd < 0 || versionEnd + 1 >= text.Length) return "New Buffer";
        string file = text[(versionEnd + 1)..].Trim();
        if (file.EndsWith('*')) file = file[..^1].TrimEnd();
        return file.Length == 0 ? "New Buffer" : file;
    }

    /// <summary>
    /// Finds a nano prompt that needs an answer. These live in nano's status area rather than
    /// the editable document, so they must be announced explicitly instead of being exposed as
    /// navigable lines in the keyboard proxy.
    /// </summary>
    public static string? NanoPrompt(IEnumerable<string> rows)
    {
        string[] visible = rows.Select(row => row.Trim()).Where(row => row.Length > 0).ToArray();
        string[] prompts =
        [
            "Save modified buffer?",
            "File Name to Write:",
            "File to insert:",
            "Search:",
            "Replace with:",
            "Goto Line:",
        ];

        foreach (string row in visible)
        {
            string match = prompts.FirstOrDefault(prompt =>
                row.StartsWith(prompt, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            if (match.Length > 0) return row;
        }
        return null;
    }

    private static ScreenAnnouncement Speak(string text, SpeechPriority priority)
    {
        text = text.Trim();
        return text.Length == 0 ? new ScreenAnnouncement("blank", priority) : new ScreenAnnouncement(text, priority);
    }

    private static string RowAt(string[] rows, int index)
        => index >= 0 && index < rows.Length ? rows[index] : string.Empty;

}
