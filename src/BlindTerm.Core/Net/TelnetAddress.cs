using System.Diagnostics.CodeAnalysis;

namespace BlindTerm.Core.Net;

/// <summary>
/// Reading and writing "host:port", the form a MUD's front page prints and everyone copies.
///
/// A bare host is a host on the standard port. A bracketed address is how a literal IPv6
/// address carries a port at all, since its own notation is full of colons.
/// </summary>
public static class TelnetAddress
{
    public const int DefaultPort = 23;

    public static bool TryParse(string? text, [NotNullWhen(true)] out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();

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
    public static string Format(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string shown = host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
        return port == DefaultPort ? shown : $"{shown}:{port}";
    }

    private static bool ParsePort(string text, out int port)
        => int.TryParse(text, out port) && port is >= 1 and <= 65535;
}
