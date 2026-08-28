using System.Runtime.Versioning;
using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>Asks for an OpenSSH destination using ordinary, screen-reader-friendly controls.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SshConnectForm : Form
{
    private readonly ComboBox _host = new();
    private readonly TextBox _username = new();
    private readonly NumericUpDown _port = new();

    public SshTarget Target { get; private set; } = new("localhost");

    public SshConnectForm(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Text = "Connect to an SSH host";
        AccessibleName = "Connect to an SSH host";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _host.DropDownStyle = ComboBoxStyle.DropDown;
        _host.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _host.AutoCompleteSource = AutoCompleteSource.ListItems;
        _host.Width = 360;
        _host.AccessibleName = "SSH host";
        _host.AccessibleDescription = "The SSH host name or address. A remembered destination may also be chosen.";
        foreach (string entry in settings.RecentSshHosts) _host.Items.Add(entry);

        _username.Width = 240;
        _username.AccessibleName = "SSH username";
        _username.AccessibleDescription =
            "The account name on the remote host. Leave blank to use your Windows account name.";

        _port.Minimum = 1;
        _port.Maximum = 65_535;
        _port.Value = SshTarget.DefaultPort;
        _port.Width = 100;
        _port.AccessibleName = "SSH port";
        _port.AccessibleDescription = "The SSH port. The standard is 22.";

        if (settings.RecentSshHosts.FirstOrDefault() is { } recent &&
            SshTarget.TryParse(recent, out SshTarget? target)) Fill(target!);

        _host.SelectedIndexChanged += (_, _) =>
        {
            if (_host.SelectedItem is string selected &&
                SshTarget.TryParse(selected, out SshTarget? target)) Fill(target!);
        };

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, "&Host", _host);
        AddField(fields, "&Username", _username);
        AddField(fields, "&Port", _port);

        var connect = new Button
        {
            Text = "&Connect",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            AccessibleName = "Connect to SSH host",
        };
        var cancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel SSH connection",
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(connect);
        fields.Controls.Add(buttons, 1, fields.RowCount);

        Controls.Add(fields);
        AcceptButton = connect;
        CancelButton = cancel;
    }

    private void Fill(SshTarget target)
    {
        _host.Text = target.Host;
        _username.Text = target.Username;
        _port.Value = target.Port;
    }

    private static void AddField(TableLayoutPanel panel, string label, Control control)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 12, 0),
            AccessibleName = label.Replace("&", string.Empty),
        }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            string typed = _host.Text.Trim();
            bool fullDestination = typed.Contains('@') || typed.Contains("://", StringComparison.Ordinal)
                                   || typed.StartsWith('[') || typed.Count(character => character == ':') == 1;
            try
            {
                if (fullDestination && SshTarget.TryParse(typed, out SshTarget? parsed)
                                    && parsed is not null)
                {
                    string username = parsed.Username.Length > 0
                        ? parsed.Username
                        : _username.Text.Trim();
                    Target = new SshTarget(parsed.Host, parsed.Port, username);
                }
                else
                {
                    Target = new SshTarget(typed, (int)_port.Value, _username.Text.Trim());
                }
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid SSH destination",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
                _host.Focus();
                return;
            }
        }
        base.OnFormClosing(e);
    }
}
