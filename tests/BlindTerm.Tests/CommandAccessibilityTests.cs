using BlindTerm.App;

namespace BlindTerm.Tests;

public class CommandAccessibilityTests
{
    [Fact]
    public void PasswordPromptsProtectAndRelabelTheNativeEditControl()
    {
        using var command = new TextBox { AccessibleName = "Command line" };

        CommandAccessibility.Apply(command, "Password:");

        Assert.True(command.UseSystemPasswordChar);
        Assert.Equal("Password", command.AccessibleName);
        Assert.Contains("hidden", command.AccessibleDescription);
    }

    [Fact]
    public void OrdinaryPromptsRestoreTheNormalCommandControl()
    {
        using var command = new TextBox { AccessibleName = "Command line" };
        CommandAccessibility.Apply(command, "Password:");

        CommandAccessibility.Apply(command, "CORE>");

        Assert.False(command.UseSystemPasswordChar);
        Assert.Equal("Command line", command.AccessibleName);
        Assert.Null(command.AccessibleDescription);
    }
}
