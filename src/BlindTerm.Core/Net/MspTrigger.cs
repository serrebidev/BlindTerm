using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BlindTerm.Core.Net;

/// <summary>Whether a trigger asks for a sound effect or for background music.</summary>
public enum MspKind
{
    /// <summary>A sound effect. Several can play at once, and each carries a priority.</summary>
    Sound,

    /// <summary>Background music. One at a time, and it may be left alone if already playing.</summary>
    Music,
}

/// <summary>
/// One MUD Sound Protocol request, as it arrives in the text stream:
///
///   !!SOUND(fname V=volume L=loops P=priority T=type U=url)
///   !!MUSIC(fname V=volume L=loops C=continue T=type U=url)
///   !!SOUND(Off)
///
/// The parameters are all optional and may appear in any order. Anything unrecognised is
/// ignored rather than treated as an error: this arrives from a server, and a MUD that adds a
/// parameter of its own must not stop the sound it asked for from playing.
/// </summary>
public sealed record MspTrigger(
    MspKind Kind,
    string FileName,
    int Volume,
    int Loops,
    int Priority,
    bool Continue,
    string? Type,
    string? Url)
{
    public const int DefaultVolume = 100;
    public const int DefaultPriority = 50;

    /// <summary>Loop for as long as nothing stops it.</summary>
    public const int Forever = -1;

    /// <summary>Whether this asks for everything of its kind to stop.</summary>
    public bool IsOff => FileName.Equals("Off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a whole trigger, brackets and all.
    ///
    /// Some MUDs put their triggers in the text and some send them out of band, inside a
    /// telnet subnegotiation, so that a client which does not speak the protocol never sees
    /// them at all. The text between the brackets is the same either way.
    /// </summary>
    public static bool TryParseLine(string? line, [NotNullWhen(true)] out MspTrigger? trigger)
    {
        trigger = null;
        if (line is null) return false;

        string text = line.Trim();
        if (!text.EndsWith(')')) return false;

        MspKind kind;
        if (text.StartsWith("!!SOUND(", StringComparison.OrdinalIgnoreCase)) kind = MspKind.Sound;
        else if (text.StartsWith("!!MUSIC(", StringComparison.OrdinalIgnoreCase)) kind = MspKind.Music;
        else return false;

        return TryParse(kind, text[8..^1], out trigger);
    }

    /// <summary>
    /// Reads a trigger from the text between "!!SOUND(" and its closing bracket.
    /// </summary>
    public static bool TryParse(MspKind kind, string body, [NotNullWhen(true)] out MspTrigger? trigger)
    {
        trigger = null;
        if (body is null) return false;

        string[] parts = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries
                                                   | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        string name = parts[0];
        // The name is the one thing that is not optional, and a name that is only a parameter
        // means a malformed trigger rather than a file called "V=50".
        if (name.Length == 0 || name.Contains('=')) return false;

        int volume = DefaultVolume;
        int loops = 1;
        int priority = DefaultPriority;
        bool keepPlaying = true;
        string? type = null;
        string? url = null;

        foreach (string part in parts[1..])
        {
            if (part.Length < 2 || part[1] != '=') continue;
            string value = part[2..];
            switch (char.ToUpperInvariant(part[0]))
            {
                case 'V': volume = Number(value, volume); break;
                case 'L': loops = Number(value, loops); break;
                case 'P': priority = Number(value, priority); break;
                case 'C': keepPlaying = Number(value, 1) != 0; break;
                case 'T': type = value.Length > 0 ? value : null; break;
                case 'U': url = value.Length > 0 ? value : null; break;
            }
        }

        trigger = new MspTrigger(
            kind,
            name,
            Math.Clamp(volume, 0, 100),
            // Anything below -1 is meaningless; treat it as the one negative that means
            // something rather than as a count that can never be reached.
            loops < Forever ? Forever : loops,
            Math.Clamp(priority, 0, 100),
            keepPlaying,
            type,
            url);
        return true;
    }

    private static int Number(string text, int fallback)
        => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
}
