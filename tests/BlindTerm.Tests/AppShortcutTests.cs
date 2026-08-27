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
    [InlineData(Keys.V)]
    public void ControlChordsReachAnyActiveInlineProgram(Keys key)
        => Assert.True(AppShortcuts.ShouldPassControlChord(
            Keys.Control | key, foregroundProgramActive: true, terminalInputFocused: true));

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
}
