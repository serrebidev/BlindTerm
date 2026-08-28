using BlindTerm.Core.Vt;

namespace BlindTerm.Core;

/// <summary>
/// The terminal without a window: bytes in, transcript updates out.
///
/// Everything here is driven by <see cref="Feed"/>, so a live session and a replayed capture
/// go down exactly the same path. That is the point: a program whose output comes out wrong
/// becomes a capture, and the capture becomes a test that needs no window, no shell and no
/// pty.
/// </summary>
public sealed class TerminalCore
{
    public TerminalEngine Engine { get; }
    public TranscriptBuilder Builder { get; }
    public Transcript Transcript => Builder.Transcript;
    public CommandBlockTracker CommandBlocks { get; }

    /// <summary>A batch of changes is ready.</summary>
    public event Action<TerminalUpdate>? Updated;

    public TerminalCore(int columns = 120, int rows = 30, int scrollback = 100_000)
    {
        Engine = new TerminalEngine(columns, rows, scrollback);
        Builder = new TranscriptBuilder(Engine);
        CommandBlocks = new CommandBlockTracker();
        Engine.MarkReceived += CommandBlocks.MarkReceived;
        Builder.RowBecameLine += CommandBlocks.RowBecameLine;
        Builder.RowsResynced += CommandBlocks.ResetRows;
    }

    /// <summary>
    /// Adds lines the app writes itself rather than the far end: a ready message, "Connecting
    /// to...", an exit message.
    ///
    /// They take transcript numbers like any other line, so anything counting lines is not
    /// thrown out, and they come back as a batch to publish so the window mirrors them and the
    /// reader announces them without anything having to know where they came from. The batch
    /// is marked external: nothing was read from the terminal, so it carries no verdict on the
    /// prompt the far end is sitting at.
    /// </summary>
    /// <param name="quiet">
    /// Whether the lines go in without being announced. See <see cref="TerminalUpdate.Quiet"/>.
    /// </param>
    public TerminalUpdate AppendExternal(IReadOnlyList<string> lines, bool quiet = false)
    {
        var update = new TerminalUpdate
        {
            FirstNewLine = Transcript.Count,
            External = true,
            Quiet = quiet,
        };
        Builder.AppendExternal(lines);
        update.NewLines.AddRange(lines);
        return update;
    }

    /// <summary>
    /// Feeds bytes from the pty and publishes what they changed.
    ///
    /// What is on the screen becomes lines before anything wipes it. Otherwise the line a
    /// screen-clearing command was typed on, and anything printed just before the wipe in the
    /// same read, are gone before they were ever read. The feed is split at the wipe rather
    /// than merely flushed beforehand, so it makes no difference whether the two arrived
    /// together.
    /// </summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;

        // An escape sequence can be split across two reads. The VT parser copes -- it is a
        // state machine -- but the wipe scanner below reads whole sequences, and a wipe it
        // fails to see is a wipe that destroys the screen before what was on it has been read.
        // So a trailing sequence that is still incomplete is held back and prepended to the
        // next read, which is the only way the scanner ever sees one that arrived in pieces.
        if (_carry.Count > 0)
        {
            var joined = new byte[_carry.Count + bytes.Length];
            _carry.CopyTo(joined);
            bytes.CopyTo(joined.AsSpan(_carry.Count));
            _carry.Clear();
            FeedComplete(joined);
            return;
        }

