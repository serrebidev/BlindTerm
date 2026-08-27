using System.Text;

namespace BlindTerm.Core.Vt;

/// <summary>Modifiers held with a key, as a terminal encodes them.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>
/// Turns a key into the bytes a terminal program expects to read.
///
/// This is the whole of screen mode's input path: in a full-screen program every keystroke
/// goes to the program rather than to a control, and nano's Ctrl-X, vim's arrows and htop's
/// function keys only work if they arrive as the sequences those programs are listening for.
///
/// Modifiers matter as much as the keys. Ctrl+Right is how you move by word, and it is not
/// "Ctrl" plus "Right" -- it is its own sequence, ESC[1;5C. Sending the plain arrow instead
/// moves by one character and the word-movement command silently does the wrong thing.
///
/// Names are also accepted as text, so a capture can drive a program without a keyboard:
/// "C-x", "Down", "C-Right", "F6", "Enter", or "hex:1b5b41".
/// </summary>
public static class KeyEncoder
{
    private const byte Esc = 0x1b;

    /// <summary>
    /// The parameter a terminal uses for a modifier combination: 1, plus one for shift, two
    /// for alt and four for control. So Shift is 2, Control is 5, Control+Shift is 6.
    /// </summary>
    public static int ModifierParameter(KeyModifiers modifiers) => 1 + (int)modifiers;

    /// <summary>
    /// A cursor key. Without modifiers a program that asked for application cursor keys --
    /// vim does -- expects SS3 (ESC O A) where an ordinary shell expects CSI (ESC [ A);
    /// sending the wrong one makes arrow keys insert letters. With modifiers there is only
    /// one form, CSI with the modifier as a parameter.
    /// </summary>
    public static byte[] Arrow(char direction, bool applicationCursorKeys, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (modifiers != KeyModifiers.None)
            return Ascii($"{(char)Esc}[1;{ModifierParameter(modifiers)}{direction}");

        return Ascii($"{(char)Esc}{(applicationCursorKeys ? 'O' : '[')}{direction}");
    }

    /// <summary>
    /// Parses one key name into bytes, or null if the name is not recognised. The name may
    /// carry modifier prefixes: "C-Right", "S-Home", "C-S-Left", "M-u".
    /// </summary>
    public static byte[]? Parse(string name, bool applicationCursorKeys = false)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (name.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            return FromHex(name[4..]);

        var modifiers = KeyModifiers.None;
        while (name.Length > 2 && name[1] == '-')
        {
            switch (char.ToLowerInvariant(name[0]))
            {
                case 'c': modifiers |= KeyModifiers.Control; break;
                case 's': modifiers |= KeyModifiers.Shift; break;
                case 'm' or 'a': modifiers |= KeyModifiers.Alt; break;
                default: return null;
            }
            name = name[2..];
        }

        return Encode(name, modifiers, applicationCursorKeys);
    }

    /// <summary>The bytes for a named key held with the given modifiers.</summary>
    public static byte[]? Encode(string name, KeyModifiers modifiers, bool applicationCursorKeys)
    {
        bool control = modifiers.HasFlag(KeyModifiers.Control);
        bool alt = modifiers.HasFlag(KeyModifiers.Alt);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);

        string lower = name.ToLowerInvariant();

        // Cursor and navigation keys, which carry their modifiers as a parameter.
        char? direction = lower switch
        {
            "up" => 'A',
            "down" => 'B',
            "right" => 'C',
            "left" => 'D',
            "home" => 'H',
            "end" => 'F',
            _ => null,
        };
        if (direction is char d)
        {
            // Home and End have no application-cursor form worth distinguishing here.
            if (d is 'H' or 'F' && modifiers == KeyModifiers.None)
                return Ascii($"{(char)Esc}[{d}");
            return Arrow(d, applicationCursorKeys, modifiers);
        }

        // Keys that end in a tilde carry their modifier after the number.
        int? tilde = lower switch
        {
            "insert" => 2,
            "delete" or "del" => 3,
            "pgup" or "pageup" => 5,
            "pgdn" or "pagedown" => 6,
            "f5" => 15,
            "f6" => 17,
            "f7" => 18,
            "f8" => 19,
            "f9" => 20,
            "f10" => 21,
            "f11" => 23,
            "f12" => 24,
            _ => null,
        };
        if (tilde is int n)
        {
            return modifiers == KeyModifiers.None
                ? Ascii($"{(char)Esc}[{n}~")
                : Ascii($"{(char)Esc}[{n};{ModifierParameter(modifiers)}~");
        }

        // F1 to F4 are SS3, and take a modifier the same way the arrows do.
        int? ss3 = lower switch { "f1" => 'P', "f2" => 'Q', "f3" => 'R', "f4" => 'S', _ => null };
        if (ss3 is int letter)
        {
            return modifiers == KeyModifiers.None
                ? Ascii($"{(char)Esc}O{(char)letter}")
                : Ascii($"{(char)Esc}[1;{ModifierParameter(modifiers)}{(char)letter}");
        }

        byte[]? simple = lower switch
        {
            "enter" or "return" or "cr" => [0x0d],
            "tab" => shift ? Ascii($"{(char)Esc}[Z") : [0x09],
            "backtab" or "shift-tab" => Ascii($"{(char)Esc}[Z"),
            "backspace" or "bs" => [0x7f],
            "escape" or "esc" => [Esc],
            "space" => control ? [0x00] : [0x20],
            _ => null,
        };
        if (simple is not null) return alt ? Prefix(simple) : simple;

        // A single character. Control turns a letter into its control code; alt prefixes it
        // with escape, which is how a meta shortcut such as nano's Alt-U arrives.
        if (name.Length == 1)
        {
            char c = name[0];
            if (control)
            {
                char letterCode = char.ToLowerInvariant(c);
                byte[]? code = letterCode switch
                {
                    >= 'a' and <= 'z' => [(byte)(letterCode - 'a' + 1)],
                    '[' => [0x1b],
                    '\\' => [0x1c],
                    ']' => [0x1d],
                    '^' or '6' => [0x1e],
                    '_' or '-' => [0x1f],
                    ' ' or '@' => [0x00],
                    _ => null,
                };
                if (code is not null) return alt ? Prefix(code) : code;
                return null;
            }

            byte[] typed = Encoding.UTF8.GetBytes(c.ToString());
            return alt ? Prefix(typed) : typed;
        }

        return null;
    }

    private static byte[] Prefix(byte[] bytes)
    {
        var result = new byte[bytes.Length + 1];
        result[0] = Esc;
        bytes.CopyTo(result, 1);
        return result;
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
