using System.Globalization;
using BlindTerm.App;
using BlindTerm.Core;
using BlindTerm.Core.Triggers;

namespace BlindTerm.Tests;

/// <summary>
/// Every control a screen reader can land on has to have something to say about itself.
///
/// A control with no accessible name is announced as its bare role -- "edit", "button" -- which
/// only tells a blind user where they are when there is exactly one of them. Two things make
/// this worth a test rather than a reading. The control that owns the keyboard while a
/// full-screen program runs had no name at all, and its entire text is a zero-width space, so
/// it looked named to anything that only asked whether the text was empty. And the answer is
/// per-dialog: a dialog added later is exactly the one nobody will remember to check.
/// </summary>
public class AccessibilityTests
{
    [Fact]
    public void TheMainWindowNamesEverythingAReaderCanLandOn()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());

            Assert.Empty(Unnamed(form));
        });

    [Fact]
    public void EveryDialogNamesEverythingAReaderCanLandOn()
        => OnAWindowThread(() =>
        {
            var settings = new AppSettings();
            Form[] dialogs =
            [
                new AboutForm(),
                new SettingsForm(settings),
                new TriggersForm([], active: true),
                new TriggerEditForm(new Trigger(), isNew: true),
                new TelnetConnectForm(settings),
                new SshConnectForm(settings),
                new MudBrowserForm(settings),
            ];
            try
            {
                var gaps = new List<string>();
                foreach (Form dialog in dialogs) gaps.AddRange(Unnamed(dialog));

                Assert.Empty(gaps);
            }
            finally
            {
                foreach (Form dialog in dialogs) dialog.Dispose();
            }
        });

    /// <summary>
    /// What a reader can land on and be told about: the controls a user types into or chooses
    /// from. Labels and layout panels are read as the text they hold and are not focus stops.
    /// </summary>
    private static bool IsInteractive(Control control) => control is
        TextBoxBase or ComboBox or ListBox or CheckedListBox or ButtonBase
        or NumericUpDown or LinkLabel or ListView or TreeView;

    private static List<string> Unnamed(Control root)
    {
        var gaps = new List<string>();
        Walk(root, root.GetType().Name, gaps);
        return gaps;
    }

    private static void Walk(Control parent, string path, List<string> gaps)
    {
        foreach (Control child in parent.Controls)
        {
            if (IsInteractive(child) && Spoken(child).Length == 0)
                gaps.Add($"{path} -> {child.GetType().Name} has nothing to say for itself");
            Walk(child, path, gaps);
        }
    }

    /// <summary>
    /// The name a reader would actually say, with the parts it cannot say taken out. A
    /// zero-width space and a run of blanks are both an absence, not a name.
    /// </summary>
    private static string Spoken(Control control)
    {
        string name = control.AccessibleName ?? string.Empty;
        if (name.Length == 0) name = control.Text ?? string.Empty;

        var sayable = new string([.. name.Where(c => !char.IsControl(c)
            && char.GetUnicodeCategory(c) is not (UnicodeCategory.Format
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.OtherNotAssigned))]);
        return sayable.Replace("&", string.Empty).Trim();
    }

    [Fact]
    public void ThePaintedScreenCannotBeClickedIntoFocus()
    {
        // The painted surface deliberately has no accessible name, role, value or children --
        // the keyboard proxy is the reader's object. TabStop only stops the Tab key, so what
        // stops a mouse click is Selectable, and a Control is selectable by default. Left on,
        // a click moved focus off the proxy and onto a node the reader has nothing to say
        // about, in the middle of a full-screen program.
        using var surface = new ScreenSurface();
        var getStyle = typeof(Control).GetMethod("GetStyle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.False((bool)getStyle.Invoke(surface, [ControlStyles.Selectable])!);
    }

    private static void OnAWindowThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
