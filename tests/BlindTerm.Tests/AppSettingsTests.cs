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
    public void AValueOutOfRangeIsBroughtInsideRatherThanLosingEverything()
    {
        string path = Path.GetTempFileName();
        try
        {
            // What a hand-edited file looks like. The dimensions are impossible, and there are
            // preferences in here that cannot be got back from the defaults -- which is why
            // this must not answer a bad number with a whole new set of settings.
            File.WriteAllText(path,
                "{\"Columns\":0,\"Rows\":-1,\"Shell\":\"pwsh.exe -NoLogo\",\"SoundVolume\":400}");
            AppSettings actual = new SettingsStore().Load(path);

            Assert.Equal(TerminalSize.MinimumColumns, actual.Columns);
            Assert.Equal(TerminalSize.MinimumRows, actual.Rows);
            Assert.Equal("pwsh.exe -NoLogo", actual.Shell);
            Assert.Equal(100, actual.SoundVolume);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AFileThatWillNotParseIsKeptRatherThanOverwritten()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{ this is not json");

            AppSettings actual = new SettingsStore().Load(path);

            Assert.Equal(120, actual.Columns);
            // Kept beside the original, because the next save writes over that and whatever was
            // in it -- every trigger its owner wrote, most likely -- is not reproducible.
            Assert.True(File.Exists(path + ".corrupt"));
            File.Delete(path + ".corrupt");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ColoursDefaultToFollowingWindows()
    {
        Assert.Equal(AppTheme.System, new AppSettings().Theme);
    }

    [Fact]
    public void SavesAndLoadsTheChosenColours()
    {
        string path = Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var store = new SettingsStore();
            store.Save(new AppSettings { Theme = AppTheme.Dark }, path);

            Assert.Equal(AppTheme.Dark, store.Load(path).Theme);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AnUnknownColourSchemeFallsBackToFollowingWindows()
    {
        string path = Path.GetTempFileName();
        try
        {
            // What a file edited by hand, or written by a later version offering more of
            // these, looks like to this one.
            File.WriteAllText(path, "{\"Theme\":99}");

            Assert.Equal(AppTheme.System, new SettingsStore().Load(path).Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CopyCarriesTheChosenColours()
    {
        // The settings dialog saves a Copy of what it was given, so a preference the copy
        // drops is a preference that is silently reset every time the dialog is used.
        Assert.Equal(AppTheme.Dark, new AppSettings { Theme = AppTheme.Dark }.Copy().Theme);
    }

    [Fact]
    public void AMissingWordBecomesAnEmptyOneWithoutLosingTheRest()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Shell\":null,\"Columns\":88,\"Rows\":22}");

            AppSettings actual = new SettingsStore().Load(path);

            Assert.Equal(string.Empty, actual.Shell);
            // A null where a word should be is one field's problem. Both of these used to be
            // reset with it, because the whole file was thrown away and rebuilt.
            Assert.Equal(88, actual.Columns);
            Assert.Equal(22, actual.Rows);
        }
        finally { File.Delete(path); }
    }
}
