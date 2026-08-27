using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BlindTerm.Core.DefaultTerminal;

namespace BlindTerm.App.Defterm;

/// <summary>
/// Windows Terminal's <c>ITerminalHandoff3</c>, declared exactly as microsoft/terminal
/// declares it: IUnknown-derived, one method, handles passed raw.
///
/// The console API server marshals this call through Windows Terminal's own proxy, which
/// duplicates every handle into our process on the way in and back out again on the way out.
/// That is why the parameters are bare pointers here and why nothing on this side may hold
/// on to them past the call.
/// </summary>
[ComVisible(true)]
[Guid("6F23DA90-15C5-4203-9DB0-64E73F1B1B00")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITerminalHandoff3
{
    void EstablishPtyHandoff(
        out IntPtr consoleInput,
        out IntPtr consoleOutput,
        IntPtr signal,
        IntPtr reference,
        IntPtr server,
        IntPtr client,
        IntPtr startupInfo);
}

/// <summary>
/// The standard COM class factory, redeclared because .NET exposes no implementable version
/// of it.
/// </summary>
/// <remarks>
/// Public, like everything else in this file, and not by preference: .NET builds a COM
/// callable wrapper only for public types, so a tidier internal or nested declaration would
/// leave the object answering nothing but IUnknown -- and Windows would give up on the
/// handoff without a word.
/// </remarks>
[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool @lock);
}

/// <summary>Hands out <see cref="TerminalHandoff"/> objects to Windows, one per console.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[SupportedOSPlatform("windows")]
public sealed class HandoffClassFactory : IClassFactory
{
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_POINTER = unchecked((int)0x80004003);
    private const int S_OK = 0;

    private readonly SynchronizationContext _ui;
    private readonly Action<ConsoleHandoff> _onHandoff;

    public HandoffClassFactory(SynchronizationContext ui, Action<ConsoleHandoff> onHandoff)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(onHandoff);
        _ui = ui;
        _onHandoff = onHandoff;
    }

    public int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero) return CLASS_E_NOAGGREGATION;

        IntPtr unknown = Marshal.GetIUnknownForObject(new TerminalHandoff(_ui, _onHandoff));
        try
        {
            int hr = Marshal.QueryInterface(unknown, in riid, out instance);
            return hr < 0 ? (hr == E_POINTER ? E_POINTER : E_NOINTERFACE) : S_OK;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Nothing to do. The process already outlives every call, because it stays up for as
    /// long as a terminal window is open.
    /// </summary>
    public int LockServer(bool @lock) => S_OK;
}

/// <summary>
/// BlindTerm's answer to "here is a console, will you show it?".
///
/// The console API server is blocked on this call and so, behind it, is the program that
/// wanted a terminal. So it does the least it can: claim the handles, make the pipe, and
/// leave building a window to the next turn of the message loop.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[SupportedOSPlatform("windows")]
public sealed class TerminalHandoff : ITerminalHandoff3
{
    private readonly SynchronizationContext _ui;
    private readonly Action<ConsoleHandoff> _onHandoff;

    /// <param name="ui">
    /// The window thread's context, captured at startup rather than read here.
    ///
    /// This call does not arrive on the thread that registered the class: COM delivers it on
    /// an RPC worker, where <see cref="SynchronizationContext.Current"/> is null. Building the
    /// window on that thread appears to work -- the form is created, the title is right -- and
    /// then nothing ever happens in it, because no message loop is pumping that thread.
    /// </param>
    public TerminalHandoff(SynchronizationContext ui, Action<ConsoleHandoff> onHandoff)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(onHandoff);
        _ui = ui;
        _onHandoff = onHandoff;
    }

    public void EstablishPtyHandoff(
        out IntPtr consoleInput, out IntPtr consoleOutput,
        IntPtr signal, IntPtr reference, IntPtr server, IntPtr client, IntPtr startupInfo)
    {
        // Throwing here is a supported answer: the console API server logs the failure and the
        // inbox console host takes the session back, so the program still gets a terminal.
        // That is what makes offering this at startup safe.
        ConsoleHandoff handoff = ConsoleHandoff.Accept(
            signal, reference, server, client, startupInfo, out consoleInput, out consoleOutput);

        _ui.Post(_ =>
        {
            try
            {
                _onHandoff(handoff);
            }
            catch (Exception)
            {
                handoff.Dispose();
                throw;
            }
        }, null);
    }
}
