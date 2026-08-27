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

    [Theory]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Back)]
    public void FocusMovementAndSubmissionAreNeverStolenFromTheWindow(Keys key)
        => Assert.False(AppShortcuts.ShouldPassNavigationKey(
            key, foregroundProgramActive: true, terminalInputFocused: true, commandLineEmpty: true));
}
