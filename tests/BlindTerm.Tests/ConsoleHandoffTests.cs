using System.Runtime.InteropServices;
using BlindTerm.App.Defterm;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using Microsoft.Win32.SafeHandles;

namespace BlindTerm.Tests;

/// <summary>
/// A whole inbound handoff, driven directly rather than through COM.
///
/// <c>EstablishPtyHandoff</c> is one call, made once per console, that BlindTerm gets no
/// second chance at: the program that wanted a terminal is blocked on it, and a mistake shows
/// up as a window that opens and then never says anything. Calling it here with real pipes and
/// real process handles exercises everything except the marshalling.
/// </summary>
public sealed class ConsoleHandoffTests : IDisposable
{
    private readonly List<SafeHandle> _handles = [];
    private readonly List<IntPtr> _unmanaged = [];

    public void Dispose()
    {
        foreach (SafeHandle handle in _handles) handle.Dispose();
        foreach (IntPtr pointer in _unmanaged) Marshal.FreeHGlobal(pointer);
    }

    [Fact]
    public void AHandoffProducesPipesForTheConsoleAndKeepsTheOtherEnds()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();
        using (handoff)
        {
            // The two returned handles belong to the caller now: COM duplicates them out and
            // closes ours. They must be real, and they must not be the ends we kept.
            Assert.NotEqual(IntPtr.Zero, consoleInput);
            Assert.NotEqual(IntPtr.Zero, consoleOutput);
            Assert.NotEqual(consoleInput, consoleOutput);
            Assert.NotEqual(consoleInput, handoff.Input.DangerousGetHandle());
            Assert.NotEqual(consoleOutput, handoff.Output.DangerousGetHandle());

            Assert.False(handoff.Input.IsInvalid);
            Assert.False(handoff.Output.IsInvalid);
            Assert.False(handoff.Signal.IsInvalid);
            Assert.False(handoff.Reference.IsInvalid);
            Assert.False(handoff.Server.IsInvalid);
            Assert.False(handoff.Client.IsInvalid);
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void WhatTheConsoleWritesIsWhatBlindTermReads()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();
        using (handoff)
        {
            using var consoleSide = new FileStream(new SafeFileHandle(consoleOutput, ownsHandle: true), FileAccess.Write);
            using var ourSide = new FileStream(handoff.Output, FileAccess.Read);

            consoleSide.Write("hello\r\n"u8);
            consoleSide.Flush();

            var buffer = new byte[7];
            int read = ourSide.Read(buffer, 0, buffer.Length);

            Assert.Equal(7, read);
            Assert.Equal("hello\r\n"u8.ToArray(), buffer);
        }

        Close(consoleInput);
    }

    [Fact]
    public void WhatBlindTermWritesIsWhatTheConsoleReads()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();
        using (handoff)
        {
            using var ourSide = new FileStream(handoff.Input, FileAccess.Write);
            using var consoleSide = new FileStream(new SafeFileHandle(consoleInput, ownsHandle: true), FileAccess.Read);

            ourSide.Write("dir\r"u8);
            ourSide.Flush();

            var buffer = new byte[4];
            int read = consoleSide.Read(buffer, 0, buffer.Length);

            Assert.Equal(4, read);
            Assert.Equal("dir\r"u8.ToArray(), buffer);
        }

