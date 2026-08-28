using System.Diagnostics.CodeAnalysis;

namespace BlindTerm.Core.Net;

/// <summary>
/// Reading and writing "host:port", the form a MUD's front page prints and everyone copies.
///
/// A bare host is a host on the standard port. A bracketed address is how a literal IPv6
/// address carries a port at all, since its own notation is full of colons.
///
/// An address may also carry a scheme saying the connection is encrypted. MUD front pages and
/// the other clients spell that "ssl://", "tls://" or "telnets://" interchangeably, so all
/// three are read and the first is what gets written back.
/// </summary>
public static class TelnetAddress
{
    public const int DefaultPort = 23;

    /// <summary>What a secure address is written as. The others are read but never produced.</summary>
    public const string SecureScheme = "ssl://";

    private static readonly string[] SecureSchemes = ["ssl://", "tls://", "telnets://", "ssltelnet://"];
    private static readonly string[] PlainSchemes = ["telnet://", "tcp://"];

    public static bool TryParse(string? text, [NotNullWhen(true)] out string host, out int port)
        => TryParse(text, out host, out port, out _);

    public static bool TryParse(string? text, [NotNullWhen(true)] out string host, out int port,
        out bool secure)
    {
        host = string.Empty;
        port = DefaultPort;
        secure = false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();

        // The scheme comes off before anything else looks for a colon: leaving it on would
        // make "ssl" the host and "//mud.example.com" the port.
        foreach (string scheme in SecureSchemes)
        {
            if (!value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) continue;
            value = value[scheme.Length..].Trim();
            secure = true;
            break;
        }
        if (!secure)
        {
            foreach (string scheme in PlainSchemes)
            {
                if (!value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) continue;
                value = value[scheme.Length..].Trim();
                break;
            }
        }
        if (value.Length == 0) return false;

        // A trailing slash is what a browser adds to an address someone pasted out of one.
        value = value.TrimEnd('/');
        if (value.Length == 0) return false;

        if (value.StartsWith('['))
        {
            int close = value.IndexOf(']');
            if (close < 0) return false;
            host = value[1..close];
            string rest = value[(close + 1)..];
            if (rest.Length == 0) return host.Length > 0;
            return rest[0] == ':' && ParsePort(rest[1..], out port) && host.Length > 0;
        }

        // Only a single colon separates a host from a port. More than one is an unbracketed
        // IPv6 address, which has no port and must not have one guessed for it.
        int separator = value.IndexOf(':');
        if (separator < 0)
        {
            host = value;
            return host.Length > 0;
        }
        if (value.IndexOf(':', separator + 1) >= 0)
        {
            host = value;
            return true;
        }

        host = value[..separator];
        return host.Length > 0 && ParsePort(value[(separator + 1)..], out port);
    }

    /// <summary>The remembered form: a port is only worth writing when it is not the default.</summary>
    public static string Format(string host, int port) => Format(host, port, secure: false);

    /// <summary>
    /// The remembered form, saying so when the connection is encrypted.
    ///
    /// The scheme is worth writing and the default port is not, because arrowing to a
    /// remembered address has to bring back everything needed to dial it again -- and on a
    /// MUD that offers both, the plain port and the TLS port are two different numbers with
    /// nothing in the address itself to tell them apart.
    /// </summary>
    public static string Format(string host, int port, bool secure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string shown = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
        string address = port == DefaultPort ? shown : $"{shown}:{port}";
        return secure ? SecureScheme + address : address;
    }

    private static bool ParsePort(string text, out int port)
        => int.TryParse(text, out port) && port is >= 1 and <= 65535;
}
