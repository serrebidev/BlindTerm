using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BlindTerm.Core.DefaultTerminal;

/// <summary>
/// Which terminal Windows opens when a command-line program needs one.
///
/// Windows 11 splits that job in two. A *delegation console* is the console API server --
/// the process that owns the console driver connection and turns API calls into a pseudo
/// console. A *delegation terminal* is the window that pseudo console is handed to. Both are
/// named by CLSID in <c>HKCU\Console\%%Startup</c>, and the inbox <c>conhost.exe</c> reads
/// them at the moment a console is created:
///
///  1. conhost calls <c>IConsoleHandoff::EstablishHandoff</c> on the delegation console.
///  2. That console calls <c>ITerminalHandoff3::EstablishPtyHandoff</c> on the delegation
///     terminal, which is where BlindTerm comes in.
///
/// BlindTerm implements step 2 only. Step 1 needs a console API server, and the one that
/// ships with Windows Terminal (<c>OpenConsole.exe</c>) is already installed, already signed
/// by Microsoft, and already registered -- so BlindTerm names it rather than redistributing
/// a console host of its own.
///
/// If any part of that chain is missing, conhost logs the failure and hosts the console
/// itself, exactly as it did before. A wrong or stale registration cannot stop command-line
/// programs from starting, which is what makes this safe to offer in a dialog at startup.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DefaultTerminalConfig
{
    /// <summary>BlindTerm's own delegation terminal class. Registered per-user, never machine-wide.</summary>
    public static readonly Guid BlindTermTerminal = new("A5CDF81F-9E27-4F5D-A63A-FBA21DCB8BDD");

    /// <summary>Windows Terminal's <c>OpenConsole.exe</c>, used as the console API server.</summary>
    public static readonly Guid WindowsTerminalConsole = new("2EACA947-7F5F-4CFA-BA87-8F7FBEEFBE69");

    /// <summary>Windows Terminal's own window, for recognising it in <see cref="Read"/>.</summary>
    public static readonly Guid WindowsTerminalTerminal = new("E12CFF52-A866-4C77-9A90-F570A7AA2C6B");

    /// <summary>The inbox console host. Either half set to this means "never hand off".</summary>
    public static readonly Guid Conhost = new("B23D10C0-E52E-411E-9D5B-C09FDF709C7D");

    private const string ConsoleValue = "DelegationConsole";
    private const string TerminalValue = "DelegationTerminal";

    /// <summary>What the two registry values currently name.</summary>
    public readonly record struct Selection(Guid Console, Guid Terminal)
    {
        /// <summary>Nothing chosen: Windows picks, which today means Windows Terminal.</summary>
        public bool IsWindowsDefault => Console == Guid.Empty || Terminal == Guid.Empty;

        /// <summary>The inbox console host, which is the way of saying "never hand off".</summary>
        public bool IsConhost => Console == Conhost || Terminal == Conhost;

        /// <summary>
        /// True only when a handoff would actually reach BlindTerm. conhost resolves the two
        /// values in that order -- either one empty means Windows decides, either one naming
        /// the inbox console host means no handoff at all -- and only then reads the pair as
        /// a custom choice, so the same order applies here.
        /// </summary>
        public bool IsBlindTerm => !IsWindowsDefault && !IsConhost && Terminal == BlindTermTerminal;
    }

    /// <summary>
    /// Whether this Windows can hand a console over at all. The mechanism arrived in Windows
    /// 11; on Windows 10 the registry values can be written but nothing reads them.
    /// </summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Which terminal is configured now. Unreadable or malformed values read as "Windows
    /// decides", which is what conhost itself does with them.
    /// </summary>
    public static Selection Read(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(scope.StartupKeyPath);
            if (key is null) return default;
            return new Selection(ReadGuid(key, ConsoleValue), ReadGuid(key, TerminalValue));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return default;
        }
    }

    private static Guid ReadGuid(RegistryKey key, string name)
        => key.GetValue(name) is string text && Guid.TryParse(text, out Guid value) ? value : Guid.Empty;

    /// <summary>
    /// Makes BlindTerm the terminal Windows hands new consoles to.
    ///
    /// The plumbing goes in before the choice does. Selecting a class that cannot be launched,
    /// or that Windows cannot make the call to once it has been, would mean every console
    /// falls back to conhost with a failed activation on the way -- working, but slower and
    /// for no reason anyone could see.
    /// </summary>
    /// <param name="executablePath">
    /// The BlindTerm executable COM should launch. Defaults to the running one.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// Windows Terminal is not installed, so there is no console API server to hand over from.
    /// </exception>
    public static void MakeDefault(string? executablePath = null, RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;

        RegisterComServer(executablePath, scope);
        HandoffMarshaling.Register(scope);

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(scope.StartupKeyPath, writable: true);
        key.SetValue(ConsoleValue, RegistrationScope.Format(WindowsTerminalConsole), RegistryValueKind.String);
        key.SetValue(TerminalValue, RegistrationScope.Format(BlindTermTerminal), RegistryValueKind.String);
    }

    /// <summary>
    /// Hands the choice back to Windows. The values are deleted rather than set to a specific
    /// terminal: an empty value is how conhost spells "let Windows decide", and naming a
    /// competitor's CLSID would be a worse guess than that.
    /// </summary>
    public static void ClearDefault(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(scope.StartupKeyPath, writable: true);
        if (key is null) return;
        key.DeleteValue(ConsoleValue, throwOnMissingValue: false);
        key.DeleteValue(TerminalValue, throwOnMissingValue: false);
    }

    /// <summary>
    /// Publishes BlindTerm as a COM local server for <see cref="BlindTermTerminal"/>, so that
    /// a console handed over while BlindTerm is closed can still start it.
    ///
    /// Under HKCU, so it needs no elevation and belongs to the one user who asked for it.
    /// </summary>
    public static void RegisterComServer(string? executablePath = null, RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        string path = executablePath ?? CurrentExecutable();
        string command = $"\"{path}\"";

        // Overwriting the command in place is not enough. COM remembers where a class was
        // last launched from, and goes on launching that -- so a user who installs over a
        // copy they were running from elsewhere, or who moves the folder, keeps getting the
        // old executable until they sign out, with the registry plainly saying otherwise.
        // Removing the class and putting it back is what makes the change take effect now.
        if (RegisteredCommand(scope) is string existing && existing != command)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(scope.ClassKey(BlindTermTerminal), throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Rewriting in place still leaves the registry correct for the next sign-in.
            }
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(scope.ClassKey(BlindTermTerminal), writable: true);
        key.SetValue(null, "BlindTerm", RegistryValueKind.String);

        using RegistryKey server = key.CreateSubKey("LocalServer32", writable: true);
        // Quoted: the install path has a space in it, and COM splits the command line on
        // whitespace before appending -Embedding, which is how BlindTerm knows to come up as
        // a handoff server with no shell of its own.
        server.SetValue(null, $"\"{path}\"", RegistryValueKind.String);
    }

    /// <summary>The command line currently registered for BlindTerm's class, or null if there is none.</summary>
    public static string? RegisteredCommand(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                $@"{scope.ClassKey(BlindTermTerminal)}\LocalServer32");
            return key?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Removes everything <see cref="MakeDefault"/> put in place except the choice itself.</summary>
    public static void UnregisterComServer(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(scope.ClassKey(BlindTermTerminal), throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Leaving a stale class behind is harmless; nothing selects it any more.
        }

        HandoffMarshaling.Unregister(scope);
    }

    /// <summary>
    /// Whether everything a handoff needs is in place: the choice, the class Windows starts,
    /// and the marshalling that lets it make the call. Any one of the three missing means
    /// consoles quietly keep opening somewhere else.
    /// </summary>
    public static bool IsFullyRegistered(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        if (!Read(scope).IsBlindTerm) return false;

        using RegistryKey? server = Registry.CurrentUser.OpenSubKey(
            $@"{scope.ClassKey(BlindTermTerminal)}\LocalServer32");
        if (server?.GetValue(null) is not string command || command.Length == 0) return false;

        return HandoffMarshaling.IsRegistered(scope);
    }

    /// <summary>
    /// The path recorded for COM activation. <see cref="Environment.ProcessPath"/> is the real
    /// executable even for a self-contained publish. AppContext is the safe fallback for a
    /// single-file process, where Assembly.Location is deliberately empty.
    /// </summary>
    public static string CurrentExecutable()
        => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BlindTerm.App.exe");

    /// <summary>Registry spelling of a CLSID: braces, upper case, hyphens.</summary>
    public static string Format(Guid value) => RegistrationScope.Format(value);

    /// <summary>A short description of the current choice, for a menu item or a status line.</summary>
    public static string Describe(Selection selection)
    {
        if (selection.IsWindowsDefault) return "whatever Windows chooses";
        if (selection.IsConhost) return "Windows Console Host";
        if (selection.IsBlindTerm) return "BlindTerm";
        if (selection.Terminal == WindowsTerminalTerminal) return "Windows Terminal";
        return "another terminal";
    }
}
