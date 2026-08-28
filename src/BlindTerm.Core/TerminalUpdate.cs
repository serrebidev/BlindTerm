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
    /// Whether these lines were written by the app rather than read from the far end: a ready
    /// message, "Connecting to...", "[Disconnected]".
    ///
    /// Such a batch says nothing about the terminal's unfinished current line, which is why
    /// <see cref="LiveText"/> must not be read from it. Treating its empty value as "the
    /// prompt is gone" makes the prompt look new again the moment it is published next, and
    /// it is then announced a second time.
    /// </summary>
    public bool External { get; set; }

    /// <summary>
    /// Whether these lines go into the transcript without being announced.
    ///
    /// A MUD's own account of the room and the character belongs in the transcript at the
    /// moment it arrives, so that reading back through a session finds it in the right place.
    /// Whether it is also read out as it happens is a separate question, and the answer is
    /// usually no: nobody wants their remaining hit points spoken over the fight taking them.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// Non-null while a full-screen program owns the alternate screen -- vim, htop, an editor
    /// over ssh. The transcript is not built while this is set; the screen is what matters.
    /// </summary>
    public string[]? AlternateScreen { get; set; }

    /// <summary>
    /// Where the cursor is, counted from the top of the screen. Screen mode reads by
    /// following it: the row it moves to is the line to speak, and a change to the row it is
    /// already on is the text being typed or deleted under it.
    /// </summary>
    public int CursorRow { get; set; }

    public int CursorColumn { get; set; }
}
