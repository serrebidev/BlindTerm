namespace BlindTerm.Core;

/// <summary>
/// The transcript as a document: logical lines, and where each one starts in the assembled
/// text the UI mirrors.
///
/// Lines are usually appended and never touched again. They cannot be only appended, though:
/// a program that redraws rows it has already printed -- Claude Code's screen reader mode
/// erases and reprints its whole frame several times a second -- keeps rewriting the rows
/// earlier lines were built from. Repeating those rows would fill the transcript with stale
/// copies of every frame, so the line a row produced is rewritten in place instead.
///
/// Offsets count one separator per line, which is what the transcript's own <see cref="Text"/>
/// writes. A Win32 edit control holds CRLF, so its offset for a line is <see cref="CaretOffset"/>,
/// which is that many characters further along again.
/// </summary>
public sealed class Transcript
{
    private readonly List<string> _lines = new();
    private readonly List<int> _offsets = new();

    /// <summary>
    /// Held while the lines are changed or walked.
    ///
    /// Everything else here is on one thread at a time. These are not: the terminal's reader
    /// thread appends a line while the window thread is reading the transcript out to copy
    /// it, and walking a list that is being appended to throws "Collection was modified" from
    /// the window thread, which is the end of the program. Reading one line by index is safe
    /// either way, so only the paths that walk the whole list take this.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>One entry per logical line, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Character offset of the start of each line in the assembled text.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    /// <summary>Length of the assembled text, counting one separator after every line.</summary>
    public int Length { get; private set; }

    public int Count => _lines.Count;

    /// <summary>
    /// One replacement to make in the text a mirror of this transcript holds. Ranges are the
    /// ranges to replace at the moment the edit is produced, so a mirror that applies a batch
    /// in the order it arrived stays in step; applying them out of order does not work.
    /// </summary>
    public readonly record struct Edit(int Line, int Start, int OldLength, string Text);

    /// <summary>
    /// Offset of the start of a line, clamped, so callers can turn a line number into
    /// somewhere to put the caret.
    /// </summary>
    public int OffsetOfLine(int line)
    {
        if (_offsets.Count == 0) return 0;
        return _offsets[Math.Clamp(line, 0, _offsets.Count - 1)];
    }

    /// <summary>
    /// Offset of the start of a line as the edit control holding the mirror counts, which is
    /// where its caret goes. A line number past the end gives the end of the document.
    ///
    /// One character per line further along than <see cref="OffsetOfLine"/>: the control holds
    /// CRLF, so a caret placed by the other one lands a character too early per line before it
    /// -- a whole screenful out by the time a session is long enough to matter.
    /// </summary>
    public int CaretOffset(int line)
    {
        int clamped = Math.Clamp(line, 0, _lines.Count);
        return clamped == _lines.Count ? CaretLength : _offsets[clamped] + clamped;
    }

    /// <summary>The length of the document as the mirror holds it, past the last line.</summary>
    public int CaretLength => Length + _lines.Count;

    /// <summary>Line containing a character offset, by binary search over the starts.</summary>
    public int LineAtOffset(int offset)
    {
        if (_offsets.Count == 0) return 0;
        int low = 0, high = _offsets.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_offsets[middle] <= offset) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    public int Append(string text)
    {
        lock (_gate)
        {
            _offsets.Add(Length);
            _lines.Add(text);
            Length += text.Length + 1;
            return _lines.Count - 1;
        }
    }

    /// <summary>
    /// Rewrites one line. Null when the text is unchanged, which is the common case: most of
    /// what a redraw rewrites is identical to what was there.
    /// </summary>
    public Edit? Revise(int line, string text)
    {
        lock (_gate)
        {
            if (line < 0 || line >= _lines.Count || _lines[line] == text) return null;

            int old = _lines[line].Length;
            var edit = new Edit(line, _offsets[line], old, text);
            _lines[line] = text;

            int delta = text.Length - old;
            if (delta != 0)
            {
                for (int i = line + 1; i < _offsets.Count; i++) _offsets[i] += delta;
                Length += delta;
            }
            return edit;
        }
    }

    /// <summary>
    /// The whole transcript as one string, for the initial fill and for copying.
    ///
    /// Lines end the way Windows counts them rather than the way this class does: the two
    /// places this goes are a Win32 edit control, which takes a lone separator as no line
    /// break at all, and the clipboard.
    /// </summary>
    public string Text()
    {
        lock (_gate)
        {
            var builder = new System.Text.StringBuilder(Length + _lines.Count);
            foreach (string line in _lines) builder.Append(line).Append(Environment.NewLine);
            return builder.ToString();
        }
    }

    /// <summary>
    /// A copy of the lines from <paramref name="from"/> onwards, taken in one piece.
    ///
    /// Used by the paths that walk the transcript on the window thread, where walking the
    /// live list while the reader thread appends to it is an exception rather than a slightly
    /// stale answer.
    /// </summary>
    public string[] Snapshot(int from)
    {
        lock (_gate)
        {
            from = Math.Clamp(from, 0, _lines.Count);
            return _lines.GetRange(from, _lines.Count - from).ToArray();
        }
    }
}
