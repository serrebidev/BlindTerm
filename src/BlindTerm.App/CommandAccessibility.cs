using BlindTerm.Core.Speech;

namespace BlindTerm.App;

/// <summary>Applies prompt-sensitive accessibility state to the native command edit.</summary>
internal static class CommandAccessibility
{
    /// <summary>
    /// What a hidden character shows as.
    ///
    /// Set through <see cref="TextBox.PasswordChar"/> rather than
    /// <see cref="TextBox.UseSystemPasswordChar"/> deliberately. The latter recreates the
    /// control's window handle, which destroys the focused window and creates another in its
    /// place; a screen reader hears that as a focus change and reads the whole field again --
    /// name, role, "protected", description and value -- in the middle of answering a login.
    /// This sets the mode on the existing handle, so nothing about the focus changes.
    /// </summary>
    private const char Hidden = '●';

    public static void Apply(TextBox command, string prompt)
    {
        ArgumentNullException.ThrowIfNull(command);
        bool secret = PromptNews.RequestsSecret(prompt);
        if (IsSecret(command) == secret) return;

        command.PasswordChar = secret ? Hidden : '\0';
        command.AccessibleName = secret ? "Password" : "Command line";
        command.AccessibleDescription = secret
            ? "Terminal password input. Typed characters are hidden."
            : null;
    }

    /// <summary>Whether the command edit is hiding what is typed into it.</summary>
    public static bool IsSecret(TextBox command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.PasswordChar != '\0';
    }
}
