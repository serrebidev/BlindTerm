using System.Text;

namespace BlindTerm.Core.Net;

/// <summary>
/// Lifts MUD Sound Protocol triggers out of the text stream.
///
/// Two things matter here, and the second is the one that matters most. A trigger asks for a
/// sound; a trigger left in the stream is a line reading "exclamation exclamation SOUND left
/// paren sword dot wav" in the middle of a fight. Whether or not sounds are turned on, these
/// have to come out of the text before anything reads it aloud.
///
/// A trigger only counts at the start of a line. That is what the protocol says, and it is
/// also the only thing standing between a sound effect and any player who types
/// "!!SOUND(scream.wav)" into a chat channel -- what the MUD echoes of that arrives after a
/// name and a colon, in the middle of a line, and is left alone as the ordinary text it is.
///
/// A trigger can arrive split across two reads, so an exclamation mark at the start of a line
/// is withheld until it is known to be a trigger or known not to be. Anything that turns out
/// not to be one is released unchanged, and nothing is held indefinitely: a run that stops
/// looking like a trigger, or grows past what a trigger could be, goes straight out as text.
/// </summary>
public sealed class MspScanner
{
    /// <summary>
    /// The longest a trigger may be before it is treated as ordinary text. Long enough for a
    /// file name, a type, a URL and every parameter, and short enough that a stream which
    /// merely starts with an exclamation mark is never held back for long.
    /// </summary>
    public const int MaximumTriggerLength = 512;

    private static readonly byte[] SoundPrefix = "!!SOUND("u8.ToArray();
    private static readonly byte[] MusicPrefix = "!!MUSIC("u8.ToArray();

    private readonly List<byte> _held = new();

    private bool _atLineStart = true;
    private bool _swallowLineEnding;

    /// <summary>Whether anything is currently being withheld from the text.</summary>
    public bool HasPartialTrigger => _held.Count > 0;

    /// <summary>
    /// The extra room a caller's text buffer needs beyond the bytes it passes in.
    ///
    /// This removes bytes from a read, but it can also hand back bytes withheld from an
    /// earlier one, so a read can come out longer than it went in -- by at most the length of
    /// what could be held.
    /// </summary>
    public const int Headroom = MaximumTriggerLength + 1;

    /// <summary>
    /// Splits received text into the text to show and the triggers to act on.
    /// <paramref name="text"/> must be at least <see cref="Headroom"/> bytes longer than
    /// <paramref name="received"/>.
    /// </summary>
    /// <returns>How many bytes of <paramref name="text"/> were written.</returns>
    public int Scan(ReadOnlySpan<byte> received, Span<byte> text, List<MspTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (text.Length < received.Length + Headroom)
            throw new ArgumentException(
                $"The text buffer must be at least {Headroom} bytes longer than the input.",
                nameof(text));

        int written = 0;
        foreach (byte value in received)
        {
            if (_held.Count > 0)
            {
                _held.Add(value);
                switch (Classify())
                {
                    case Verdict.Complete:
                        Complete(triggers);
                        break;
                    case Verdict.NotATrigger:
                        Release(text, ref written);
                        break;
                    case Verdict.Possible:
                        // Still could be one. Nothing is written until it is settled either way.
                        break;
                }
                continue;
            }

            // A trigger that took up its whole line would otherwise leave a blank line behind,
            // and a blank line is something a screen reader announces.
            if (_swallowLineEnding)
            {
                if (value == (byte)'\r') continue;
                _swallowLineEnding = false;
                if (value == (byte)'\n') { _atLineStart = true; continue; }
            }

            if (_atLineStart && value == (byte)'!')
            {
                _held.Add(value);
                continue;
            }

            Emit(value, text, ref written);
        }

        return written;
    }

    /// <summary>
    /// Releases anything still withheld. The end of a connection is where a run that never
    /// finished arriving has to be let through rather than lost.
    /// <paramref name="text"/> must have room for <see cref="Headroom"/> bytes.
    /// </summary>
    public int Flush(Span<byte> text)
    {
        if (text.Length < _held.Count)
            throw new ArgumentException("The text buffer is too small for what is held.", nameof(text));
        int written = 0;
        Release(text, ref written);
        return written;
    }

    private enum Verdict { Possible, Complete, NotATrigger }

    private Verdict Classify()
    {
        if (_held.Count > MaximumTriggerLength) return Verdict.NotATrigger;

        bool sound = StartsLike(SoundPrefix);
        bool music = StartsLike(MusicPrefix);
        if (!sound && !music) return Verdict.NotATrigger;

        // Still inside the name; nothing to decide yet.
        if (_held.Count < SoundPrefix.Length) return Verdict.Possible;

        for (int i = SoundPrefix.Length; i < _held.Count; i++)
        {
            byte value = _held[i];
            // A trigger lives on one line and holds printable text. Anything else means this
            // was never a trigger, and holding on to it would swallow real output.
            if (value is (byte)'\r' or (byte)'\n' || value < 0x20) return Verdict.NotATrigger;
            if (value == (byte)')') return i == _held.Count - 1 ? Verdict.Complete : Verdict.NotATrigger;
        }

        return Verdict.Possible;
    }

    /// <summary>Whether what is held so far could still grow into <paramref name="prefix"/>.</summary>
    private bool StartsLike(byte[] prefix)
    {
        int shared = Math.Min(_held.Count, prefix.Length);
        for (int i = 0; i < shared; i++)
        {
            if (Upper(_held[i]) != prefix[i]) return false;
        }
        return true;
    }

    private static byte Upper(byte value)
        => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    private void Complete(List<MspTrigger> triggers)
    {
        MspKind kind = StartsLike(MusicPrefix) ? MspKind.Music : MspKind.Sound;
        string body = Encoding.UTF8.GetString(
            [.. _held.GetRange(SoundPrefix.Length, _held.Count - SoundPrefix.Length - 1)]);

        if (MspTrigger.TryParse(kind, body, out MspTrigger? trigger)) triggers.Add(trigger);

        _held.Clear();
        // The trigger began at the start of a line, so the line ending that follows it belongs
        // to the trigger rather than to any text.
        _swallowLineEnding = true;
        _atLineStart = false;
    }

    private void Release(Span<byte> text, ref int written)
    {
        foreach (byte value in _held) Emit(value, text, ref written);
        _held.Clear();
    }

    private void Emit(byte value, Span<byte> text, ref int written)
    {
        text[written++] = value;
        _atLineStart = value == (byte)'\n';
    }
}
