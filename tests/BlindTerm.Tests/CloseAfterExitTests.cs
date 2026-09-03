using BlindTerm.App;
using BlindTerm.Core;

namespace BlindTerm.Tests;

public class CloseAfterExitTests
{
    [Fact]
    public void AHandedOverConsoleThatRanCleanlyClosesItsWindow()
    {
        Assert.True(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, 0, followingOutput: true, screenMode: false));
    }

    [Fact]
    public void AShellOrSshSessionThatEndedCleanlyClosesItsWindow()
    {
        Assert.True(CloseAfterExit.Wanted(
            TerminalSessionKind.Shell, 0, followingOutput: true, screenMode: false));
        Assert.True(CloseAfterExit.Wanted(
            TerminalSessionKind.Ssh, 0, followingOutput: true, screenMode: false));
    }

    [Fact]
    public void ARunThatFailedIsKeptForTheErrorToBeHeardOrRead()
    {
        // An ordinary failure, and a program ended by Ctrl+C (0xC000013A).
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, 1, followingOutput: true, screenMode: false));
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, unchecked((int)0xC000013A), followingOutput: true, screenMode: false));
    }

    [Fact]
    public void AnExitCodeThatCouldNotBeReadIsKept()
    {
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, null, followingOutput: true, screenMode: false));
    }

    [Fact]
    public void AConnectionWindowIsNeverClosed()
    {
        // A MUD or other network window ends with a disconnect rather than an exit code, but
        // even a reported zero must not close it: the transcript is all that is left of the
        // session, and the far end ended it, not the user.
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Remote, 0, followingOutput: true, screenMode: false));
    }

    [Fact]
    public void ReadingTheTranscriptKeepsTheWindowOpen()
    {
        // The caret parked in history rather than at the live end: somebody is reading.
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, 0, followingOutput: false, screenMode: false));
    }

    [Fact]
    public void AFullScreenViewStillUpAtTheEndKeepsTheWindowOpen()
    {
        // The program's last screen has not been cleared yet, so it may still be being read.
        Assert.False(CloseAfterExit.Wanted(
            TerminalSessionKind.Handoff, 0, followingOutput: true, screenMode: true));
    }
}
