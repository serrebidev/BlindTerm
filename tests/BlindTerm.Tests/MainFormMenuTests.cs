using System.Reflection;
using BlindTerm.App;
using BlindTerm.Core;

namespace BlindTerm.Tests;

/// <summary>Keyboard ownership at the boundary between the menu and the terminal.</summary>
public class MainFormMenuTests
{
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
