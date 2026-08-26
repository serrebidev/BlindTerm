namespace BlindTerm.Core;

/// <summary>One batch of changes derived from the terminal buffer.</summary>
public sealed class TerminalUpdate
{
    /// <summary>Lines added to the end of the transcript in this batch.</summary>
    public List<string> NewLines { get; } = new();

    /// <summary>Transcript index of the first of those lines.</summary>
    public int FirstNewLine { get; set; }

    /// <summary>
    /// Lines already in the transcript whose rows were redrawn, as replacements to apply to a
    /// mirror of the transcript, in the order they are given.
    /// </summary>
    public List<Transcript.Edit> Edits { get; } = new();

    /// <summary>
    /// Everything at and below the cursor that is not yet part of a line: normally the shell
    /// prompt, a partially printed line, or a progress line. When the cursor is parked on a
    /// blank row underneath a frame a program has just painted, this is the last line of that
    /// frame instead, which is what someone asking "what is on screen right now" means.
    /// </summary>
    public string LiveText { get; set; } = string.Empty;

    /// <summary>
    /// Non-null while a full-screen program owns the alternate screen -- vim, htop, an editor
    /// over ssh. The transcript is not built while this is set; the screen is what matters.
    /// </summary>
    public string[]? AlternateScreen { get; set; }
}
