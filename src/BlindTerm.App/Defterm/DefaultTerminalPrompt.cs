using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;

namespace BlindTerm.App.Defterm;

/// <summary>
/// The one-time offer to make BlindTerm the terminal Windows opens by itself.
///
/// It is a native task dialog rather than a form of our own. NVDA and JAWS announce the
/// whole of one of those on open -- heading, body, the checkbox and both buttons -- and Yes
/// and No are real Yes and No buttons, so the answer is a single keystroke.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DefaultTerminalPrompt
{
    private const string Heading = "Make BlindTerm your default terminal?";

    private const string Body =
        "Windows can open BlindTerm whenever a command-line program needs a terminal, " +
        "instead of opening Windows Terminal or the console host. You can change this " +
        "later from the Terminal menu.";

    /// <summary>
    /// Whether there is anything worth asking. Already being the default, or being on a
    /// Windows that cannot hand a console over, both make the question noise.
    /// </summary>
    public static bool ShouldAsk(AppSettings settings)
        => settings.AskAboutDefaultTerminal
           && DefaultTerminalConfig.IsSupported
           && !DefaultTerminalConfig.IsFullyRegistered();

    /// <summary>
    /// Asks, applies the answer, and remembers it. Returns the message to speak, or null if
    /// nothing needs saying.
    /// </summary>
    public static string? AskAndApply(IWin32Window owner, AppSettings settings, SettingsStore store)
    {
        (bool yes, bool dontAskAgain) = Ask(owner);

        // "Don't ask me again" starts checked, so the default outcome of the dialog is that
        // it is never seen a second time -- whichever button was pressed.
        if (dontAskAgain && settings.AskAboutDefaultTerminal)
        {
            settings.AskAboutDefaultTerminal = false;
            TrySave(settings, store);
        }

        if (!yes) return null;
        return Apply(owner);
    }

    /// <summary>Turns the setting on, reporting what happened in a sentence.</summary>
    public static string Apply(IWin32Window owner)
    {
        try
        {
            DefaultTerminalConfig.MakeDefault();
            return "BlindTerm is now your default terminal.";
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UnauthorizedAccessException
                                         or SecurityException or Win32Exception)
        {
            MessageBox.Show(owner,
                $"BlindTerm could not be made the default terminal.\n\n{ex.Message}",
                "Default terminal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return "Could not change the default terminal.";
        }
    }

    /// <summary>Hands the choice back to Windows, reporting what happened in a sentence.</summary>
    public static string Revert(IWin32Window owner)
    {
        try
        {
            DefaultTerminalConfig.ClearDefault();
            DefaultTerminalConfig.UnregisterComServer();
            return "BlindTerm is no longer your default terminal.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            MessageBox.Show(owner,
                $"BlindTerm could not stop being the default terminal.\n\n{ex.Message}",
                "Default terminal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return "Could not change the default terminal.";
        }
    }

    private static (bool Yes, bool DontAskAgain) Ask(IWin32Window owner)
    {
        var verification = new TaskDialogVerificationCheckBox("&Don't ask me again") { Checked = true };
        var page = new TaskDialogPage
        {
            Caption = "BlindTerm",
            Heading = Heading,
            Text = Body,
            Icon = TaskDialogIcon.ShieldBlueBar,
            Verification = verification,
            Buttons = [TaskDialogButton.Yes, TaskDialogButton.No],
            DefaultButton = TaskDialogButton.Yes,
            AllowCancel = true,
        };

        try
        {
            TaskDialogButton answer = TaskDialog.ShowDialog(owner, page);
            return (answer == TaskDialogButton.Yes, verification.Checked);
        }
        catch (Exception ex) when (ex is InvalidOperationException or EntryPointNotFoundException or Win32Exception)
        {
            // Task dialogs need the common controls v6 manifest. Without it, ask plainly and
            // treat the answer as final either way, which is what the checkbox would have said.
            DialogResult result = MessageBox.Show(owner, $"{Heading}\n\n{Body}", "BlindTerm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return (result == DialogResult.Yes, true);
        }
    }

    private static void TrySave(AppSettings settings, SettingsStore store)
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Failing to record the answer only means being asked once more.
        }
    }
}
