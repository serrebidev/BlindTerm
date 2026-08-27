using BlindTerm.Core;

namespace BlindTerm.Tests;

public class AppSettingsTests
{
    [Fact]
    public void SavesAndLoadsSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var expected = new AppSettings { Shell = "pwsh.exe -NoLogo", Columns = 88, Rows = 22 };
            var store = new SettingsStore();
            store.Save(expected, path);

            AppSettings actual = store.Load(path);
            Assert.Equal(expected.Shell, actual.Shell);
            Assert.Equal(expected.Columns, actual.Columns);
            Assert.Equal(expected.Rows, actual.Rows);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InvalidSettingsFallBackToDefaults()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Columns\":0,\"Rows\":-1}");
            AppSettings actual = new SettingsStore().Load(path);
            Assert.Equal(120, actual.Columns);
            Assert.Equal(30, actual.Rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NullShellFallsBackToDefaults()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Shell\":null,\"Columns\":88,\"Rows\":22}");

            AppSettings actual = new SettingsStore().Load(path);

            Assert.Equal(string.Empty, actual.Shell);
            Assert.Equal(120, actual.Columns);
            Assert.Equal(30, actual.Rows);
        }
        finally { File.Delete(path); }
    }
}
