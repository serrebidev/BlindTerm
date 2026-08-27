using BlindTerm.Core.DefaultTerminal;
using Microsoft.Win32;

namespace BlindTerm.Tests;

/// <summary>
/// Staging and registering the library that carries handles across the handoff call.
///
/// This is the part that has no visible symptom when it is wrong. Windows finds BlindTerm,
/// starts it, fails to marshal the one call it wanted to make, and gives the console back to
/// the console host -- so the terminal simply never appears and nothing is logged where
/// anyone would look. Each of these pins down one thing that would produce exactly that.
/// </summary>
public sealed class HandoffMarshalingTests : IDisposable
{
    // One root per test instance, deleted whole afterwards. Test classes run in parallel, so
    // a shared root would mean one class tearing down another's keys mid-run.
    private readonly string _root = $@"Software\BlindTermTests\{Guid.NewGuid():N}";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"blindterm-marshal-{Guid.NewGuid():N}");

    private readonly string _source;
    private readonly RegistrationScope _scope;

    public HandoffMarshalingTests()
    {
        _source = Path.Combine(_directory, "source", HandoffMarshaling.ProxyFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_source)!);
        File.WriteAllBytes(_source, [0x4D, 0x5A, 0x90, 0x00]);

        _scope = new RegistrationScope(
            StartupKeyPath: $@"{_root}\Startup",
            ClassesRootPath: $@"{_root}\Classes",
            StagingDirectory: Path.Combine(_directory, "interop"),
            ProxySource: _source);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void NothingIsRegisteredToStartWith()
    {
        Assert.False(HandoffMarshaling.IsRegistered(_scope));
    }

    [Fact]
    public void RegisteringStagesTheLibraryOutsideThePackage()
    {
        string staged = HandoffMarshaling.Register(_scope);

        Assert.True(File.Exists(staged));
        Assert.Equal(HandoffMarshaling.StagedProxyPath(_scope), staged);
        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(staged));

        // Not left where it came from: an app package refuses to load its own files into a
        // process outside the package, which is what makes the copy necessary at all.
        Assert.NotEqual(Path.GetFullPath(_source), Path.GetFullPath(staged));
    }

    [Fact]
    public void RegisteringPointsTheInterfaceAtTheProxyClass()
    {
        HandoffMarshaling.Register(_scope);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            $@"{_scope.InterfaceKey(HandoffMarshaling.TerminalHandoffInterface)}\ProxyStubClsid32")!;

        Assert.Equal(RegistrationScope.Format(HandoffMarshaling.OpenConsoleProxyStub), key.GetValue(null));
    }

    [Fact]
    public void RegisteringPointsTheProxyClassAtTheStagedFile()
    {
        string staged = HandoffMarshaling.Register(_scope);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(
            $@"{_scope.ClassKey(HandoffMarshaling.OpenConsoleProxyStub)}\InprocServer32")!;

        Assert.Equal(staged, key.GetValue(null));

        // Both: the handoff call arrives on whichever apartment COM has to hand, and a proxy
        // that declared a single apartment would be marshalled through another one.
        Assert.Equal("Both", key.GetValue("ThreadingModel"));
    }

    [Fact]
    public void RegisteringIsRecognisedAfterwards()
    {
        HandoffMarshaling.Register(_scope);

        Assert.True(HandoffMarshaling.IsRegistered(_scope));
    }

    [Fact]
    public void RegisteringTwiceIsHarmless()
    {
        string first = HandoffMarshaling.Register(_scope);
        string second = HandoffMarshaling.Register(_scope);

        Assert.Equal(first, second);
        Assert.True(HandoffMarshaling.IsRegistered(_scope));
    }

    [Fact]
    public void AChangedLibraryIsRestaged()
    {
        HandoffMarshaling.Register(_scope);
        File.WriteAllBytes(_source, [0x4D, 0x5A, 0x11, 0x22, 0x33]);

        string staged = HandoffMarshaling.Register(_scope);

        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(staged));
    }

    [Fact]
    public void AMissingLibraryIsRestaged()
    {
        string staged = HandoffMarshaling.Register(_scope);
        File.Delete(staged);
        Assert.False(HandoffMarshaling.IsRegistered(_scope));

        HandoffMarshaling.Register(_scope);

        Assert.True(HandoffMarshaling.IsRegistered(_scope));
    }

    [Fact]
    public void WithoutWindowsTerminalRegisteringSaysWhy()
    {
        var scope = _scope with { ProxySource = null, StagingDirectory = Path.Combine(_directory, "empty") };

        // On a machine that has Windows Terminal this finds it and succeeds, which is also a
        // correct outcome; the point is that it never silently registers nothing.
        if (HandoffMarshaling.FindWindowsTerminalProxy() is null)
        {
            NotSupportedException error = Assert.Throws<NotSupportedException>(() => HandoffMarshaling.Register(scope));
            Assert.Contains("Windows Terminal", error.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(File.Exists(HandoffMarshaling.Register(scope)));
        }
    }

    [Fact]
    public void UnregisteringRemovesBothHalvesAndTheFile()
    {
        string staged = HandoffMarshaling.Register(_scope);
        HandoffMarshaling.Unregister(_scope);

        Assert.False(HandoffMarshaling.IsRegistered(_scope));
        Assert.False(File.Exists(staged));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_scope.InterfaceKey(HandoffMarshaling.TerminalHandoffInterface)));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_scope.ClassKey(HandoffMarshaling.OpenConsoleProxyStub)));
    }

    [Fact]
    public void UnregisteringWhatWasNeverRegisteredIsNotAnError()
    {
        HandoffMarshaling.Unregister(_scope);
        HandoffMarshaling.Unregister(_scope);
    }

    [Fact]
    public void TheInterfaceAndProxyIdsMatchWindowsTerminals()
    {
        // Straight out of microsoft/terminal's Package.appxmanifest. If either of these drifts
        // the registration is for an interface nobody calls.
        Assert.Equal(Guid.Parse("6F23DA90-15C5-4203-9DB0-64E73F1B1B00"), HandoffMarshaling.TerminalHandoffInterface);
        Assert.Equal(Guid.Parse("3171DE52-6EFA-4AEF-8A9F-D02BD67E7A4F"), HandoffMarshaling.OpenConsoleProxyStub);
    }

    [Fact]
    public void TheDefaultScopeNamesTheKeysWindowsActuallyReads()
    {
        RegistrationScope scope = RegistrationScope.Default;

        Assert.Equal(@"Console\%%Startup", scope.StartupKeyPath);
        Assert.Equal(@"Software\Classes", scope.ClassesRootPath);
        Assert.EndsWith(@"BlindTerm\interop", scope.StagingDirectory, StringComparison.Ordinal);
        Assert.Null(scope.ProxySource);
    }

    [Fact]
    public void KeyPathsAreBuiltTheWayComExpects()
    {
        var scope = new RegistrationScope("Startup", @"Software\Classes", "staging");
        Guid id = Guid.Parse("A5CDF81F-9E27-4F5D-A63A-FBA21DCB8BDD");

        Assert.Equal(@"Software\Classes\CLSID\{A5CDF81F-9E27-4F5D-A63A-FBA21DCB8BDD}", scope.ClassKey(id));
        Assert.Equal(@"Software\Classes\Interface\{A5CDF81F-9E27-4F5D-A63A-FBA21DCB8BDD}", scope.InterfaceKey(id));
    }
}
