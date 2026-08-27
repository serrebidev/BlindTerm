using System.Net.Sockets;
using System.Runtime.Versioning;
using BlindTerm.App.Defterm;
using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using BlindTerm.Core.Net;
using BlindTerm.Core.Speech;

namespace BlindTerm.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = BlindTerm.Core.Updates.UpdateApplier.Run(args[1..]);
            return;
        }

        // The same two choices the Terminal menu offers, without a window. The installer
        // uses these, and --reset-default-terminal is the escape hatch: if a handoff ever
        // goes wrong it puts the default terminal back the way Windows shipped it, needing
        // neither a working COM registration nor a terminal to type it into.
        if (args.Length > 0 && args[0].Equals("--reset-default-terminal", StringComparison.OrdinalIgnoreCase))
        {
            DefaultTerminalConfig.ClearDefault();
            DefaultTerminalConfig.UnregisterComServer();
            return;
        }

        if (args.Length > 0 && args[0].Equals("--set-default-terminal", StringComparison.OrdinalIgnoreCase))
        {
            DefaultTerminalConfig.MakeDefault();
            return;
        }

        ApplicationConfiguration.Initialize();

        // WinForms needs a synchronisation context before anything can be marshalled onto the
        // UI thread, and it only installs one once a message loop exists.
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settingsStore = new SettingsStore();
        AppSettings settings = settingsStore.Load();
        var windows = new TerminalWindows();

        // COM starts BlindTerm with -Embedding when a command-line program needs a terminal
        // and BlindTerm is the default. There is no shell to open in that case: the console
        // already exists and arrives shortly, over the handoff.
        bool embedding = IsEmbedding(args);
        DefaultTerminalServer.Start(SynchronizationContext.Current!,
            handoff => windows.OpenHandoff(handoff, settings, settingsStore));

        if (!embedding && TelnetArgument(args) is (string host, int port))
        {
            windows.OpenTelnet(host, port, settings, settingsStore);
        }
        else if (!embedding)
        {
            string shell = args.Length > 0 ? string.Join(' ', args) : ShellFor(settings.Shell);
            windows.OpenShell(shell, settings, settingsStore);
        }
        else if (!DefaultTerminalServer.IsListening)
        {
            // Nothing can arrive, so there is nothing to wait for.
            return;
        }

        Application.Run(windows);
        DefaultTerminalServer.Stop();
    }

    /// <summary>
    /// The host and port in "--telnet host[:port]" or "--telnet host port", or null when this
    /// is an ordinary launch. Both spellings are accepted because both are what anyone types.
    /// </summary>
    internal static (string Host, int Port)? TelnetArgument(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int at = Array.FindIndex(args, argument =>
            argument.Equals("--telnet", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length) return null;

        if (!TelnetAddress.TryParse(args[at + 1], out string host, out int port)) return null;

        // A port given as its own argument wins: "--telnet host 4000" is unambiguous, and a
        // host that already carried one would not have left the default in place.
        if (at + 2 < args.Length && int.TryParse(args[at + 2], out int separate)
            && separate is >= 1 and <= 65535 && !args[at + 1].Contains(':'))
            port = separate;

        return (host, port);
    }

    /// <summary>
    /// COM appends -Embedding to the registered command line. Both spellings are accepted
    /// because the switch has been written with either prefix for thirty years.
    /// </summary>
    internal static bool IsEmbedding(string[] args)
        => args.Any(argument =>
            argument.Equals("-Embedding", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("/Embedding", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What to launch. PowerShell 7 by default, because Windows PowerShell 5.1 disables
    /// PSReadLine the moment it detects a screen reader -- and PSReadLine is what provides
    /// tab completion, history search, and the shell integration markers command blocks are
    /// built on. Turning it off for screen reader users is precisely backwards, so prefer the
    /// shell that does not do it.
    /// </summary>
    internal static string ShellFor(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        string? pwsh = Which("pwsh.exe");
        if (pwsh is not null) return $"\"{pwsh}\" -NoLogo";

        // Windows PowerShell, with PSReadLine put back explicitly.
        return "powershell.exe -NoLogo -NoExit -Command \"Import-Module PSReadLine\"";
    }

    private static string? Which(string executable)
    {
        string paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in paths.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;
            try
            {
                string candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* a malformed PATH entry is not fatal */ }
        }
        return null;
    }
}

/// <summary>
/// Every terminal window this process is showing.
///
/// One process can own more than one now: being the default terminal means Windows can hand
/// over a console at any moment, including while a shell window is already open. The process
/// lives until the last window closes -- and, when it was started only to receive a handoff,
/// until one arrives or plainly is not going to.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TerminalWindows : ApplicationContext
{
    private const int HandoffWaitSeconds = 30;
    private const int ConnectTimeoutSeconds = 20;

    private int _open;
    private bool _everOpened;
    private System.Windows.Forms.Timer? _startupWait;

    public TerminalWindows()
    {
        // Started for a handoff that never comes -- a cancelled launch, or a console the
        // API server gave up on -- would otherwise leave an invisible process behind.
        _startupWait = new System.Windows.Forms.Timer { Interval = HandoffWaitSeconds * 1000 };
        _startupWait.Tick += (_, _) =>
        {
            StopWaiting();
            if (!_everOpened) ExitThread();
        };
        _startupWait.Start();
    }

    /// <summary>Opens the ordinary window, running a shell of the user's choosing.</summary>
    public void OpenShell(string shell, AppSettings settings, SettingsStore store)
    {
        var host = new TerminalHost(settings.Columns, settings.Rows, SynchronizationContext.Current!);
        var form = new MainForm(host, settings, store);
        form.Shown += (_, _) => host.Start(shell);
        Track(form, settings, store);
        form.Show();
    }

    /// <summary>
    /// Opens a window onto a telnet host, dialled by BlindTerm itself.
    ///
    /// Connecting happens before the window does. A host that cannot be reached should say so
    /// in a dialog and leave nothing behind, rather than leaving an empty terminal to work out
    /// what went wrong -- and a login banner that arrives in the first millisecond must not be
    /// delivered before there is a window subscribed to receive it, which is why reading only
    /// starts once the window is up.
    /// </summary>
    public async void OpenTelnet(string host, int port, AppSettings settings, SettingsStore store)
    {
        var terminal = new TerminalHost(settings.Columns, settings.Rows, SynchronizationContext.Current!);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
            await terminal.ConnectAsync(host, port, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException
                                   or IOException or ArgumentException)
        {
            terminal.Dispose();
            string reason = ex is OperationCanceledException
                ? $"{host} did not answer within {ConnectTimeoutSeconds} seconds."
                : ex.Message;
            MessageBox.Show(
                $"Could not connect to {TelnetAddress.Format(host, port)}."
                + Environment.NewLine + Environment.NewLine + reason,
                "BlindTerm could not connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // Nothing opened, and nothing else is going to: a launch that was only ever this
            // connection has no window to keep the process alive for.
            if (!_everOpened) ExitThread();
            return;
        }

        settings.RememberTelnetHost(TelnetAddress.Format(host, port));
        TrySave(settings, store);

        var form = new MainForm(terminal, settings, store)
        {
            Text = $"{TelnetAddress.Format(host, port)} — BlindTerm",
        };
        form.Shown += (_, _) => terminal.Begin();
        Track(form, settings, store);
        form.Show();
    }

    /// <summary>A remembered address is a convenience; failing to write one is not an error.</summary>
    private static void TrySave(AppSettings settings, SettingsStore store)
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentOutOfRangeException) { }
    }

    /// <summary>Opens a window onto a console Windows has just handed to BlindTerm.</summary>
    public void OpenHandoff(ConsoleHandoff handoff, AppSettings settings, SettingsStore store)
    {
        TerminalSize size = handoff.RequestedSize ?? new TerminalSize(settings.Columns, settings.Rows);
        var host = new TerminalHost(size.Columns, size.Rows, SynchronizationContext.Current!);
        var form = new MainForm(host, settings, store);
        if (handoff.Title.Length > 0) form.Text = $"{handoff.Title} — BlindTerm";

        form.Shown += (_, _) =>
        {
            try { host.Adopt(handoff); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                handoff.Dispose();
                MessageBox.Show(form, ex.Message, "BlindTerm could not take over this console",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                form.Close();
            }
        };

        Track(form, settings, store);
        form.Show();
    }

    private void Track(MainForm form, AppSettings settings, SettingsStore store)
    {
        StopWaiting();
        _everOpened = true;
        _open++;
        // A window cannot open another window, so the connection request comes back here.
        form.TelnetRequested += (host, port) => OpenTelnet(host, port, settings, store);
        form.FormClosed += (_, _) =>
        {
            if (--_open <= 0) ExitThread();
        };
    }

    private void StopWaiting()
    {
        _startupWait?.Stop();
        _startupWait?.Dispose();
        _startupWait = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopWaiting();
        base.Dispose(disposing);
    }
}
