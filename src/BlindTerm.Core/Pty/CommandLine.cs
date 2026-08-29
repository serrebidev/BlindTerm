namespace BlindTerm.Core.Pty;

/// <summary>
/// Turns what somebody typed into something CreateProcess can actually start.
///
/// CreateProcess is not a shell. Given a bare name it searches PATH for that name with ".exe"
/// appended, and nothing else: it does not read PATHEXT, and it cannot run a .cmd or a .bat at
/// all. Every command line tool installed by npm is a .cmd shim with no .exe beside it, which
/// is to say "codex", "claude" and "opencode" are exactly the commands this affects. They run
/// from any shell, they are on PATH, and handing them straight to CreateProcess returns
/// ERROR_FILE_NOT_FOUND -- which arrived as an unhandled exception and closed the window.
///
/// So the search a shell would do is done here, and a shim is launched the way a shell
/// launches one: through cmd.exe.
/// </summary>
internal static class CommandLine
{
    /// <summary>Extensions that can be started, in the order a shim is preferred.</summary>
    private static readonly string[] Runnable = [".exe", ".com", ".cmd", ".bat"];

    /// <summary>Extensions that have to go through cmd.exe rather than CreateProcess.</summary>
    private static readonly string[] NeedsShell = [".cmd", ".bat"];

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
        // left to decide is whether it needs a shell to run it.
        if (program.Contains('\\') || program.Contains('/') || program.Contains(':'))
        {
            return NeedsShell.Contains(Path.GetExtension(program), StringComparer.OrdinalIgnoreCase)
                ? ThroughCmd(leading, program, arguments)
                : commandLine;
        }

        if (Resolve(program, path, pathExt, exists) is not string found) return commandLine;

        // A .exe found on PATH is left exactly as it was typed: CreateProcess finds it by the
        // same search, and rewriting it would only make the command line harder to read back.
        return NeedsShell.Contains(Path.GetExtension(found), StringComparer.OrdinalIgnoreCase)
            ? ThroughCmd(leading, found, arguments)
            : commandLine;
    }

    /// <summary>
    /// The full path of <paramref name="program"/> as a shell would find it, or null.
    /// </summary>
    private static string? Resolve(
        string program, string? path, string? pathExt, Func<string, bool> exists)
    {
        string[] directories = (path ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'))
            .Where(directory => directory.Length > 0)
            .ToArray();
        if (directories.Length == 0) return null;

        // PATHEXT decides which extensions count and in which order, but only the ones that
        // can actually be started are tried: a .ps1 shim beside the .cmd is not something
        // cmd.exe can run, and picking it because PATHEXT listed it first would swap one
        // "cannot start" for another.
        string[] extensions = (pathExt ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => Runnable.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (extensions.Length == 0) extensions = Runnable;

        foreach (string candidate in Candidates(program, extensions))
            foreach (string directory in directories)
            {
                string full;
                try { full = Path.Combine(directory, candidate); }
                catch (ArgumentException) { continue; } // A malformed PATH entry is skipped.
                if (exists(full)) return full;
            }

        return null;
    }

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
    /// Wraps a shim so cmd.exe runs it.
    ///
    /// "/s /c" with the whole command inside one pair of quotes is the form cmd documents for
    /// a quoted program path: everything between the first and last quote is taken literally,
    /// so a tool living under "Program Files" survives.
    /// </summary>
    private static string ThroughCmd(string leading, string program, string arguments)
        => $"{leading}cmd.exe /s /c \"\"{program}\"{arguments}\"";

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
