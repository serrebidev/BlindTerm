using System.Text.RegularExpressions;
using BlindTerm.Core.Net;

namespace BlindTerm.App;

/// <summary>
/// Recognises a plain "telnet host port" typed at the command line, so BlindTerm can dial it
/// itself instead of handing it to Windows' telnet.exe.
///
/// telnet.exe does not write lines. It paints its window through the console API, and a
/// pseudo console can only report what that window looks like after the next repaint: every
/// time the conversation scrolls, every row on screen carries different text than it did
/// before, so the whole screen reads as new output and the last screenful is announced again
/// from the top on every line the far end sends. A MUD, which is the reason anyone still
/// types this, becomes unusable -- and anything that scrolled past between two repaints was
/// never anywhere to be read at all.
///
/// BlindTerm already speaks telnet, over a socket, as lines. This is the same connection the
/// Terminal menu and --telnet make; all that was missing was noticing that the command line
/// had just asked for one.
/// </summary>
internal static partial class TelnetCommand
{
    /// <summary>
    /// The host a command line asks for, or null when it is not a plain dial and must be left
    /// to the shell exactly as it was typed.
    /// </summary>
    public static TelnetTarget? Parse(string? command)
    {
        if (command is null) return null;

        Match match = SimpleLaunch().Match(command);
        if (!match.Success) return null;

        string rest = match.Groups["rest"].Value;

        // Anything the shell would act on -- a pipeline, a redirection, a second command --
        // belongs to the shell. Dialling a socket instead would quietly drop the rest of it.
        if (rest.IndexOfAny(['|', ';', '&', '<', '>', '`', '(', ')', '"', '\'']) >= 0) return null;

        string[] arguments = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // telnet.exe's own switches -- -a, -l user, -t term, -f log -- have no equivalent
        // here, and a bare "telnet" opens its interactive prompt rather than a connection.
        // Both are still its work to do.
        if (arguments.Length is 0 or > 2) return null;
        if (arguments.Any(argument => argument[0] is '-' or '/')) return null;

        if (!TelnetAddress.TryParse(arguments[0], out string host, out int port, out bool secure))
            return null;

        if (arguments.Length == 2)
        {
            // "telnet host 4000" is the spelling every MUD's front page prints. A port given
            // separately wins, as it does for --telnet, but only when the host did not carry
            // one already: "telnet host:4000 4022" contradicts itself and is not ours to
            // resolve. A service name rather than a number is telnet.exe's to resolve too.
            // Past the scheme, because "ssl://" is full of colons and none of them is a port.
            if (Bare(arguments[0]).Contains(':')) return null;
            if (!int.TryParse(arguments[1], out int separate) || separate is < 1 or > 65535) return null;
            port = separate;
        }

        // "telnet ssl://host 4022" is the spelling the other clients use for the encrypted
        // port, and the scheme survives a port given separately.
        return new TelnetTarget(host, port, secure);
    }

    /// <summary>The address with any "ssl://" or "telnet://" in front of it taken off.</summary>
    private static string Bare(string address)
    {
        int scheme = address.IndexOf("://", StringComparison.Ordinal);
        return scheme < 0 ? address : address[(scheme + 3)..];
    }

    [GeneratedRegex(@"^\s*telnet(?:\.exe)?(?<rest>(?:\s.*)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleLaunch();
}
