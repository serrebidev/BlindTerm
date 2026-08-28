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
///
/// Browsing is here rather than in a menu of its own because "which MUD" and "what address"
/// are one question. Somebody who does not already know an address has nothing to type into
/// this dialog, and the answer to that has always been to go and read a website: the browser
/// puts the same directory behind a list that can be arrowed through and fills these fields
/// in from it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TelnetConnectForm : Form
{
    private readonly ComboBox _host = new();
    private readonly NumericUpDown _port = new();
    private readonly CheckBox _secure = new();
    private readonly Button _connect;
    private readonly AppSettings _settings;
    private readonly Action<string, string>? _saveDirectorySettings;

    /// <summary>Where to connect, once the dialog has been answered.</summary>
    public TelnetTarget Target { get; private set; } = new(string.Empty, TelnetAddress.DefaultPort);

    public string Host => Target.Host;
    public int Port => Target.Port;

    /// <param name="settings">Read for remembered addresses and for the directory key.</param>
    /// <param name="saveDirectorySettings">
    /// Called with a key and endpoint entered while browsing, so it is asked for once rather
    /// than once per session. Null in a test, where nothing is being kept.
    /// </param>
    public TelnetConnectForm(AppSettings settings, Action<string, string>? saveDirectorySettings = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _saveDirectorySettings = saveDirectorySettings;
        IReadOnlyList<string> recent = settings.RecentTelnetHosts;

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

        _secure.Text = "&Secure connection (TLS)";
        _secure.AutoSize = true;
        _secure.AccessibleName = "Secure connection";
        _secure.AccessibleDescription =
            "Encrypt the connection, the way a web address beginning https is encrypted. A MUD "
            + "that offers this publishes a separate port for it, so the port changes too.";

        if (recent.Count > 0 &&
            TelnetAddress.TryParse(recent[0], out string host, out int port, out bool secure))
        {
            _host.Text = host;
            _port.Value = port;
            _secure.Checked = secure;
        }
        else
        {
            _port.Value = TelnetAddress.DefaultPort;
        }

        // Choosing a remembered address fills in the port and the encryption that went with
        // it, so the three are never left half-updated.
        _host.SelectedIndexChanged += (_, _) =>
        {
            if (_host.SelectedItem is string chosen &&
                TelnetAddress.TryParse(chosen, out string pickedHost, out int pickedPort, out bool pickedTls))
            {
                _host.Text = pickedHost;
                _port.Value = pickedPort;
                _secure.Checked = pickedTls;
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
        AddField(fields, "Encryption", _secure);

        var browse = new Button
        {
            Text = "&Browse for MUDs...",
            AutoSize = true,
            AccessibleName = "Browse for MUDs",
            AccessibleDescription =
                "Search a directory of MUDs by genre, players online or name, and fill these "
                + "fields in from the one you choose.",
        };
        browse.Click += (_, _) => Browse();

        _connect = new Button
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
        buttons.Controls.Add(_connect);
        buttons.Controls.Add(browse);
        fields.Controls.Add(buttons, 1, fields.RowCount);

        Controls.Add(fields);
        AcceptButton = _connect;
        CancelButton = cancel;
    }

    /// <summary>
    /// Opens the directory and takes the address out of whatever was chosen.
    ///
    /// The fields are filled and the dialog stays open, rather than connecting straight away.
    /// A listing can offer a plain port and an encrypted one, and the choice between them is
    /// worth being able to see and change before dialling rather than after.
    /// </summary>
    private void Browse()
    {
        using var browser = new MudBrowserForm(_settings, _saveDirectorySettings);
        if (browser.ShowDialog(this) != DialogResult.OK || browser.Chosen is not { } game) return;

        _host.Text = game.Host;
        // A listing that publishes an encrypted port is taken at its word, because a MUD only
        // publishes one when it means it to be used. Unticking it here goes back to the plain
        // port without another trip through the directory.
        if (game.TlsPort is int tls)
        {
            _port.Value = Math.Clamp(tls, (int)_port.Minimum, (int)_port.Maximum);
            _secure.Checked = true;
        }
        else
        {
            _port.Value = Math.Clamp(game.Port, (int)_port.Minimum, (int)_port.Maximum);
            _secure.Checked = false;
        }

        // Focus lands on Connect, so Enter finishes the job, and the description says what is
        // about to be dialled -- which is the only place the choice is confirmed out loud.
        _connect.AccessibleDescription =
            $"Connect to {game.Name}, {_host.Text} port {(int)_port.Value}"
            + (_secure.Checked ? ", encrypted." : ".");
        _connect.Focus();
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
            // A pasted "host:port", or "ssl://host:port", is what anyone copies from a MUD's
            // front page, so accept it in the host field rather than making them split it up.
            string typed = _host.Text.Trim();
            bool spelledOut = TelnetAddress.TryParse(typed, out string host, out int port, out bool secure)
                              && (typed.Contains("://", StringComparison.Ordinal) || Bare(typed).Contains(':'));

            Target = spelledOut
                ? new TelnetTarget(host, port, secure)
                : new TelnetTarget(typed, (int)_port.Value, _secure.Checked);

            if (Target.Host.Length == 0)
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

    /// <summary>The address past any scheme, where a colon really does mean a port.</summary>
    private static string Bare(string address)
    {
        int scheme = address.IndexOf("://", StringComparison.Ordinal);
        return scheme < 0 ? address : address[(scheme + 3)..];
    }
}
