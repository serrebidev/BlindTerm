using BlindTerm.Core;
using BlindTerm.Core.Triggers;

namespace BlindTerm.Tests;

public class TriggerSettingsTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}", "settings.json");

    private static void InTempSettings(Action<SettingsStore, string> body)
    {
        string path = TempPath();
        try
        {
            body(new SettingsStore(), path);
        }
        finally
        {
            string? folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void TriggersSurviveBeingSavedAndLoaded()
        => InTempSettings((store, path) =>
        {
            var settings = new AppSettings();
            settings.Triggers.Add(new Trigger
            {
                Name = "Someone arrives",
                Pattern = "* arrives from *",
                Match = TriggerMatch.Wildcard,
                CaseSensitive = true,
                Where = TriggerWhere.Mud,
                Speak = "$1 from the $2",
                SpeakNow = true,
                Silence = true,
                Sound = @"C:\sounds\door.wav",
                Beep = true,
                Send = "look $1",
                StopProcessing = true,
                RepeatAfterMilliseconds = 2500,
                Enabled = false,
            });
            store.Save(settings, path);

            Trigger loaded = Assert.Single(store.Load(path).Triggers);
            Assert.Equal("Someone arrives", loaded.Name);
            Assert.Equal("* arrives from *", loaded.Pattern);
            Assert.Equal(TriggerMatch.Wildcard, loaded.Match);
            Assert.True(loaded.CaseSensitive);
            Assert.Equal(TriggerWhere.Mud, loaded.Where);
            Assert.Equal("$1 from the $2", loaded.Speak);
            Assert.True(loaded.SpeakNow);
            Assert.True(loaded.Silence);
            Assert.Equal(@"C:\sounds\door.wav", loaded.Sound);
            Assert.True(loaded.Beep);
            Assert.Equal("look $1", loaded.Send);
            Assert.True(loaded.StopProcessing);
            Assert.Equal(2500, loaded.RepeatAfterMilliseconds);
            Assert.False(loaded.Enabled);
        });

    /// <summary>
    /// The settings file is meant to be readable by whoever opens it, and a trigger written
    /// as "Match: 1" is not. The kind is stored by name.
    /// </summary>
    [Fact]
    public void TheFileNamesTheKindOfMatchRatherThanNumberingIt()
        => InTempSettings((store, path) =>
        {
            var settings = new AppSettings();
            settings.Triggers.Add(new Trigger
            {
                Pattern = "*dragon*",
                Match = TriggerMatch.Wildcard,
                Where = TriggerWhere.Mud,
                Beep = true,
            });
            store.Save(settings, path);

            string written = File.ReadAllText(path);
            Assert.Contains("\"Wildcard\"", written);
            Assert.Contains("\"Mud\"", written);
        });

    [Fact]
    public void TriggersAreOnUnlessTurnedOff()
        => InTempSettings((store, path) =>
        {
            Assert.True(new AppSettings().TriggersEnabled);

            var settings = new AppSettings { TriggersEnabled = false };
            store.Save(settings, path);
            Assert.False(store.Load(path).TriggersEnabled);
        });

    [Fact]
    public void ATriggerWithNothingToMatchIsDropped()
    {
        var settings = new AppSettings();
        settings.Triggers.Add(new Trigger { Pattern = "   ", Beep = true });
        settings.Triggers.Add(new Trigger { Pattern = "dragon", Beep = true });

        settings.Validate();

        Assert.Equal("dragon", Assert.Single(settings.Triggers).Pattern);
    }

    /// <summary>
    /// A file edited by hand, or written by a later version, is brought back inside the
    /// limits rather than thrown away. These are lines the user wrote.
    /// </summary>
    [Fact]
    public void AFieldOutOfRangeIsBroughtBackRatherThanLosingTheTrigger()
    {
        var settings = new AppSettings();
        settings.Triggers.Add(new Trigger
        {
            Pattern = "dragon",
            Speak = new string('x', Trigger.MaximumTextLength + 500),
            RepeatAfterMilliseconds = -20,
        });

        settings.Validate();

        Trigger kept = Assert.Single(settings.Triggers);
        Assert.Equal(Trigger.MaximumTextLength, kept.Speak.Length);
        Assert.Equal(0, kept.RepeatAfterMilliseconds);
    }

    [Fact]
    public void CopyingSettingsCopiesTheTriggersRatherThanSharingThem()
    {
        var settings = new AppSettings();
        settings.Triggers.Add(new Trigger { Pattern = "dragon", Beep = true });

        AppSettings copy = settings.Copy();
        copy.Triggers[0].Pattern = "troll";

        Assert.Equal("dragon", settings.Triggers[0].Pattern);
    }

    [Fact]
    public void ATriggerDescribesItselfInASentenceTheListCanReadOut()
    {
        string described = new Trigger
        {
            Name = "Low health",
            Pattern = "*hp: ?*",
            Match = TriggerMatch.Wildcard,
            Where = TriggerWhere.Mud,
            Speak = "health low",
            SpeakNow = true,
            Beep = true,
        }.Describe();

        Assert.Contains("Low health", described);
        Assert.Contains("On", described);
        Assert.Contains("Wildcard *hp: ?*", described);
        Assert.Contains("on a MUD only", described);
        Assert.Contains("Says something at once, beeps.", described);
    }

    [Fact]
    public void ATriggerWithNoActionSaysThatItDoesNothing()
    {
        var trigger = new Trigger { Pattern = "dragon" };
        Assert.False(trigger.DoesSomething);
        Assert.Contains("Does nothing", trigger.Describe());
    }

    [Fact]
    public void ATriggerWithNoNameIsCalledByItsPattern()
        => Assert.Equal("*dragon*", new Trigger { Pattern = "*dragon*" }.DisplayName);
}
