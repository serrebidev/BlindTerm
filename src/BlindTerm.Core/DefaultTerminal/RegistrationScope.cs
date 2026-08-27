using System.Runtime.Versioning;

namespace BlindTerm.Core.DefaultTerminal;

/// <summary>
/// Where the default-terminal registration is written.
///
/// It exists so that tests can exercise the real code against throwaway registry keys and a
/// throwaway directory. Getting this wrong on a real machine means every console the user
/// opens goes somewhere unexpected, which is not a thing to find out from a test run.
/// </summary>
/// <param name="StartupKeyPath">
/// The key under HKEY_CURRENT_USER holding <c>DelegationConsole</c> and
/// <c>DelegationTerminal</c>. Windows reads exactly one: <c>Console\%%Startup</c>.
/// </param>
/// <param name="ClassesRootPath">
/// The per-user COM registration root, normally <c>Software\Classes</c>. <c>CLSID</c> and
/// <c>Interface</c> hang off it.
/// </param>
/// <param name="StagingDirectory">
/// Where the handoff marshalling library is kept. See <see cref="HandoffMarshaling"/> for
/// why it is not used from where Windows Terminal keeps it.
/// </param>
/// <param name="ProxySource">
/// The library to stage, or null to take Windows Terminal's. Only tests pass this.
/// </param>
[SupportedOSPlatform("windows")]
public sealed record RegistrationScope(
    string StartupKeyPath,
    string ClassesRootPath,
    string StagingDirectory,
    string? ProxySource = null)
{
    public static RegistrationScope Default { get; } = new(
        StartupKeyPath: @"Console\%%Startup",
        ClassesRootPath: @"Software\Classes",
        StagingDirectory: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlindTerm", "interop"));

    /// <summary>The registry path of a class, given its CLSID.</summary>
    public string ClassKey(Guid clsid) => $@"{ClassesRootPath}\CLSID\{Format(clsid)}";

    /// <summary>The registry path of an interface, given its IID.</summary>
    public string InterfaceKey(Guid iid) => $@"{ClassesRootPath}\Interface\{Format(iid)}";

    /// <summary>Registry spelling of a CLSID: braces, upper case, hyphens.</summary>
    public static string Format(Guid value) => value.ToString("B").ToUpperInvariant();
}
