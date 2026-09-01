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
///
/// Who pays it matters as much as how long it is. The wait buys nothing from a program that
/// has no composer -- a nested cmd, ssh, a Python prompt, less -- because there is no paste
/// heuristic there to fool: those read whatever arrives. Charging every child process for it
/// put a quarter of a second in front of every Return for the whole time one was running,
/// which is most of the time anyone is actually working. Only a composer BlindTerm knows
/// about, or a handed-over console whose program it cannot see, waits the long gap.
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
    /// The shortest run of characters any of these composers will call a paste. Codex is the
    /// strict one and needs three arriving together. A line shorter than that -- a bare
    /// Return, "y", "no", a menu digit -- trips no heuristic in any of them, so nothing ever
    /// suppresses its Return and there is nothing for the long wait to protect against.
    /// </summary>
    public const int ShortestPaste = 3;

    /// <summary>
    /// How long to wait before the Return, for a far end of this kind.
    ///
    /// A connection is never a composer -- everything on the far end of a MUD reads whole
    /// lines -- so the wait is not added to commands that are played at the speed they are
    /// sent. Everything else gets it as soon as a composer rather than a shell prompt is
    /// reading the line.
    /// </summary>
    public static int For(TerminalSessionKind kind, bool composerOwnsInput)
        => kind != TerminalSessionKind.Remote && composerOwnsInput ? Program : Prompt;

    /// <summary>
    /// The same, for a line whose length is known.
    ///
    /// A line too short to be read as a paste is sent at prompt speed whoever is reading it.
    /// This is the common case that felt worst: answering a question with a digit, or pressing
    /// Return on its own, waited exactly as long as submitting a paragraph.
    /// </summary>
    public static int For(TerminalSessionKind kind, bool composerOwnsInput, int lineLength)
        => lineLength >= ShortestPaste ? For(kind, composerOwnsInput) : Prompt;
}
