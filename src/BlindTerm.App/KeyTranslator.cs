using System.Runtime.Versioning;
using System.Text;
using BlindTerm.Core.Vt;

namespace BlindTerm.App;

/// <summary>
/// Turns a Windows key press into the bytes a terminal program expects.
///
/// Screen mode has to claim keys the framework would otherwise eat: Tab moves focus, arrows
/// move between controls, Escape closes dialogs, Enter presses buttons. In a full-screen
/// program every one of those belongs to the program, so they are intercepted before the
/// framework sees them and translated here.
///
/// Modifiers travel with the key rather than being dropped. Ctrl+Right is how a terminal
/// editor moves by word; sending a plain Right instead moves one character and quietly does
/// the wrong thing, which is worse than doing nothing.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class KeyTranslator
{
    /// <summary>
    /// The bytes for a key press, or null if this is not a key screen mode should send --
    /// a bare modifier, or something with no terminal meaning. Plain typing returns null too
    /// and is handled as a character instead, so that the keyboard layout and dead keys are
    /// Windows' problem rather than ours.
    /// </summary>
    public static byte[]? Translate(Keys keyData, bool applicationCursorKeys)
    {
        Keys key = keyData & Keys.KeyCode;

        var modifiers = KeyModifiers.None;
        if ((keyData & Keys.Control) == Keys.Control) modifiers |= KeyModifiers.Control;
        if ((keyData & Keys.Alt) == Keys.Alt) modifiers |= KeyModifiers.Alt;
        if ((keyData & Keys.Shift) == Keys.Shift) modifiers |= KeyModifiers.Shift;

        // Bare modifiers are not key presses.
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return null;

        string? name = key switch
        {
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            Keys.Enter => "Enter",
            Keys.Escape => "Escape",
            Keys.Back => "Backspace",
            Keys.Tab => "Tab",
            >= Keys.F1 and <= Keys.F12 => "F" + (key - Keys.F1 + 1),
            _ => null,
        };

        if (name is not null) return KeyEncoder.Encode(name, modifiers, applicationCursorKeys);

        // Control or Alt with a character key. Without either, this is ordinary typing and is
        // left for the character path.
        if (modifiers is KeyModifiers.None or KeyModifiers.Shift) return null;

        char? character = key switch
        {
            >= Keys.A and <= Keys.Z => (char)('a' + (key - Keys.A)),
            >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
            Keys.Space => ' ',
            Keys.OemOpenBrackets => '[',
            Keys.OemCloseBrackets => ']',
            Keys.Oem5 => '\\',
            Keys.OemMinus => '-',
            _ => null,
        };

        return character is char c
            ? KeyEncoder.Encode(c.ToString(), modifiers, applicationCursorKeys)
            : null;
    }

    /// <summary>
    /// The bytes for pasted text.
    ///
    /// A program that has enabled bracketed paste asked to be told where a paste begins and
    /// ends, so it can tell pasted text from typing. vim uses it to switch auto-indent off for
    /// a pasted block, and without the markers a whole block pasted at once is re-indented line
    /// by line into nonsense. When no program asked for it, the raw text is what it expects.
    /// </summary>
    public static byte[] Paste(string text, bool bracketedPaste)
    {
        var body = Encoding.UTF8.GetBytes(text);
        if (!bracketedPaste) return body;

        return [0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~',
                .. body,
                0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~'];
    }
}
