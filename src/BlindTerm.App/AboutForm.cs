using System.Runtime.Versioning;
using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>Project information and reachable community links.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AboutForm : Form
{
    public const string RepositoryUrl = "https://github.com/serrebidev/BlindTerm";
    public const string GitHubProfileUrl = "https://github.com/serrebidev";
    public const string TelegramUrl = "https://t.me/SerrebiProjects";

    public AboutForm()
    {
        Text = "About BlindTerm";
        AccessibleName = "About BlindTerm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var title = new Label
        {
            Text = $"BlindTerm {VersionInfo.Display}",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold),
            AccessibleName = $"BlindTerm {VersionInfo.Display}",
        };
        var description = new Label
        {
            Text = "An accessible terminal and MUD client built for screen-reader users.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = "An accessible terminal and MUD client built for screen-reader users.",
        };
        var repository = new LinkLabel
        {
            Text = "BlindTerm GitHub repository",
            AutoSize = true,
            AccessibleName = "Open the BlindTerm GitHub repository",
            TabStop = true,
        };
        repository.LinkClicked += (_, _) => ExternalLinks.Open(this, RepositoryUrl);

        var follow = LinkButton("&Follow me on GitHub", GitHubProfileUrl,
            "Open Serrebi's GitHub profile, where you can follow the project author");
        var telegram = LinkButton("&Join Telegram", TelegramUrl,
            "Join the Serrebi Projects Telegram channel");
        var close = new Button
        {
            Text = "&Close",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            AccessibleName = "Close About BlindTerm",
        };

        var links = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        links.Controls.Add(follow);
        links.Controls.Add(telegram);

        var closeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        closeRow.Controls.Add(close);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
        };
        layout.Controls.Add(title);
        layout.Controls.Add(description);
        layout.Controls.Add(repository);
        layout.Controls.Add(links);
        layout.Controls.Add(closeRow);
        Controls.Add(layout);

        AcceptButton = close;
        CancelButton = close;
    }

    private Button LinkButton(string text, string url, string description)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AccessibleName = text.Replace("&", string.Empty),
            AccessibleDescription = description,
        };
        button.Click += (_, _) => ExternalLinks.Open(this, url);
        return button;
    }
}
