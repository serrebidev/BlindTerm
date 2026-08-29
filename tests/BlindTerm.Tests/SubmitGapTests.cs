using BlindTerm.Core;

namespace BlindTerm.Tests;

public class SubmitGapTests
{
    [Fact]
    public void AProgramReadingTheLineIsGivenTimeToSeeTheReturnAsAKeypress()
    {
        // The bug this exists for: Codex counted a whole line arriving in one write as a
        // paste, and a Return inside a paste is a newline. The text landed in its composer
        // and nothing was ever sent.
        Assert.Equal(SubmitGap.Program, SubmitGap.For(TerminalSessionKind.Shell, true));
        Assert.Equal(SubmitGap.Program, SubmitGap.For(TerminalSessionKind.Handoff, true));
        Assert.Equal(SubmitGap.Program, SubmitGap.For(TerminalSessionKind.Ssh, true));
    }

    [Fact]
    public void AnIdleShellPromptIsNotKeptWaiting()
    {
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Shell, false));
    }

    [Fact]
    public void AConnectionIsNeverKeptWaiting()
    {
        // A MUD reads whole lines and is played at the speed they are sent. Its session
        // always reports that the far end owns the input, so the kind is what decides.
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Remote, true));
    }

    [Fact]
    public void TheProgramGapClearsTheWindowsCodexSuppressesReturnFor()
    {
        // 60 ms deciding a burst has ended on Windows, then 120 ms of suppression after it.
        Assert.True(SubmitGap.Program > 60 + 120);
    }
}
