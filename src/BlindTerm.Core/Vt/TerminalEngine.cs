using System.Text;
using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Options;

namespace BlindTerm.Core.Vt;

/// <summary>Where a shell integration marker landed, in scroll-invariant rows.</summary>
public readonly record struct MarkAt(ShellIntegrationMark Mark, int? ExitCode, int Row, int Column);

/// <summary>
/// The VT engine, addressed the way the transcript needs rather than the way a screen does.
///
/// A terminal buffer is a grid that scrolls: row 3 means something different after the screen
/// has moved up. Every row here is instead counted from the first line ever written
/// (<see cref="TotalLinesTrimmed"/> plus the index within the buffer), so a row number keeps
/// identifying the same text for as long as that text exists. Lines in the transcript are
/// built from those numbers, which is what lets a program redraw a row and have the line it
/// produced rewritten rather than repeated.
/// </summary>
public sealed class TerminalEngine
{
    private readonly Terminal _terminal;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private char[] _chars = new char[16 * 1024];
    private TerminalBuffer? _watchedBuffer;

    /// <summary>Lines that have fallen out of the far end of the scrollback and are gone.</summary>
    public int TotalLinesTrimmed { get; private set; }

    /// <summary>A marker arrived. Raised while parsing, so the cursor is where it appeared.</summary>
    public event Action<MarkAt>? MarkReceived;

    /// <summary>The program rang the bell.</summary>
    public event Action? Bell;

    public event Action<string>? TitleChanged;

    /// <summary>Bytes the terminal owes the program: cursor position reports and the like.</summary>
    public event Action<byte[]>? Respond;

    /// <summary>The alternate screen was entered or left.</summary>
    public event Action<bool>? AlternateScreenChanged;

    public int Columns => _terminal.Cols;
    public int Rows => _terminal.Rows;

    /// <summary>True while a full-screen program owns the screen: vim, htop, an editor over ssh.</summary>
    public bool IsAlternateScreen => _terminal.IsAlternateBufferActive;

    /// <summary>Whether the program has asked for pasted text to be bracketed.</summary>
    public bool BracketedPaste => _terminal.BracketedPasteMode;

    /// <summary>
    /// Whether the program has asked for application cursor keys. vim does. Sending the
    /// ordinary shell form to a program in this mode makes the arrow keys insert letters
    /// instead of moving, which is the classic broken-terminal symptom.
    /// </summary>
    public bool ApplicationCursorKeys => _terminal.ApplicationCursorKeys;

    public TerminalEngine(int columns = 120, int rows = 30, int scrollback = 100_000)
    {
        _terminal = new Terminal(new TerminalOptions
        {
            Cols = columns,
            Rows = rows,
            Scrollback = scrollback,
            TermName = "xterm-256color",
        });

        _terminal.BellRang += (_, _) => Bell?.Invoke();
        _terminal.TitleChanged += (_, e) => TitleChanged?.Invoke(e.Title);
        _terminal.DataReceived += (_, e) => Respond?.Invoke(Encoding.UTF8.GetBytes(e.Data));
        _terminal.ShellIntegrationMarkReceived += (_, e) =>
            MarkReceived?.Invoke(new MarkAt(e.Mark, e.ExitCode, CursorRow, _terminal.Buffer.X));
        _terminal.BufferChanged += (_, _) =>
        {
            WatchTrimming();
            AlternateScreenChanged?.Invoke(_terminal.IsAlternateBufferActive);
        };

        WatchTrimming();
    }

    /// <summary>
    /// Counts lines as the scrollback discards them, so row numbers stay meaningful. The
    /// buffer object is swapped when a full-screen program takes over, so this follows it.
    /// </summary>
    private void WatchTrimming()
    {
        if (ReferenceEquals(_watchedBuffer, _terminal.Buffer)) return;
        if (_watchedBuffer is not null) _watchedBuffer.Trimmed -= OnTrimmed;
        _watchedBuffer = _terminal.Buffer;
        _watchedBuffer.Trimmed += OnTrimmed;
    }

