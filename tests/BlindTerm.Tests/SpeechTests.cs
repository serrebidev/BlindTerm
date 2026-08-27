using BlindTerm.Core;
using BlindTerm.Core.Speech;
using Xunit;

namespace BlindTerm.Tests;

public class SsmlTests
{
    /// <summary>
    /// Terminal output is full of ampersands and angle brackets -- shell redirection, HTML,
    /// diffs, C++ -- and an unescaped one does not mispronounce a word, it makes the whole
    /// utterance invalid so NVDA says nothing at all. Silence is the worst failure this
    /// program has, so the escaping is pinned down here.
    /// </summary>
    [Theory]
    [InlineData("a & b", "<speak>a &amp; b</speak>")]
    [InlineData("cat < in > out", "<speak>cat &lt; in &gt; out</speak>")]
    [InlineData("say \"hi\"", "<speak>say &quot;hi&quot;</speak>")]
    [InlineData("it's", "<speak>it&apos;s</speak>")]
    [InlineData("make 2>&1", "<speak>make 2&gt;&amp;1</speak>")]
    public void EscapesXmlSignificantCharacters(string input, string expected)
        => Assert.Equal(expected, NvdaScreenReader.Ssml(input));

    [Fact]
    public void DropsControlCharactersXmlForbids()
    {
        // A stray control byte would otherwise invalidate the document and silence the line.
        string input = "before" + (char)0x07 + (char)0x1b + (char)0x00 + "after";
        string result = NvdaScreenReader.Ssml(input);
        Assert.Equal("<speak>beforeafter</speak>", result);
    }

    [Fact]
    public void KeepsWhitespaceXmlAllows()
        => Assert.Equal("<speak>a\tb\nc</speak>", NvdaScreenReader.Ssml("a\tb\nc"));

    [Fact]
    public void KeepsNonAsciiText()
        => Assert.Equal("<speak>café — ok</speak>", NvdaScreenReader.Ssml("café — ok"));
}

public class LineNewsTests
{
    private static TerminalUpdate Appending(int firstLine, params string[] lines)
    {
        var update = new TerminalUpdate { FirstNewLine = firstLine };
        update.NewLines.AddRange(lines);
        return update;
    }

    [Fact]
    public void SpeaksNewLinesOnce()
    {
        var news = new LineNews();
        Assert.Equal(new[] { "one", "two" }, news.News(Appending(0, "one", "two")));
    }

    [Fact]
    public void SaysNothingWhenAFrameRepaintsTheSameWords()
    {
        var news = new LineNews();
        news.News(Appending(0, "Working", "Please wait"));

        // The same lines rewritten with the text they already had: a repaint, not news.
        var repaint = new TerminalUpdate();
        repaint.Edits.Add(new Transcript.Edit(0, 0, 7, "Working"));
        repaint.Edits.Add(new Transcript.Edit(1, 8, 11, "Please wait"));

        Assert.Empty(news.News(repaint));
    }

    [Fact]
    public void SpeaksALineThatActuallyChanges()
    {
        var news = new LineNews();
        news.News(Appending(0, "Working 1"));

        var changed = new TerminalUpdate();
        changed.Edits.Add(new Transcript.Edit(0, 0, 9, "Working 2"));

        Assert.Equal(new[] { "Working 2" }, news.News(changed));
    }

    [Fact]
    public void SkipsBlankLines()
    {
        var news = new LineNews();
        Assert.Equal(new[] { "text" }, news.News(Appending(0, "", "   ", "text")));
    }

    [Fact]
    public void ReportsALineOnceEvenWhenAppendedAndRewrittenInTheSameBatch()
    {
        // A line can be added and then patched before the batch is published. It is one line
        // and should be spoken once, with the text it ended up holding.
        var news = new LineNews();
        var update = Appending(3, "partial");
        update.Edits.Add(new Transcript.Edit(3, 0, 7, "complete"));

        Assert.Equal(new[] { "complete" }, news.News(update));
    }
}
