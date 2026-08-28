using System.Reflection;
using BlindTerm.App;
using BlindTerm.Core;

namespace BlindTerm.Tests;

/// <summary>Keyboard ownership at the boundary between the menu and the terminal.</summary>
public class MainFormMenuTests
{
    [Fact]
    public void TheMenuBarHasFourTaskBasedMenusInOrder()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            MenuStrip menu = Assert.IsType<MenuStrip>(form.MainMenuStrip);

            Assert.Equal(["Terminal", "Edit", "Tools", "Help"],
                menu.Items.Cast<ToolStripMenuItem>().Select(item => Plain(item.Text)));
        });

    [Fact]
    public void ToolsContainsSettingsTriggersAndTheFormerReadMenu()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            MenuStrip menu = Assert.IsType<MenuStrip>(form.MainMenuStrip);
            ToolStripMenuItem tools = Top(menu, "Tools");

            Assert.Contains(tools.DropDownItems.Cast<ToolStripItem>(), item => Plain(item.Text) == "Settings...");
            Assert.Contains(tools.DropDownItems.Cast<ToolStripItem>(), item => Plain(item.Text) == "Triggers...");
            ToolStripMenuItem reading = Assert.IsType<ToolStripMenuItem>(
                tools.DropDownItems.Cast<ToolStripItem>().Single(item => Plain(item.Text) == "Reading"));
            Assert.Contains(reading.DropDownItems.Cast<ToolStripItem>(),
                item => Plain(item.Text).StartsWith("Read the screen", StringComparison.Ordinal));
            Assert.Contains(reading.DropDownItems.Cast<ToolStripItem>(),
                item => Plain(item.Text) == "Server information");
        });

    [Fact]
    public void HelpContainsUpdatesAndAbout()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            MenuStrip menu = Assert.IsType<MenuStrip>(form.MainMenuStrip);
            ToolStripMenuItem help = Top(menu, "Help");

            Assert.Contains(help.DropDownItems.Cast<ToolStripItem>(),
                item => Plain(item.Text) == "Check for updates...");
            Assert.Contains(help.DropDownItems.Cast<ToolStripItem>(),
                item => Plain(item.Text) == "About BlindTerm...");
            Assert.DoesNotContain(Top(menu, "Terminal").DropDownItems.Cast<ToolStripItem>(),
                item => Plain(item.Text) == "Check for updates...");
        });

    [Fact]
    public void AutomaticUpdatesUseTheConfiguredOneHourDefault()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            var timer = Assert.IsType<System.Windows.Forms.Timer>(typeof(MainForm)
                .GetField("_updateTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form));

            Assert.True(timer.Enabled);
            Assert.Equal(TimeSpan.FromHours(1).TotalMilliseconds, timer.Interval);
        });

    [Fact]
    public void TerminalOffersTelnetAndSshConnections()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            MenuStrip menu = Assert.IsType<MenuStrip>(form.MainMenuStrip);
            string[] commands = [.. Top(menu, "Terminal").DropDownItems.Cast<ToolStripItem>()
                .Select(item => Plain(item.Text))];

            Assert.Contains("Connect to a telnet host...", commands);
            Assert.Contains("Connect to an SSH host...", commands);
        });

    [Fact]
    public void AnActiveMenuOwnsTheDownArrowBeforePassThroughDoes()
        => OnAWindowThread(() =>
        {
            using var host = new TerminalHost(80, 25, new SynchronizationContext());
            using var form = new MainForm(host, new AppSettings(), new SettingsStore());
            MenuStrip menu = Assert.IsType<MenuStrip>(form.MainMenuStrip);

            // Arm the routing branch that would otherwise consume Down before the menu sees it.
            FieldInfo passThrough = typeof(MainForm).GetField("_passThroughNext",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            passThrough.SetValue(form, true);

            Raise(menu, "OnMenuActivate");
            InvokeProcessCmdKey(form, Keys.Down);
            Assert.True((bool)passThrough.GetValue(form)!);

            Raise(menu, "OnMenuDeactivate");
            InvokeProcessCmdKey(form, Keys.Down);
            Assert.False((bool)passThrough.GetValue(form)!);
        });

    private static void Raise(MenuStrip menu, string method)
        => typeof(MenuStrip).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, [EventArgs.Empty]);

    private static ToolStripMenuItem Top(MenuStrip menu, string name)
        => menu.Items.Cast<ToolStripMenuItem>().Single(item => Plain(item.Text) == name);

    private static string Plain(string text) => text.Replace("&", string.Empty);

    private static void InvokeProcessCmdKey(MainForm form, Keys key)
    {
        object message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [message, key]);
    }

    private static void OnAWindowThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
