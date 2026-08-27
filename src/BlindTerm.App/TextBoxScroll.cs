using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BlindTerm.App;

/// <summary>
/// Scrolls a text box without moving the caret.
///
/// The obvious way -- put the selection at the end and call ScrollToCaret -- moves the caret,
/// and a moved caret is something the screen reader announces. New output would then be read
/// twice: once by this app, deliberately, and once by the reader following a caret the user
/// never moved. The caret is the reading position and nothing but the user should move it,
/// so the view is scrolled directly instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TextBoxScroll
{
    private const int EM_LINESCROLL = 0x00B6;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_GETLINECOUNT = 0x00BA;

    /// <summary>Scrolls so the last line is in view, leaving the selection untouched.</summary>
    public static void ToBottom(TextBox box)
    {
        if (!box.IsHandleCreated) return;

        // Asked of the control rather than of TextBox.Lines, which copies the whole transcript
        // out and splits it into an array of every line. This runs on every batch of output.
        int lines = SendMessage(box.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
        int first = SendMessage(box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();

        // Scrolling further than there is content is clamped by the control, so overshooting
        // is both safe and simpler than working out the visible line count.
        int by = lines - first;
        if (by > 0) SendMessage(box.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)by);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
