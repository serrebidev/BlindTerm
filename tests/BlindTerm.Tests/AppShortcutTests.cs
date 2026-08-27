using System.Windows.Forms;
using BlindTerm.App;

namespace BlindTerm.Tests;

public class AppShortcutTests
{
    [Fact]
    public void EveryAssignedApplicationShortcutUsesAltAndNotControl()
    {
        Assert.All(AppShortcuts.Assigned, shortcut =>
        {
            Assert.True(shortcut.HasFlag(Keys.Alt), $"{shortcut} does not use Alt");
            Assert.False(shortcut.HasFlag(Keys.Control), $"{shortcut} reserves Control");
        });
    }

    [Theory]
    [InlineData(Keys.Control | Keys.C)]
    [InlineData(Keys.Control | Keys.X)]
    [InlineData(Keys.Control | Keys.Z)]
    [InlineData(Keys.Control | Keys.V)]
    public void StandardEditingShortcutsAreNotApplicationChords(Keys shortcut)
        => Assert.False(AppShortcuts.IsApplicationChord(shortcut));

    [Fact]
    public void PrimaryNavigationUsesAltNumbers()
    {
        Assert.Equal(Keys.Alt | Keys.D1, AppShortcuts.FocusTranscript);
        Assert.Equal(Keys.Alt | Keys.D2, AppShortcuts.FocusCommandLine);
        Assert.Equal(Keys.Alt | Keys.D3, AppShortcuts.ToggleReview);
    }

    [Fact]
    public void EveryAltChordIsKeptForCommandsAndMenuAccess()
        => Assert.True(AppShortcuts.IsApplicationChord(Keys.Alt | Keys.T));

    [Theory]
    [InlineData(Keys.C)]
    [InlineData(Keys.X)]
    [InlineData(Keys.Z)]
    [InlineData(Keys.R)]
    public void ControlChordsReachAnyActiveInlineProgram(Keys key)
        => Assert.True(AppShortcuts.ShouldPassControlChord(
            Keys.Control | key, foregroundProgramActive: true, terminalInputFocused: true));

    [Theory]
    [InlineData(Keys.Control | Keys.V)]
    [InlineData(Keys.Control | Keys.Shift | Keys.V)]
    public void PasteStaysLocalSoATypedLineCanStillBePastedIntoIt(Keys shortcut)
        => Assert.False(AppShortcuts.ShouldPassControlChord(
            shortcut, foregroundProgramActive: true, terminalInputFocused: true));

    [Fact]
    public void ControlChordsStayNativeWhenNoInlineProgramIsActive()
        => Assert.False(AppShortcuts.ShouldPassControlChord(
            Keys.Control | Keys.C, foregroundProgramActive: false, terminalInputFocused: true));

    [Theory]
    [InlineData(Keys.Control | Keys.A)]
    [InlineData(Keys.Control | Keys.C)]
    [InlineData(Keys.Control | Keys.V)]
    [InlineData(Keys.Control | Keys.Shift | Keys.Home)]
    [InlineData(Keys.Control | Keys.Shift | Keys.End)]
    public void OutputFocusKeepsNativeCaretSelectionAndClipboardShortcuts(Keys shortcut)
        => Assert.False(AppShortcuts.ShouldPassControlChord(
            shortcut, foregroundProgramActive: true, terminalInputFocused: false));

