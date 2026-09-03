using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>
/// Whether a window whose session has just ended should close itself.
///
/// A console Windows pops up for a single program is over when that program is over, and a
/// console opened for a shell or an SSH connection is over when the shell or connection
/// ends. Such a window has nothing left to do -- there is no prompt to come back to, and
/// the command line has been disabled -- so it can only sit there saying so. A run that
/// ended cleanly is the case where that is all it is doing: whatever the program printed
/// has been said as it happened, and closing the window is what the console it stands in
/// for would have done anyway.
///
/// A run that failed is kept, because the window is then the place the error can be heard
/// or read back from. A connection dropped by the far end is kept too: it ended without
/// anyone meaning it to, and nothing says there is no more use for the transcript.
/// </summary>
internal static class CloseAfterExit
{
    /// <summary>
    /// Whether a session that ended with this exit code leaves nothing worth keeping the
    /// window open for.
    /// </summary>
    /// <param name="kind">What the session was showing, as it was at the end.</param>
    /// <param name="code">The exit code, or null when it could not be read.</param>
    /// <param name="followingOutput">
    /// Whether the reader is at the live end of the transcript rather than parked
    /// somewhere earlier in it. A window someone is reading is not closed under them.
    /// </param>
    /// <param name="screenMode">
    /// Whether a full-screen view is still up at the moment the session ends. A screen a
    /// program drew is still being read until it has been cleared, so the window waits.
    /// </param>
    public static bool Wanted(TerminalSessionKind kind, int? code, bool followingOutput, bool screenMode)
        => code == 0
           && kind is TerminalSessionKind.Handoff or TerminalSessionKind.Shell or TerminalSessionKind.Ssh
           && followingOutput
           && !screenMode;
}
