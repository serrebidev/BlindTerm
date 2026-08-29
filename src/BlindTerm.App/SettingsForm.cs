using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.Sound;

namespace BlindTerm.App;

[SupportedOSPlatform("windows")]
internal sealed class SettingsForm : Form
{
    private readonly TextBox _shell = new();
    private readonly NumericUpDown _columns = new();
    private readonly NumericUpDown _rows = new();
    private readonly ComboBox _theme = new();
    private readonly CheckBox _automaticUpdates = new();
    private readonly NumericUpDown _updateInterval = new();
    private readonly CheckBox _mudSounds = new();
    private readonly TextBox _soundDirectory = new();
    private readonly NumericUpDown _soundVolume = new();
    private readonly CheckBox _downloadSounds = new();

    /// <summary>
    /// The colour choices and their order in the box. One list, so what is offered and what
    /// is saved cannot drift apart.
    /// </summary>
    private static readonly (AppTheme Theme, string Label)[] Themes =
    [
        (AppTheme.System, "Follow Windows"),
        (AppTheme.Light, "Light"),
        (AppTheme.Dark, "Dark"),
    ];

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

        // DropDownList so the arrows move between real choices and nothing can be typed into
        // it: there are three answers and no fourth one worth spelling out.
        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.Width = 200;
        _theme.AccessibleName = "Colours";
        foreach ((_, string label) in Themes) _theme.Items.Add(label);
        _theme.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, entry => entry.Theme == Settings.Theme));
        AddField(fields, "Colours", _theme,
            "Which colours BlindTerm draws its windows in. Follow Windows uses the desktop's "
            + "own light or dark setting. A change here takes effect the next time BlindTerm "
            + "starts.");

        _automaticUpdates.Text = "Automatically check for updates";
        _automaticUpdates.AutoSize = true;
        _automaticUpdates.Checked = Settings.AutomaticallyCheckForUpdates;
        _automaticUpdates.AccessibleName = "Automatically check for updates";
        _automaticUpdates.AccessibleDescription =
            "Check once when BlindTerm starts, then regularly while it remains open.";

        _updateInterval.Minimum = AppSettings.MinimumUpdateCheckIntervalMinutes;
        _updateInterval.Maximum = AppSettings.MaximumUpdateCheckIntervalMinutes;
        _updateInterval.Value = Math.Clamp(Settings.UpdateCheckIntervalMinutes,
            (int)_updateInterval.Minimum, (int)_updateInterval.Maximum);
        _updateInterval.Width = 100;
        _updateInterval.AccessibleName = "Update check interval in minutes";
        _updateInterval.Enabled = _automaticUpdates.Checked;
        _automaticUpdates.CheckedChanged += (_, _) =>
            _updateInterval.Enabled = _automaticUpdates.Checked;

        AddField(fields, "Automatic updates", _automaticUpdates,
            "Whether BlindTerm checks for updates at startup and regularly afterward.");
        AddField(fields, "Check every, minutes", _updateInterval,
            "Minutes between automatic update checks. The default is 60 minutes.");

        _mudSounds.Text = "Play MUD sounds";
        _mudSounds.AutoSize = true;
        _mudSounds.Checked = Settings.MudSounds;
        _mudSounds.AccessibleName = "Play MUD sounds";
        _mudSounds.AccessibleDescription =
            "Whether a MUD may play sounds. Its sound triggers are kept out of the text either way.";

        _soundDirectory.Text = Settings.SoundDirectory;
        _soundDirectory.Width = 420;
        _soundDirectory.AccessibleName = "Sound folder";
        _soundDirectory.PlaceholderText = SoundLibrary.DefaultDirectory;

        _soundVolume.Minimum = 0;
        _soundVolume.Maximum = 100;
        _soundVolume.Value = Math.Clamp(Settings.SoundVolume, 0, 100);
        _soundVolume.Width = 100;
        _soundVolume.AccessibleName = "Sound volume";

        _downloadSounds.Text = "Download sounds a MUD offers";
        _downloadSounds.AutoSize = true;
        _downloadSounds.Checked = Settings.DownloadSounds;
        _downloadSounds.AccessibleName = "Download sounds a MUD offers";
        _downloadSounds.AccessibleDescription =
            "Fetch a sound this machine does not have from the address the MUD gives. "
            + "The address comes from the server, so this is off unless you turn it on.";

        AddField(fields, "MUD sounds", _mudSounds, "Whether a MUD may play sounds.");
        AddField(fields, "Sound folder", _soundDirectory,
            "Where sound packs are unpacked. Leave blank for the default folder.");
        AddField(fields, "Sound volume", _soundVolume, "Scales every MUD sound, 0 to 100.");
        AddField(fields, "Download sounds", _downloadSounds,
            "Whether a sound this machine does not have may be fetched from the MUD's address.");

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
            edited.Theme = Themes[Math.Max(0, _theme.SelectedIndex)].Theme;
            edited.AutomaticallyCheckForUpdates = _automaticUpdates.Checked;
            edited.UpdateCheckIntervalMinutes = (int)_updateInterval.Value;
            edited.MudSounds = _mudSounds.Checked;
            edited.SoundDirectory = _soundDirectory.Text.Trim();
            edited.SoundVolume = (int)_soundVolume.Value;
            edited.DownloadSounds = _downloadSounds.Checked;
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