    [Fact]
    public void ControlAltNeverBypassesTheAltCommandNamespace()
        => Assert.False(AppShortcuts.ShouldPassControlChord(
            Keys.Control | Keys.Alt | Keys.C,
            foregroundProgramActive: true,
            terminalInputFocused: true));

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Down)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.End)]
    [InlineData(Keys.PageUp)]
    [InlineData(Keys.PageDown)]
    [InlineData(Keys.Escape)]
    public void AnEmptyCommandLineDrivesTheRunningProgram(Keys key)
        => Assert.True(AppShortcuts.ShouldPassNavigationKey(
            key, foregroundProgramActive: true, terminalInputFocused: true, commandLineEmpty: true));

    [Theory]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.Up)]
    public void TypedTextCanStillBeEdited(Keys key)
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            key, foregroundProgramActive: true, terminalInputFocused: true, commandLineEmpty: false));

    [Fact]
    public void TheShellPromptKeepsItsOwnHistoryAndCaretKeys()
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            Keys.Up, foregroundProgramActive: false, terminalInputFocused: true,
            commandLineEmpty: true));

    [Fact]
    public void OutputFocusKeepsNativeReadingKeys()
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            Keys.Down, foregroundProgramActive: true, terminalInputFocused: false,
            commandLineEmpty: true));

    [Theory]
    [InlineData(Keys.Alt | Keys.Up)]
    [InlineData(Keys.Alt | Keys.Down)]
    [InlineData(Keys.Alt | Keys.End)]
    public void AltNavigationRemainsABlindTermCommand(Keys shortcut)
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            shortcut, foregroundProgramActive: true, terminalInputFocused: true,
            commandLineEmpty: true));

    [Fact]
    public void ControlNavigationIsAnsweredOnlyByTheControlChordRule()
    {
        Assert.False(AppShortcuts.ShouldPassNavigationKey(
            Keys.Control | Keys.Left, foregroundProgramActive: true, terminalInputFocused: true,
            commandLineEmpty: true));
        Assert.True(AppShortcuts.ShouldPassControlChord(
            Keys.Control | Keys.Left, foregroundProgramActive: true, terminalInputFocused: true));
    }

    [Fact]
    public void TabInAnActiveInlineProgramsInputRequestsCompletion()
        => Assert.True(AppShortcuts.ShouldSendCompletionTab(
            Keys.Tab, foregroundProgramActive: true, terminalInputFocused: true));

    [Theory]
    [InlineData(Keys.Shift | Keys.Tab)]
    [InlineData(Keys.Control | Keys.Tab)]
    [InlineData(Keys.Alt | Keys.Tab)]
    public void ModifiedTabRemainsWindowFocusOrApplicationNavigation(Keys key)
        => Assert.False(AppShortcuts.ShouldSendCompletionTab(
            key, foregroundProgramActive: true, terminalInputFocused: true));

    [Fact]
    public void TabFromOutputStillMovesFocusToInput()
        => Assert.False(AppShortcuts.ShouldSendCompletionTab(
            Keys.Tab, foregroundProgramActive: true, terminalInputFocused: false));

    [Fact]
    public void TabAtTheShellPromptIsNotTakenBeforeAgentLaunchAdaptation()
        => Assert.False(AppShortcuts.ShouldSendCompletionTab(
            Keys.Tab, foregroundProgramActive: false, terminalInputFocused: true));

    [Fact]
    public void ShiftTabMovesAFullScreenInputToReadableOutput()
        => Assert.Equal(AppShortcuts.ScreenTabTarget.Output,
            AppShortcuts.ScreenTab(Keys.Shift | Keys.Tab, screenMode: true, reviewing: false));

    [Fact]
    public void TabMovesFullScreenReviewOutputBackToLiveInput()
        => Assert.Equal(AppShortcuts.ScreenTabTarget.Input,
            AppShortcuts.ScreenTab(Keys.Tab, screenMode: true, reviewing: true));

    [Theory]
    [InlineData(Keys.Tab, true, false)]
    [InlineData(Keys.Shift | Keys.Tab, true, true)]
    [InlineData(Keys.Shift | Keys.Tab, false, false)]
    [InlineData(Keys.Control | Keys.Tab, true, false)]
    public void OtherScreenTabCombinationsKeepTheirExistingMeaning(
        Keys key, bool screenMode, bool reviewing)
        => Assert.Equal(AppShortcuts.ScreenTabTarget.None,
            AppShortcuts.ScreenTab(key, screenMode, reviewing));

    [Fact]
    public void ShiftTabExplicitlyMovesLineInputToOutput()
        => Assert.Equal(AppShortcuts.ScreenTabTarget.Output,
            AppShortcuts.LineTab(Keys.Shift | Keys.Tab, screenMode: false,
                inputFocused: true, outputFocused: false));

    [Fact]
    public void TabExplicitlyMovesLineOutputToInput()
        => Assert.Equal(AppShortcuts.ScreenTabTarget.Input,
            AppShortcuts.LineTab(Keys.Tab, screenMode: false,
                inputFocused: false, outputFocused: true));

    [Theory]
    [InlineData(Keys.Tab, false, true, false)]
    [InlineData(Keys.Shift | Keys.Tab, false, false, true)]
    [InlineData(Keys.Tab, true, false, true)]
    public void OtherLineTabCombinationsAreNotFocusChanges(
        Keys key, bool screenMode, bool inputFocused, bool outputFocused)
        => Assert.Equal(AppShortcuts.ScreenTabTarget.None,
            AppShortcuts.LineTab(key, screenMode, inputFocused, outputFocused));

    [Theory]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Back)]
    public void FocusMovementAndSubmissionAreNeverStolenFromTheWindow(Keys key)
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            key, foregroundProgramActive: true, terminalInputFocused: true, commandLineEmpty: true));
}
