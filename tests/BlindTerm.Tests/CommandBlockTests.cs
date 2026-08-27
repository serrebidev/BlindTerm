using System.Text;
using BlindTerm.Core;
using BlindTerm.Core.Vt;
using XTerm.Common;

namespace BlindTerm.Tests;

public class CommandBlockTests
{
    [Fact]
    public void TracksOsc133OutputAndCopiesOnlyOutput()
    {
        var core = new TerminalCore(80, 20);
        var tracker = core.CommandBlocks;
        tracker.MarkReceived(new MarkAt(ShellIntegrationMark.PromptStart, null, 10, 0));
        tracker.MarkReceived(new MarkAt(ShellIntegrationMark.CommandStart, null, 10, 0));
        tracker.MarkReceived(new MarkAt(ShellIntegrationMark.CommandExecuted, null, 10, 0));
        tracker.RowBecameLine(10, core.Transcript.Append("prompt"));
        tracker.RowBecameLine(11, core.Transcript.Append("output"));
        tracker.MarkReceived(new MarkAt(ShellIntegrationMark.CommandFinished, 0, 12, 0));
        tracker.RowBecameLine(12, core.Transcript.Append("next prompt"));

        Assert.Single(tracker.Blocks);
        CommandBlock block = tracker.Blocks[0];
        Assert.Equal(0, block.StartLine);
        Assert.Equal(0, block.ExitCode);
        Assert.Equal(1, block.OutputStartLine);
        Assert.Equal(2, block.OutputEndLine);
        Assert.Equal("output", tracker.CopyOutput(0, core.Transcript));
    }

    [Fact]
    public void FallsBackToWholeTranscriptWithoutMarkers()
    {
        var core = new TerminalCore(80, 20);
        core.Transcript.Append("one");
        core.Transcript.Append("two");

        Assert.Empty(core.CommandBlocks.Blocks);
        Assert.Contains("one", core.CommandBlocks.CopyOutput(0, core.Transcript));
        Assert.Contains("two", core.CommandBlocks.CopyOutput(0, core.Transcript));
    }

    [Fact]
    public void ScreenResyncDoesNotReuseOldAnchorLines()
    {
        var tracker = new CommandBlockTracker();
        tracker.MarkReceived(new BlindTerm.Core.Vt.MarkAt(XTerm.Common.ShellIntegrationMark.CommandStart, null, 4, 0));
        tracker.MarkReceived(new BlindTerm.Core.Vt.MarkAt(XTerm.Common.ShellIntegrationMark.CommandExecuted, null, 4, 0));
        tracker.MarkReceived(new BlindTerm.Core.Vt.MarkAt(XTerm.Common.ShellIntegrationMark.CommandFinished, 0, 5, 0));

        tracker.RowBecameLine(4, 10);
        Assert.Equal(10, tracker.Blocks[0].StartLine);
        tracker.ResetRows();
        tracker.RowBecameLine(4, 20);
        tracker.RowBecameLine(5, 21);
        Assert.False(tracker.Blocks[0].IsResolved);
    }

    [Fact]
    public void CoreResyncInvalidatesCommandRows()
    {
        var core = new TerminalCore(80, 20);
        int resyncs = 0;
        core.Builder.RowsResynced += () => resyncs++;
        core.CommandBlocks.MarkReceived(new MarkAt(ShellIntegrationMark.CommandStart, null, 0, 0));
        core.CommandBlocks.MarkReceived(new MarkAt(ShellIntegrationMark.CommandFinished, 0, 1, 0));
        core.CommandBlocks.RowBecameLine(0, 10);
        Assert.True(core.CommandBlocks.Blocks[0].IsResolved);

        core.Engine.Feed(Encoding.ASCII.GetBytes("\x1b[2J"));
        core.Builder.NoteScreenErase();
        core.CommandBlocks.RowBecameLine(0, 20);

        Assert.Equal(1, resyncs);
        Assert.False(core.CommandBlocks.Blocks[0].IsResolved);
    }
}
