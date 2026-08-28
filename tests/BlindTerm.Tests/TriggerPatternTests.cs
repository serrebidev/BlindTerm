using BlindTerm.Core.Triggers;

namespace BlindTerm.Tests;

public class TriggerPatternTests
{
    private static TriggerPattern Compile(string pattern, TriggerMatch match, bool caseSensitive = false)
    {
        Assert.True(TriggerPattern.TryCompile(pattern, match, caseSensitive,
            out TriggerPattern? compiled, out string? problem), problem);
        return compiled!;
    }

    [Fact]
    public void PlainTextMatchesAnywhereInTheLine()
    {
        TriggerPattern pattern = Compile("build failed", TriggerMatch.Contains);
        Assert.NotNull(pattern.Match("error: the build failed after 3 warnings"));
        Assert.Null(pattern.Match("the build succeeded"));
    }

    [Fact]
    public void PlainTextIgnoresCapitalsUnlessAsked()
    {
        Assert.NotNull(Compile("HUNGRY", TriggerMatch.Contains).Match("You are hungry."));
        Assert.Null(Compile("HUNGRY", TriggerMatch.Contains, caseSensitive: true)
            .Match("You are hungry."));
    }

    /// <summary>
    /// Plain text is a search, not an expression: a MUD prompt full of brackets and stars is
    /// exactly the thing anyone would paste into this box first.
    /// </summary>
    [Fact]
    public void PlainTextTakesRegularExpressionCharactersLiterally()
    {
        TriggerPattern pattern = Compile("[HP=100]", TriggerMatch.Contains);
        Assert.NotNull(pattern.Match("< [HP=100] > "));
        Assert.Null(pattern.Match("HP=100"));
    }

    [Fact]
    public void AWildcardHasToMatchTheWholeLine()
    {
        TriggerPattern pattern = Compile("*arrives from the north*", TriggerMatch.Wildcard);
        Assert.NotNull(pattern.Match("Fred arrives from the north."));

        // Without the stars it is the whole line or nothing, which is the rule that makes
        // "star at each end" mean something.
        Assert.Null(Compile("arrives", TriggerMatch.Wildcard).Match("Fred arrives."));
    }

    [Fact]
    public void AQuestionMarkStandsForExactlyOneCharacter()
    {
        TriggerPattern pattern = Compile("You have ? new messages.", TriggerMatch.Wildcard);
        Assert.NotNull(pattern.Match("You have 4 new messages."));
        Assert.Null(pattern.Match("You have 12 new messages."));
    }

    [Fact]
    public void EachWildcardComesBackInOrder()
    {
        TriggerPattern pattern = Compile("* tells you '*'", TriggerMatch.Wildcard);
        TriggerCapture? capture = pattern.Match("Fred tells you 'meet me at the gate'");

        Assert.NotNull(capture);
        Assert.Equal(["Fred", "meet me at the gate"], capture!.Groups);
    }

    [Fact]
    public void ExpandFillsInTheWholeLineAndEachWildcard()
    {
        TriggerCapture capture = Compile("* hits you for * damage*", TriggerMatch.Wildcard)
            .Match("A troll hits you for 42 damage.")!;

        Assert.Equal("42 from A troll", capture.Expand("$2 from $1"));
        Assert.Equal("A troll hits you for 42 damage.", capture.Expand("$0"));
        Assert.Equal("costs $50", capture.Expand("costs $$50"));
    }

    /// <summary>
    /// One trigger has to serve the line that sometimes has a second half. A number with
    /// nothing behind it is nothing, rather than the text "$3" being read out.
    /// </summary>
    [Fact]
    public void AWildcardThatMatchedNothingExpandsToNothing()
    {
        TriggerCapture capture = Compile("*Fred*", TriggerMatch.Wildcard).Match("Fred")!;
        Assert.Equal("[]", capture.Expand("[$1]"));
        Assert.Equal("[]", capture.Expand("[$7]"));
    }

    [Fact]
    public void ADollarSignThatIsNotASubstitutionIsLeftAlone()
    {
        TriggerCapture capture = Compile("gold", TriggerMatch.Contains).Match("100 gold")!;
        Assert.Equal("cost $x", capture.Expand("cost $x"));
        Assert.Equal("ends in $", capture.Expand("ends in $"));
    }

    [Fact]
    public void RegularExpressionGroupsComeBackTheSameWayWildcardsDo()
    {
        TriggerPattern pattern = Compile(@"^(\w+) has connected\.$", TriggerMatch.Regex);
        TriggerCapture? capture = pattern.Match("Fred has connected.");

        Assert.NotNull(capture);
        Assert.Equal("Fred", capture!.Groups[0]);
        Assert.Null(pattern.Match("Fred has disconnected."));
    }

    [Fact]
    public void ARegularExpressionThatWillNotCompileSaysSo()
    {
        Assert.False(TriggerPattern.TryCompile("(unclosed", TriggerMatch.Regex, false,
            out TriggerPattern? compiled, out string? problem));
        Assert.Null(compiled);
        Assert.NotNull(problem);
        Assert.NotEmpty(problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void APatternWithNothingInItIsRefused(string? pattern)
    {
        Assert.False(TriggerPattern.TryCompile(pattern, TriggerMatch.Contains, false,
            out _, out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void APatternLongerThanTheLimitIsRefusedRatherThanCompiled()
        => Assert.False(TriggerPattern.TryCompile(
            new string('a', TriggerPattern.MaximumLength + 1), TriggerMatch.Contains, false,
            out _, out _));

    /// <summary>
    /// The pattern is typed by the user and matched on the thread that draws the window. One
    /// awkward expression must cost a matching timeout, not the terminal.
    /// </summary>
    [Fact]
    public void APatternThatTakesTooLongIsTreatedAsNotMatching()
    {
        TriggerPattern pattern = Compile("^(a+)+$", TriggerMatch.Regex);
        Assert.Null(pattern.Match(new string('a', 40) + "!"));
    }
}
