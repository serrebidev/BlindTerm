using BlindTerm.Core;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class AppSettingsAliasTests
{
    [Fact]
    public void AliasesWithNothingToTypeAreDropped()
    {
        var settings = new AppSettings();
        settings.Aliases.Add(new MudAlias { Pattern = " ", Replacement = "look" });
        settings.Aliases.Add(new MudAlias { Pattern = "gg", Replacement = "get gold" });

        settings.Validate();

        Assert.Single(settings.Aliases);
        Assert.Equal("gg", settings.Aliases[0].Pattern);
    }

    [Fact]
    public void OnePatternOnceKeepingTheFirst()
    {
        var settings = new AppSettings();
        settings.Aliases.Add(new MudAlias { Pattern = "GG", Replacement = "get gold" });
        settings.Aliases.Add(new MudAlias { Pattern = "gg", Replacement = "get gems" });

        settings.Validate();

        Assert.Single(settings.Aliases);
        Assert.Equal("get gold", settings.Aliases[0].Replacement);
    }

    [Fact]
    public void AliasesAreBroughtWithinTheirLengths()
    {
        var settings = new AppSettings();
        settings.Aliases.Add(new MudAlias
        {
            Pattern = new string('a', MudAlias.MaximumPatternLength + 50),
            Replacement = new string('b', MudAlias.MaximumReplacementLength + 50),
        });

        settings.Validate();

        Assert.Equal(MudAlias.MaximumPatternLength, settings.Aliases[0].Pattern.Length);
        Assert.Equal(MudAlias.MaximumReplacementLength, settings.Aliases[0].Replacement.Length);
    }

    [Fact]
    public void TheAliasListIsCapped()
    {
        var settings = new AppSettings();
        for (int i = 0; i < AppSettings.MaximumAliases + 5; i++)
            settings.Aliases.Add(new MudAlias { Pattern = $"a{i}", Replacement = "look" });

        settings.Validate();

        Assert.Equal(AppSettings.MaximumAliases, settings.Aliases.Count);
    }

    [Fact]
    public void CopyingSettingsCopiesTheAliasesSeparately()
    {
        var settings = new AppSettings();
        settings.Aliases.Add(new MudAlias { Pattern = "gg", Replacement = "get gold" });

        AppSettings copy = settings.Copy();
        copy.Aliases[0].Replacement = "get gems";

        Assert.Equal("get gold", settings.Aliases[0].Replacement);
    }

    [Fact]
    public void AliasesSurviveSavingAndLoading()
    {
        string path = Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var settings = new AppSettings();
            settings.Aliases.Add(new MudAlias { Pattern = "sh *", Replacement = "shoot $1" });
            var store = new SettingsStore();
            store.Save(settings, path);

            AppSettings loaded = store.Load(path);
            Assert.Single(loaded.Aliases);
            Assert.Equal("sh *", loaded.Aliases[0].Pattern);
            Assert.Equal("shoot $1", loaded.Aliases[0].Replacement);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