    /// <summary>
    /// Only the normal buffer's trimming counts. The alternate screen has no scrollback and
    /// is thrown away wholesale when the program using it exits, so counting anything it does
    /// would shift every row number in the transcript underneath it.
    /// </summary>
    private void OnTrimmed(int count)
    {
        if (_terminal.IsAlternateBufferActive) return;
        TotalLinesTrimmed += count;
    }

    /// <summary>
    /// Feeds bytes from the pty.
    ///
    /// The decoder is kept between calls: a multi-byte character can be split across two
    /// reads, and decoding each read on its own would turn it into replacement characters.
    /// </summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;

        int needed = _decoder.GetCharCount(bytes, flush: false);
        if (needed > _chars.Length) _chars = new char[Math.Max(needed, _chars.Length * 2)];

        int count = _decoder.GetChars(bytes, _chars, flush: false);
        if (count > 0) _terminal.Write(new string(_chars, 0, count));
    }

    public void Resize(int columns, int rows) => _terminal.Resize(columns, rows);

    // ---- Scroll-invariant addressing ----

    /// <summary>Row at the top of the screen. Rows above this are scrollback.</summary>
    public int ScreenTop => TotalLinesTrimmed + _terminal.Buffer.YBase;

    /// <summary>Row the cursor is on.</summary>
    public int CursorRow => ScreenTop + _terminal.Buffer.Y;

    public int CursorColumn => _terminal.Buffer.X;

    /// <summary>
    /// The cursor's row counted from the top of the screen rather than from the first line
    /// ever written. This is the one screen mode cares about: in a full-screen program the
    /// screen is the document, and "which row am I on" is the question being asked.
    /// </summary>
    public int CursorScreenRow => _terminal.Buffer.Y;

    /// <summary>One past the last row on screen.</summary>
    public int ScreenEnd => ScreenTop + _terminal.Rows;

    /// <summary>The oldest row still held.</summary>
    public int FirstAvailableRow => TotalLinesTrimmed;

    private BufferLine? LineAt(int row)
    {
        int index = row - TotalLinesTrimmed;
        var lines = _terminal.Buffer.Lines;
        if (index < 0 || index >= lines.Length) return null;
        return lines[index];
    }

    /// <summary>Whether the row is a continuation of the one above it.</summary>
    public bool IsWrapped(int row) => LineAt(row)?.IsWrapped ?? false;

    public bool HasRow(int row) => LineAt(row) is not null;

    /// <summary>
    /// Text of one row.
    ///
    /// Cells a program never wrote hold nothing at all, and `ls` separates its columns by
    /// tabbing across rather than padding, so taking the cells at face value runs the
    /// filenames together. Those cells become one space each. The empty cell that trails a
    /// double-width character is not one of them -- it is part of the character before it --
    /// so it is skipped rather than turned into a space.
    ///
    /// Trailing spaces are kept when the next row is a wrapped continuation, because there
    /// they are real characters at the wrap point.
    /// </summary>
    public string RowText(int row)
    {
        var line = LineAt(row);
        if (line is null) return string.Empty;

        var builder = new StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (i > 0 && line.GetWidth(i - 1) == 2 && line.GetWidth(i) == 0) continue;

            string content = line[i].Content;
            if (string.IsNullOrEmpty(content) || content == "\t") builder.Append(' ');
            else builder.Append(content);
        }

        string text = builder.ToString();
        return IsWrapped(row + 1) ? text : text.TrimEnd();
    }

    /// <summary>Every row of the screen as it stands, for a full-screen program.</summary>
    public string[] ScreenRows()
    {
        int top = ScreenTop;
        var rows = new string[_terminal.Rows];
        for (int i = 0; i < rows.Length; i++) rows[i] = RowText(top + i);
        return rows;
    }

    /// <summary>
    /// Whether every row on screen is blank, which is how a screen wipe is confirmed. The
    /// escape sequence alone is not enough: a program may clear a screen it is about to
    /// repaint in the same batch.
    /// </summary>
    public bool ScreenIsBlank()
    {
        for (int row = ScreenTop; row < ScreenEnd; row++)
        {
            if (!HasRow(row)) break;
            if (RowText(row).Trim().Length > 0) return false;
        }
        return true;
    }
}
