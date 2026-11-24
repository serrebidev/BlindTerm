using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BlindTerm.Core;

namespace BlindTerm.App;

/// <summary>
/// Help > Report an issue on GitHub: the title and description are asked here,
/// where the screen reader knows the boxes, then GitHub's new-issue page opens
/// with both filled in and the version line added. GitHub needs the user's own
/// account, so the last step, Create, is theirs in the browser.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ReportIssue
{
    public static void Show(IWin32Window owner)
    {
        string? summary = Ask(owner,
            "Issue title: one line saying what went wrong or what you would like", false);
        if (string.IsNullOrEmpty(summary)) return;
        string? details = Ask(owner,
            "Describe what you did, what happened, and what you expected.\n"
            + "Your browser then opens GitHub with this filled in. Sign in and press Create.", true);
        if (details is null) return;
        ExternalLinks.Open(owner, IssueUrl(summary, details));
    }

    internal static string IssueUrl(string summary, string details)
    {
        string body = $"{details}\n\n---\nBlindTerm {VersionInfo.Display} on "
            + $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";
        return AboutForm.GitHubUrl + "/issues/new?title=" + Uri.EscapeDataString(summary)
            + "&body=" + Uri.EscapeDataString(body);
    }

    /// <summary>Trimmed text, or null on Cancel. Multiline: Enter is a new line, Tab reaches OK.</summary>
    private static string? Ask(IWin32Window owner, string message, bool multiline)
    {
        using var form = new Form
        {
            Text = "Report an issue",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var label = new Label { Text = message, AutoSize = true, MaximumSize = new Size(520, 0) };
        var box = new TextBox
        {
            AccessibleName = message,
            Multiline = multiline,
            AcceptsReturn = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Width = 520,
            Height = multiline ? 200 : 24,
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(10) };
        layout.Controls.Add(label);
        layout.Controls.Add(box);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        // A multiline box keeps Enter for new lines; OK is reached with Tab.
        if (!multiline) form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ActiveControl = box;
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}
