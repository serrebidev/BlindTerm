using System.Text;
using BlindTerm.Core.Vt;
using Xunit;

namespace BlindTerm.Tests;

/// <summary>
/// The input half of screen mode. In a full-screen program every keystroke goes to the
/// program, and nano's Ctrl-O, vim's arrows and htop's function keys only work if they arrive
/// as the exact sequences those programs listen for.
/// </summary>
public class KeyEncoderTests
{
    private static string Show(byte[]? bytes) =>
        bytes is null ? "<null>" : string.Concat(bytes.Select(b =>
            b == 0x1b ? "^[" : b < 0x20 || b >= 0x7f ? $"<{b:x2}>" : ((char)b).ToString()));

    [Theory]
    [InlineData("C-c", "<03>")]     // interrupt
    [InlineData("C-d", "<04>")]     // end of input
    [InlineData("C-o", "<0f>")]     // nano: write out
    [InlineData("C-x", "<18>")]     // nano: exit
    [InlineData("C-l", "<0c>")]     // redraw
    public void EncodesControlCombinations(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Theory]
    [InlineData("Enter", "<0d>")]
    [InlineData("Tab", "<09>")]
    [InlineData("Escape", "^[")]
    [InlineData("Backspace", "<7f>")]
    [InlineData("Shift-Tab", "^[[Z")]
    public void EncodesEditingKeys(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Theory]
    [InlineData("Up", "^[[A")]
    [InlineData("Down", "^[[B")]
    [InlineData("Right", "^[[C")]
    [InlineData("Left", "^[[D")]
    public void EncodesArrowsForAnOrdinaryShell(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Theory]
    [InlineData("Up", "^[OA")]
    [InlineData("Down", "^[OB")]
    public void EncodesArrowsForApplicationCursorKeys(string key, string expected)
    {
        // vim asks for application cursor keys. Sending the shell form there makes the arrow
        // keys insert letters instead of moving, which is the classic broken-terminal symptom.
        Assert.Equal(expected, Show(KeyEncoder.Parse(key, applicationCursorKeys: true)));
    }

    [Theory]
    [InlineData("F1", "^[OP")]
    [InlineData("F4", "^[OS")]
    [InlineData("F5", "^[[15~")]
    [InlineData("F10", "^[[21~")]   // htop: quit
    public void EncodesFunctionKeys(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Fact]
    public void EncodesAltAsEscapePrefix()
        => Assert.Equal("^[u", Show(KeyEncoder.Parse("M-u")));   // nano: undo

    /// <summary>
    /// Word movement. This is its own sequence, not the arrow with a modifier bolted on, and
    /// sending a plain arrow instead moves one character -- the command appears to work and
    /// quietly does the wrong thing, which is worse than doing nothing.
    /// </summary>
    [Theory]
    [InlineData("C-Right", "^[[1;5C")]
    [InlineData("C-Left", "^[[1;5D")]
    [InlineData("C-Up", "^[[1;5A")]
    [InlineData("C-Down", "^[[1;5B")]
    [InlineData("S-Right", "^[[1;2C")]      // extend the selection
    [InlineData("C-S-Left", "^[[1;6D")]
    [InlineData("C-Home", "^[[1;5H")]
    [InlineData("C-End", "^[[1;5F")]
    public void EncodesModifiedNavigationKeys(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Theory]
    [InlineData("C-Delete", "^[[3;5~")]
    [InlineData("S-PgUp", "^[[5;2~")]
    public void EncodesModifiedTildeKeys(string key, string expected)
        => Assert.Equal(expected, Show(KeyEncoder.Parse(key)));

    [Fact]
    public void ModifiedArrowsIgnoreApplicationCursorKeys()
    {
        // There is only one form once a modifier is involved, so vim and the shell agree.
        Assert.Equal("^[[1;5C", Show(KeyEncoder.Parse("C-Right", applicationCursorKeys: true)));
    }

    [Theory]
    [InlineData(KeyModifiers.Shift, 2)]
    [InlineData(KeyModifiers.Alt, 3)]
    [InlineData(KeyModifiers.Control, 5)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, 6)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, 7)]
    public void ModifierParameterFollowsTheTerminalConvention(KeyModifiers modifiers, int expected)
        => Assert.Equal(expected, KeyEncoder.ModifierParameter(modifiers));

    [Fact]
    public void AcceptsRawHexForAnythingUnnamed()
        => Assert.Equal("^[[A", Show(KeyEncoder.Parse("hex:1b5b41")));

    [Fact]
    public void PassesASinglePrintableCharacterThrough()
        => Assert.Equal("i", Show(KeyEncoder.Parse("i")));

    [Fact]
    public void EncodesNonAsciiAsUtf8()
        => Assert.Equal(Encoding.UTF8.GetBytes("é"), KeyEncoder.Parse("é"));

    [Theory]
    [InlineData("NotAKey")]
    [InlineData("hex:zz")]
    [InlineData("hex:1b5")]     // odd number of digits
    public void ReturnsNullForNamesItDoesNotKnow(string key)
        => Assert.Null(KeyEncoder.Parse(key));
}
