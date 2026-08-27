using BlindTerm.App;

namespace BlindTerm.Tests;

public class TextSelectionTests
{
    [Fact]
    public void ReplacementBeforeSelectionMovesTheWholeSelection()
    {
        var selection = new TextSelection(Start: 20, Length: 8);

        TextSelection adjusted = selection.AfterReplacement(
            replacementStart: 4, oldLength: 3, newLength: 7);

        Assert.Equal(new TextSelection(Start: 24, Length: 8), adjusted);
    }

    [Fact]
    public void ReplacementAfterSelectionLeavesItAlone()
    {
        var selection = new TextSelection(Start: 4, Length: 6);

        TextSelection adjusted = selection.AfterReplacement(
            replacementStart: 20, oldLength: 4, newLength: 9);

        Assert.Equal(selection, adjusted);
    }

    [Fact]
    public void ReplacementInsideSelectionPreservesBothOutsideEndpoints()
    {
        var selection = new TextSelection(Start: 4, Length: 20);

        TextSelection adjusted = selection.AfterReplacement(
            replacementStart: 10, oldLength: 4, newLength: 7);

        Assert.Equal(new TextSelection(Start: 4, Length: 23), adjusted);
    }

    [Fact]
    public void EndpointInsideReplacementMovesToAvailableReplacementText()
    {
        var selection = new TextSelection(Start: 12, Length: 10);

        TextSelection adjusted = selection.AfterReplacement(
            replacementStart: 10, oldLength: 8, newLength: 3);

        Assert.Equal(new TextSelection(Start: 12, Length: 5), adjusted);
    }
}
