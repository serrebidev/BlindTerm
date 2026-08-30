using BlindTerm.App;

namespace BlindTerm.Tests;

/// <summary>
/// Turning a Windows key press into the bytes a terminal program is waiting for.
///
/// This is the layer between the window and <c>KeyEncoder</c>, and it is where the
/// framework's own ideas about keys have to be overruled: Tab moves focus, arrows move
/// between controls, Escape closes things, Enter presses buttons. In nano or vim every one of
/// those belongs to the program instead, and a key translated to nothing is a key the program
/// never hears.
/// </summary>
public class KeyTranslatorTests
{
    private static string Show(byte[]? bytes) =>
        bytes is null ? "<null>" : string.Concat(bytes.Select(b =>
            b == 0x1b ? "^[" : b < 0x20 || b >= 0x7f ? $"<{b:x2}>" : ((char)b).ToString()));

    [Theory]
    [InlineData(Keys.Up, "^[[A")]
    [InlineData(Keys.Down, "^[[B")]
    [InlineData(Keys.Right, "^[[C")]
    [InlineData(Keys.Left, "^[[D")]
    public void ArrowsReachTheProgram(Keys key, string expected)
        => Assert.Equal(expected, Show(KeyTranslator.Translate(key, applicationCursorKeys: false)));

    [Theory]
    [InlineData(Keys.Up, "^[OA")]
    [InlineData(Keys.Down, "^[OB")]
    public void ArrowsFollowApplicationCursorKeyMode(Keys key, string expected)
    {
        // vim asks for application cursor keys. Sending the shell form there types letters
        // instead of moving, which is the classic broken-terminal symptom.
        Assert.Equal(expected, Show(KeyTranslator.Translate(key, applicationCursorKeys: true)));
    }

    [Theory]
    [InlineData(Keys.Enter, "<0d>")]
    [InlineData(Keys.Tab, "<09>")]
    [InlineData(Keys.Escape, "^[")]
    [InlineData(Keys.Back, "<7f>")]
    public void KeysTheFrameworkWouldHaveEatenReachTheProgram(Keys key, string expected)
        => Assert.Equal(expected, Show(KeyTranslator.Translate(key, false)));

    [Fact]
    public void FunctionKeysAreTranslatedAcrossTheWholeRow()
    {
        // htop puts its whole menu on F1 to F10, so a gap anywhere in this range is a command
        // the user cannot reach at all.
        for (Keys key = Keys.F1; key <= Keys.F12; key++)
            Assert.NotNull(KeyTranslator.Translate(key, false));
    }

    [Fact]
    public void ModifiersTravelWithTheKey()
    {
        // Ctrl+Right is how a terminal editor moves by word. Dropping the modifier moves one
        // character instead and quietly does the wrong thing, which is worse than nothing.
        string plain = Show(KeyTranslator.Translate(Keys.Right, false));
        string withControl = Show(KeyTranslator.Translate(Keys.Right | Keys.Control, false));

        Assert.NotEqual(plain, withControl);
        Assert.Contains("5C", withControl, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Keys.C, "<03>")]
    [InlineData(Keys.D, "<04>")]
    [InlineData(Keys.O, "<0f>")]
    [InlineData(Keys.X, "<18>")]
    public void ControlLettersBecomeControlCodes(Keys key, string expected)
        => Assert.Equal(expected, Show(KeyTranslator.Translate(key | Keys.Control, false)));

    [Theory]
    [InlineData(Keys.ControlKey)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.Menu)]
    [InlineData(Keys.LWin)]
    [InlineData(Keys.RWin)]
    public void HoldingAModifierIsNotAKeyPress(Keys key)
        => Assert.Null(KeyTranslator.Translate(key, false));

    [Theory]
    [InlineData(Keys.A)]
    [InlineData(Keys.Z)]
    [InlineData(Keys.D5)]
    [InlineData(Keys.A | Keys.Shift)]
    public void OrdinaryTypingIsLeftToWindows(Keys key)
    {
        // Returning null here is the point: the character path handles it instead, so the
        // keyboard layout, dead keys and the screen reader's own echo stay Windows' problem.
        Assert.Null(KeyTranslator.Translate(key, false));
    }

    [Fact]
    public void KeysWithNoTerminalMeaningAreLeftAlone()
    {
        Assert.Null(KeyTranslator.Translate(Keys.VolumeUp, false));
        Assert.Null(KeyTranslator.Translate(Keys.BrowserBack, false));
    }

    [Fact]
    public void PasteIsRawWhenNoProgramAskedForBracketing()
        => Assert.Equal("hello", Show(KeyTranslator.Paste("hello", bracketedPaste: false)));

    [Fact]
    public void PasteIsWrappedWhenTheProgramEnabledBracketedPaste()
    {
        // vim switches auto-indent off for a block wrapped this way. Without the markers a
        // pasted block is re-indented line by line into nonsense.
        Assert.Equal("^[[200~hello^[[201~", Show(KeyTranslator.Paste("hello", bracketedPaste: true)));
    }
}
