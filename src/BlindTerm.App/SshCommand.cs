using System.Text.RegularExpressions;

namespace BlindTerm.App;

/// <summary>
/// Recognises a line that starts the Windows OpenSSH client.
///
/// BlindTerm tells a shell from a program by asking whether the shell has a child, and for who
/// owns the keyboard that is the right question. It says nothing about where the far end is. An
/// ssh client typed at a prompt is a child process whose other end is another machine's shell,
/// so the window counts that session as remote for as long as the client runs.
///
/// The session kind cannot say this. A connection BlindTerm opened itself -- --ssh, or the
/// Terminal menu -- is Ssh from the start, but a typed "ssh host" leaves the kind Shell and the
/// local shell running a child, exactly as a nested cmd or a Python prompt would. Everything
/// that asks the kind then says "local", so the arrow keys are handed to the far end: a bash
/// prompt replies with its bell, which is read out as "Attention" beside the prompt on every
/// press, while the caret the reader is standing in never moves.
///
/// Which is also why this is asked of the line and not of the process. BlindTerm counts a
/// shell's children with a Windows job object and deliberately does not walk the process table
/// on the key path, so the name of the child is not something it already knows.
/// </summary>
internal static partial class SshCommand
{
    /// <summary>
    /// Whether this line is the ssh client being started, so that what it opens is a shell on
    /// the far end of a connection rather than a program on this one.
    ///
    /// Only the command itself counts. "cat ~/.ssh/config" and "scp file host:" mention ssh
    /// without being it, and a line the shell has work to do on -- a pipeline, a redirection, a
    /// second command, a conditional -- is the shell's to run and says nothing about the child
    /// it will eventually produce.
    /// </summary>
    public static bool IsSshLaunch(string? command)
    {
        if (command is null) return false;

        Match match = SimpleLaunch().Match(command);
        if (!match.Success) return false;

        string rest = match.Groups["rest"].Value;
        return rest.IndexOfAny(['|', ';', '&', '<', '>', '`', '(', ')']) < 0;
    }

    // The name on its own, or with the .exe a Windows shell would have resolved it from.
    // "ssh-keygen", "sshfs" and "sshpass" are different programs and do not match: what follows
    // the name has to be a space or the end of the line.
    [GeneratedRegex(@"^\s*ssh(?:\.exe)?(?<rest>(?:\s.*)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleLaunch();
}
