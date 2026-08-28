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

        if (!embedding && SshArgument(args) is SshTarget ssh)
        {
            windows.OpenSsh(ssh, settings, settingsStore);
        }
        else if (!embedding && TelnetArgument(args) is TelnetTarget target)
        {
            windows.OpenTelnet(target, settings, settingsStore);
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
    /// The host in "--telnet host[:port]" or "--telnet host port", or null when this is an
    /// ordinary launch. Both spellings are accepted because both are what anyone types, and
    /// so is "--telnet ssl://host 4022" for a MUD's encrypted port.
    /// </summary>
    internal static TelnetTarget? TelnetArgument(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int at = Array.FindIndex(args, argument =>
            argument.Equals("--telnet", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length) return null;

        string written = args[at + 1];
        if (!TelnetAddress.TryParse(written, out string host, out int port, out bool secure)) return null;

        // A port given as its own argument wins: "--telnet host 4000" is unambiguous, and a
        // host that already carried one would not have left the default in place. A scheme is
        // not a port, so it is looked past before deciding whether one was written.
        int scheme = written.IndexOf("://", StringComparison.Ordinal);
        bool carriedPort = written[(scheme < 0 ? 0 : scheme + 3)..].Contains(':');
        if (at + 2 < args.Length && int.TryParse(args[at + 2], out int separate)
            && separate is >= 1 and <= 65535 && !carriedPort)
            port = separate;

        return new TelnetTarget(host, port, secure);
    }

    /// <summary>The destination in "--ssh user@host[:port]" or "--ssh user@host port".</summary>
    internal static SshTarget? SshArgument(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int at = Array.FindIndex(args, argument =>
            argument.Equals("--ssh", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length ||
            !SshTarget.TryParse(args[at + 1], out SshTarget? parsed) || parsed is null) return null;
        SshTarget target = parsed;

        if (at + 2 < args.Length && int.TryParse(args[at + 2], out int separate)
            && separate is >= 1 and <= 65_535)
            target = new SshTarget(target.Host, separate, target.Username);
        return target;
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

    /// <summary>Opens Windows OpenSSH inside BlindTerm's accessible terminal surface.</summary>
    public void OpenSsh(SshTarget target, AppSettings settings, SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(target);
        var host = new TerminalHost(settings.Columns, settings.Rows, SynchronizationContext.Current!);
        var form = new MainForm(host, settings, store)
        {
            Text = $"SSH {target.Address} — BlindTerm",
        };
        form.Shown += (_, _) =>
        {
            try
            {
                host.StartSsh(target);
                settings.RememberSshHost(target.Address);
                TrySave(settings, store);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or IOException or ArgumentException
                                       or InvalidOperationException)
            {
                MessageBox.Show(form,
                    "Could not start Windows OpenSSH." + Environment.NewLine
                    + Environment.NewLine + ex.Message,
                    "BlindTerm could not connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                form.Close();
            }
        };
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
    public async void OpenTelnet(TelnetTarget target, AppSettings settings, SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(target);
        var terminal = new TerminalHost(settings.Columns, settings.Rows, SynchronizationContext.Current!);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
            await terminal.ConnectAsync(target, timeout.Token);
        }
        catch (TelnetCertificateException certificate)
        {
            terminal.Dispose();
            // Asked rather than refused outright: a MUD on a certificate it signed itself is
            // ordinary, and the person dialling it is the one who can tell whether that is
            // expected here.
            if (!AcceptCertificate(null, certificate))
            {
                if (!_everOpened) ExitThread();
                return;
            }
            OpenTelnet(certificate.Anyway, settings, store);
            return;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException
                                   or IOException or ArgumentException
                                   or System.Security.Authentication.AuthenticationException)
        {
            terminal.Dispose();
            string reason = ex is OperationCanceledException
                ? $"{target.Host} did not answer within {ConnectTimeoutSeconds} seconds."
                : ex.Message;
            MessageBox.Show(
                $"Could not connect to {target.Address}."
                + Environment.NewLine + Environment.NewLine + reason,
                "BlindTerm could not connect", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // Nothing opened, and nothing else is going to: a launch that was only ever this
            // connection has no window to keep the process alive for.
            if (!_everOpened) ExitThread();
            return;
        }

        settings.RememberTelnetHost(target.Address);
        TrySave(settings, store);

        var form = new MainForm(terminal, settings, store)
        {
            Text = $"{target.Address} — BlindTerm",
        };
        // Written before reading starts, so it is the first line of the transcript rather than
        // something that landed in the middle of the login banner. It says which encryption was
        // actually negotiated, which is the only claim about it worth anything.
        string security = terminal.Security;
        if (security.Length > 0)
            form.Shown += (_, _) => terminal.AppendExternal([$"Connected to {target.Host} over {security}."]);
        form.Shown += (_, _) => terminal.Begin();
        Track(form, settings, store);
        form.Show();
    }

    /// <summary>
    /// Puts what is wrong with a certificate in front of somebody and asks whether to go on.
    ///
    /// No by default, and the whole of the objection is in the box rather than behind a
    /// "details" button, because a dialog that has to be explored before it can be answered
    /// is a dialog that gets answered without being read.
    /// </summary>
    internal static bool AcceptCertificate(IWin32Window? owner, TelnetCertificateException problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        string question = problem.Message + Environment.NewLine + Environment.NewLine
            + "Connect anyway? What you type is still encrypted, but BlindTerm cannot tell you "
            + "that the host at the other end is the one you meant.";
        DialogResult answer = owner is null
            ? MessageBox.Show(question, "Certificate could not be verified",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            : MessageBox.Show(owner, question, "Certificate could not be verified",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        return answer == DialogResult.Yes;
    }

    /// <summary>A remembered address is a convenience; failing to write one is not an error.</summary>
    private static void TrySave(AppSettings settings, SettingsStore store)
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentOutOfRangeException)
        { }
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
        form.TelnetRequested += target => OpenTelnet(target, settings, store);
        form.SshRequested += target => OpenSsh(target, settings, store);
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
