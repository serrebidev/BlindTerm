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

    /// <summary>Flushes the pending native text once and appends the terminal Tab byte.</summary>
    public byte[] Begin(string pendingText)
    {
        ArgumentNullException.ThrowIfNull(pendingText);
        if (Active) return [0x09];

        Active = true;
        byte[] pending = Encoding.UTF8.GetBytes(pendingText);
        byte[] completion = new byte[pending.Length + 1];
        pending.CopyTo(completion, 0);
        completion[^1] = 0x09;
        return completion;
    }

    /// <summary>Returns a typed character only while the terminal owns the line.</summary>
    public byte[]? Character(char character)
        => Active && !char.IsControl(character)
            ? Encoding.UTF8.GetBytes(character.ToString())
            : null;

    /// <summary>Ends the live line and reports whether Enter must send only a terminator.</summary>
    public bool FinishLine()
    {
        bool wasActive = Active;
        Active = false;
        return wasActive;
    }
}
