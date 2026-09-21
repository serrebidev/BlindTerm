using System.Runtime.Versioning;

namespace BlindTerm.App;

/// <summary>
/// A native edit control containing only the line under the terminal cursor.
///
/// It is not a copy of the terminal screen. A copied screen gives NVDA a second, drifting
/// caret and makes Up/Down read padding or nano's title bar. Vertical terminal navigation is
/// intercepted by the form; this control exists only so ordinary typing uses NVDA/JAWS's own
/// keyboard-echo settings.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class KeyboardEchoProxy : TextBox
{
    public KeyboardEchoProxy()
    {
        Multiline = false;
        WordWrap = false;
        HideSelection = false;
        ReadOnly = false;
        AccessibleRole = AccessibleRole.Text;
        Text = "\u200B";
        SelectionStart = 1;
        TabStop = false;
    }

    /// <summary>Updates the proxy to the remote cursor line and column.</summary>
    public void SetLine(string line, int column)
    {
        line = line.TrimEnd();
        int caret = Math.Clamp(column, 0, line.Length);
        if (!string.Equals(Text, line, StringComparison.Ordinal)) Text = line;
        SelectionStart = caret;
        SelectionLength = 0;
    }

    /// <summary>
    /// Keys whose movement this control cannot express, because it holds a single row: the
    /// cursor they move belongs to the program and is reported by the screen, not by a caret.
    ///
    /// Home and End are deliberately not among them. They are movement along a line, and the
    /// one line this control holds is exactly the line they move along, so the native edit
    /// lands on the same character the program does. Being held here was what stopped them
    /// reaching the control at all: the program's cursor moved, the caret the reader was
    /// standing in did not, and NVDA waited for a caret move that never arrived, timed out,
    /// and read out the character it was still sitting on instead of where Home had gone.
    /// </summary>
    public static bool IsTerminalNavigation(Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        bool alt = (keyData & Keys.Alt) == Keys.Alt;
        return !alt && key is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown;
    }

    public static bool IsNativeEditKey(Keys keyData) => !IsTerminalNavigation(keyData);

    /// <summary>
    /// End, as a terminal means it.
    ///
    /// A native edit puts the caret past the last character, and a reader is told nothing
    /// about the position past the end of the text -- pressing End to hear the end of a line
    /// answered "blank". The terminal's cursor is always on a character, so this one lands on
    /// the last one. Modified End is left alone: Shift and Control are Windows' own selection
    /// commands, and this control is not the document.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData != Keys.End)
        {
            base.OnKeyDown(e);
            return;
        }

        SelectionStart = Math.Max(0, TextLength - 1);
        SelectionLength = 0;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }
}
