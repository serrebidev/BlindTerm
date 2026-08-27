using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BlindTerm.Core.DefaultTerminal;

namespace BlindTerm.App.Defterm;

/// <summary>
/// Offers this process to Windows as the terminal for consoles that have just been created.
///
/// This is the whole of BlindTerm's side of the default-terminal contract. Windows' console
/// API server has already made the console by the time it calls; it asks for a pair of pipe
/// handles to drive it through, and hands over the session's signal, reference and process
/// handles at the same time. Answering that one call is what makes BlindTerm a terminal
/// Windows can open on its own rather than one the user has to launch first.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DefaultTerminalServer
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;

    /// <summary>
    /// One registration serves every console rather than being consumed by the first. A user
    /// who opens six command prompts should not be running six copies of BlindTerm.
    /// </summary>
    private const uint REGCLS_MULTIPLEUSE = 1;

    private static uint _cookie;

    /// <summary>Whether this process is currently offering itself as a default terminal.</summary>
    public static bool IsListening => _cookie != 0;

    /// <summary>
    /// Starts answering handoffs, calling <paramref name="onHandoff"/> once per console on
    /// <paramref name="ui"/>.
    /// </summary>
    /// <remarks>
    /// Registering is deliberately unconditional and deliberately quiet. It costs nothing
    /// when BlindTerm is not the default terminal -- nothing will ever activate the class --
    /// and when it is, having the running instance answer saves starting a second one.
    /// </remarks>
    public static bool Start(SynchronizationContext ui, Action<ConsoleHandoff> onHandoff)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(onHandoff);
        if (_cookie != 0) return true;

        try
        {
            Guid clsid = DefaultTerminalConfig.BlindTermTerminal;
            var factory = new HandoffClassFactory(ui, onHandoff);
            int hr = CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out _cookie);
            if (hr < 0)
            {
                _cookie = 0;
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or PlatformNotSupportedException)
        {
            // Not being able to offer the service is no reason to fail to be a terminal.
            _cookie = 0;
            return false;
        }
    }

    public static void Stop()
    {
        if (_cookie == 0) return;
        try { CoRevokeClassObject(_cookie); }
        catch (COMException) { /* shutting down anyway */ }
        _cookie = 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object unknown,
        uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint cookie);
}
