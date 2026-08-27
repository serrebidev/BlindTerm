using BlindTerm.Core.Vt;
using XTerm.Common;

namespace BlindTerm.Core;

internal sealed class CommandBlockAnchor(int row)
{
    public int Row { get; } = row;
    public int Line { get; set; } = -1;
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
}

/// <summary>
/// Converts OSC 133 markers, which arrive at buffer rows, into transcript line ranges.
/// </summary>
public sealed class CommandBlockTracker
{
    private sealed class ActiveBlock
    {
        public CommandBlockAnchor Start { get; init; } = null!;
        public CommandBlockAnchor? Output { get; set; }
    }

    private readonly List<CommandBlock> _blocks = new();
    private readonly List<CommandBlockAnchor> _anchors = new();
    private readonly Dictionary<int, int> _rowLines = new();
    private ActiveBlock? _active;

    public IReadOnlyList<CommandBlock> Blocks => _blocks;
    public bool HasMarkers { get; private set; }

    public void ResetRows()
    {
        _rowLines.Clear();
        foreach (var anchor in _anchors) anchor.Line = -1;
        _anchors.Clear();
        _active = null;
        foreach (var block in _blocks) block.SyncAnchors();
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
                var end = NewAnchor(mark.Row);
                block.EndAnchor = end;
                block.SyncAnchors();
                _blocks.Add(block);
                _active = null;
                break;
        }
    }

    /// <summary>Called as the transcript assembler turns a buffer row into a line.</summary>
    public void RowBecameLine(int row, int line)
    {
        _rowLines[row] = line;
        foreach (var anchor in _anchors)
            if (anchor.Row == row) anchor.Line = line;
        foreach (var block in _blocks) block.SyncAnchors();
        if (_active is null) return;
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
        return string.Join(Environment.NewLine, transcript.Lines.Skip(start).Take(end - start));
    }

    private CommandBlockAnchor NewAnchor(int row)
    {
        var anchor = new CommandBlockAnchor(row);
        _anchors.Add(anchor);
        if (_rowLines.TryGetValue(row, out int line)) anchor.Line = line;
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
