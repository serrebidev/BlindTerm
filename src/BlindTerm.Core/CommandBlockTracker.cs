using BlindTerm.Core.Vt;
using XTerm.Common;

namespace BlindTerm.Core;

internal sealed class CommandBlockAnchor(int row)
{
    public int Row { get; } = row;
    public int Line { get; set; } = -1;

    /// <summary>
    /// The finished command this marker belongs to, once there is one.
    ///
    /// Set when the command finishes, so a marker whose row is read later knows which block to
    /// bring up to date. Null while the command is still running, and on a marker that was
    /// resolved before the block existed -- neither has anything to sync.
    /// </summary>
    public CommandBlock? Block { get; set; }
}

/// <summary>A completed OSC 133 command and the transcript lines containing its output.</summary>
public sealed class CommandBlock
{
    public int StartLine { get; internal set; } = -1;
    public int OutputStartLine { get; internal set; } = -1;
    public int OutputEndLine { get; internal set; } = -1;
    public int? ExitCode { get; internal set; }

    public bool IsResolved => StartLine >= 0;

    internal CommandBlockAnchor? StartAnchor { get; set; }
    internal CommandBlockAnchor? OutputAnchor { get; set; }
    internal CommandBlockAnchor? EndAnchor { get; set; }

    internal void SyncAnchors()
    {
        StartLine = StartAnchor?.Line is >= 0 and var start ? start : -1;
        OutputStartLine = OutputAnchor?.Line is >= 0 and var output ? output + 1 : -1;
        OutputEndLine = EndAnchor?.Line is >= 0 and var end ? end : -1;
    }

    /// <summary>Those rows mean something else now, so their markers resolve to nothing.</summary>
    internal void ForgetRows()
    {
        if (StartAnchor is not null) StartAnchor.Line = -1;
        if (OutputAnchor is not null) OutputAnchor.Line = -1;
        if (EndAnchor is not null) EndAnchor.Line = -1;
        SyncAnchors();
    }
}

/// <summary>
/// Converts OSC 133 markers, which arrive at buffer rows, into transcript line ranges.
/// </summary>
public sealed class CommandBlockTracker
{
    /// <summary>
    /// How many finished commands are kept.
    ///
    /// Alt+Up and Alt+Down step through these and copying a command's output takes the most
    /// recent one, so this only has to outlast any session somebody would walk back through.
    /// There has to be a bound at all: this list grew for the whole life of a session, and a
    /// long one -- a shell prompting after every command, all day -- held every command it had
    /// ever run, and every one of them was re-checked for every row the transcript read.
    /// </summary>
    private const int MaximumBlocks = 2000;

    private sealed class ActiveBlock
    {
        public CommandBlockAnchor Start { get; init; } = null!;
        public CommandBlockAnchor? Output { get; set; }
    }

    private readonly List<CommandBlock> _blocks = new();

    /// <summary>
    /// Markers whose row has not been read yet, by the row they are waiting on.
    ///
    /// A marker usually arrives on a row that is not a line yet and becomes a position in the
    /// transcript only when that row is. Indexing them by that row is what makes it cheap:
    /// rows are asked about one at a time, and looking through every marker of the session for
    /// each of them made a shell session cost more the longer it ran. A resolved marker is
    /// dropped from here -- the block holds it from then on, which is all a screen wipe needs.
    /// </summary>
    private readonly Dictionary<int, List<CommandBlockAnchor>> _anchors = new();

    private readonly Dictionary<int, int> _rowLines = new();
    private ActiveBlock? _active;

    public IReadOnlyList<CommandBlock> Blocks => _blocks;
    public bool HasMarkers { get; private set; }

    public void ResetRows()
    {
        _rowLines.Clear();
        _anchors.Clear();
        _active = null;
        foreach (CommandBlock block in _blocks) block.ForgetRows();
    }

    public void MarkReceived(MarkAt mark)
    {
        HasMarkers = true;
        switch (mark.Mark)
        {
            case ShellIntegrationMark.PromptStart:
                FinishUnclosedBlock();
                _active = new ActiveBlock { Start = NewAnchor(mark.Row) };
                break;
            case ShellIntegrationMark.CommandStart:
                _active ??= new ActiveBlock { Start = NewAnchor(mark.Row) };
                break;
            case ShellIntegrationMark.CommandExecuted:
                if (_active is not null) _active.Output = NewAnchor(mark.Row);
                break;
            case ShellIntegrationMark.CommandFinished:
                if (_active is null) return;
                var block = new CommandBlock
                {
                    ExitCode = mark.ExitCode,
                    StartAnchor = _active.Start,
                    OutputAnchor = _active.Output,
                };
                _active.Start.Block = block;
                if (_active.Output is not null) _active.Output.Block = block;
                var end = NewAnchor(mark.Row, block);
                block.EndAnchor = end;
                block.SyncAnchors();
                _blocks.Add(block);
                if (_blocks.Count > MaximumBlocks) _blocks.RemoveRange(0, _blocks.Count - MaximumBlocks);
                _active = null;
                break;
        }
    }

    /// <summary>Called as the transcript assembler turns a buffer row into a line.</summary>
    public void RowBecameLine(int row, int line)
    {
        _rowLines[row] = line;
        if (!_anchors.Remove(row, out List<CommandBlockAnchor>? waiting)) return;

        foreach (CommandBlockAnchor anchor in waiting)
        {
            anchor.Line = line;
            // Only the block that owns this marker can have changed, and only by this one line.
            anchor.Block?.SyncAnchors();
        }
    }

    public string CopyOutput(int index, Transcript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        if (_blocks.Count == 0) return transcript.Text();
        if (index < 0 || index >= _blocks.Count) return string.Empty;

        CommandBlock block = _blocks[index];
        // A shell can emit C and D on the blank row after its output. In that case neither
        // marker row becomes a transcript line, so use the first line after the prompt boundary
        // rather than dropping the whole output range.
        int fallback = block.StartLine >= 0 ? block.StartLine + 1 : 0;
        int start = Math.Clamp(block.OutputStartLine >= 0 ? block.OutputStartLine : fallback, 0, transcript.Count);
        int end = block.OutputEndLine < 0
            ? transcript.Count
            : Math.Clamp(block.OutputEndLine, start, transcript.Count);
        return string.Join(Environment.NewLine, transcript.Snapshot(start).Take(end - start));
    }

    private CommandBlockAnchor NewAnchor(int row, CommandBlock? block = null)
    {
        var anchor = new CommandBlockAnchor(row) { Block = block };

        // A marker on a row that has already been read has its answer already, and is never
        // looked at again.
        if (_rowLines.TryGetValue(row, out int line))
        {
            anchor.Line = line;
            return anchor;
        }

        if (!_anchors.TryGetValue(row, out List<CommandBlockAnchor>? waiting))
        {
            waiting = new List<CommandBlockAnchor>(2);
            _anchors[row] = waiting;
        }
        waiting.Add(anchor);
        return anchor;
    }

    private void FinishUnclosedBlock()
    {
        // A fresh prompt is a safe boundary after a shell crash or a remote disconnect. Do not
        // expose the abandoned state as a command the user can navigate to.
        if (_active is null) return;
        _active = null;
    }
}
