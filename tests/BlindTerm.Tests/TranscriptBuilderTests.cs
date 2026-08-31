using System.Text;
using BlindTerm.Core;

namespace BlindTerm.Tests;

public class TranscriptBuilderTests
{
    [Theory]
    [InlineData(16_384)]
    [InlineData(7)]
    [InlineData(1)]
    public void InlineViewportRepaintDoesNotEraseScrolledHistory(int chunkSize)
    {
        var core = new TerminalCore(40, 5);

        Feed(core, "one\r\ntwo\r\nthree\r\nfour\r\nfive\r\n", chunkSize);

        // Inline TUIs keep completed output in scrollback but repaint their visible viewport
        // from the home position. The first visible row is now a line already recorded later
        // in the transcript; it moved, it did not replace the line that used to occupy this
        // terminal row.
        Feed(core, "\x1b[Hthree\x1b[K\r\nfour\x1b[K\r\nfive\x1b[K\r\nsix\x1b[K\r\n", chunkSize);

        Assert.Equal(["one", "two", "three", "four", "five", "six"],
                     core.Transcript.Lines.Where(line => line.Length > 0));
    }

    private static void Feed(TerminalCore core, string text, int chunkSize)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            core.Feed(bytes.AsSpan(offset, Math.Min(chunkSize, bytes.Length - offset)));
    }
}
