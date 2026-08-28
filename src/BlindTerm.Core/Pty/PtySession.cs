using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BlindTerm.Core.DefaultTerminal;
using Microsoft.Win32.SafeHandles;
using static BlindTerm.Core.Pty.NativeMethods;

namespace BlindTerm.Core.Pty;

/// <summary>
/// A child process attached to a Windows pseudo console: the bytes it writes arrive as
/// <see cref="Output"/>, and bytes handed to <see cref="Write"/> arrive at its input.
///
/// Writes are serialised through a queue rather than issued from the caller's thread. Two
/// rapid submissions must not interleave: a command's text and the Return that sends it are
/// deliberately separate writes (see <see cref="WriteLineSplit"/>), and another submission
/// landing between them would send the wrong thing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PtySession : ITerminalSession
{
    private readonly TerminalSessionKind _kind;
    private readonly bool _alwaysOwnsInput;
    private IntPtr _handle = IntPtr.Zero;
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private FileStream? _writer;
    private FileStream? _reader;
    private IntPtr _process = IntPtr.Zero;
    private IntPtr _thread = IntPtr.Zero;
    private IntPtr _attributes = IntPtr.Zero;

    /// <summary>
    /// Set when this session was handed to us by the console API server rather than started
    /// by us. The pseudo console then belongs to that process, so there is no HPCON to resize
    /// or close and the handoff owns the pipes.
    /// </summary>
    private ConsoleHandoff? _handoff;

    private readonly BlockingCollection<byte[]> _writes = new(new ConcurrentQueue<byte[]>());
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _readThread;
    private Thread? _writeThread;
    private Thread? _waitThread;
    private int _disposed;

    public PtySession(TerminalSessionKind kind = TerminalSessionKind.Shell,
        bool alwaysOwnsInput = false)
    {
        if (kind is not (TerminalSessionKind.Shell or TerminalSessionKind.Ssh))
            throw new ArgumentOutOfRangeException(nameof(kind));
        _kind = kind;
        _alwaysOwnsInput = alwaysOwnsInput;
    }

    /// <summary>
    /// Raw bytes from the child, exactly as they arrived. Never reordered.
    ///
    /// Raised synchronously on the read thread, and the memory is a window onto a buffer that
    /// is reused by the next read: handlers must consume or copy it before returning, and
    /// must not hold on to it.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? Output;

    /// <summary>The child has gone. The argument is its exit code, or null if unavailable.</summary>
    public event Action<int?>? Exited;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public bool IsRunning { get; private set; }
    public int ProcessId { get; private set; }

    /// <summary>Whether this session arrived from Windows rather than being started here.</summary>
    public bool IsHandoff => _handoff is not null;

    public TerminalSessionKind Kind
        => _handoff is not null ? TerminalSessionKind.Handoff : _kind;

    /// <summary>
    /// Whether something other than an idle shell prompt is reading what is typed.
    ///
    /// A shell that has started a program is not the one reading the keyboard any more, and a
    /// process either exists or it does not. The alternative -- waiting for the shell's OSC 133
    /// completed-command marker -- only works when the shell emits them, and a stock PowerShell
    /// 7 prompt emits none. A handed-over console has no shell in front of it at all: the
    /// program Windows started is the whole session.
    /// </summary>
    public bool ProgramOwnsInput
        => _handoff is not null || _alwaysOwnsInput || ProcessTree.HasChild(ProcessId);

    /// <summary>A pseudo console's line discipline turns the Return into a new line itself.</summary>
    public string LineTerminator => "\r";

    /// <summary>
    /// Starts <paramref name="commandLine"/> attached to a new pseudo console.
    /// </summary>
    /// <param name="environment">
    /// Variables to set for the child, merged over the current process's environment.
    /// </param>
    public void Start(
        string commandLine,
        int columns,
        int rows,
        IDictionary<string, string?>? environment = null,
        string? workingDirectory = null)
    {
        if (IsRunning) throw new InvalidOperationException("Session is already running.");
        TerminalSize.Validate(columns, rows);

        Columns = columns;
        Rows = rows;

        // Two anonymous pipes. The pseudo console keeps the ends the child reads from and
        // writes to; we keep the other ends.
        if (!CreatePipe(out SafeFileHandle inputRead, out SafeFileHandle inputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (input) failed.");
        if (!CreatePipe(out SafeFileHandle outputRead, out SafeFileHandle outputWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (output) failed.");

        _inputWrite = inputWrite;
        _outputRead = outputRead;

        try
        {
            var size = new COORD { X = (short)columns, Y = (short)rows };
            int hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out _handle);
            if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole failed.");
        }
        finally
        {
            // The pseudo console has duplicated these; our copies go now, before the child is
            // created. Closing them is also what lets the read loop see EOF when the child
            // exits -- holding them open would hang the session forever.
            inputRead.Dispose();
            outputWrite.Dispose();
        }

        StartChild(commandLine, environment, workingDirectory);

        _writer = new FileStream(_inputWrite, FileAccess.Write);
        _reader = new FileStream(_outputRead, FileAccess.Read);
        IsRunning = true;

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "BlindTerm PTY read" };
        _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "BlindTerm PTY write" };
        _waitThread = new Thread(WaitLoop) { IsBackground = true, Name = "BlindTerm PTY wait" };
        _readThread.Start();
        _writeThread.Start();
        _waitThread.Start();
    }

    /// <summary>
    /// Adopts a console Windows has already created, because BlindTerm is the default
    /// terminal and a command-line program was started without one.
    ///
    /// Everything after this point is identical to a session we started: the same VT bytes
    /// arrive on <see cref="Output"/>, the same writes reach the program. What differs is
    /// ownership -- the pseudo console lives in the console API server, so resizing goes down
    /// the signal pipe and there is no child process of ours to wait on, only the program
    /// that asked for the console in the first place.
    /// </summary>
    public void Adopt(ConsoleHandoff handoff, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        if (IsRunning) throw new InvalidOperationException("Session is already running.");
        TerminalSize.Validate(columns, rows);

        _handoff = handoff;
        Columns = columns;
        Rows = rows;

        // A pipe each way, read and written by their own threads, exactly as for a session
        // BlindTerm started itself.
        _writer = new FileStream(handoff.Input, FileAccess.Write);
        _reader = new FileStream(handoff.Output, FileAccess.Read);

        _process = handoff.Client.DangerousGetHandle();
        ProcessId = SafeProcessId(handoff.Client);
        IsRunning = true;

        // Tell the console API server what size we are before anything is drawn. It has been
        // guessing until now, and a program that measured the console at startup would
        // otherwise lay itself out for the wrong screen.
        handoff.Resize(columns, rows);

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "BlindTerm PTY read" };
        _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "BlindTerm PTY write" };
        _waitThread = new Thread(WaitLoop) { IsBackground = true, Name = "BlindTerm PTY wait" };
        _readThread.Start();
        _writeThread.Start();
        _waitThread.Start();
    }

    private static int SafeProcessId(SafeProcessHandle handle)
    {
        try { return GetProcessId(handle.DangerousGetHandle()); }
        catch (EntryPointNotFoundException) { return 0; }
    }

    internal static readonly bool Debug =
        Environment.GetEnvironmentVariable("BLINDTERM_DEBUG") is { Length: > 0 };

    private static void Trace(string message)
    {
        if (Debug) Console.Error.WriteLine($"[pty] {message}");
    }

    private void StartChild(
        string commandLine, IDictionary<string, string?>? environment, string? workingDirectory)
    {
        var startup = new STARTUPINFOEX();
        startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Force the child onto the console it is attached to -- the pseudo console -- for its
        // standard handles.
        //
        // Without this the child inherits ours. bInheritHandles is false, which ought to be
        // enough, but when the parent's own standard handles are redirected (a pipe or a file,
        // which is every case where BlindTerm is launched by another tool) CreateProcess hands
        // those very handles to the child anyway. The pseudo console attribute still applies,
        // so the child is genuinely attached -- `mode con` reports the size we asked for -- and
        // yet everything it prints goes to our stdout instead of down the pty, and the terminal
        // sees nothing but the pseudo console's own startup and teardown sequences.
        //
        // Naming the flag with all three handles null leaves the child no inherited handles to
        // use, so it falls back to CONIN$/CONOUT$, which are the pseudo console's.
        startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        startup.StartupInfo.hStdInput = IntPtr.Zero;
        startup.StartupInfo.hStdOutput = IntPtr.Zero;
        startup.StartupInfo.hStdError = IntPtr.Zero;

        // Attribute list carrying the pseudo console handle. Sized by a first call that is
        // expected to fail with ERROR_INSUFFICIENT_BUFFER.
        IntPtr listSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
        Trace($"hPC=0x{_handle:x} attributeListSize={(long)listSize} startupInfoCb={startup.StartupInfo.cb}");

        _attributes = Marshal.AllocHGlobal(listSize);
        startup.lpAttributeList = _attributes;

        if (!InitializeProcThreadAttributeList(_attributes, 1, 0, ref listSize))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");

        if (!UpdateProcThreadAttribute(
                _attributes, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _handle,
                (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");

        Trace("attribute list ready");

        IntPtr block = IntPtr.Zero;
        try
        {
            block = BuildEnvironmentBlock(environment);
            int flags = EXTENDED_STARTUPINFO_PRESENT
                        | (block == IntPtr.Zero ? 0 : CREATE_UNICODE_ENVIRONMENT);

            if (!CreateProcessW(
                    null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    flags, block, workingDirectory, ref startup, out PROCESS_INFORMATION info))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"CreateProcess failed for: {commandLine}");

            _process = info.hProcess;
            _thread = info.hThread;
            ProcessId = info.dwProcessId;
            Trace($"created pid={ProcessId} flags=0x{flags:x} envBlock={(block == IntPtr.Zero ? "inherited" : "custom")}");
        }
        finally
        {
            if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
        }
    }

    /// <summary>
    /// The child's environment: this process's, with <paramref name="overrides"/> applied.
    /// A null value removes a variable. CreateProcess requires the block sorted by name,
    /// case-insensitively, and terminated by an extra null.
    /// </summary>
    private static IntPtr BuildEnvironmentBlock(IDictionary<string, string?>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return IntPtr.Zero;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = (string?)entry.Value ?? string.Empty;

        foreach (var (key, value) in overrides)
        {
            if (value is null) merged.Remove(key);
            else merged[key] = value;
        }

        var text = new StringBuilder();
        foreach (var key in merged.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            text.Append(key).Append('=').Append(merged[key]).Append('\0');
        text.Append('\0');

        return Marshal.StringToHGlobalUni(text.ToString());
    }

    // ---- Reading and writing ----

    private void ReadLoop()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                int read = _reader!.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                Output?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, read));
            }
        }
        catch (Exception) when (_stopping.IsCancellationRequested || _disposed != 0)
        {
            // Shutting down; the pipe closing under us is expected.
        }
        catch (IOException)
        {
            // The program closed its end.
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (byte[] chunk in _writes.GetConsumingEnumerable(_stopping.Token))
            {
                _writer!.Write(chunk, 0, chunk.Length);
                _writer.Flush();
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void WaitLoop()
    {
        if (_process == IntPtr.Zero) return;
        WaitForSingleObject(_process, 0xFFFFFFFF);
        int? code = GetExitCodeProcess(_process, out int value) ? value : null;
        IsRunning = false;
        Exited?.Invoke(code);
    }

    /// <summary>Queues bytes for the child's input, in call order.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || _writes.IsAddingCompleted) return;
        _writes.Add(bytes.ToArray());
    }

    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Sends a typed line as two writes: the text, then the terminator a moment later.
    ///
    /// Programs that read their own input decide whether a chunk was typed or pasted by how
    /// much of it arrives at once -- Claude Code treats anything over about sixty bytes as a
    /// paste -- so a Return in the same write as a long line is pasted text rather than
    /// "send this", and nothing happens. Splitting the write leaves the Return unmistakable
    /// however long the line is.
    /// </summary>
    public async Task WriteLineSplit(string text, string terminator = "\r", int gapMs = 20)
    {
        if (text.Length > 0) Write(text);
        if (gapMs > 0) await Task.Delay(gapMs).ConfigureAwait(false);
        Write(terminator);
    }

    /// <summary>
    /// Changes the terminal size the child sees. Fixed sizes are a limitation, not a
    /// feature: a remote editor lays itself out for whatever it is told.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        if (columns == Columns && rows == Rows) return;

        if (_handoff is not null)
        {
            _handoff.Resize(size.Columns, size.Rows);
        }
        else
        {
            if (_handle == IntPtr.Zero) return;
            int hr = ResizePseudoConsole(_handle, new COORD { X = (short)size.Columns, Y = (short)size.Rows });
            if (hr != 0) throw new Win32Exception(hr, "ResizePseudoConsole failed.");
        }

        Columns = size.Columns;
        Rows = size.Rows;
    }

    public void Kill()
    {
        if (_process != IntPtr.Zero && IsRunning) TerminateProcess(_process, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stopping.Cancel();
        _writes.CompleteAdding();

        // Closing the pseudo console signals the child that the terminal has gone, which is
        // what lets a well-behaved shell exit on its own.
        if (_handle != IntPtr.Zero) { ClosePseudoConsole(_handle); _handle = IntPtr.Zero; }

        _writer?.Dispose();
        _reader?.Dispose();
        _inputWrite?.Dispose();
        _outputRead?.Dispose();

        // A handed-off session owns the process handles too, so the raw copy in _process is
        // not ours to close below.
        if (_handoff is not null)
        {
            _handoff.Dispose();
            _handoff = null;
            _process = IntPtr.Zero;
        }

        if (_attributes != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributes);
            Marshal.FreeHGlobal(_attributes);
            _attributes = IntPtr.Zero;
        }
        if (_thread != IntPtr.Zero) { CloseHandle(_thread); _thread = IntPtr.Zero; }
        if (_process != IntPtr.Zero) { CloseHandle(_process); _process = IntPtr.Zero; }

        _stopping.Dispose();
        _writes.Dispose();
        IsRunning = false;
    }
}
