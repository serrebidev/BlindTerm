using System.Text;
using BlindTerm.Core;
using Xunit;

namespace BlindTerm.Tests;

/// <summary>
/// The two byte-level scanners in TerminalCore. Both exist because a screen wipe has to be
/// seen *before* the engine applies it -- once the screen is gone, what was on it cannot be
/// read -- and a wipe that arrives in pieces is the case the macOS original gets wrong.
/// </summary>
public class ScreenWipeTests
{
    private static byte[] Bytes(string text) =>
        Encoding.ASCII.GetBytes(text.Replace("^[", ((char)0x1b).ToString()));

    [Theory]
    [InlineData("^[[2J", 0, 4)]
    [InlineData("^[[3J", 0, 4)]
    [InlineData("^[c", 0, 2)]
    [InlineData("hello^[[2J", 5, 4)]
    // The private form, which a fixed-length scanner mistakes for something else.
    [InlineData("^[[?2J", 0, 5)]
    // An explicitly written default, which is the same erase.
    [InlineData("^[[02J", 0, 5)]
    // Multiple parameters, the erase among them.
    [InlineData("^[[1;2J", 0, 6)]
    public void FindsAWipe(string input, int offset, int length)
    {
        var found = TerminalCore.FindScreenWipe(Bytes(input));
        Assert.NotNull(found);
        Assert.Equal((offset, length), found!.Value);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("^[[0J")]        // erase to end of screen only
    [InlineData("^[[1J")]        // erase to start of screen only
    [InlineData("^[[2A")]        // cursor up, not an erase
    [InlineData("^[[2K")]        // erase in line, not in screen
    [InlineData("^[]0;title")]
    public void IgnoresEverythingElse(string input)
        => Assert.Null(TerminalCore.FindScreenWipe(Bytes(input)));

    [Fact]
    public void TreatsATruncatedSequenceAsNotYetFound()
    {
        // Reporting a wipe here would consume bytes that are not all present.
        Assert.Null(TerminalCore.FindScreenWipe(Bytes("text^[[2")));
        Assert.Null(TerminalCore.FindScreenWipe(Bytes("text^[")));
    }
}

public class TrailingPartialTests
{
    private static byte[] Bytes(string text) =>
        Encoding.ASCII.GetBytes(text.Replace("^[", ((char)0x1b).ToString()));

    [Theory]
    [InlineData("hello^[", 1)]        // a lone ESC, so far
    [InlineData("hello^[[", 2)]       // CSI with nothing after it
    [InlineData("hello^[[2", 3)]      // still gathering parameters
    [InlineData("hello^[[?2", 4)]
    public void HoldsBackAnUnfinishedSequence(string input, int expected)
        => Assert.Equal(expected, TerminalCore.TrailingPartialLength(Bytes(input)));

    [Theory]
    [InlineData("hello")]
    [InlineData("hello^[[2J")]        // complete
    [InlineData("hello^[[2A")]        // complete, and not an erase
    [InlineData("hello^[c")]          // complete
    public void LetsCompleteInputThrough(string input)
        => Assert.Equal(0, TerminalCore.TrailingPartialLength(Bytes(input)));

    [Fact]
    public void GivesUpOnAnAbsurdlyLongSequence()
    {
        // A stray escape byte in binary output must not stall the stream forever.
        Assert.Equal(0, TerminalCore.TrailingPartialLength(Bytes("^[[" + new string('1', 40))));
    }

    [Fact]
    public void AWipeSplitAcrossTwoReadsIsStillSeen()
    {
        // The whole point of holding bytes back: fed in two pieces, the wipe is still found,
        // so what was on screen is read before it is destroyed.
        var core = new TerminalCore(40, 10);
        var seen = new List<string>();
        core.Updated += u => seen.AddRange(u.NewLines);

        core.Feed(Bytes("before the wipe\r\n^[["));
        core.Feed(Bytes("2J^[[H"));
        core.Feed(Bytes("after the wipe\r\n"));
        core.Flush();

        Assert.Equal(new[] { "before the wipe", "after the wipe" }, core.Transcript.Lines);
        Assert.Contains("before the wipe", seen);
    }
}
