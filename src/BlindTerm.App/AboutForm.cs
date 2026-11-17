using System.Runtime.Versioning;
using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>Project information and reachable community links.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AboutForm : Form
{
    public const string RepositoryUrl = "https://git.serrebiradio.com/serrebi/BlindTerm";
    public const string ForgejoProfileUrl = "https://git.serrebiradio.com/serrebi";
    public const string TelegramUrl = "https://t.me/SerrebiProjects";
    public const string GitHubUrl = "https://github.com/serrebidev/BlindTerm";
    public const string GitHubProfileUrl = "https://github.com/serrebidev";
    public const string ProjectsUrl = "https://projects.serrebiradio.com";
    public const string CreditsUrl = RepositoryUrl + "/src/branch/master/CREDITS.md";

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
        // The links are buttons below, each a tab stop; the text only names the author.
        const string descriptionText = "An accessible terminal and MUD client built for screen-reader users.\n\n"
            + "Author: serrebidev.";
        var description = new Label
        {
            Text = descriptionText,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            AccessibleName = descriptionText,
        };
        var repository = new LinkLabel
        {
            Text = "BlindTerm Forgejo repository",
            AutoSize = true,
            AccessibleName = "Open the BlindTerm Forgejo repository",
            TabStop = true,
        };
        repository.LinkClicked += (_, _) => ExternalLinks.Open(this, RepositoryUrl);

        var follow = LinkButton("&Serrebi on Forgejo", ForgejoProfileUrl,
            "Open Serrebi's Forgejo profile");
        var github = LinkButton("&GitHub", GitHubUrl,
            "Open BlindTerm on GitHub");
        var projects = LinkButton("&Projects", ProjectsUrl,
            "Open projects.serrebiradio.com");
        var telegram = LinkButton("&Join Telegram", TelegramUrl,
            "Join the Serrebi Projects Telegram channel");
        var credits = LinkButton("&Full credits", CreditsUrl,
            "Open the full credits, CREDITS.md");
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
        links.Controls.Add(github);
        links.Controls.Add(projects);
        links.Controls.Add(telegram);
        links.Controls.Add(credits);

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
