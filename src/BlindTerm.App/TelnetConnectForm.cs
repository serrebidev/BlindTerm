using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.Net;

namespace BlindTerm.App;

/// <summary>
/// Asks where to connect.
///
/// The host is a combo box rather than a plain field because a MUD address is exactly the
/// kind of thing nobody types correctly twice: it is remembered, and arrowing to a previous
/// one reads it out. Port 23 is the default only because the standard says so -- MUDs almost
/// never use it, which is why the last port used is remembered with the host.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TelnetConnectForm : Form
{
    private readonly ComboBox _host = new();
    private readonly NumericUpDown _port = new();

    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = TelnetAddress.DefaultPort;

    public TelnetConnectForm(IReadOnlyList<string> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        Text = "Connect to a telnet host";
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
        _host.AccessibleName = "Host";
        _host.AccessibleDescription = "The host name or address to connect to.";
        foreach (string entry in recent) _host.Items.Add(entry);

        _port.Minimum = 1;
        _port.Maximum = 65535;
        _port.Width = 100;
        _port.AccessibleName = "Port";
        _port.AccessibleDescription = "The port to connect to. 23 is the telnet standard; MUDs usually use another.";

        if (recent.Count > 0 && TelnetAddress.TryParse(recent[0], out string host, out int port))
        {
            _host.Text = host;
            _port.Value = port;
        }
        else
        {
            _port.Value = TelnetAddress.DefaultPort;
        }

        // Choosing a remembered address fills in the port that went with it, so the two are
        // never half-updated.
        _host.SelectedIndexChanged += (_, _) =>
        {
            if (_host.SelectedItem is string chosen &&
                TelnetAddress.TryParse(chosen, out string pickedHost, out int pickedPort))
            {
                _host.Text = pickedHost;
                _port.Value = pickedPort;
            }
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
        AddField(fields, "&Port", _port);

        var connect = new Button
        {
            Text = "Connect",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AccessibleName = "Connect",
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AccessibleName = "Cancel",
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
            // A pasted "host:port" is what anyone copies from a MUD's front page, so accept it
            // in the host field rather than making them split it by hand.
            string typed = _host.Text.Trim();
            if (TelnetAddress.TryParse(typed, out string host, out int port) && typed.Contains(':'))
            {
                Host = host;
                Port = port;
            }
            else
            {
                Host = typed;
                Port = (int)_port.Value;
            }

            if (Host.Length == 0)
            {
                MessageBox.Show(this, "Enter a host name or address to connect to.",
                    "No host", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
                _host.Focus();
                return;
            }
        }
        base.OnFormClosing(e);
    }
}