        Close(consoleOutput);
    }

    [Fact]
    public void ResizingSendsThePacketThePseudoConsoleExpects()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept(out SafeFileHandle signalRead);
        using (handoff)
        {
            handoff.Resize(100, 40);

            using var reader = new FileStream(signalRead, FileAccess.Read);
            var packet = new byte[6];
            Assert.Equal(6, reader.Read(packet, 0, packet.Length));

            // PTY_SIGNAL_RESIZE_WINDOW, then width and height, each a little-endian ushort.
            // The pseudo console belongs to another process here, so this pipe is the only way
            // to tell it the terminal has changed size.
            Assert.Equal(8, BitConverter.ToUInt16(packet, 0));
            Assert.Equal(100, BitConverter.ToUInt16(packet, 2));
            Assert.Equal(40, BitConverter.ToUInt16(packet, 4));
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void TheProgramsOwnTitleComesThrough()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) =
            Accept(title: "C:\\Windows\\system32\\cmd.exe");

        using (handoff)
        {
            Assert.Equal("C:\\Windows\\system32\\cmd.exe", handoff.Title);
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void ASizeAskedForAtLaunchIsHonoured()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) =
            Accept(columns: 132, rows: 50);

        using (handoff)
        {
            Assert.Equal(new TerminalSize(132, 50), handoff.RequestedSize);
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void WithoutAStartupSizeThereIsNoneToHonour()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();

        using (handoff)
        {
            // A shortcut that did not ask for a size must not be given one, or every console
            // opened without one would silently take the last program's dimensions.
            Assert.Null(handoff.RequestedSize);
            Assert.Equal(string.Empty, handoff.Title);
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void NoStartupInformationAtAllIsSurvivable()
    {
        ConsoleHandoff handoff = ConsoleHandoff.Accept(
            Signal(out _), Reference(), CurrentProcess(), CurrentProcess(), IntPtr.Zero,
            out IntPtr consoleInput, out IntPtr consoleOutput);

        using (handoff)
        {
            Assert.Equal(string.Empty, handoff.Title);
            Assert.Null(handoff.RequestedSize);
        }

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();

        handoff.Dispose();
        handoff.Dispose();

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void ResizingAfterDisposalIsIgnoredRatherThanFatal()
    {
        (ConsoleHandoff handoff, IntPtr consoleInput, IntPtr consoleOutput) = Accept();
        handoff.Dispose();

        // A window closing races the last resize the layout engine asked for, and losing that
        // race must not take the process down.
        handoff.Resize(80, 25);

        Close(consoleInput);
        Close(consoleOutput);
    }

    [Fact]
    public void TheWindowIsBuiltOnTheUiThreadAndNotOnWindowsCallingThread()
    {
        var context = new RecordingContext();
        ConsoleHandoff? received = null;
        var handoffObject = new TerminalHandoff(context, h => received = h);

        handoffObject.EstablishPtyHandoff(
            out IntPtr consoleInput, out IntPtr consoleOutput,
            Signal(out _), Reference(), CurrentProcess(), CurrentProcess(), StartupInfo("cmd.exe", 0, 0));

        // The console API server, and the program behind it, are blocked until this returns.
        // So the answer comes back first and the window is built afterwards.
        Assert.NotEqual(IntPtr.Zero, consoleInput);
        Assert.NotEqual(IntPtr.Zero, consoleOutput);
        Assert.Null(received);
        Assert.Single(context.Posted);

        context.Drain();

        Assert.NotNull(received);
        Assert.Equal("cmd.exe", received!.Title);
        received.Dispose();

        Close(consoleInput);
        Close(consoleOutput);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public List<Action> Posted { get; } = [];

        public override void Post(SendOrPostCallback callback, object? state)
            => Posted.Add(() => callback(state));

        public void Drain()
        {
            foreach (Action action in Posted.ToArray()) action();
            Posted.Clear();
        }
    }

    // ---- Building a plausible handoff ----

    private (ConsoleHandoff Handoff, IntPtr ConsoleInput, IntPtr ConsoleOutput) Accept(
        string title = "", int columns = 0, int rows = 0)
        => Accept(out _, title, columns, rows);

    private (ConsoleHandoff Handoff, IntPtr ConsoleInput, IntPtr ConsoleOutput) Accept(
        out SafeFileHandle signalRead, string title = "", int columns = 0, int rows = 0)
    {
        IntPtr signal = Signal(out signalRead);
        ConsoleHandoff handoff = ConsoleHandoff.Accept(
            signal, Reference(), CurrentProcess(), CurrentProcess(), StartupInfo(title, columns, rows),
            out IntPtr consoleInput, out IntPtr consoleOutput);
        return (handoff, consoleInput, consoleOutput);
    }

    /// <summary>A stand-in for the console's signal pipe, with the reading end kept for tests.</summary>
    private IntPtr Signal(out SafeFileHandle read)
    {
        Assert.True(CreatePipe(out SafeFileHandle readEnd, out SafeFileHandle writeEnd, IntPtr.Zero, 0));
        read = readEnd;
        _handles.Add(readEnd);
        _handles.Add(writeEnd);
        return writeEnd.DangerousGetHandle();
    }

    /// <summary>Any file handle will do: nothing reads it, it only has to stay open.</summary>
    private IntPtr Reference()
    {
        Assert.True(CreatePipe(out SafeFileHandle readEnd, out SafeFileHandle writeEnd, IntPtr.Zero, 0));
        _handles.Add(readEnd);
        _handles.Add(writeEnd);
        return readEnd.DangerousGetHandle();
    }

    /// <summary>
    /// The pseudo-handle for this process, which is what the console API server passes for
    /// itself. DuplicateHandle turns it into a real one, which is the behaviour under test.
    /// </summary>
    private static IntPtr CurrentProcess() => GetCurrentProcess();

    /// <summary>
    /// A <c>TERMINAL_STARTUP_INFO</c>, laid out by hand so that a change to the struct in the
    /// product shows up here as a failure rather than as silently misread startup information.
    /// </summary>
    private IntPtr StartupInfo(string title, int columns, int rows)
    {
        const int size = 56;
        const uint STARTF_USECOUNTCHARS = 0x00000008;

        IntPtr block = Marshal.AllocHGlobal(size);
        _unmanaged.Add(block);
        for (int offset = 0; offset < size; offset++) Marshal.WriteByte(block, offset, 0);

        if (title.Length > 0)
        {
            IntPtr bstr = Marshal.StringToBSTR(title);
            _unmanaged.Add(bstr);
            Marshal.WriteIntPtr(block, 0, bstr);
        }

        if (columns > 0 && rows > 0)
        {
            Marshal.WriteInt32(block, 36, columns);
            Marshal.WriteInt32(block, 40, rows);
            Marshal.WriteInt32(block, 48, (int)STARTF_USECOUNTCHARS);
        }

        return block;
    }

    private static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
