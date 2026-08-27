using BlindTerm.Core.Updates;

namespace BlindTerm.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.1.0", "1.0.9", true)]
    [InlineData("1.0.0", "v1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    public void ComparesReleaseVersions(string candidate, string current, bool expected)
        => Assert.Equal(expected, UpdateClient.IsNewer(candidate, current));

    [Fact]
    public void ManifestUsesReleaseAssetMetadata()
    {
        var manifest = new UpdateManifest
        {
            Version = "v1.0.1",
            Asset = "BlindTerm-v1.0.1.zip",
            DownloadUrl = "https://example.test/BlindTerm-v1.0.1.zip",
            Sha256 = new string('a', 64),
        };

        Assert.Equal("v1.0.1", manifest.Version);
        Assert.EndsWith(".zip", manifest.Asset);
        Assert.Equal(64, manifest.Sha256.Length);
    }

    [Fact]
    public void ReplacementPreservesSettingsAndInstallerMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "blindterm-update-test-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(root, "install");
        string source = Path.Combine(root, "source");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(install, "BlindTerm.App.exe"), "old");
            File.WriteAllText(Path.Combine(install, "settings.json"), "user settings");
            File.WriteAllText(Path.Combine(install, ".windows-installed"), "marker");
            File.WriteAllText(Path.Combine(source, "BlindTerm.App.exe"), "new");

            UpdateApplier.ReplaceContents(install, source, "BlindTerm.App.exe");

            Assert.Equal("new", File.ReadAllText(Path.Combine(install, "BlindTerm.App.exe")));
            Assert.Equal("user settings", File.ReadAllText(Path.Combine(install, "settings.json")));
            Assert.Equal("marker", File.ReadAllText(Path.Combine(install, ".windows-installed")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
