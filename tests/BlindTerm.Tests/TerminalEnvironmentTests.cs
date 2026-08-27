using BlindTerm.Core;

namespace BlindTerm.Tests;

/// <summary>
/// What a program launched inside BlindTerm is told about where it is running.
///
/// A terminal cannot make somebody else's output accessible after the fact. A spinner that
/// has already been drawn as forty braille frames is forty braille frames, and the best any
/// reader can do with it is read them out. The one moment where that can be prevented is
/// before the program starts, by telling it that it is being read -- so the tools that know
/// how to behave differently do, and nobody has to know to set anything.
/// </summary>
public class TerminalEnvironmentTests
{
    private static readonly Dictionary<string, string?> Child = TerminalEnvironment.ForChild();

    [Theory]
    // Not BlindTerm's invention: GNOME's AT-SPI stack and Debian's dpkg-reconfigure already
    // use this to mean "a screen reader is active", and libraries such as term-a11y read it.
    // Reusing it is what makes a CLI nobody wrote for this terminal behave well inside it.
    [InlineData("ACCESSIBLE")]
    [InlineData("TERM_A11Y")]
    [InlineData("CLAUDE_AX_SCREEN_READER")]
    [InlineData("GH_ACCESSIBLE_PROMPTER")]
    [InlineData("GH_ACCESSIBLE_COLORS")]
    [InlineData("GH_SPINNER_DISABLED")]
    public void ToolsThatCanRenderPlainlyAreAskedTo(string variable)
    {
        Assert.Equal("1", Child[variable]);
    }

    [Fact]
    public void TheTerminalDescribesItselfHonestly()
    {
        // Claiming to be something it is not would get escape sequences the VT engine does
        // not implement; claiming less would lose colour and keys that do work.
        Assert.Equal("xterm-256color", Child["TERM"]);
        Assert.Equal("truecolor", Child["COLORTERM"]);
        Assert.Equal("BlindTerm", Child["TERM_PROGRAM"]);
        Assert.False(string.IsNullOrWhiteSpace(Child["TERM_PROGRAM_VERSION"]));
    }

    [Fact]
    public void AVersionCanBeStatedRatherThanLookedUp()
    {
        Assert.Equal("9.9.9", TerminalEnvironment.ForChild("9.9.9")["TERM_PROGRAM_VERSION"]);
    }

    [Fact]
    public void InheritedSizesAreRemovedRatherThanGuessedAt()
    {
        // The pseudo console is the authority on size. A value inherited from whatever
        // launched BlindTerm makes programs lay out for a terminal that does not exist, and
        // a null here means "remove this from the child's environment".
        Assert.True(Child.ContainsKey("LINES"));
        Assert.True(Child.ContainsKey("COLUMNS"));
        Assert.Null(Child["LINES"]);
        Assert.Null(Child["COLUMNS"]);
    }

    [Fact]
    public void VariableNamesAreMatchedTheWayWindowsMatchesThem()
    {
        // Windows environment variables are case-insensitive, and a dictionary that is not
        // would quietly set a second, ignored copy of one that already exists.
        Assert.Equal("1", Child["accessible"]);
        Assert.Equal("xterm-256color", Child["term"]);
    }
}
