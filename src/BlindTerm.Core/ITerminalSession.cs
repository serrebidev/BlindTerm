namespace BlindTerm.Core;

/// <summary>Where the bytes in a terminal window are coming from.</summary>
public enum TerminalSessionKind
{
    /// <summary>A shell BlindTerm started.</summary>
    Shell,

    /// <summary>A console Windows handed over, because BlindTerm is the default terminal.</summary>
    Handoff,

    /// <summary>A host on the network, spoken to directly.</summary>
    Remote,

    /// <summary>A remote shell reached through the Windows OpenSSH client.</summary>
    Ssh,
}

/// <summary>
/// The far end of a terminal window: something that produces bytes, accepts bytes, and one
/// day stops.
///
/// A pseudo console was the only kind for a long time, and a telnet connection does not fit
/// through one. Windows' own telnet.exe repaints its window through the console API rather
/// than writing lines, and a pseudo console can only report what is on that window when it
/// next redraws -- so a burst longer than the terminal is tall is overwritten before anyone
/// sees it. Dialling the socket directly is the only way those lines exist at all.
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>
    /// Bytes from the far end, exactly as they arrived, less anything that was protocol
    /// rather than text.
    ///
    /// Raised synchronously on the reading thread, over memory that the next read reuses:
    /// handlers must consume or copy it before returning.
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? Output;

    /// <summary>
    /// The far end has gone. The argument is an exit code where there is one; a closed
    /// connection has none.
    /// </summary>
    event Action<int?>? Exited;

    TerminalSessionKind Kind { get; }

    bool IsRunning { get; }

    /// <summary>
    /// Whether something other than an idle shell prompt is reading what is typed.
    ///
    /// This is what decides who a keystroke belongs to. A shell answers it by whether it has
    /// started a program; a remote host and a handed-over console are the program.
    /// </summary>
    bool ProgramOwnsInput { get; }

    /// <summary>
    /// What ends a submitted line. A pseudo console wants the Return alone; the network
    /// virtual terminal telnet defines wants both characters.
    /// </summary>
    string LineTerminator { get; }

    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>Sends a typed line as two writes: the text, then the terminator.</summary>
    Task WriteLineSplit(string text, string terminator, int gapMs);

    void Resize(int columns, int rows);
}
