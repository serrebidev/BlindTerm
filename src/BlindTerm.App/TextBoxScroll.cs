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
    private const int EM_LINEINDEX = 0x00BB;
    private const int EM_LINELENGTH = 0x00C1;

    /// <summary>
    /// Character offset of the final nonempty line, without copying the edit control's text.
    ///
    /// <see cref="TextBox.Text"/> marshals the whole native document into a managed string.
    /// Asking for it on every terminal update makes the cost of one new character grow with
    /// the entire session. The edit messages below ask the native control about line starts
    /// and lengths in place, so this remains constant-time for the usual trailing newline and
    /// only walks farther when the transcript genuinely ends in blank lines.
    /// </summary>
    public static int LastContentLineStart(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (!box.IsHandleCreated) return 0;

        int line = SendMessage(box.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32() - 1;
        while (line > 0)
        {
            int start = SendMessage(box.Handle, EM_LINEINDEX, (IntPtr)line, IntPtr.Zero).ToInt32();
            if (start < 0) break;
            int length = SendMessage(box.Handle, EM_LINELENGTH, (IntPtr)start, IntPtr.Zero).ToInt32();
            if (length > 0) return start;
            line--;
        }

        int first = SendMessage(box.Handle, EM_LINEINDEX, IntPtr.Zero, IntPtr.Zero).ToInt32();
        return Math.Max(0, first);
    }

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
