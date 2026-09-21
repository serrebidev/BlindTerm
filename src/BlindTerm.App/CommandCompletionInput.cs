using System.Text;

namespace BlindTerm.App;

/// <summary>
/// Bridges BlindTerm's native buffered edit field to a terminal program's live line editor
/// when the user asks that program to complete text with Tab.
/// </summary>
internal sealed class CommandCompletionInput
{
    /// <summary>
    /// Whether the terminal program now owns the current line. Before the first Tab the text
    /// exists only in the native edit; afterwards each edit must reach the program immediately.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>
    /// Whether anything is known to be on the line the terminal is holding. A line with
    /// something on it can start a program, and BlindTerm no longer has its text to judge by
    /// once the terminal's own editor has completed it.
    /// </summary>
    public bool HasText { get; private set; }

    /// <summary>Flushes the pending native text once and appends the terminal Tab byte.</summary>
    public byte[] Begin(string pendingText)
    {
        ArgumentNullException.ThrowIfNull(pendingText);
        if (Active) return [0x09];

        Active = true;
        HasText = pendingText.Length > 0;
        byte[] pending = Encoding.UTF8.GetBytes(pendingText);
        byte[] completion = new byte[pending.Length + 1];
        pending.CopyTo(completion, 0);
        completion[^1] = 0x09;
        return completion;
    }

    /// <summary>Returns a typed character only while the terminal owns the line.</summary>
    public byte[]? Character(char character)
    {
        if (!Active || char.IsControl(character)) return null;

        HasText = true;
        return Encoding.UTF8.GetBytes(character.ToString());
    }

    /// <summary>
    /// What the terminal's own editor made of the line. Completion can put text on a line
    /// that was empty when Tab was pressed, so this is the only account of it BlindTerm gets.
    /// </summary>
    public void Completed(string completedText)
    {
        ArgumentNullException.ThrowIfNull(completedText);
        if (Active) HasText = completedText.Length > 0;
    }

    /// <summary>Ends the live line and reports whether Enter must send only a terminator.</summary>
    public bool FinishLine()
    {
        bool wasActive = Active;
        TakeBack();
        return wasActive;
    }

    /// <summary>
    /// BlindTerm owns the line again.
    ///
    /// Tab handed the line to the terminal's own editor, and something has since put text
    /// there that the far end has never seen -- a recalled history line, which exists only in
    /// this window. Left as it was, the next Return would send the terminator alone and run
    /// the empty line still sitting at the far end, and every arrow would be forwarded to an
    /// editor whose cursor is somewhere else entirely, which is what a shell answers with its
    /// bell.
    /// </summary>
    public void TakeBack()
    {
        Active = false;
        HasText = false;
    }
}
