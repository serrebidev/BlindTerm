namespace BlindTerm.Core;

/// <summary>An OpenSSH destination entered through BlindTerm's connection dialog.</summary>
public sealed record SshTarget
{
    public const int DefaultPort = 22;

    public string Host { get; }
    public int Port { get; }
    public string Username { get; }

    public SshTarget(string host, int port = DefaultPort, string username = "")
    {
        Host = SafePart(host, nameof(host));
        Username = string.IsNullOrWhiteSpace(username)
            ? string.Empty
            : SafePart(username, nameof(username));
        if (port is < 1 or > 65_535) throw new ArgumentOutOfRangeException(nameof(port));
        Port = port;
    }

    public string Destination => Username.Length == 0 ? Host : $"{Username}@{Host}";

    public string Address => Port == DefaultPort ? Destination : $"{Destination}:{Port}";

    /// <summary>The child command line. Its fields cannot contain shell or argument separators.</summary>
    public string CommandLine => $"ssh.exe -tt -p {Port} {Destination}";

    private static string SafePart(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        value = value.Trim();
        if (value.Length > 300 || value[0] == '-' || value.Any(character =>
                char.IsWhiteSpace(character) || char.IsControl(character) || character == '"'))
            throw new ArgumentException("SSH names cannot contain spaces, control characters, or quotes.",
                parameter);
        return value;
    }

    public static bool TryParse(string? text, out SshTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) value = value[6..];

        string username = string.Empty;
        int at = value.LastIndexOf('@');
        if (at >= 0)
        {
            username = value[..at];
            value = value[(at + 1)..];
        }

        int port = DefaultPort;
        string host = value;
        if (value.StartsWith('['))
        {
            int close = value.IndexOf(']');
            if (close < 1) return false;
            host = value[..(close + 1)];
            if (close + 1 < value.Length)
            {
                if (value[close + 1] != ':' ||
                    !int.TryParse(value[(close + 2)..], out port)) return false;
            }
        }
        else if (value.Count(character => character == ':') == 1)
        {
            int colon = value.LastIndexOf(':');
            if (!int.TryParse(value[(colon + 1)..], out port)) return false;
            host = value[..colon];
        }

        try
        {
            target = new SshTarget(host, port, username);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}
