using BlindTerm.App;

namespace BlindTerm.Tests;

/// <summary>
/// Which keys the running program gets alone, and which are also allowed into the native edit
/// that gives a reader a caret.
///
/// The failure this pins down: Home and End were held as program-only keys. In nano the
/// program's cursor moved to the start of the line and the caret the reader was standing in
/// did not move at all, so NVDA waited for a caret move that never arrived, timed out, and
/// read out the character it was still sitting on. Up and Down escaped that because they move
/// the cursor to another row, which is the one thing this control is told about.
///
/// So the rule is about what the control can express, not about what a terminal program wants:
/// one row of text can be moved along, and cannot be moved away from.
/// </summary>
public class KeyboardEchoProxyTests
{
    [Theory]
    [InlineData(Keys.Home)]
    [InlineData(Keys.End)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    public void MovementAlongTheLineReachesTheControlThatGivesTheReaderACaret(Keys key)
    {
        Assert.True(KeyboardEchoProxy.IsNativeEditKey(key));
        Assert.False(KeyboardEchoProxy.IsTerminalNavigation(key));
    }

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Down)]
    [InlineData(Keys.PageUp)]
    [InlineData(Keys.PageDown)]
    public void MovementToAnotherRowStaysWithTheProgram(Keys key)
    {
        Assert.True(KeyboardEchoProxy.IsTerminalNavigation(key));
        Assert.False(KeyboardEchoProxy.IsNativeEditKey(key));
    }
}
