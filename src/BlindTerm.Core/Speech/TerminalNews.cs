namespace BlindTerm.Core.Speech;

/// <summary>
/// What a batch of terminal changes is worth saying, as one decision.
///
/// Two things speak: the lines the terminal has finished, and the prompt it has not. They are
/// not independent. A prompt deliberately ends without a newline, so it is read while the
/// cursor is still sitting on it -- and its provisional transcript entry becomes ordinary
/// later, when something moves past that row: a program starting, or a connection taking the
/// window over. Decided separately, the same words are read out twice.
/// </summary>
public sealed class TerminalNews
{
    private readonly LineNews _lines = new();
    private readonly PromptNews _prompt = new();

    /// <summary>Suppresses the shell's one-line echo of a command already spoken while typing.</summary>
    public void SuppressCommandEcho(string command) => _lines.SuppressCommandEcho(command);

    /// <summary>
    /// Forgets what has been said about which line. The screen has been wiped or handed to a
    /// full-screen program, so line numbers no longer mean what they meant.
    /// </summary>
    public void Reset() => _lines.Reset();

    public IReadOnlyList<string> News(TerminalUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        // The lines still go through, so what has been said about each one stays right. Only
        // the speaking is dropped.
        IReadOnlyList<string> lines = _lines.News(update);
        var spoken = new List<string>(update.Quiet ? [] : lines);

        // A batch the app wrote itself carries no reading of the terminal, so it says nothing
        // about the prompt the far end is waiting at. Reading its empty live text as "there is
        // no prompt any more" makes the prompt look new again the next time the terminal is
        // read, and it is announced a second time.
        if (update.External) return spoken;

        spoken.AddRange(_prompt.News(update.LiveText));

        // The prompt is read here, while the cursor is still on it, and it stays there while
        // its answer is typed. Its provisional transcript copy, and the finalized copy after
        // output pushes past it, are the same words again rather than new speech.
        string current = LastLine(update.LiveText);
        if (PromptNews.IsPrompt(current)) _lines.SuppressPromptEcho(current);

        return spoken;
    }

    /// <summary>
    /// The prompt within the current line. Everything at and below the cursor can be several
    /// rows; only the last of them is the row that will become a transcript line.
    /// </summary>
    private static string LastLine(string liveText)
    {
        int end = liveText.LastIndexOf('\n');
        return end < 0 ? liveText : liveText[(end + 1)..];
    }
}
