namespace BlindTerm.Core.Pty;

/// <summary>
/// Turns what somebody typed into something CreateProcess can actually start.
///
/// CreateProcess is not a shell. Given a bare name it searches PATH for that name with ".exe"
/// appended, and nothing else: it does not read PATHEXT, and it cannot run a .cmd, a .bat or
/// a .ps1 at all. Every command line tool installed by npm is a .cmd shim with no .exe beside
/// it, which is to say "codex", "claude" and "opencode" are exactly the commands this affects.
/// They run from any shell, they are on PATH, and handing them straight to CreateProcess
/// returns ERROR_FILE_NOT_FOUND -- which arrived as an unhandled exception and closed the
/// window.
///
/// So the search a shell would do is done here, in the order a shell does it: each directory
/// on PATH in turn, and within a directory each extension PATHEXT allows. That order is the
/// whole point. Searching by extension first -- every directory for a .exe, then every
/// directory for a .cmd -- finds a different file whenever two programs share a name, and
/// "opencode" on a machine with an unrelated opencode.exe further down PATH started the wrong
/// program, which printed a usage error and exited before anything could be typed at it.
///
/// A shim is then launched the way a shell launches one: a .cmd or .bat through cmd.exe, a
/// .ps1 through PowerShell.
/// </summary>
internal static class CommandLine
{
    /// <summary>Extensions that can be started, in the order a shim is preferred.</summary>
    private static readonly string[] Runnable = [".exe", ".com", ".cmd", ".bat", ".ps1"];

    /// <summary>Extensions cmd.exe has to run rather than CreateProcess.</summary>
    private static readonly string[] NeedsCmd = [".cmd", ".bat"];

    /// <summary>Extensions PowerShell has to run rather than CreateProcess.</summary>
    private static readonly string[] NeedsPowerShell = [".ps1"];

    /// <summary>
    /// Rewrites <paramref name="commandLine"/> so CreateProcess can start it, or returns it
    /// unchanged when it is already startable or when nothing on PATH matches.
    ///
    /// Unchanged is the right answer for the miss: the command line is still what the user
    /// typed, CreateProcess still reports the same error for it, and nothing here has to
    /// guess at what they meant.
    /// </summary>
    public static string ForCreateProcess(string commandLine)
        => ForCreateProcess(commandLine,
                            Environment.GetEnvironmentVariable("PATH"),
                            Environment.GetEnvironmentVariable("PATHEXT"),
                            File.Exists);

