using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Whether the lock screen or another secure desktop is in front.
///
/// Screen readers keep running there, so an application that speaks without checking will
/// happily read a terminal session out to whoever is standing at a locked machine. NV Access
/// call this out explicitly in the controller client documentation, and it costs one call to
/// respect.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecureDesktop
{
    private const int UOI_NAME = 2;
    private const uint DESKTOP_READOBJECTS = 0x0001;

    /// <summary>
    /// True when the desktop receiving input is not the ordinary one. Errs towards true: if
    /// the input desktop cannot even be opened, this process is not on it, which is itself
    /// the situation where speaking would be wrong.
    /// </summary>
    public static bool IsActive()
    {
        IntPtr desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desktop == IntPtr.Zero) return true;

        try
        {
            var name = new StringBuilder(256);
            if (!GetUserObjectInformationW(desktop, UOI_NAME, name, name.Capacity * 2, out _))
                return true;

            // "Default" is the interactive desktop. "Winlogon" is the lock and credential
            // screens; "Screen-saver" is its own; anything else is not ours to talk over.
            return !string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(
        uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(
        IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
}
