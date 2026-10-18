using BlindTerm.Core;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class AppSettingsTimerTests
{
    [Fact]
    public void TimersWithNothingToCallThemOrSendAreDropped()
    {
        var settings = new AppSettings();
        settings.Timers.Add(new MudTimer { Name = " ", Seconds = 45, Command = "drink water" });
        settings.Timers.Add(new MudTimer { Name = "sip", Seconds = 45, Command = " " });
        settings.Timers.Add(new MudTimer { Name = "sip", Seconds = 45, Command = "drink water" });

        settings.Validate();

        Assert.Single(settings.Timers);
        Assert.Equal("sip", settings.Timers[0].Name);
    }

    [Fact]
    public void OneNameOnceKeepingTheFirst()
    {
        var settings = new AppSettings();
        settings.Timers.Add(new MudTimer { Name = "SIP", Seconds = 45, Command = "drink water" });
        settings.Timers.Add(new MudTimer { Name = "sip", Seconds = 30, Command = "drink tea" });

        settings.Validate();

        Assert.Single(settings.Timers);
        Assert.Equal("drink water", settings.Timers[0].Command);
    }

    [Fact]
    public void TimersAreBroughtWithinTheirLimits()
    {
        var settings = new AppSettings();
        settings.Timers.Add(new MudTimer
        {
            Name = new string('a', MudTimer.MaximumNameLength + 10),
            Seconds = 0,
            Command = "look",
        });
        settings.Timers.Add(new MudTimer { Name = "slow", Seconds = int.MaxValue, Command = "look" });

        settings.Validate();

        Assert.Equal(MudTimer.MaximumNameLength, settings.Timers[0].Name.Length);
        Assert.Equal(MudTimer.MinimumSeconds, settings.Timers[0].Seconds);
        Assert.Equal(MudTimer.MaximumSeconds, settings.Timers[1].Seconds);
    }

    [Fact]
    public void TheTimerListIsCapped()
    {
        var settings = new AppSettings();
        for (int i = 0; i < AppSettings.MaximumTimers + 5; i++)
            settings.Timers.Add(new MudTimer { Name = $"t{i}", Seconds = 60, Command = "look" });

        settings.Validate();

        Assert.Equal(AppSettings.MaximumTimers, settings.Timers.Count);
    }

    [Fact]
    public void CopyingSettingsCopiesTheTimersSeparately()
    {
        var settings = new AppSettings();
        settings.Timers.Add(new MudTimer { Name = "sip", Seconds = 45, Command = "drink water" });

        AppSettings copy = settings.Copy();
        copy.Timers[0].Command = "drink tea";

        Assert.Equal("drink water", settings.Timers[0].Command);
    }

    [Fact]
    public void TimersSurviveSavingAndLoading()
    {
        string path = Path.Combine(Path.GetTempPath(), $"blindterm-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var settings = new AppSettings();
            settings.Timers.Add(new MudTimer
            {
                Name = "greet",
                Seconds = 10,
                Command = "say hello",
                Repeat = false,
            });
            var store = new SettingsStore();
            store.Save(settings, path);

            AppSettings loaded = store.Load(path);
            MudTimer timer = Assert.Single(loaded.Timers);
            Assert.Equal("greet", timer.Name);
            Assert.Equal(10, timer.Seconds);
            Assert.Equal("say hello", timer.Command);
            Assert.False(timer.Repeat);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
