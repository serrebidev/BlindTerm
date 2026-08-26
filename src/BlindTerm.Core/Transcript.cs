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
/// Offsets are in UTF-16 code units, which is what a Win32 edit control counts in, so a
/// mirror can apply an edit without converting anything.
/// </summary>
public sealed class Transcript
{
    private readonly List<string> _lines = new();
    private readonly List<int> _offsets = new();

    /// <summary>One entry per logical line, in order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Character offset of the start of each line in the assembled text.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    /// <summary>Length of the assembled text, counting the newline after every line.</summary>
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
        _offsets.Add(Length);
        _lines.Add(text);
        Length += text.Length + 1;
        return _lines.Count - 1;
    }

    /// <summary>
    /// Rewrites one line. Null when the text is unchanged, which is the common case: most of
    /// what a redraw rewrites is identical to what was there.
    /// </summary>
    public Edit? Revise(int line, string text)
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

    /// <summary>The whole transcript as one string, for the initial fill and for copying.</summary>
    public string Text()
    {
        var builder = new System.Text.StringBuilder(Length);
        foreach (string line in _lines) builder.Append(line).Append('\n');
        return builder.ToString();
    }
}
