using System.Diagnostics.CodeAnalysis;

namespace BlindTerm.Core.Net;

/// <summary>
/// One Generic MUD Communication Protocol message: a dotted package name and, usually, a JSON
/// value.
///
/// GMCP travels inside a telnet subnegotiation rather than in the text, which is the whole
/// point of it. A MUD that sends "Room" this way is saying what the room is, in a form that
/// does not have to be found by reading the description it also printed -- which is how a
/// player who cannot see the screen gets a list of exits without hunting for the word "Exits"
/// in a paragraph.
/// </summary>
public sealed record GmcpMessage(string Package, string Payload)
{
    /// <summary>
    /// The longest message worth reading. GMCP carries small facts; anything of this size is a
    /// server misbehaving, and parsing it would only cost the reading thread time.
    /// </summary>
    public const int MaximumLength = 128 * 1024;

    /// <summary>
    /// Splits "Package.Name { ... }" into its two halves.
    ///
    /// The payload is optional -- "Core.Ping" alone is a whole message -- and is left exactly
    /// as it arrived, because deciding what it means belongs to whatever asked for it.
    /// </summary>
    public static bool TryParse(string? message, [NotNullWhen(true)] out GmcpMessage? parsed)
    {
        parsed = null;
        if (message is null || message.Length > MaximumLength) return false;

        string text = message.Trim();
        if (text.Length == 0) return false;

        int split = text.IndexOfAny([' ', '\t', '\r', '\n']);
        string package = split < 0 ? text : text[..split];
        string payload = split < 0 ? string.Empty : text[(split + 1)..].Trim();

        // A package name is dotted words and nothing else. Anything else is not GMCP, and
        // treating it as a package would put a server's arbitrary text where a name belongs.
        if (!IsPackageName(package)) return false;

        parsed = new GmcpMessage(package, payload);
        return true;
    }

    private static bool IsPackageName(string package)
    {
        if (package.Length is 0 or > 128) return false;
        if (package[0] == '.' || package[^1] == '.') return false;

        bool afterDot = true;
        foreach (char c in package)
        {
            if (c == '.')
            {
                if (afterDot) return false;
                afterDot = true;
                continue;
            }
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-') return false;
            afterDot = false;
        }
        return !afterDot;
    }

    /// <summary>Whether this message belongs to a package, or to one of its subpackages.</summary>
    public bool IsIn(string package)
        => Package.Equals(package, StringComparison.OrdinalIgnoreCase)
           || (Package.Length > package.Length
               && Package[package.Length] == '.'
               && Package.AsSpan(0, package.Length).Equals(package, StringComparison.OrdinalIgnoreCase));
}
