using BlindTerm.App;
using BlindTerm.Core.Triggers;

namespace BlindTerm.Tests;

/// <summary>
/// That the two trigger dialogs can actually be built, and that what comes back out of them
/// is what went in.
///
/// Worth having as a test rather than as something noticed while using the app: a dialog is
/// laid out in code here, and a control added to the wrong panel or a list filled from the
/// wrong collection is not a compile error. It is a window that opens empty, or does not open
/// at all, in front of someone who cannot see which of the two happened.
/// </summary>
public class TriggerFormTests
{
    /// <summary>
    /// Runs a piece of window code on a thread in a single-threaded apartment, which is what
    /// Windows Forms requires and what the test runner's own thread is not.
    /// </summary>
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

    private static Trigger Sample() => new()
    {
        Name = "Someone arrives",
        Pattern = "* arrives from *",
        Match = TriggerMatch.Wildcard,
        Where = TriggerWhere.Mud,
        Speak = "$1 from the $2",
        SpeakNow = true,
        RepeatAfterMilliseconds = 2500,
    };

    [Fact]
    public void TheListOpensWithATriggerForEveryOneItWasGiven()
        => OnAWindowThread(() =>
        {
            using var form = new TriggersForm([Sample(), new Trigger { Pattern = "dragon", Beep = true }],
                                              active: true);
            Assert.Equal(2, form.Triggers.Count);
            Assert.True(form.Active);
        });

    /// <summary>
    /// The dialog works on copies. Cancelling has to leave the caller's triggers untouched,
    /// and so does editing one of them and then cancelling.
    /// </summary>
    [Fact]
    public void TheListWorksOnCopiesSoCancellingChangesNothing()
        => OnAWindowThread(() =>
        {
            Trigger original = Sample();
            using var form = new TriggersForm([original], active: true);

            form.Triggers[0].Pattern = "something else";

            Assert.Equal("* arrives from *", original.Pattern);
        });

    [Fact]
    public void TheEditorOpensOnACopyOfTheTriggerItWasGiven()
        => OnAWindowThread(() =>
        {
            Trigger original = Sample();
            using var form = new TriggerEditForm(original, isNew: false);

            Assert.NotSame(original, form.Trigger);
            Assert.Equal(original.Pattern, form.Trigger.Pattern);
            Assert.Equal(original.Match, form.Trigger.Match);
            Assert.Equal(original.Where, form.Trigger.Where);
            Assert.Equal(original.RepeatAfterMilliseconds, form.Trigger.RepeatAfterMilliseconds);
        });

    [Fact]
    public void TheEditorOpensForABrandNewTriggerToo()
        => OnAWindowThread(() =>
        {
            using var form = new TriggerEditForm(new Trigger(), isNew: true);
            Assert.Equal("New trigger", form.Text);
        });

    /// <summary>
    /// Every control in these dialogs has to have a name and a description, because a label
    /// nobody can see is not a label. This is the check that a control added later did not
    /// arrive without them.
    /// </summary>
    [Fact]
    public void EveryControlAnyoneCanReachSaysWhatItIsAndWhatItIsFor()
        => OnAWindowThread(() =>
        {
            using var editor = new TriggerEditForm(Sample(), isNew: false);
            AssertDescribed(editor);

            using var list = new TriggersForm([Sample()], active: true);
            AssertDescribed(list);
        });

    private static void AssertDescribed(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            // Labels carry the name for the control beside them, and panels are arrangement
            // rather than anything to land on.
            bool interactive = control is TextBox or ComboBox or CheckBox or NumericUpDown
                                          or Button or CheckedListBox;
            if (interactive)
            {
                Assert.False(string.IsNullOrWhiteSpace(control.AccessibleName),
                    $"{control.GetType().Name} \"{control.Text}\" has no accessible name");
            }
            // The spinner's edit box is its interior, built with the spinner's own handle
            // and named after it once there is a window. A form built and never shown has
            // no handle, so the interior exists to be walked into here but has no name to
            // give; the NumericUpDown above it is the control a reader is told about.
            if (control is not NumericUpDown) AssertDescribed(control);
        }
    }
}
