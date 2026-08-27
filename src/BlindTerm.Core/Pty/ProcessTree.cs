using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace BlindTerm.Core.Pty;

/// <summary>
/// Answers one question about a running shell: has it started a program?
///
/// This is how BlindTerm knows whether the keyboard belongs to the shell's own line editor or
/// to something the shell launched. The alternative -- watching for the shell's OSC 133
/// completed-command marker -- only works when the shell emits those markers, and the shells
/// people actually get do not: a stock PowerShell 7 prompt emits none, so a session that
/// relied on them would decide a program was running from the first command until the window
/// closed. A process either exists or it does not, and every shell reports it the same way.
///
/// Only direct children count. A pseudo console's conhost is a child of BlindTerm rather than
/// of the shell, so it is never mistaken for a program the user launched.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessTree
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    /// <summary>
    /// Whether <paramref name="processId"/> currently has a child process.
    ///
    /// False for a process id of zero, which is what a session that has not started yet
    /// reports, and false rather than throwing if Windows refuses the snapshot: a keystroke
    /// must never fail because a diagnostic call did.
    /// </summary>
    public static bool HasChild(int processId)
    {
        if (processId <= 0) return false;

        using SafeSnapshotHandle snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid) return false;

        var entry = new PROCESSENTRY32W { dwSize = Marshal.SizeOf<PROCESSENTRY32W>() };
        if (!Process32FirstW(snapshot, ref entry)) return false;

        do
        {
            // The idle process reports itself as its own parent; skip it rather than let a
            // shell that happens to be process 0 -- which cannot happen -- read as busy.
            if (entry.th32ProcessID != entry.th32ParentProcessID &&
                entry.th32ParentProcessID == (uint)processId)
                return true;
        }
        while (Process32NextW(snapshot, ref entry));

        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public int dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private sealed class SafeSnapshotHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeSnapshotHandle snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeSnapshotHandle snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
