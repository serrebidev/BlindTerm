using BlindTerm.Core.Vt;

namespace BlindTerm.Core;

/// <summary>
/// Turns the terminal's grid into the transcript of logical lines the UI presents.
///
/// Rows become lines as the cursor passes them, wrapped rows are joined back into the line
/// they came from, and -- the part that matters -- a row that is redrawn afterwards rewrites
/// the line it already produced instead of adding another copy. A program that repaints its
/// own output in place therefore reads as one line that changes rather than as one copy per
/// frame, and what the transcript holds at the end is what a sighted user would see.
///
/// Only two things reset that: the scrollback being thrown away, and the screen being wiped.
/// Then the lines already in the transcript keep their text -- it is a transcript, not a
/// screen -- and let go of the rows they were built from, which are about to mean something
/// else.
/// </summary>
public sealed class TranscriptBuilder
{
    private readonly TerminalEngine _engine;

    /// <summary>The lines themselves. Nothing else writes to this.</summary>
    public Transcript Transcript { get; } = new();

    /// <summary>One past the last row that has been turned into a transcript line.</summary>
    private int _extentRow;

    /// <summary>
    /// The lowest row that still has a line. Rows below this were recycled out of the
    /// scrollback or renumbered by a clear, and are not ours to re-read.
    /// </summary>
    private int _mappedFrom;

    /// <summary>For each transcript line, the rows it was built from, as [start, end).</summary>
    private readonly List<(int Start, int End)> _lineRows = new();

    /// <summary>Row to the line it is part of, so a redrawn row can find the line to rewrite.</summary>
    private readonly Dictionary<int, int> _rowToLine = new();

    /// <summary>
    /// First row of the last frame that was painted, which is as far back as "what is on
    /// screen right now" reaches when the cursor is parked on a blank row under it.
    /// </summary>
    private int _frameStart;

    private int _lastTop;
    private int _lastTrimmed;

    /// <summary>Rows for a line that did not come from the buffer.</summary>
    private static readonly (int Start, int End) NoRows = (0, 0);

    /// <summary>
    /// A buffer row became, or was folded into, a transcript line. Shell integration markers
    /// wait on this: one usually lands on a row that is not a line yet, and only becomes a
    /// position in the transcript once the row it sits on has been read.
    /// </summary>
    public event Action<int, int>? RowBecameLine;

    public TranscriptBuilder(TerminalEngine engine) => _engine = engine;

