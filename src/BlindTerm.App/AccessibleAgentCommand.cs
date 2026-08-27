using System.Text.RegularExpressions;

namespace BlindTerm.App;

/// <summary>
/// Selects the least repainting interface an agent CLI offers when it is launched as a
/// simple shell command.
///
/// Environment variables cover Claude too, but its explicit flag is retained here because
/// it takes precedence over settings and has survived regressions in environment detection.
/// Codex currently has no screen-reader renderer; raw scrollback with animations disabled
/// is its copy-friendly linear TUI, and staying out of the alternate screen is what keeps the
/// command line -- and therefore the keys its pickers are driven with -- on screen at all.
/// OpenCode's minimal interface is its intended linear path. Freebuff exposes no corresponding
/// switch and already renders inline, so no unsupported argument is invented for it.
/// </summary>
internal static partial class AccessibleAgentCommand
{
    private static readonly HashSet<string> OpenCodeSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "acp", "agent", "attach", "completion", "db", "debug", "export", "github",
        "import", "mcp", "models", "plugin", "pr", "providers", "run", "serve",
        "session", "stats", "uninstall", "upgrade", "web",
    };

    public static string Adapt(string command)
    {
        Match match = SimpleLaunch().Match(command);
        if (!match.Success) return command;

        string leading = match.Groups["leading"].Value;
        string executable = match.Groups["executable"].Value;
        string rest = match.Groups["rest"].Value;
        string name = Path.GetFileNameWithoutExtension(executable);

        // Do not reinterpret compound shell expressions. The user may be testing an exact
        // command line or depending on quoting rules that belong to the shell, not to us.
        if (rest.IndexOfAny(['|', ';', '&']) >= 0) return command;

        var additions = new List<string>();
        if (name.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasOption(rest, "--ax-screen-reader")) additions.Add("--ax-screen-reader");
        }
        else if (name.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasOption(rest, "--no-alt-screen") && !HasConfig(rest, "tui.alternate_screen"))
                additions.Add("--no-alt-screen");
            if (!HasConfig(rest, "tui.raw_output_mode"))
                additions.Add("-c tui.raw_output_mode=true");
            if (!HasConfig(rest, "tui.animations"))
                additions.Add("-c tui.animations=false");
        }
        else if (name.Equals("opencode", StringComparison.OrdinalIgnoreCase))
        {
            if (Arguments(rest).Any(OpenCodeSubcommands.Contains)) return command;

            if (!HasOption(rest, "--mini")) additions.Add("--mini");
            if (!HasOption(rest, "--no-replay") && !HasOption(rest, "--replay"))
                additions.Add("--no-replay");
        }

        if (additions.Count == 0) return command;
        return $"{leading}{executable} {string.Join(' ', additions)}{rest}";
    }

    private static bool HasOption(string arguments, string option)
        => Regex.IsMatch(arguments, $@"(?:^|\s){Regex.Escape(option)}(?:\s|=|$)",
                         RegexOptions.IgnoreCase);

    private static bool HasConfig(string arguments, string key)
        => Regex.IsMatch(arguments,
                         $@"(?:^|\s)(?:-c|--config)(?:\s+|=)[^\r\n]*{Regex.Escape(key)}\s*=",
                         RegexOptions.IgnoreCase);

    private static IEnumerable<string> Arguments(string arguments)
        => Regex.Matches(arguments, "(?:\\\"[^\\\"]*\\\"|'[^']*'|[^\\s]+)")
            .Select(match => match.Value.Trim('"', '\''));

    [GeneratedRegex(@"^(?<leading>\s*)(?<executable>(?:claude|codex|opencode|freebuff)(?:\.(?:exe|cmd|ps1))?)(?<rest>(?:\s.*)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleLaunch();
}
