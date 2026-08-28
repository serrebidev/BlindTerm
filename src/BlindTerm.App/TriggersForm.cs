using System.Runtime.Versioning;
using BlindTerm.Core.Triggers;

namespace BlindTerm.App;

/// <summary>
/// The list of triggers, and everything that can be done to it.
///
/// A checked list box rather than a grid, for two reasons. The check is the on switch, so the
/// commonest thing anyone comes here to do -- turn one off because it is in the way, turn it
/// back on tomorrow -- is the space bar and nothing else. And each item is a sentence rather
/// than a row of columns: a name on its own is a thing whose effect you have to open a dialog
/// to find out, so the item says what it watches for and what it does, and arrowing down the
/// list is hearing what the whole set does.
///
/// Order is the user's, and it matters: a trigger that stops the ones after it only means
/// something against a list someone arranged. That is what Move up and Move down are for.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TriggersForm : Form
{
    private readonly CheckedListBox _list = new();
    private readonly CheckBox _active = new();
    private readonly Button _edit = new();
    private readonly Button _duplicate = new();
    private readonly Button _remove = new();
    private readonly Button _up = new();
    private readonly Button _down = new();
    private readonly List<Trigger> _triggers;

    /// <summary>The list as edited. Only meaningful once the dialog returned OK.</summary>
    public IReadOnlyList<Trigger> Triggers => _triggers;

    /// <summary>Whether triggers run at all.</summary>
    public bool Active { get; private set; }

    public TriggersForm(IEnumerable<Trigger> triggers, bool active)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        _triggers = [.. triggers.Select(trigger => trigger.Copy())];
        Active = active;

        Text = "Triggers";
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 460);
        MinimumSize = new Size(560, 360);

        BuildControls();
        Refill(0);
    }

    private void BuildControls()
    {
        _active.Text = "&Triggers are active";
        _active.AutoSize = true;
        _active.Checked = Active;
        _active.AccessibleName = "Triggers are active";
        _active.AccessibleDescription =
            "The master switch over the whole list. Turning it off stops every trigger "
            + "without changing any of them.";
        _active.Dock = DockStyle.Top;
        _active.Padding = new Padding(6);
        _active.TabIndex = 0;

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        // One click is a selection, not a change of state. The space bar toggles the check,
        // which is the deliberate gesture; clicking through a list should not switch things
        // off on the way past.
        _list.CheckOnClick = false;
        _list.AccessibleName = "Triggers";
        _list.AccessibleDescription =
            "Each item says what it watches for and what it does. Space turns one on or off, "
            + "Enter opens it, and Delete removes it.";
        _list.TabIndex = 1;
        _list.KeyDown += OnListKeyDown;
        _list.DoubleClick += (_, _) => EditSelected();
        _list.ItemCheck += OnItemCheck;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(6),
            WrapContents = true,
            TabIndex = 2,
        };
        buttons.Controls.Add(Command("&Add...", "Write a new trigger", AddNew));
        _edit.Click += (_, _) => EditSelected();
        buttons.Controls.Add(Configure(_edit, "&Edit...", "Change the selected trigger"));
        _duplicate.Click += (_, _) => DuplicateSelected();
        buttons.Controls.Add(Configure(_duplicate, "D&uplicate",
            "Make a copy of the selected trigger to change"));
        _remove.Click += (_, _) => RemoveSelected();
        buttons.Controls.Add(Configure(_remove, "&Remove", "Delete the selected trigger"));
        _up.Click += (_, _) => Reorder(-1);
        buttons.Controls.Add(Configure(_up, "Move u&p",
            "Check the selected trigger earlier. Order decides which trigger stops the rest."));
        _down.Click += (_, _) => Reorder(1);
        buttons.Controls.Add(Configure(_down, "Move dow&n", "Check the selected trigger later"));

        var close = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
            TabIndex = 3,
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AccessibleName = "Cancel, and leave the triggers as they were",
        };
        var save = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AccessibleName = "Save these triggers",
        };
        close.Controls.Add(cancel);
        close.Controls.Add(save);

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(close);
        Controls.Add(_active);

        AcceptButton = save;
        CancelButton = cancel;
        ActiveControl = _list;
    }

    private Button Command(string text, string description, Action action)
    {
        var button = new Button();
        button.Click += (_, _) => action();
        return Configure(button, text, description);
    }

    private static Button Configure(Button button, string text, string description)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AccessibleName = text.Replace("&", string.Empty).TrimEnd('.');
        button.AccessibleDescription = description;
        return button;
    }

    // ---- The list ----

    /// <summary>
    /// Rebuilds the whole list and puts the selection back where it should be.
    ///
    /// Wholesale rather than in place, because an item's text is a description of the trigger
    /// and almost every change here changes it. Getting the selection back is the part that
    /// matters: after removing the fourth of six, the keyboard belongs on the new fourth.
    /// </summary>
    private void Refill(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (Trigger trigger in _triggers)
            _list.Items.Add(trigger.Describe(), trigger.Enabled);
        _list.EndUpdate();

        if (_triggers.Count > 0)
            _list.SelectedIndex = Math.Clamp(select, 0, _triggers.Count - 1);

        bool any = _triggers.Count > 0;
        _edit.Enabled = any;
        _duplicate.Enabled = any;
        _remove.Enabled = any;
        _up.Enabled = any;
        _down.Enabled = any;
    }

    /// <summary>
    /// Keeps the trigger in step with its check box, so that the sentence the list reads out
    /// says "On" or "Off" to match the state the reader has just announced.
    /// </summary>
    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _triggers.Count) return;
        bool enabled = e.NewValue == CheckState.Checked;
        // Adding an item with a check state fires this event too, during Refill, with the
        // state the item already had: nothing changed, and the text was built to match it
        // one line earlier. Reacting again would be a second time round the same loop.
        if (enabled == _triggers[e.Index].Enabled) return;
        _triggers[e.Index].Enabled = enabled;
        // The item's own text has "On" or "Off" in it. Rewriting it during the event would
        // reset the list, so it waits until the event has been dealt with. A form not yet
        // shown has no handle to invoke on, and nothing the user has done can have got here.
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed || e.Index >= _list.Items.Count) return;
            _list.Items[e.Index] = _triggers[e.Index].Describe();
            _list.SetItemChecked(e.Index, enabled);
        });
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                EditSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Delete:
                RemoveSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    // ---- Commands ----

    private void AddNew()
    {
        using var dialog = new TriggerEditForm(new Trigger(), isNew: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _triggers.Add(dialog.Trigger);
        Refill(_triggers.Count - 1);
        _list.Focus();
    }

    private void EditSelected()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _triggers.Count) return;

        using var dialog = new TriggerEditForm(_triggers[index], isNew: false);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _triggers[index] = dialog.Trigger;
        Refill(index);
        _list.Focus();
    }

    private void DuplicateSelected()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _triggers.Count) return;

        Trigger copy = _triggers[index].Copy();
        copy.Name = copy.DisplayName + " copy";
        _triggers.Insert(index + 1, copy);
        Refill(index + 1);
        _list.Focus();
    }

    private void RemoveSelected()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _triggers.Count) return;

        // Asked rather than undone: there is no undo here, and Delete is next to the arrow
        // keys the list is being moved through.
        if (MessageBox.Show(this, $"Remove the trigger {_triggers[index].DisplayName}?",
                "Remove trigger", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes) return;

        _triggers.RemoveAt(index);
        Refill(index);
        _list.Focus();
    }

    private void Reorder(int delta)
    {
        int index = _list.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _triggers.Count) return;

        (_triggers[index], _triggers[target]) = (_triggers[target], _triggers[index]);
        Refill(target);
        _list.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Active = _active.Checked;
            for (int i = 0; i < _triggers.Count && i < _list.Items.Count; i++)
                _triggers[i].Enabled = _list.GetItemChecked(i);
        }
        base.OnFormClosing(e);
    }
}
