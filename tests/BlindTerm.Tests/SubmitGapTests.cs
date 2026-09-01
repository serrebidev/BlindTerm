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

    [Fact]
    public void ALineTooShortToBeReadAsAPasteIsSentAtPromptSpeed()
    {
        // No composer calls one or two characters a paste, so nothing suppresses their
        // Return. A bare Return and a one-key answer were the submissions that waited a
        // quarter of a second for a heuristic that could never have fired.
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Shell, true, 0));
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Shell, true, 1));
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Shell, true, 2));
    }

    [Fact]
    public void ALineLongEnoughToBeReadAsAPasteStillWaitsForItsComposer()
    {
        // The regression this must never reintroduce: the line lands in Codex's composer and
        // the Return adds a newline to it instead of sending it.
        Assert.Equal(SubmitGap.Program, SubmitGap.For(TerminalSessionKind.Shell, true, 3));
        Assert.Equal(
            SubmitGap.Program,
            SubmitGap.For(TerminalSessionKind.Shell, true, "explain this file".Length));
    }

    [Fact]
    public void AProgramWithNoComposerIsNeverWaitedFor()
    {
        // A nested cmd, ssh or a Python prompt reads whatever arrives. There is no paste
        // heuristic to fool, so the wait bought nothing and delayed every Return.
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Shell, false, 40));
        Assert.Equal(SubmitGap.Prompt, SubmitGap.For(TerminalSessionKind.Ssh, false, 40));
    }

    [Fact]
    public void ThePasteThresholdIsTheStrictestAnyComposerUses()
    {
        // Codex's is three characters arriving together; anything below that is safe for all
        // of them. Raising this past three would start skipping a wait that is needed.
        Assert.Equal(3, SubmitGap.ShortestPaste);
    }
}
