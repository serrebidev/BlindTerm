using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BlindTerm.Core.Pty;
using Microsoft.Win32.SafeHandles;

namespace BlindTerm.Core.DefaultTerminal;

/// <summary>
/// The handles that arrive with an inbound console handoff, once they belong to us.
///
/// COM releases everything it passed in as soon as <c>EstablishPtyHandoff</c> returns, so
/// every handle here is our own duplicate. The two pipes are ours outright: we create them,
/// keep one end of each, and give the console API server the others on the way out.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConsoleHandoff : IDisposable
{
    /// <summary>What we write to reach the program's input.</summary>
    public SafeFileHandle Input { get; }

    /// <summary>What we read to receive the program's output.</summary>
    public SafeFileHandle Output { get; }

    /// <summary>Out-of-band control channel to the console API server. Resize goes here.</summary>
    public SafeFileHandle Signal { get; }

    /// <summary>
    /// The console session's reference handle. While it is open the console stays alive, so
    /// it is the last thing closed and the thing that ends the session when it goes.
    /// </summary>
    public SafeFileHandle Reference { get; }

    /// <summary>The console API server process (OpenConsole.exe), for lifetime tracking.</summary>
    public SafeProcessHandle Server { get; }

    /// <summary>The command-line program that asked for a console.</summary>
    public SafeProcessHandle Client { get; }

    /// <summary>The window title the program was launched with, if it named one.</summary>
    public string Title { get; }

    /// <summary>Size in characters if the launch asked for one, otherwise null.</summary>
    public TerminalSize? RequestedSize { get; }

    private int _disposed;

    private ConsoleHandoff(
        SafeFileHandle input, SafeFileHandle output, SafeFileHandle signal, SafeFileHandle reference,
        SafeProcessHandle server, SafeProcessHandle client, string title, TerminalSize? size)
    {
        Input = input;
        Output = output;
        Signal = signal;
        Reference = reference;
        Server = server;
        Client = client;
        Title = title;
        RequestedSize = size;
    }

    /// <summary>
    /// Takes ownership of an inbound handoff and produces the pipe ends the caller must hand
    /// back to the console API server.
    /// </summary>
    /// <remarks>
    /// The two returned handles are deliberately raw and deliberately not owned by us: the
    /// COM proxy duplicates them into the caller and closes ours. Returning them from a
    /// <c>SafeHandle</c> would mean the finalizer closed a handle that had already been
    /// consumed.
    /// </remarks>
    public static ConsoleHandoff Accept(
        IntPtr signal, IntPtr reference, IntPtr server, IntPtr client, IntPtr startupInfo,
        out IntPtr consoleInput, out IntPtr consoleOutput)
    {
        consoleInput = IntPtr.Zero;
        consoleOutput = IntPtr.Zero;

        // Two one-way pipes rather than one duplex pipe. Windows Terminal hands the same
        // duplex handle over twice, which is legal, but it makes reading and writing share a
        // file object -- and a file object can be attached to the I/O completion port only
        // once, so only one direction can be asynchronous. Two pipes are the same shape as an
        // ordinary session, and let each direction have a thread of its own.
        HandoffNative.CreatePipes(
            out SafeFileHandle ourWrite, out IntPtr theirRead,
            out SafeFileHandle ourRead, out IntPtr theirWrite);

        try
        {
            SafeFileHandle ownedSignal = HandoffNative.DuplicateFile(signal);
            SafeFileHandle ownedReference = HandoffNative.DuplicateFile(reference);
            SafeProcessHandle ownedServer = HandoffNative.DuplicateProcess(server);
            SafeProcessHandle ownedClient = HandoffNative.DuplicateProcess(client);

            (string title, TerminalSize? size) = ReadStartupInfo(startupInfo);

            consoleInput = theirRead;
            consoleOutput = theirWrite;
            return new ConsoleHandoff(
                ourWrite, ourRead, ownedSignal, ownedReference, ownedServer, ownedClient, title, size);
        }
        catch
        {
            ourWrite.Dispose();
            ourRead.Dispose();
            HandoffNative.CloseHandle(theirRead);
            HandoffNative.CloseHandle(theirWrite);
            throw;
        }
    }

    private static (string Title, TerminalSize? Size) ReadStartupInfo(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return (string.Empty, null);

        TERMINAL_STARTUP_INFO info = Marshal.PtrToStructure<TERMINAL_STARTUP_INFO>(pointer);
        string title = info.pszTitle == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(info.pszTitle);

        TerminalSize? size = null;
        bool countCharsGiven = (info.dwFlags & STARTF_USECOUNTCHARS) != 0;
        if (countCharsGiven &&
            info.dwXCountChars >= TerminalSize.MinimumColumns && info.dwXCountChars <= TerminalSize.MaximumColumns &&
            info.dwYCountChars >= TerminalSize.MinimumRows && info.dwYCountChars <= TerminalSize.MaximumRows)
        {
            size = new TerminalSize((int)info.dwXCountChars, (int)info.dwYCountChars);
        }

        return (title, size);
    }

    /// <summary>
    /// Tells the console API server the terminal is now this many characters across.
    ///
    /// There is no resize API on this side of a handoff: the pseudo console handle belongs to
    /// the other process, and the signal pipe is the documented way to reach it. The packet
    /// is the one <c>ResizePseudoConsole</c> writes.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        TerminalSize size = TerminalSize.Validate(columns, rows);
        Write([PTY_SIGNAL_RESIZE_WINDOW, (ushort)size.Columns, (ushort)size.Rows]);
    }

    /// <summary>Tells the console API server whether the terminal window is on screen.</summary>
    public void ShowWindow(bool visible)
        => Write([PTY_SIGNAL_SHOWHIDE_WINDOW, visible ? (ushort)1 : (ushort)0]);

    private void Write(ushort[] packet)
    {
        if (Signal.IsInvalid || Signal.IsClosed) return;

        var bytes = new byte[packet.Length * sizeof(ushort)];
        Buffer.BlockCopy(packet, 0, bytes, 0, bytes.Length);
        if (!HandoffNative.WriteFile(Signal, bytes, bytes.Length, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Writing to the console signal pipe failed.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Order matters. Dropping the signal pipe and then the reference is what tells the
        // console API server the terminal has gone; it exits once its clients have.
        Signal.Dispose();
        Input.Dispose();
        Output.Dispose();
        Reference.Dispose();
        Server.Dispose();
        Client.Dispose();
    }

    private const ushort PTY_SIGNAL_SHOWHIDE_WINDOW = 1;
    private const ushort PTY_SIGNAL_RESIZE_WINDOW = 8;
    private const uint STARTF_USECOUNTCHARS = 0x00000008;

    /// <summary>
    /// The startup information conhost collects for us, including anything a shortcut asked
    /// for. Laid out to match <c>TERMINAL_STARTUP_INFO</c> in microsoft/terminal.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TERMINAL_STARTUP_INFO
    {
        public IntPtr pszTitle;
        public IntPtr pszIconPath;
        public int iconIndex;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
    }
}

[SupportedOSPlatform("windows")]
internal static class HandoffNative
{
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const uint SYNCHRONIZE = 0x00100000;

    /// <summary>
    /// Two one-way pipes: the ends BlindTerm keeps, and the raw ends to hand back.
    /// </summary>
    /// <remarks>
    /// The two handed-back handles are deliberately raw and deliberately unowned. The COM
    /// proxy duplicates them into the caller and closes ours, so a SafeHandle would later
    /// close a handle that had already gone.
    /// </remarks>
    internal static void CreatePipes(
        out SafeFileHandle ourWrite, out IntPtr theirRead,
        out SafeFileHandle ourRead, out IntPtr theirWrite)
    {
        if (!NativeMethods.CreatePipe(out SafeFileHandle inputRead, out ourWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (console input) failed.");

        if (!NativeMethods.CreatePipe(out ourRead, out SafeFileHandle outputWrite, IntPtr.Zero, 0))
        {
            int error = Marshal.GetLastWin32Error();
            inputRead.Dispose();
            ourWrite.Dispose();
            throw new Win32Exception(error, "CreatePipe (console output) failed.");
        }

        theirRead = Release(inputRead);
        theirWrite = Release(outputWrite);
    }

    /// <summary>Gives up ownership of a handle without closing it.</summary>
    private static IntPtr Release(SafeFileHandle handle)
    {
        IntPtr raw = handle.DangerousGetHandle();
        handle.SetHandleAsInvalid();
        return raw;
    }

    internal static SafeFileHandle DuplicateFile(IntPtr handle)
        => new(Duplicate(handle, DUPLICATE_SAME_ACCESS), ownsHandle: true);

    /// <summary>
    /// A process handle with the rights BlindTerm needs to watch and describe the program.
    /// Windows Terminal asks for the same set and falls back the same way, because a client
    /// launched at a higher integrity level will not grant them.
    /// </summary>
    internal static SafeProcessHandle DuplicateProcess(IntPtr handle)
    {
        const uint wanted = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_SET_INFORMATION | SYNCHRONIZE;
        IntPtr duplicate = IntPtr.Zero;
        if (DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out duplicate, wanted, false, 0))
            return new SafeProcessHandle(duplicate, ownsHandle: true);

        return new SafeProcessHandle(Duplicate(handle, DUPLICATE_SAME_ACCESS), ownsHandle: true);
    }

    private static IntPtr Duplicate(IntPtr handle, uint options)
    {
        if (!DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out IntPtr copy, 0, false, options))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateHandle failed for a handoff handle.");
        return copy;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateNamedPipeW(
        string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances,
        uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle, uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten, IntPtr lpOverlapped);
}