    /// <summary>The same, against a stated PATH and file system, so it can be tested.</summary>
    public static string ForCreateProcess(
        string commandLine, string? path, string? pathExt, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (string.IsNullOrWhiteSpace(commandLine)) return commandLine;

        (string leading, string program, string arguments) = Split(commandLine);
        if (program.Length == 0) return commandLine;

        // An absolute or relative path is already an answer to "which file"; the only thing
        // left to decide is what has to run it.
        bool pathed = program.Contains('\\') || program.Contains('/') || program.Contains(':');
        if ((pathed ? program : Resolve(program, path, pathExt, exists)) is not string found)
            return commandLine;

        string extension = Path.GetExtension(found);

        if (NeedsCmd.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return ThroughCmd(leading, found, arguments);

        if (NeedsPowerShell.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return ThroughPowerShell(leading, PowerShellHost(path, exists), found, arguments);

        // A program found on PATH is left exactly as it was typed: CreateProcess finds it by
        // the same search, and rewriting it would only make the command line harder to read
        // back.
        return commandLine;
    }

    /// <summary>
    /// The full path of <paramref name="program"/> as a shell would find it, or null.
    /// </summary>
    private static string? Resolve(
        string program, string? path, string? pathExt, Func<string, bool> exists)
    {
        string[] directories = Directories(path);
        if (directories.Length == 0) return null;

        // PATHEXT decides which extensions count and in which order, but only the ones that
        // can actually be started are tried: a .vbs shim beside the .cmd is not something
        // anything here knows how to run, and picking it because PATHEXT listed it first
        // would swap one "cannot start" for another.
        string[] extensions = (pathExt ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => Runnable.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (extensions.Length == 0) extensions = Runnable;

        // PowerShell finds a script by name whether or not PATHEXT mentions .PS1 -- and the
        // stock PATHEXT does not mention it -- so a script somebody runs by name at their
        // prompt has to be findable here too. Last, so anything PATHEXT does list still wins.
        if (!extensions.Contains(".ps1", StringComparer.OrdinalIgnoreCase))
            extensions = [.. extensions, ".ps1"];

        string[] candidates = Candidates(program, extensions).ToArray();

        // Directory first, then extension, because that is the order every shell searches in:
        // a .cmd in an earlier directory beats a .exe in a later one.
        foreach (string directory in directories)
            foreach (string candidate in candidates)
            {
                string full;
                // A malformed PATH entry cannot hold anything; move on to the next directory.
                try { full = Path.Combine(directory, candidate); }
                catch (ArgumentException) { break; }
                if (exists(full)) return full;
            }

        return null;
    }

    /// <summary>The directories on a PATH, in order, with the quoting a PATH may carry.</summary>
    private static string[] Directories(string? path)
        => (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'))
            .Where(directory => directory.Length > 0)
            .ToArray();

    /// <summary>The file names to look for, in order.</summary>
    private static IEnumerable<string> Candidates(string program, string[] extensions)
    {
        string extension = Path.GetExtension(program);
        if (extension.Length > 0)
        {
            // Named with an extension, so that is what was asked for.
            yield return program;

            // "codex.exe" is worth a second look as "codex", because an .exe is what someone
            // reasonably expects a command to be and the shim beside it is what actually
            // exists. Only for .exe: any other extension names a real, different file.
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) yield break;
            program = Path.GetFileNameWithoutExtension(program);
        }

        // Lowercased: PATHEXT is conventionally written in capitals, the files themselves are
        // not, and this path ends up in the command line the user reads back and the error
        // they are read out. "codex.CMD" is nobody's file name.
        foreach (string candidate in extensions) yield return program + candidate.ToLowerInvariant();
    }

    /// <summary>
    /// The PowerShell that runs a .ps1: PowerShell 7 where it is installed, and Windows
    /// PowerShell otherwise.
    ///
    /// Named in full so the one on PATH is the one that runs, and falling back to the bare
    /// name when neither is on PATH -- Windows PowerShell lives in the system directory,
    /// which CreateProcess searches whether or not PATH mentions it.
    /// </summary>
    private static string PowerShellHost(string? path, Func<string, bool> exists)
    {
        foreach (string name in (string[])["pwsh.exe", "powershell.exe"])
            foreach (string directory in Directories(path))
            {
                string full;
                try { full = Path.Combine(directory, name); }
                catch (ArgumentException) { continue; }
                if (exists(full)) return full;
            }

        return "powershell.exe";
    }

    /// <summary>
    /// Wraps a shim so cmd.exe runs it.
    ///
    /// "/s /c" with the whole command inside one pair of quotes is the form cmd documents for
    /// a quoted program path: everything between the first and last quote is taken literally,
    /// so a tool living under "Program Files" survives.
    /// </summary>
    private static string ThroughCmd(string leading, string program, string arguments)
        => $"{leading}cmd.exe /s /c \"\"{program}\"{arguments}\"";

    /// <summary>
    /// Wraps a script so PowerShell runs it.
    ///
    /// "-File" is the form that takes a script and its arguments and leaves them as arguments
    /// rather than as more PowerShell to parse. The execution policy is set aside because the
    /// script was named by the person at the keyboard, in their own terminal, on purpose; a
    /// terminal that refuses to run what its user typed is the bug, not the safeguard. The
    /// profile is skipped so a banner printed by someone's $PROFILE is not the first thing
    /// read out of a script's output.
    /// </summary>
    private static string ThroughPowerShell(
        string leading, string host, string script, string arguments)
        => $"{leading}\"{host}\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\"{arguments}";

    /// <summary>The program a command line names, for saying which one could not be found.</summary>
    public static string Program(string commandLine) => Split(commandLine).Program;

    /// <summary>
    /// Splits a command line into its leading space, its program, and everything after.
    /// </summary>
    private static (string Leading, string Program, string Arguments) Split(string commandLine)
    {
        int start = 0;
        while (start < commandLine.Length && char.IsWhiteSpace(commandLine[start])) start++;
        string leading = commandLine[..start];
        if (start >= commandLine.Length) return (leading, string.Empty, string.Empty);

        if (commandLine[start] == '"')
        {
            int close = commandLine.IndexOf('"', start + 1);
            if (close < 0) return (leading, string.Empty, string.Empty);
            return (leading, commandLine[(start + 1)..close], commandLine[(close + 1)..]);
        }

        int end = start;
        while (end < commandLine.Length && !char.IsWhiteSpace(commandLine[end])) end++;
        return (leading, commandLine[start..end], commandLine[end..]);
    }
}
