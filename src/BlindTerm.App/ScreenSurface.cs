using System.Runtime.Versioning;

namespace BlindTerm.App;

/// <summary>
/// Where the keyboard lives while a full-screen program is running.
///
/// It exists because of what a screen reader does with a focused *edit* control. NVDA treats
/// one as something the arrow keys navigate: on every arrow it waits for the caret to move,
/// times out when it does not -- the caret belongs to the program, not the control -- and
/// reads the control out again. The result was two utterances for every keystroke, the app's
/// correct one and the reader's spurious one, on every single line.
///
/// The screen surface is deliberately not the accessibility focus target. A separate native
/// edit proxy owns focus because NVDA and JAWS need a real caret to apply their normal echo and
/// line navigation behavior. This surface only paints what the terminal is showing.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenSurface : Control
{
    private string[] _rows = [];

    public ScreenSurface()
    {
        // Focusable, and painted by us without flicker.
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        TabStop = false;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        Font = new Font("Consolas", 11f);
    }

    public void SetRows(string[] rows, int cursorRow)
    {
        _rows = rows;
        Invalidate();
    }

    /// <summary>
    /// A non-focusable visual surface. The keyboard proxy is the reader-facing control.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance()
        => new SurfaceAccessibleObject(this);

    private sealed class SurfaceAccessibleObject : ControlAccessibleObject
    {
        public SurfaceAccessibleObject(ScreenSurface owner) : base(owner) { }

        public override AccessibleRole Role => AccessibleRole.None;

        // Keep the surface out of the reader's object and focus path.
        public override string? Name
        {
            get => null;
            set { }
        }

        // The painted rows are read through the native keyboard proxy or Alt+3 review mode.
        public override string? Value => string.Empty;

        // Dynamic child text causes NVDA to announce every repaint as a value change. Detailed
        // navigation is intentionally provided by the frozen review edit control instead.
        public override int GetChildCount() => 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        float lineHeight = Font.GetHeight(e.Graphics);
        using var brush = new SolidBrush(ForeColor);

        for (int i = 0; i < _rows.Length; i++)
        {
            float y = i * lineHeight;
            if (y > Height) break;
            if (_rows[i].Length > 0) e.Graphics.DrawString(_rows[i], Font, brush, 2, y);
        }
    }

    // Arrow keys and Tab are claimed by the form before they reach here, but saying so
    // explicitly stops the framework treating them as navigation if that ever changes.
    protected override bool IsInputKey(Keys keyData) => true;
}
