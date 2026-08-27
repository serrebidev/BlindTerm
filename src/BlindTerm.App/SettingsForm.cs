using System.Runtime.Versioning;
using BlindTerm.Core;

namespace BlindTerm.App;

[SupportedOSPlatform("windows")]
internal sealed class SettingsForm : Form
{
    private readonly TextBox _shell = new();
    private readonly NumericUpDown _columns = new();
    private readonly NumericUpDown _rows = new();

    public AppSettings Settings { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Settings = settings.Copy();
        Text = "BlindTerm settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _shell.AccessibleName = "Shell command line";
        _shell.Text = Settings.Shell;
        _shell.Width = 420;
        ConfigureNumber(_columns, "Columns", Settings.Columns);
        ConfigureNumber(_rows, "Rows", Settings.Rows);

        var fields = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, "Shell command line", _shell, "Shell command line. Leave blank for PowerShell 7, or Windows PowerShell.");
        AddField(fields, "Terminal columns", _columns, "Terminal width in columns.");
        AddField(fields, "Terminal rows", _rows, "Terminal height in rows.");

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, AccessibleName = "Save settings" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, AccessibleName = "Cancel settings" };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        fields.Controls.Add(buttons, 1, fields.RowCount);

        Controls.Add(fields);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void ConfigureNumber(NumericUpDown control, string name, int value)
    {
        control.Minimum = TerminalSize.MinimumColumns;
        control.Maximum = TerminalSize.MaximumColumns;
        control.Value = Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);
        control.AccessibleName = name;
        control.Width = 100;
    }

    private static void AddField(TableLayoutPanel panel, string label, Control control, string description)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 12, 0), AccessibleName = label }, 0, row);
        control.AccessibleDescription = description;
        panel.Controls.Add(control, 1, row);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            // Copied rather than rebuilt: preferences this dialog does not show, such as
            // whether the default-terminal question has been answered, must survive a save.
            AppSettings edited = Settings.Copy();
            edited.Shell = _shell.Text.Trim();
            edited.Columns = (int)_columns.Value;
            edited.Rows = (int)_rows.Value;
            Settings = edited;
            try { Settings.Validate(); }
            catch (ArgumentOutOfRangeException ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }
}