        FeedComplete(bytes);
    }

    private readonly List<byte> _carry = new();

    /// <summary>
    /// Feeds everything that is not an incomplete trailing escape sequence, and keeps what is.
    /// </summary>
    private void FeedComplete(ReadOnlySpan<byte> bytes)
    {
        int partial = TrailingPartialLength(bytes);
        if (partial > 0)
        {
            _carry.AddRange(bytes[^partial..]);
            bytes = bytes[..^partial];
            if (bytes.IsEmpty) return;
        }

        FeedSplittingAtWipes(bytes);
    }

    /// <summary>
    /// Feeds anything held back waiting to be completed. The end of a capture, or of a
    /// session, is where a sequence that never finished has to be let through.
    /// </summary>
    public void Flush()
    {
        if (_carry.Count == 0) return;
        byte[] held = _carry.ToArray();
        _carry.Clear();
        FeedSplittingAtWipes(held);
    }

    /// <summary>
    /// How many bytes at the end are an escape sequence that has not finished arriving, and
    /// so cannot be scanned yet. Capped, so that a stray escape byte in binary output cannot
    /// stall the stream.
    /// </summary>
    internal static int TrailingPartialLength(ReadOnlySpan<byte> bytes)
    {
        const int longest = 16;

        int floor = Math.Max(0, bytes.Length - longest);
        for (int i = bytes.Length - 1; i >= floor; i--)
        {
            if (bytes[i] != 0x1b) continue;

            int length = bytes.Length - i;
            if (length == 1) return 1;                          // a lone ESC, so far
            if (bytes[i + 1] != (byte)'[') return 0;            // not a CSI; nothing to wait for

            // CSI ends at its final byte. Until one arrives this could still become an erase.
            for (int p = i + 2; p < bytes.Length; p++)
            {
                if (bytes[p] >= 0x40 && bytes[p] <= 0x7e) return 0;   // final byte: complete
                if (bytes[p] < 0x20 || bytes[p] > 0x3f) return 0;     // malformed: let it through
            }
            return length;
        }
        return 0;
    }

    private void FeedSplittingAtWipes(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (true)
        {
            var wipe = FindScreenWipe(bytes[index..]);
            if (wipe is not var (offset, length)) break;

            int at = index + offset;
            if (at > index)
            {
                Engine.Feed(bytes[index..at]);
                Publish(beforeWipe: true);
            }

            // Then the wipe itself, on its own, so that what it leaves behind can be looked at
            // before whatever follows it in the same read is drawn into it.
            Engine.Feed(bytes[at..(at + length)]);
            Builder.NoteScreenErase();
            index = at + length;
        }

        if (index < bytes.Length) Engine.Feed(bytes[index..]);

        // Deliberately no "cursor is at the top left corner, so the screen was probably
        // wiped" fallback here. That guess is only needed when a wipe split across two reads
        // would otherwise be missed, which the carry buffer prevents; left in, it fires on the
        // ESC[H that legitimately follows a wipe and throws away the row mapping just rebuilt,
        // which appends a stale blank line instead of rewriting it.
        Publish();
    }

    /// <summary>
    /// Turns what has been fed into transcript lines, and hands out what changed.
    ///
    /// The assembly happens whether or not anyone is listening. Folding it into the event
    /// invocation would mean a core with no subscriber quietly never building a transcript at
    /// all, and reading one from it later would return nothing with no sign of why.
    /// </summary>
    public void Publish(bool beforeWipe = false)
    {
        TerminalUpdate update = Builder.Publish(beforeWipe);
        Updated?.Invoke(update);
    }

    /// <summary>
    /// Where this chunk erases the screen (ED 2), the scrollback (ED 3) or the terminal (RIS),
    /// and how long that sequence is.
    ///
    /// The parameters are read properly rather than assumed to be one byte: private forms
    /// (`ESC [ ? 2 J`) and explicit defaults (`ESC [ 02 J`) mean the same thing and appear in
    /// the wild, and a fixed-length guess silently mistakes them for something else.
    /// </summary>
    internal static (int Offset, int Length)? FindScreenWipe(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0x1b) continue;
            if (i + 1 >= bytes.Length) return null;    // truncated; the next read carries it

            // ESC c -- reset to initial state.
            if (bytes[i + 1] == (byte)'c') return (i, 2);
            if (bytes[i + 1] != (byte)'[') continue;

            // CSI: parameter bytes 0x30-0x3f, intermediates 0x20-0x2f, then one final byte.
            int p = i + 2;
            while (p < bytes.Length && bytes[p] >= 0x30 && bytes[p] <= 0x3f) p++;
            int paramsEnd = p;
            while (p < bytes.Length && bytes[p] >= 0x20 && bytes[p] <= 0x2f) p++;
            if (p >= bytes.Length) return null;        // truncated

            if (bytes[p] == (byte)'J' && ErasesEverything(bytes[(i + 2)..paramsEnd]))
                return (i, p - i + 1);
        }
        return null;
    }

    /// <summary>Whether an ED parameter list asks for the screen (2) or the scrollback (3).</summary>
    private static bool ErasesEverything(ReadOnlySpan<byte> parameters)
    {
        int value = 0;
        bool sawDigit = false;
        foreach (byte b in parameters)
        {
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                value = value * 10 + (b - (byte)'0');
                sawDigit = true;
            }
            else if (b == (byte)';')
            {
                if (sawDigit && (value == 2 || value == 3)) return true;
                value = 0;
                sawDigit = false;
            }
            // '?' and other private markers do not change which erase this is.
        }
        return sawDigit && (value == 2 || value == 3);
    }
}
