using System.Diagnostics;
using System.Runtime.Versioning;
using BlindTerm.Core;
using BlindTerm.Core.Mud;

namespace BlindTerm.App;

/// <summary>
/// A directory of MUDs, as a list to arrow through rather than a website to read.
///
/// Finding a MUD has always meant a directory site: a page of tables, banners, vote buttons
/// and a hundred links, where the address is the one part anybody actually wants and is
/// buried the deepest. The same directory read over its API is a list of names with the
/// player count and the genre attached, and choosing one fills in the address.
///
/// The window is deliberately a stack of plain controls in tab order -- what to sort by, what
/// to narrow it to, what to search for, the results, the details, the buttons -- because that
/// is a shape a screen reader reads correctly without being told anything about it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MudBrowserForm : Form
{
    /// <summary>
    /// The orderings, most useful first, because this is a list that gets arrowed through
    /// and the first entry is the one most people will take.
    ///
    /// "Busiest on average" sits second rather than first on purpose. It is the better
    /// question -- a month's average says whether a game has people in it, where a count
    /// taken now says whether it is busy at this hour in this timezone -- but somebody who
    /// wants to play something tonight is asking the first question, and the second is right
    /// underneath it.
    /// </summary>
    private static readonly (string Label, MudDirectorySort Sort)[] Orders =
    [
        ("Most players online now", MudDirectorySort.MostPlayers),
        ("Busiest on average over thirty days", MudDirectorySort.BusiestAverage),
        ("Highest peak in thirty days", MudDirectorySort.HighestPeak),
        ("Top voted this month", MudDirectorySort.TopVoted),
        ("Most reviewed", MudDirectorySort.MostReviewed),
        ("Recently online", MudDirectorySort.RecentlyOnline),
        ("Recently updated", MudDirectorySort.RecentlyUpdated),
        ("Newest listings", MudDirectorySort.Newest),
        ("Oldest, by the year they opened", MudDirectorySort.Oldest),
    ];

    private readonly ComboBox _order = new();
    private readonly ComboBox _genre = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _roleplaying = new();
    private readonly TextBox _search = new();
    private readonly Button _show;
    private readonly Button _more;
    private readonly Button _connect;
    private readonly Label _status = new();
    private readonly ListBox _results = new();
    private readonly TextBox _details = new();

    private readonly AppSettings _settings;
    private readonly Action<string, string>? _save;
    private readonly List<MudGame> _shown = new();

    private IMudDirectory? _directory;
    private CancellationTokenSource? _running;
    private int _page = 1;
    private bool _hasMore;
    private bool _loaded;

    /// <summary>The game that was chosen, once the dialog has been answered.</summary>
    public MudGame? Chosen { get; private set; }

    public MudBrowserForm(AppSettings settings, Action<string, string>? save = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _save = save;

        Text = "Browse for MUDs";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 620);
        Size = new Size(720, 700);

        Fill(_order, "&Browse by",
            "What to order the list by. Players online now is who is there this minute; "
            + "busiest on average is who has people in it across a month; top voted is who "
            + "campaigned hardest.");
        foreach ((string label, _) in Orders) _order.Items.Add(label);
        _order.SelectedIndex = 0;

        Fill(_genre, "&Genre", "Narrow the list to one setting, such as fantasy or cyberpunk.");
        Fill(_type, "Game &type", "Narrow the list to MUDs, MUSHes, MOOs and the rest.");
        Fill(_roleplaying, "&Roleplaying", "Narrow the list by whether roleplaying is required.");

        _search.Width = 360;
        _search.AccessibleName = "Search";
        _search.AccessibleDescription = "Words to look for in a game's name or description. Leave blank to browse.";

        _show = Button("&Show MUDs", "Fetch the list with these choices.", () => Fetch(page: 1));
        _more = Button("&More results", "Add the next page of results to the list.", () => Fetch(_page + 1));
        _more.Enabled = false;
        _connect = Button("&Use this MUD", "Take the address of the selected MUD back to the connect dialog.",
            Choose);
        _connect.Enabled = false;
        Button key = Button("MUDVerse &key...", "Enter or change the API key BlindTerm reads the directory with.",
            () => AskForKey());
        var close = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AccessibleName = "Cancel",
        };

        _status.AutoSize = true;
        _status.Text = "Nothing fetched yet.";
        _status.AccessibleName = "Status";

        _results.Dock = DockStyle.Fill;
        _results.IntegralHeight = false;
        _results.AccessibleName = "MUDs";
        _results.AccessibleDescription = "Arrow through the results. The details below follow the selection.";
        _results.SelectedIndexChanged += (_, _) => ShowDetails();
        _results.DoubleClick += (_, _) => Choose();
        _results.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Choose();
        };

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.AccessibleName = "Details";
        _details.AccessibleDescription =
            "The whole entry for the selected MUD, as lines to read down through.";

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "&Browse by", _order);
        AddRow(layout, "&Genre", _genre);
        AddRow(layout, "Game &type", _type);
        AddRow(layout, "&Roleplaying", _roleplaying);
        AddRow(layout, "&Search", _search);

        var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        top.Controls.Add(_show);
        top.Controls.Add(key);
        AddSpan(layout, top, grow: false);
        AddSpan(layout, _status, grow: false);
        AddRow(layout, "&MUDs", _results, grow: true, weight: 60);
        AddRow(layout, "&Details", _details, grow: true, weight: 40);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(_connect);
        buttons.Controls.Add(_more);
        AddSpan(layout, buttons, grow: false);

        Controls.Add(layout);
        CancelButton = close;
        // Deliberately no AcceptButton: Enter belongs to whatever has focus. In the results
        // list it chooses a MUD, and in the search box it should search rather than close the
        // window, which is what a default button would have made it do from anywhere.
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Fetch(page: 1);
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_loaded) return;
        _loaded = true;

        await LoadFilters();
        Fetch(page: 1);
    }

    /// <summary>
    /// The directory to read.
    ///
    /// Never null, and never asks for anything: with nothing configured this reads the list
    /// BlindTerm publishes, which needs no key from the person browsing. A key is only
    /// consulted because somebody deliberately entered one.
    ///
    /// Built late, because a key can be entered while this window is open and a client
    /// carries its key in a header set once, at construction.
    /// </summary>
    private IMudDirectory Directory()
        => _directory ??= MudDirectories.Open(_settings.MudDirectoryKey, _settings.MudDirectoryEndpoint);

    /// <summary>
    /// Asks for a key, and opens MUDVerse's own page for getting one.
    ///
    /// Optional, and worth saying why it exists at all: BlindTerm's published list is built
    /// every half hour, so its player counts have an age. A key reads MUDVerse live instead.
    /// There is no key compiled into BlindTerm to do that with, because MUDVerse issues keys
    /// for servers and asks that they are not published, and BlindTerm's source is published.
    /// </summary>
    private bool AskForKey()
    {
        using var dialog = new MudDirectoryKeyForm(_settings.MudDirectoryKey, _settings.MudDirectoryEndpoint);
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        _settings.MudDirectoryKey = dialog.Key;
        _settings.MudDirectoryEndpoint = dialog.Endpoint;
        _save?.Invoke(dialog.Key, dialog.Endpoint);

        _directory?.Dispose();
        _directory = MudDirectories.Open(dialog.Key, dialog.Endpoint);
        _status.Text = "Saved.";
        return true;
    }

    private async Task LoadFilters()
    {
        IMudDirectory directory = Directory();
        _status.Text = "Fetching the list of MUDs...";
        try
        {
            MudDirectoryFilters filters = await directory.FiltersAsync();
            Populate(_genre, "All genres", filters.Themes);
            Populate(_type, "All game types", filters.GameTypes);
            Populate(_roleplaying, "Any roleplaying policy", filters.Roleplaying);
        }
        catch (MudDirectoryException ex)
        {
            // A missing taxonomy is not fatal: everything still works unfiltered, and saying
            // so beats three empty combo boxes with no explanation.
            _status.Text = "The genre list could not be fetched. " + ex.Message;
        }
    }

    private static void Populate(ComboBox box, string all, IReadOnlyList<MudTag> tags)
    {
        box.Items.Clear();
        box.Items.Add(new TagItem(all, null));
        foreach (MudTag tag in tags) box.Items.Add(new TagItem(tag.Name, tag.Id));
        box.SelectedIndex = 0;
        box.Enabled = tags.Count > 0;
    }

    private async void Fetch(int page)
    {
        IMudDirectory directory = Directory();

        // A second search started while the first is in flight abandons it, so the list can
        // never be filled in by the query before last. The old source is cancelled but not
        // disposed here: the call that owns it is still holding its token, and disposes it
        // itself on the way out.
        _running?.Cancel();
        var running = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _running = running;

        bool adding = page > 1;
        Working(true);
        _status.Text = adding ? "Fetching more..." : "Fetching...";

        try
        {
            var query = new MudDirectoryQuery
            {
                Sort = Orders[Math.Max(0, _order.SelectedIndex)].Sort,
                Search = _search.Text,
                ThemeTagId = ChosenTag(_genre),
                TypeTagId = ChosenTag(_type),
                RoleplayingTagId = ChosenTag(_roleplaying),
                Page = page,
                PerPage = 25,
            };
            MudDirectoryPage results = await directory.SearchAsync(query, running.Token);
            if (IsDisposed || Disposing || running.IsCancellationRequested) return;

            _page = results.Page;
            _hasMore = results.HasMore;
            if (!adding)
            {
                _shown.Clear();
                _results.Items.Clear();
            }

            int first = _results.Items.Count;
            foreach (MudGame game in results.Games)
            {
                _shown.Add(game);
                _results.Items.Add(new GameItem(game));
            }

            _status.Text = Describe(results, adding);
            if (_results.Items.Count > 0)
            {
                // Selecting is what makes a screen reader read an item, and the newly added
                // ones are the point of More results, so selection lands on the first of them.
                _results.SelectedIndex = adding && first < _results.Items.Count ? first : 0;
                _results.Focus();
            }
            else
            {
                _details.Text = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing) _status.Text = "MUDVerse did not answer in time.";
        }
        catch (MudDirectoryException ex)
        {
            if (IsDisposed || Disposing) return;
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Could not read the directory",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // The published list being missing is the one failure a key can fix, and the
            // exception says so rather than leaving the window with nothing to offer.
            if (ex.IsAuthentication && AskForKey())
            {
                await LoadFilters();
                Fetch(page: 1);
            }
        }
        finally
        {
            if (ReferenceEquals(_running, running)) _running = null;
            running.Dispose();
            if (!IsDisposed && !Disposing) Working(false);
        }
    }

    private string Describe(MudDirectoryPage results, bool adding)
    {
        if (_results.Items.Count == 0)
            return "No MUDs matched. Try a wider genre, or clear the search box.";
        string counted = results.Total > 0
            ? $"{_results.Items.Count} of {results.Total}"
            : $"{_results.Items.Count}";
        string what = adding ? "Added" : "Showing";
        return $"{what} {counted}." + (_hasMore ? " More results is available." : " That is all of them.")
               + Age();
    }

    /// <summary>
    /// How old the published list is, said in the way anyone actually asks it.
    ///
    /// Worth saying at all because a player count is the one thing here with a shelf life,
    /// and a list built half an hour ago is not the same claim as a list read live.
    /// </summary>
    private string Age()
    {
        if (_directory is not MudFeedDirectory feed || feed.Generated is not DateTimeOffset built)
            return string.Empty;

        TimeSpan old = DateTimeOffset.UtcNow - built;
        string when = old < TimeSpan.FromMinutes(2) ? "just now"
            : old < TimeSpan.FromHours(1) ? $"{(int)old.TotalMinutes} minutes ago"
            : old < TimeSpan.FromDays(1) ? $"{(int)old.TotalHours} hours ago"
            : $"{(int)old.TotalDays} days ago";
        string from = feed.Sources.Count > 0 ? " From " + Join(feed.Sources) + "." : string.Empty;
        return $" List built {when}.{from}";
    }

    /// <summary>"A and B", not "A, B" -- this is read out, not printed.</summary>
    private static string Join(IReadOnlyList<string> names)
    {
        if (names.Count == 1) return names[0];
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    private void Working(bool busy)
    {
        _show.Enabled = !busy;
        _more.Enabled = !busy && _hasMore;
        _connect.Enabled = !busy && _results.SelectedIndex >= 0;
        _order.Enabled = !busy;
        _search.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static string? ChosenTag(ComboBox box) => (box.SelectedItem as TagItem)?.Id;

    private void ShowDetails()
    {
        int at = _results.SelectedIndex;
        if (at < 0 || at >= _shown.Count)
        {
            _details.Text = string.Empty;
            _connect.Enabled = false;
            return;
        }

        MudGame game = _shown[at];
        _details.Text = game.Details;
        // Back to the top, so reading the details of the next one down starts at its name
        // rather than wherever the last one had been scrolled to.
        _details.SelectionStart = 0;
        _details.SelectionLength = 0;
        _connect.Enabled = game.CanConnect;
        _connect.AccessibleDescription = game.CanConnect
            ? $"Use {game.Name}, {game.Host} port {game.TlsPort ?? game.Port}."
            : $"{game.Name} has no telnet address.";
    }

    private void Choose()
    {
        int at = _results.SelectedIndex;
        if (at < 0 || at >= _shown.Count) return;
        MudGame game = _shown[at];
        if (!game.CanConnect)
        {
            MessageBox.Show(this,
                $"{game.Name} is played in a web browser. There is no address for BlindTerm to dial."
                + (game.Website.Length > 0 ? Environment.NewLine + Environment.NewLine + game.Website : string.Empty),
                "Nothing to connect to", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Chosen = game;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button Button(string text, string description, Action clicked)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AccessibleName = text.Replace("&", string.Empty),
            AccessibleDescription = description,
        };
        button.Click += (_, _) => clicked();
        return button;
    }

    private static void Fill(ComboBox box, string name, string description)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Width = 320;
        box.AccessibleName = name.Replace("&", string.Empty);
        box.AccessibleDescription = description;
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control,
        bool grow = false, int weight = 0)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(grow ? new RowStyle(SizeType.Percent, weight) : new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Padding = new Padding(0, 5, 12, 0),
            AccessibleName = label.Replace("&", string.Empty),
        }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private static void AddSpan(TableLayoutPanel panel, Control control, bool grow)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(grow ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 1, row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _running?.Cancel();
            _running?.Dispose();
            _directory?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// A list entry. Wrapped rather than added raw, because a list box reads what ToString
    /// gives it and a record's own ToString would spell out every property it has.
    /// </summary>
    private sealed class GameItem(MudGame game)
    {
        public override string ToString() => game.Summary;
    }

    private sealed class TagItem(string name, string? id)
    {
        public string? Id { get; } = id;
        public override string ToString() => name;
    }
}

/// <summary>
/// Where a MUDVerse key goes, for the people who want one.
///
/// Nothing here is required. BlindTerm reads a list it publishes itself, which is rebuilt
/// every half hour and needs no key from anyone. A key is for reading MUDVerse live instead,
/// so the player counts are current to the minute rather than to the half hour -- and for
/// the case where the published list is unreachable.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MudDirectoryKeyForm : Form
{
    private readonly TextBox _key = new();
    private readonly TextBox _endpoint = new();

    public string Key { get; private set; } = string.Empty;
    public string Endpoint { get; private set; } = string.Empty;

    public MudDirectoryKeyForm(string key, string endpoint)
    {
        Text = "MUDVerse key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Text = "You do not need any of this. BlindTerm reads a list of MUDs that it "
                   + "publishes and rebuilds every half hour, which needs no key."
                   + Environment.NewLine + Environment.NewLine
                   + "A MUDVerse key reads their directory live instead, so player counts are "
                   + "current to the minute. MUDVerse issues one free key per account; Get a key "
                   + "opens the page where one is generated. There is no key inside BlindTerm to "
                   + "share out, because MUDVerse asks that keys are not published and "
                   + "BlindTerm's source is public.",
            AccessibleName = "About the MUDVerse key",
        };

        _key.Width = 460;
        _key.Text = key;
        _key.AccessibleName = "API key";
        _key.AccessibleDescription = "The key from the API tab of your MUDVerse account.";
        // Not a password box. Nobody memorises this, it is pasted, and a field that will not
        // read back what it holds cannot be checked for a stray space by anyone who cannot
        // see it.
        _endpoint.Width = 460;
        _endpoint.Text = endpoint;
        _endpoint.PlaceholderText = MudFeedDirectory.DefaultFeedUrl;
        _endpoint.AccessibleName = "Directory address";
        _endpoint.AccessibleDescription =
            "Leave blank unless you are pointing BlindTerm somewhere else. With a key, this is "
            + "the MUDVerse API to send it to. Without one, it is where the published list of "
            + "MUDs is fetched from.";

        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = fields.RowCount++;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.SetColumnSpan(explanation, 2);
        fields.Controls.Add(explanation, 0, row);

        Add(fields, "API &key", _key);
        Add(fields, "&Directory address", _endpoint);

        var get = new Button { Text = "&Get a key", AutoSize = true, AccessibleName = "Get a key" };
        get.AccessibleDescription = "Opens " + MudVerseDirectory.ApiKeyPage + " in your browser.";
        get.Click += (_, _) => Open(MudVerseDirectory.ApiKeyPage);

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true, AccessibleName = "Save" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, AccessibleName = "Cancel" };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(get);
        fields.Controls.Add(buttons, 1, fields.RowCount);

        Controls.Add(fields);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void Add(TableLayoutPanel panel, string label, Control control)
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

    private void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not open a browser. The address is " + url,
                "MUDVerse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            // Both may be empty, and that is the ordinary answer: it means "go back to the
            // list BlindTerm publishes", which is what somebody clearing a bad key wants.
            Key = _key.Text.Trim();
            Endpoint = _endpoint.Text.Trim();
        }
        base.OnFormClosing(e);
    }
}
