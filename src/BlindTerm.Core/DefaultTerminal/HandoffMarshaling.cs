using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BlindTerm.Core.DefaultTerminal;

/// <summary>
/// Makes <c>ITerminalHandoff3</c> callable across the process boundary.
///
/// The interface passes pipe and process handles, which no automatic COM marshaller can
/// carry: each one has to be duplicated into the receiving process, and only generated
/// proxy/stub code knows to do that. Windows Terminal ships that code as
/// <c>OpenConsoleProxy.dll</c> and registers it inside its own app package, where the COM
/// runtime finds it for packaged processes and nowhere else. BlindTerm is not packaged, so
/// without this its side of the call cannot be marshalled: Windows abandons the handoff with
/// E_NOINTERFACE, and because a failed handoff simply leaves the console where it was, the
/// only symptom is that command-line programs go on opening in the old terminal.
///
/// So BlindTerm stages a copy of that library somewhere it can be loaded from and registers
/// it per-user. Nothing is redistributed: the file comes from the Windows Terminal the
/// machine already has, which BlindTerm depends on anyway for the console host that makes
/// the call. Loading it in place is not an option -- <c>WindowsApps</c> refuses to load its
/// contents into a process outside the package, and says E_ACCESSDENIED rather than
/// anything more helpful.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HandoffMarshaling
{
    /// <summary>The interface the console host calls BlindTerm through.</summary>
    public static readonly Guid TerminalHandoffInterface = new("6F23DA90-15C5-4203-9DB0-64E73F1B1B00");

    /// <summary>Windows Terminal's generated proxy/stub class, which knows how to carry handles.</summary>
    public static readonly Guid OpenConsoleProxyStub = new("3171DE52-6EFA-4AEF-8A9F-D02BD67E7A4F");

    public const string ProxyFileName = "OpenConsoleProxy.dll";

    /// <summary>
    /// Where Windows records the folder each installed app package was unpacked into. Readable
    /// without elevation, unlike the folder it names.
    /// </summary>
    private const string PackageRepository =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private const string WindowsTerminalPrefix = "Microsoft.WindowsTerminal_";

    public static string StagedProxyPath(RegistrationScope? scope = null)
        => Path.Combine((scope ?? RegistrationScope.Default).StagingDirectory, ProxyFileName);

    /// <summary>
    /// Where Windows Terminal keeps its copy, or null when there is no Windows Terminal to
    /// borrow from. Packages that do not carry the library -- the architecture-neutral bundle
    /// entry, for one -- are skipped rather than trusted, and the newest of what is left wins.
    /// </summary>
    public static string? FindWindowsTerminalProxy()
    {
        try
        {
            using RegistryKey? packages = Registry.CurrentUser.OpenSubKey(PackageRepository);
            if (packages is null) return null;

            string? best = null;
            string? bestName = null;

            foreach (string name in packages.GetSubKeyNames())
            {
                if (!name.StartsWith(WindowsTerminalPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                using RegistryKey? package = packages.OpenSubKey(name);
                if (package?.GetValue("PackageRootFolder") is not string root || root.Length == 0) continue;

                string candidate = Path.Combine(root, ProxyFileName);
                if (!File.Exists(candidate)) continue;

                // An ordinal comparison of package names, which is a tie-break rather than a
                // version comparison. Any recent copy will do: the interface it marshals has
                // not changed since it was introduced, and the older ones are usually the
                // leftovers of an update that has not been cleaned up yet.
                if (bestName is null || string.CompareOrdinal(name, bestName) > 0)
                {
                    best = candidate;
                    bestName = name;
                }
            }

            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Whether the proxy is staged and registered, so a handoff would actually arrive.</summary>
    public static bool IsRegistered(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        try
        {
            using RegistryKey? proxy = Registry.CurrentUser.OpenSubKey(
                $@"{scope.ClassKey(OpenConsoleProxyStub)}\InprocServer32");
            using RegistryKey? handoff = Registry.CurrentUser.OpenSubKey(
                $@"{scope.InterfaceKey(TerminalHandoffInterface)}\ProxyStubClsid32");

            if (proxy?.GetValue(null) is not string path) return false;
            if (handoff?.GetValue(null) is not string stub) return false;

            return File.Exists(path)
                && string.Equals(stub, RegistrationScope.Format(OpenConsoleProxyStub), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Stages the proxy and registers it for this user, returning where it was staged.</summary>
    /// <exception cref="NotSupportedException">
    /// There is no Windows Terminal on this machine to take the library from. That is the same
    /// condition that would stop the console host handing anything over, so it is reported
    /// rather than worked around.
    /// </exception>
    public static string Register(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;

        string source = scope.ProxySource ?? FindWindowsTerminalProxy()
            ?? throw new NotSupportedException(
                "Windows Terminal is not installed. BlindTerm uses its console host to receive " +
                "consoles from Windows, so it cannot be made the default terminal without it.");

        Directory.CreateDirectory(scope.StagingDirectory);
        string destination = StagedProxyPath(scope);
        CopyIfDifferent(source, destination);

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(scope.ClassKey(OpenConsoleProxyStub), writable: true))
        {
            key.SetValue(null, "OpenConsoleHandoffProxy", RegistryValueKind.String);
            using RegistryKey server = key.CreateSubKey("InprocServer32", writable: true);
            server.SetValue(null, destination, RegistryValueKind.String);

            // Both, because the call arrives on whichever apartment COM has to hand.
            server.SetValue("ThreadingModel", "Both", RegistryValueKind.String);
        }

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(scope.InterfaceKey(TerminalHandoffInterface), writable: true))
        {
            key.SetValue(null, "ITerminalHandoff3", RegistryValueKind.String);
            using RegistryKey stub = key.CreateSubKey("ProxyStubClsid32", writable: true);
            stub.SetValue(null, RegistrationScope.Format(OpenConsoleProxyStub), RegistryValueKind.String);
        }

        return destination;
    }

    /// <summary>Undoes <see cref="Register"/>. The staged file is left alone if it is in use.</summary>
    public static void Unregister(RegistrationScope? scope = null)
    {
        scope ??= RegistrationScope.Default;
        Delete(scope.InterfaceKey(TerminalHandoffInterface));
        Delete(scope.ClassKey(OpenConsoleProxyStub));

        try { File.Delete(StagedProxyPath(scope)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Still loaded by a terminal that is open. Harmless: nothing points at it now.
        }
    }

    private static void Delete(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Leaving the registration behind is harmless once the default terminal is not us.
        }
    }

    /// <summary>
    /// Copies unless the staged file already matches. It is loaded into every process that
    /// receives a console, so replacing it while one is open would fail -- and would be
    /// pointless, because the copy already there is the same library.
    /// </summary>
    private static void CopyIfDifferent(string source, string destination)
    {
        try
        {
            var from = new FileInfo(source);
            var to = new FileInfo(destination);
            if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc) return;

            File.Copy(source, destination, overwrite: true);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // In use by a terminal that is already running; the existing copy still works.
        }
    }
}
