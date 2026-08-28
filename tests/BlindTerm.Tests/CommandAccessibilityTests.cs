using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandAccessibilityTests
{
    [Fact]
    public void PasswordPromptsProtectAndRelabelTheNativeEditControl()
    {
        using var command = new TextBox { AccessibleName = "Command line" };

        CommandAccessibility.Apply(command, "Password:");

        Assert.True(CommandAccessibility.IsSecret(command));
        // Not UseSystemPasswordChar: setting that recreates the handle, and the focus change
        // that comes with it is read out over the top of the prompt being answered.
        Assert.False(command.UseSystemPasswordChar);
        Assert.Equal("Password", command.AccessibleName);
        Assert.Contains("hidden", command.AccessibleDescription);
    }

    [Fact]
    public void SwitchingModesKeepsTheSameWindowHandle()
    {
        // The whole reason this uses PasswordChar. Recreating the handle destroys the window
        // that has focus and makes another, which a screen reader hears as a focus change: it
        // reads the field's name, role, state and value again, over the top of the login
        // prompt the user is in the middle of answering.
        using var command = new TextBox();
        IntPtr handle = command.Handle;

        CommandAccessibility.Apply(command, "Password:");
        Assert.Equal(handle, command.Handle);

        CommandAccessibility.Apply(command, "CORE>");
        Assert.Equal(handle, command.Handle);
    }

    [Fact]
    public void OrdinaryPromptsRestoreTheNormalCommandControl()
    {
        using var command = new TextBox { AccessibleName = "Command line" };
        CommandAccessibility.Apply(command, "Password:");

        CommandAccessibility.Apply(command, "CORE>");

        Assert.False(CommandAccessibility.IsSecret(command));
        Assert.Equal("Command line", command.AccessibleName);
        Assert.Null(command.AccessibleDescription);
    }
}