    /// <summary>
    /// Lines the app writes itself rather than the shell: a ready message, an exit message.
    /// They take transcript numbers like any other line, so that anything counting lines is
    /// not thrown out, but no row ever maps to them.
    /// </summary>
    public void AppendExternal(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            Transcript.Append(line);
            _lineRows.Add(NoRows);
        }
    }

    /// <summary>
    /// Lets go of the rows when the screen has been wiped or renumbered. Called as the bytes
    /// arrive rather than when an update is published: the shell paints its prompt into the
    /// cleared screen a moment later, and by then it no longer looks wiped.
    /// </summary>
    public void NoteScreenErase()
    {
        if (_engine.IsAlternateScreen) return;
        if (!_engine.ScreenIsBlank()) return;

        // The whole screen is free, so reading starts again at the top of it. Where the cursor
        // happens to be is not the answer: a wipe that does not send the cursor home leaves
        // whatever is drawn next above it.
        ResyncRows(_engine.ScreenTop);
    }

    private void ResyncRows(int restart)
    {
        _rowToLine.Clear();
        for (int i = 0; i < _lineRows.Count; i++) _lineRows[i] = NoRows;
        _extentRow = restart;
        _mappedFrom = restart;
        _frameStart = restart;
    }

    /// <summary>
    /// Reads whatever the terminal has done since the last call and returns it as one batch.
    /// </summary>
    /// <param name="beforeWipe">
    /// Set for the read that happens just before the screen is erased, where the rows to read
    /// cannot be found from the cursor: a wipe is normally preceded by sending the cursor
    /// home, so by then it is above everything that is about to be lost.
    /// </param>
    public TerminalUpdate Publish(bool beforeWipe = false)
    {
        var update = new TerminalUpdate();

        // A full-screen program owns the screen. Build no lines: the screen is the document,
        // and the transcript waits where it was.
        if (_engine.IsAlternateScreen)
        {
            update.AlternateScreen = _engine.ScreenRows();
            return update;
        }

        // Nothing to do on the way back from a full-screen program. Taking the alternate
        // screen leaves the buffer underneath exactly as it was, cursor included, so every row
        // still means what it meant and the lines built from those rows are still theirs.
        // Resyncing here would throw that mapping away and read the rows a second time, which
        // appends a duplicate of whatever was on screen when the program started.

        int trimmed = _engine.TotalLinesTrimmed;
        int screenTop = _engine.ScreenTop;
        int top = screenTop - trimmed;
        int cursor = _engine.CursorRow;

        // The scrollback was thrown away, or the buffer renumbered under us, so the rows the
        // lines were built from are gone. A program moving the cursor up to repaint its own
        // output is not this: that is the ordinary case, handled by re-reading below.
        if (trimmed < _lastTrimmed || top < _lastTop) ResyncRows(cursor);
        _lastTrimmed = trimmed;
        _lastTop = top;

        // Rows recycled out of a full scrollback are gone and can no longer be read.
        if (_mappedFrom < trimmed)
        {
            foreach (int stale in _rowToLine.Keys.Where(r => r < trimmed).ToList()) _rowToLine.Remove(stale);
            _mappedFrom = trimmed;
        }
        if (_extentRow < trimmed) _extentRow = trimmed;

        // Everything from the top of the screen through the cursor is fair game: rows past the
        // extent become new lines, rows below it rewrite the lines they already produced.
        // Rows above the screen cannot be redrawn -- a program cannot address them -- so they
        // are never worth re-reading, which also keeps a batch's work to a screenful. Rows
        // that scrolled past unread in a burst still have to be read, hence the extent.
        int readFrom = Math.Max(_mappedFrom, Math.Min(_extentRow, screenTop));

        // Never start in the middle of a line: a wrapped group is read whole or not at all.
        if (_rowToLine.TryGetValue(readFrom, out int startLine) && startLine < _lineRows.Count)
            readFrom = Math.Min(readFrom, _lineRows[startLine].Start);

        int readEnd = cursor;
        if (beforeWipe)
        {
            // The cursor may already have been sent home ahead of the wipe, leaving the rows
            // about to be lost below it. Take the run of rows that still have something on
            // them; the blank row after it is where the screen's content ends.
            int bottom = _engine.ScreenEnd;
            while (readEnd < bottom && _engine.RowText(readEnd).Trim().Length > 0) readEnd++;
        }
        while (readEnd > readFrom && _engine.IsWrapped(readEnd)) readEnd--;

        update.FirstNewLine = Transcript.Count;

        int row = readFrom;
        while (row < readEnd)
        {
            string text = _engine.RowText(row);
            int end = row + 1;
            while (end < readEnd && _engine.IsWrapped(end))
            {
                text += _engine.RowText(end);
                end++;
            }

            int line;
            if (_rowToLine.TryGetValue(row, out int existing) && existing < _lineRows.Count)
            {
                line = existing;
                if (Transcript.Revise(existing, text) is { } edit) update.Edits.Add(edit);
            }
            else
            {
                line = Transcript.Append(text);
                _lineRows.Add((row, end));
                update.NewLines.Add(text);
            }

            _lineRows[line] = (row, end);
            for (int r = row; r < end; r++) _rowToLine[r] = line;
            RowBecameLine?.Invoke(row, line);

            row = end;
        }

        if (readEnd > readFrom) _frameStart = readFrom;
        if (readEnd > _extentRow) _extentRow = readEnd;

        update.LiveText = LiveText(readEnd);
        return update;
    }

    /// <summary>
    /// What the program has not finished printing: from the cursor's line group to the bottom
    /// of the screen. A prompt waiting for an answer never ends in a newline, so it never
    /// becomes a transcript line and this is the only place it is ever seen.
    /// </summary>
    private string LiveText(int readEnd)
    {
        var live = new List<string>();
        int screenEnd = _engine.ScreenEnd;

        for (int row = readEnd; row < screenEnd; row++)
        {
            if (!_engine.HasRow(row)) break;
            string text = _engine.RowText(row);
            if (_engine.IsWrapped(row) && live.Count > 0) live[^1] += text;
            else live.Add(text);
        }

        while (live.Count > 0 && live[^1].Trim().Length == 0) live.RemoveAt(live.Count - 1);
        if (live.Count > 0) return string.Join("\n", live);

        // The cursor is parked on an empty row under the frame that was just painted -- where
        // Claude Code leaves it between frames. "Nothing" is the wrong answer to "what is on
        // screen now": the last line of that frame is.
        int floor = Math.Max(Math.Max(_frameStart, _mappedFrom), readEnd - _engine.Rows);
        for (int above = readEnd - 1; above >= floor; above--)
        {
            string text = _engine.RowText(above);
            if (text.Trim().Length > 0) return text;
        }
        return string.Empty;
    }
}
