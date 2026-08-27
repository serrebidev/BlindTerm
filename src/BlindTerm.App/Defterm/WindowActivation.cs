using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BlindTerm.App.Defterm;

/// <summary>
/// Puts a window in front and gives it the keyboard, for the case where Windows opened it
/// rather than the user.
///
/// A terminal that appears behind whatever the user was doing is a nuisance for anyone and a
/// wall for someone using a screen reader: the reader announces nothing, the keyboard still
/// belongs to the old window, and there is no visible cue that anything happened at all. So
/// this matters more here than the usual advice about not stealing focus.
///
/// It takes some doing. Windows only lets the process that already owns the foreground give
/// it away, and after a default-terminal handoff BlindTerm is not that process -- the
/// foreground belongs to whatever launched the command-line program, two processes back.
/// <see cref="System.Windows.Forms.Form.Activate"/> alone comes back having done nothing but
/// flash the taskbar button.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowActivation
{
    /// <summary>
    /// Brings <paramref name="form"/> to the front and focuses it, then puts the keyboard on
    /// <paramref name="focus"/>.
    /// </summary>
    public static void TakeForeground(Form form, Control? focus = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.IsDisposed || !form.IsHandleCreated) return;

        IntPtr window = form.Handle;

        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        ShowWindow(window, SW_SHOW);

        // Borrowing the foreground thread's input queue makes this process, briefly, part of
        // the window that already has the foreground -- which is the one arrangement in which
        // Windows honours SetForegroundWindow from a process the user did not just click on.
        IntPtr foreground = GetForegroundWindow();
        uint holder = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint ours = GetCurrentThreadId();
        bool attached = holder != 0 && holder != ours && AttachThreadInput(ours, holder, true);

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            form.Activate();
        }
        finally
        {
            if (attached) AttachThreadInput(ours, holder, false);
        }

        // Whether or not any of that worked, the window is on screen and the caret has to be
        // somewhere sensible in it.
        focus?.Focus();
    }

    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
