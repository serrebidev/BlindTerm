namespace BlindTerm.Core;

/// <summary>
/// The environment a child shell is launched with.
///
/// Two jobs. First, describe the terminal honestly, so programs emit the escape sequences
/// the VT engine expects. Second, ask the tools that know how to be screen-reader friendly
/// to behave that way -- these flags are the difference between a usable session and a wall
/// of spinner frames, and no user should have to know to set them.
/// </summary>
public static class TerminalEnvironment
{
    public const string TermProgram = "BlindTerm";

    /// <summary>
    /// Overrides to merge over the current process's environment. A null value removes
    /// the variable from the child's environment entirely.
    /// </summary>
    public static Dictionary<string, string?> ForChild(string? version = null)
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
            ["TERM_PROGRAM"] = TermProgram,
            ["TERM_PROGRAM_VERSION"] = version ?? "0.1",

            // Claude Code: flat, labelled output instead of a repainting frame.
            ["CLAUDE_AX_SCREEN_READER"] = "1",

            // GitHub CLI: numbered prompts instead of arrow-key menus, no spinner.
            ["GH_ACCESSIBLE_PROMPTER"] = "1",
            ["GH_ACCESSIBLE_COLORS"] = "1",
            ["GH_SPINNER_DISABLED"] = "1",

            // The pseudo console is the authority on size. A stale inherited value here makes
            // programs lay out for a terminal that does not exist.
            ["LINES"] = null,
            ["COLUMNS"] = null,
        };

        if (Environment.GetEnvironmentVariable("LANG") is null)
            env["LANG"] = "en_US.UTF-8";

        return env;
    }
}
