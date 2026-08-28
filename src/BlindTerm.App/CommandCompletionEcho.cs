namespace BlindTerm.App;

/// <summary>
/// Reads back what a terminal's own completion made of the line.
///
/// Completion happens inside the shell or the program, on the unfinished current line, and an
/// unfinished line is only ever spoken when it reads as a prompt -- something ending in "?",
/// ":", "&gt;" or "]". A completed command ends in a file name, so nothing in the ordinary
/// speech pipeline says a word about it. Without this, pressing Tab is silent even when it
/// worked perfectly: the completed text exists only on a line the user would have to go
/// looking for, which is indistinguishable from Tab doing nothing at all.
///
/// The prompt is the anchor. Everything typed so far is held in BlindTerm's own edit until
/// Tab flushes it, so the unfinished line at that moment is the prompt alone, and the
/// completed line is that same prompt with the command after it.
/// </summary>
internal sealed class CommandCompletionEcho
{
    private string? _prompt;
    private bool _pending;

    /// <summary>Whether a completion is still waiting to be read back.</summary>
    public bool Pending => _pending;

    /// <summary>Remembers the unfinished line as it stood before the first Tab was sent.</summary>
    public void Expect(string liveTextBeforeTab)
    {
        ArgumentNullException.ThrowIfNull(liveTextBeforeTab);
        _prompt = liveTextBeforeTab;
        _pending = true;
    }

    /// <summary>
    /// Waits for the next completion on a line already handed over. Tab pressed again cycles
    /// through the candidates, and each one is as worth hearing as the first; the prompt in
    /// front of the line has not moved, so it is still the anchor.
    /// </summary>
    public void ExpectAnother() => _pending = _prompt is not null;

    /// <summary>
    /// Nothing is waiting any more. The line was submitted, the session ended, or a
    /// full-screen program took the window, and a completion read out after any of those
    /// would describe a line that is no longer there.
    /// </summary>
    public void Cancel()
    {
        _prompt = null;
        _pending = false;
    }

    /// <summary>
    /// The completed line, or null when there is nothing to report. Asking ends the wait
    /// either way: unless Tab is pressed again, the line changes from here on because the
    /// user is typing into it, and the screen reader is already echoing that.
    /// </summary>
    public string? Completed(string liveText)
    {
        ArgumentNullException.ThrowIfNull(liveText);
        if (!_pending || _prompt is not { } prompt) return null;

        _pending = false;

        // The prompt is the part the shell redraws unchanged in front of the line it is
        // editing. Anything else means the screen moved on -- a program started, the window
        // was resized and the line rewrapped -- and cutting a substring out of that would put
        // a piece of somebody's prompt into the command box as if the user had typed it.
        if (!liveText.StartsWith(prompt, StringComparison.Ordinal)) return null;

        string completed = liveText[prompt.Length..];
        return completed.Length == 0 ? null : completed;
    }
}
