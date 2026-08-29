namespace BlindTerm.Core;

/// <summary>
/// How long to leave between a submitted line and the Return that sends it.
///
/// BlindTerm types a whole line at once, because the line is composed in an edit box a screen
/// reader can review before it is sent. Every agent CLI, meanwhile, guesses whether input was
/// typed or pasted from how fast it arrives, and treats a Return inside a paste as a newline
/// in the text rather than "send this" -- which is correct for a pasted paragraph and fatal
/// for a whole line delivered in one write.
///
/// Codex is the strict one. Its composer counts three or more characters arriving within
/// eight milliseconds of each other as a paste, and then keeps suppressing Return for a
/// further window afterwards, so a line submitted the way BlindTerm submits one landed in the
/// composer and stayed there: the text appeared, Return added a blank line to it, and nothing
/// was ever sent. Claude Code and OpenCode are looser and were never affected, but the same
/// heuristic is in all of them and the numbers are theirs to change.
///
/// So the Return is left late enough to be unmistakably a keypress. It is still shorter than
/// the pause a person leaves between finishing a line and reaching for Return, which is the
/// gap these heuristics are calibrated against in the first place.
/// </summary>
public static class SubmitGap
{
    /// <summary>
    /// A shell prompt, or a host on the network. Nothing here distinguishes typing from
    /// pasting, and a MUD is played at the speed lines are sent.
    /// </summary>
    public const int Prompt = 20;

    /// <summary>
    /// A program is reading the line rather than a shell prompt: an agent CLI, or anything
    /// else with a composer of its own. Comfortably past Codex's 120 ms suppression window
    /// and the 60 ms it waits on Windows before deciding a burst has ended.
    /// </summary>
    public const int Program = 250;

    /// <summary>
    /// How long to wait before the Return, for a far end of this kind.
    ///
    /// A connection is never a composer -- everything on the far end of a MUD reads whole
    /// lines -- so the wait is not added to commands that are played at the speed they are
    /// sent. Everything else gets it as soon as a program rather than a shell prompt is
    /// reading the line.
    /// </summary>
    public static int For(TerminalSessionKind kind, bool programOwnsInput)
        => kind != TerminalSessionKind.Remote && programOwnsInput ? Program : Prompt;
}
