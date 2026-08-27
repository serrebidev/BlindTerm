using System.Text;

namespace BlindTerm.Core.Vt;

/// <summary>
/// Turns a key into the bytes a terminal program expects to read.
///
/// This is the whole of screen mode's input path: in a full-screen program every keystroke
/// goes to the program rather than to a control, and nano's Ctrl-X, vim's arrows and htop's
/// function keys only work if they arrive as the sequences those programs are listening for.
///
/// Names are also accepted as text, so a capture can drive a program without a keyboard:
/// "C-x", "Down", "F6", "Enter", or "hex:1b5b41".
/// </summary>
public static class KeyEncoder
{
    private const byte Esc = 0x1b;

    /// <summary>
    /// Cursor and editing keys have two encodings. A program that has asked for application
    /// cursor keys -- which vim does -- expects SS3 (ESC O A) where an ordinary shell expects
    /// CSI (ESC [ A). Sending the wrong one makes arrow keys insert letters.
    /// </summary>
    public static byte[] Arrow(char direction, bool applicationCursorKeys)
        => Encoding.ASCII.GetBytes($"{(char)Esc}{(applicationCursorKeys ? 'O' : '[')}{direction}");

    /// <summary>
    /// Parses one key name into bytes, or null if the name is not recognised.
    /// </summary>
    public static byte[]? Parse(string name, bool applicationCursorKeys = false)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (name.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            return FromHex(name[4..]);

        // Control combinations: C-a is 0x01, and so on up to C-z. Ctrl-C, Ctrl-D, Ctrl-X and
        // Ctrl-L are the ones a terminal user reaches for constantly.
        if (name.Length == 3 && (name[0] == 'C' || name[0] == 'c') && name[1] == '-')
        {
            char letter = char.ToLowerInvariant(name[2]);
            if (letter >= 'a' && letter <= 'z') return [(byte)(letter - 'a' + 1)];
            if (letter == '[') return [Esc];
        }

        // Alt sends the key prefixed with escape, which is how nano's Alt shortcuts arrive.
        if (name.Length == 3 && (name[0] == 'M' || name[0] == 'm') && name[1] == '-')
            return [Esc, (byte)name[2]];

        return name.ToLowerInvariant() switch
        {
            "up" => Arrow('A', applicationCursorKeys),
            "down" => Arrow('B', applicationCursorKeys),
            "right" => Arrow('C', applicationCursorKeys),
            "left" => Arrow('D', applicationCursorKeys),

            "home" => Ascii($"{(char)Esc}[H"),
            "end" => Ascii($"{(char)Esc}[F"),
            "pgup" or "pageup" => Ascii($"{(char)Esc}[5~"),
            "pgdn" or "pagedown" => Ascii($"{(char)Esc}[6~"),
            "insert" => Ascii($"{(char)Esc}[2~"),
            "delete" or "del" => Ascii($"{(char)Esc}[3~"),

            "enter" or "return" or "cr" => [0x0d],
            "tab" => [0x09],
            "backtab" or "shift-tab" => Ascii($"{(char)Esc}[Z"),
            "backspace" or "bs" => [0x7f],
            "escape" or "esc" => [Esc],
            "space" => [0x20],

            // Function keys. F1-F4 are SS3; F5 up are CSI with a number. htop and nano both
            // lean on these, and nano's help is F1.
            "f1" => Ascii($"{(char)Esc}OP"),
            "f2" => Ascii($"{(char)Esc}OQ"),
            "f3" => Ascii($"{(char)Esc}OR"),
            "f4" => Ascii($"{(char)Esc}OS"),
            "f5" => Ascii($"{(char)Esc}[15~"),
            "f6" => Ascii($"{(char)Esc}[17~"),
            "f7" => Ascii($"{(char)Esc}[18~"),
            "f8" => Ascii($"{(char)Esc}[19~"),
            "f9" => Ascii($"{(char)Esc}[20~"),
            "f10" => Ascii($"{(char)Esc}[21~"),
            "f11" => Ascii($"{(char)Esc}[23~"),
            "f12" => Ascii($"{(char)Esc}[24~"),

            _ => name.Length == 1 ? Encoding.UTF8.GetBytes(name) : null,
        };
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[]? FromHex(string hex)
    {
        hex = hex.Replace(" ", string.Empty);
        if (hex.Length == 0 || hex.Length % 2 != 0) return null;

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                               null, out bytes[i]))
                return null;
        }
        return bytes;
    }
}
