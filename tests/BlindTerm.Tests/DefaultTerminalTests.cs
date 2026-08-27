using BlindTerm.Core;
using BlindTerm.Core.DefaultTerminal;
using Microsoft.Win32;

namespace BlindTerm.Tests;

/// <summary>
/// The default terminal choice is a handful of registry values Windows reads at the moment a
/// console is created. Nothing reports a mistake in them when it is made -- a wrong CLSID
/// just means the handoff fails later and the inbox console host keeps the session -- so
/// these tests check the exact spellings and the exact order of operations.
///
/// Everything is written under a throwaway key and a throwaway directory. The real ones
/// decide what happens the next time the machine opens a command prompt, which is not
/// something a test run should be able to change.
/// </summary>
public sealed class DefaultTerminalTests : IDisposable
{
    // One root per test instance, deleted whole afterwards. Test classes run in parallel, so
    // a shared root would mean one class tearing down another's keys mid-run.
    private readonly string _root = $@"Software\BlindTermTests\{Guid.NewGuid():N}";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"blindterm-defterm-{Guid.NewGuid():N}");

    private readonly RegistrationScope _scope;

    public DefaultTerminalTests()
    {
        Directory.CreateDirectory(_directory);

        // A stand-in for Windows Terminal's marshalling library, so these tests say the same
        // thing on a machine that does not have Windows Terminal installed.
        string proxy = Path.Combine(_directory, "source", HandoffMarshaling.ProxyFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
        File.WriteAllBytes(proxy, [0x4D, 0x5A]);

        _scope = new RegistrationScope(
            StartupKeyPath: $@"{_root}\Startup",
            ClassesRootPath: $@"{_root}\Classes",
            StagingDirectory: Path.Combine(_directory, "interop"),
            ProxySource: proxy);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    // ---- Reading the choice ----

    [Fact]
    public void UnsetChoiceReadsAsWindowsDeciding()
    {
        DefaultTerminalConfig.Selection selection = DefaultTerminalConfig.Read(_scope);

        Assert.True(selection.IsWindowsDefault);
        Assert.False(selection.IsBlindTerm);
    }

    [Fact]
    public void MalformedValuesReadAsWindowsDeciding()
    {
        Write("not a clsid", "also not a clsid");

        Assert.True(DefaultTerminalConfig.Read(_scope).IsWindowsDefault);
    }

    [Fact]
    public void HalfAChoiceIsNoChoice()
    {
        // conhost treats either value being empty as "let Windows decide", so a terminal
        // written without a console must not read back as selected.
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(_scope.StartupKeyPath))
            key.SetValue("DelegationTerminal", RegistrationScope.Format(DefaultTerminalConfig.BlindTermTerminal));

        DefaultTerminalConfig.Selection selection = DefaultTerminalConfig.Read(_scope);
        Assert.True(selection.IsWindowsDefault);
        Assert.False(selection.IsBlindTerm);
    }

    [Fact]
    public void ConhostOnEitherSideMeansNoHandoff()
    {
        Write(RegistrationScope.Format(DefaultTerminalConfig.Conhost),
              RegistrationScope.Format(DefaultTerminalConfig.BlindTermTerminal));

        DefaultTerminalConfig.Selection selection = DefaultTerminalConfig.Read(_scope);
        Assert.True(selection.IsConhost);
        Assert.False(selection.IsBlindTerm);
        Assert.Equal("Windows Console Host", DefaultTerminalConfig.Describe(selection));
    }

    [Fact]
    public void WindowsTerminalIsRecognisedRatherThanCalledUnknown()
    {
        var selection = new DefaultTerminalConfig.Selection(
            DefaultTerminalConfig.WindowsTerminalConsole, DefaultTerminalConfig.WindowsTerminalTerminal);

        Assert.Equal("Windows Terminal", DefaultTerminalConfig.Describe(selection));
    }

    [Fact]
    public void SomeoneElsesTerminalIsDescribedHonestly()
    {
        var selection = new DefaultTerminalConfig.Selection(
            DefaultTerminalConfig.WindowsTerminalConsole, Guid.Parse("11111111-2222-3333-4444-555555555555"));

        Assert.Equal("another terminal", DefaultTerminalConfig.Describe(selection));
    }

    // ---- Making the choice ----

    [Fact]
    public void MakeDefaultSelectsBlindTermAndAConsoleThatCanHandOff()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);

        DefaultTerminalConfig.Selection selection = DefaultTerminalConfig.Read(_scope);
        Assert.True(selection.IsBlindTerm);

        // The console half has to name a real console API server, because that is the process
        // that makes the call BlindTerm answers. Naming BlindTerm on both sides would mean
        // nothing ever hands anything over.
        Assert.Equal(DefaultTerminalConfig.WindowsTerminalConsole, selection.Console);
        Assert.False(selection.IsWindowsDefault);
        Assert.False(selection.IsConhost);
    }

    [Fact]
    public void ValuesAreWrittenInTheFormConhostParses()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(_scope.StartupKeyPath)!;

        foreach (string name in new[] { "DelegationConsole", "DelegationTerminal" })
        {
            var value = (string)key.GetValue(name)!;

            // conhost reads these into a fixed 39-character buffer: braces, hyphens, upper
            // case, no trailing spaces, and REG_SZ rather than REG_EXPAND_SZ.
            Assert.Equal(RegistryValueKind.String, key.GetValueKind(name));
            Assert.Equal(38, value.Length);
            Assert.StartsWith("{", value, StringComparison.Ordinal);
            Assert.EndsWith("}", value, StringComparison.Ordinal);
            Assert.Equal(value.ToUpperInvariant(), value);
            Assert.True(Guid.TryParse(value, out _));
        }
    }

    [Fact]
    public void MakeDefaultLeavesEverythingAHandoffNeeds()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);

        Assert.True(DefaultTerminalConfig.IsFullyRegistered(_scope));
    }

    [Fact]
    public void ChoosingTwiceIsTheSameAsChoosingOnce()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);

        Assert.True(DefaultTerminalConfig.IsFullyRegistered(_scope));
    }

    // ---- The COM registration ----

    [Fact]
    public void RegisteringPublishesALocalServerCommandLine()
    {
        DefaultTerminalConfig.RegisterComServer(@"C:\Program Files\BlindTerm\BlindTerm.App.exe", _scope);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            $@"{_scope.ClassKey(DefaultTerminalConfig.BlindTermTerminal)}\LocalServer32")!;

        // Quoted, because the install path has a space in it and COM splits on whitespace
        // before appending -Embedding.
        Assert.Equal("\"C:\\Program Files\\BlindTerm\\BlindTerm.App.exe\"", key.GetValue(null));
    }

    [Fact]
    public void ARegisteredClassHasAReadableName()
    {
        DefaultTerminalConfig.RegisterComServer(@"C:\BlindTerm\BlindTerm.App.exe", _scope);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            _scope.ClassKey(DefaultTerminalConfig.BlindTermTerminal))!;

        Assert.Equal("BlindTerm", key.GetValue(null));
    }

    [Fact]
    public void UnregisteringRemovesTheClass()
    {
        DefaultTerminalConfig.RegisterComServer(@"C:\BlindTerm\BlindTerm.App.exe", _scope);
        DefaultTerminalConfig.UnregisterComServer(_scope);

        Assert.Null(Registry.CurrentUser.OpenSubKey(_scope.ClassKey(DefaultTerminalConfig.BlindTermTerminal)));
    }

    // ---- Handing the choice back ----

    [Fact]
    public void ClearingHandsTheChoiceBackToWindows()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);
        DefaultTerminalConfig.ClearDefault(_scope);

        Assert.True(DefaultTerminalConfig.Read(_scope).IsWindowsDefault);

        // Deleted, not blanked, and not pointed at some other terminal: an absent value is how
        // "let Windows decide" is spelled.
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(_scope.StartupKeyPath)!;
        Assert.Null(key.GetValue("DelegationConsole"));
        Assert.Null(key.GetValue("DelegationTerminal"));
    }

    [Fact]
    public void ClearingTwiceIsNotAnError()
    {
        DefaultTerminalConfig.ClearDefault(_scope);
        DefaultTerminalConfig.ClearDefault(_scope);
    }

    [Fact]
    public void RevertingUndoesEveryPart()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);
        DefaultTerminalConfig.ClearDefault(_scope);
        DefaultTerminalConfig.UnregisterComServer(_scope);

        Assert.False(DefaultTerminalConfig.IsFullyRegistered(_scope));
        Assert.False(HandoffMarshaling.IsRegistered(_scope));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_scope.ClassKey(DefaultTerminalConfig.BlindTermTerminal)));
    }

    // ---- Half-finished states ----

    [Fact]
    public void ChoosingWithoutRegisteringTheClassIsNotFullyRegistered()
    {
        Write(RegistrationScope.Format(DefaultTerminalConfig.WindowsTerminalConsole),
              RegistrationScope.Format(DefaultTerminalConfig.BlindTermTerminal));

        Assert.True(DefaultTerminalConfig.Read(_scope).IsBlindTerm);
        Assert.False(DefaultTerminalConfig.IsFullyRegistered(_scope));
    }

    [Fact]
    public void LosingTheMarshallingLibraryIsNotFullyRegistered()
    {
        DefaultTerminalConfig.MakeDefault(@"C:\BlindTerm\BlindTerm.App.exe", _scope);
        File.Delete(HandoffMarshaling.StagedProxyPath(_scope));

        // A staged file that has been cleaned away leaves the registry looking correct and the
        // handoff failing, so the check has to reach the file system as well.
        Assert.False(DefaultTerminalConfig.IsFullyRegistered(_scope));
    }

    // ---- Settings ----

    [Fact]
    public void AskingIsRememberedAcrossRestarts()
    {
        string path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore();

        Assert.True(new AppSettings().AskAboutDefaultTerminal);

        store.Save(new AppSettings { AskAboutDefaultTerminal = false }, path);
        Assert.False(store.Load(path).AskAboutDefaultTerminal);
    }

    [Fact]
    public void EditingOtherSettingsDoesNotReviveTheQuestion()
    {
        var settings = new AppSettings { AskAboutDefaultTerminal = false, Columns = 100 };

        AppSettings copy = settings.Copy();
        copy.Columns = 80;

        Assert.False(copy.AskAboutDefaultTerminal);
        Assert.Equal(80, copy.Columns);
    }

    private void Write(string console, string terminal)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_scope.StartupKeyPath);
        key.SetValue("DelegationConsole", console);
        key.SetValue("DelegationTerminal", terminal);
    }
}
