using BlindTerm.Core.Speech;

namespace BlindTerm.App;

/// <summary>Applies prompt-sensitive accessibility state to the native command edit.</summary>
internal static class CommandAccessibility
{
    public static void Apply(TextBox command, string prompt)
    {
        ArgumentNullException.ThrowIfNull(command);
        bool secret = PromptNews.RequestsSecret(prompt);
        if (command.UseSystemPasswordChar == secret) return;

        command.UseSystemPasswordChar = secret;
        command.AccessibleName = secret ? "Password" : "Command line";
        command.AccessibleDescription = secret
            ? "Terminal password input. Typed characters are hidden."
            : null;
    }
}
