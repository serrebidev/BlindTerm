using System.Text;
using System.Text.RegularExpressions;

namespace BlindTerm.Core.Net;

/// <summary>
/// Removes unavoidable visual-only startup material from known MUDs before it reaches the
/// transcript or a screen reader. Capability negotiation cannot prevent Core MUD's opening
/// logo: the server sends the logo in the same packet as its first telnet commands, before a
/// client has had any opportunity to answer with the MTTS screen-reader bit.
/// </summary>
public sealed class TelnetAccessibilityFilter
{
    private const int MaximumOpeningBytes = 64 * 1024;
    private static readonly byte[] CoreOpeningMarker =
        "Type new if you are a new player."u8.ToArray();
    private static readonly Regex AnsiSequence = new(
        "\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);
    private static readonly Regex ArtBesideText = new(
        @"^[ .oO|/\\_+*-]{2,24}\s{3,}(?<text>\S.*)$",
        RegexOptions.CultureInvariant);

    private readonly bool _cleanCoreOpening;
    private readonly List<byte> _opening = new();
    private bool _openingFinished;

    public TelnetAccessibilityFilter(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _ = port;
        _cleanCoreOpening = host.TrimEnd('.').Equals("coremud.org", StringComparison.OrdinalIgnoreCase)
                            || host.TrimEnd('.').Equals("www.coremud.org", StringComparison.OrdinalIgnoreCase);
        _openingFinished = !_cleanCoreOpening;
    }

    /// <summary>Whether this host has a known opening that needs accessible rewriting.</summary>
    public bool IsActive => _cleanCoreOpening;

    /// <summary>
    /// Filters one text chunk. The Core opening is held only until its stable final line; all
    /// later traffic passes through immediately and byte-for-byte.
    /// </summary>
    public byte[] Process(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty) return [];
        if (_openingFinished) return input.ToArray();

        _opening.AddRange(input.ToArray());
        ReadOnlySpan<byte> opening = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_opening);
        int marker = opening.IndexOf(CoreOpeningMarker);
        int lineEnd = marker < 0
            ? -1
            : opening[(marker + CoreOpeningMarker.Length)..].IndexOf((byte)'\n');
        if (marker < 0 || lineEnd < 0)
        {
            if (_opening.Count < MaximumOpeningBytes) return [];

            _openingFinished = true;
            byte[] unchanged = [.. _opening];
            _opening.Clear();
            return unchanged;
        }

        int through = marker + CoreOpeningMarker.Length + lineEnd + 1;
        byte[] accessible = RewriteCoreOpening(opening[..through]);
        byte[] result = new byte[accessible.Length + opening.Length - through];
        accessible.CopyTo(result, 0);
        opening[through..].CopyTo(result.AsSpan(accessible.Length));

        _openingFinished = true;
        _opening.Clear();
        return result;
    }

    /// <summary>Returns any bytes withheld from an opening that ended unexpectedly.</summary>
    public byte[] Flush()
    {
        if (_openingFinished || _opening.Count == 0) return [];
        _openingFinished = true;
        byte[] unchanged = [.. _opening];
        _opening.Clear();
        return unchanged;
    }

    private static byte[] RewriteCoreOpening(ReadOnlySpan<byte> opening)
    {
        string plain = AnsiSequence.Replace(Encoding.UTF8.GetString(opening), string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var readable = new List<string>();

        foreach (string source in plain.Split('\n'))
        {
            string line = source.TrimEnd();
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (readable.Count > 0 && readable[^1].Length > 0) readable.Add(string.Empty);
                continue;
            }

            if (IsOnlyVisualArt(trimmed)) continue;

            Match beside = ArtBesideText.Match(line);
            string text = beside.Success ? beside.Groups["text"].Value.Trim() : trimmed;
            readable.Add(text);
        }

        while (readable.Count > 0 && readable[0].Length == 0) readable.RemoveAt(0);
        while (readable.Count > 0 && readable[^1].Length == 0) readable.RemoveAt(readable.Count - 1);

        int welcome = readable.FindIndex(line => line.Equals("Welcome to", StringComparison.OrdinalIgnoreCase));
        if (welcome >= 0 && welcome + 1 < readable.Count &&
            readable[welcome + 1].Equals("Core MUD", StringComparison.OrdinalIgnoreCase))
        {
            readable[welcome] = "Welcome to Core MUD";
            readable.RemoveAt(welcome + 1);
        }

        return Encoding.UTF8.GetBytes(string.Join("\r\n", readable) + "\r\n");
    }

    private static bool IsOnlyVisualArt(string text)
        => text.All(character => character == ' ' || ".oO|/\\_+*-".Contains(character));
}
