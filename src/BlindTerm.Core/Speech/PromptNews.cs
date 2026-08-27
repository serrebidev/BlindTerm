using System.Text.RegularExpressions;

namespace BlindTerm.Core.Speech;

/// <summary>
/// Finds complete prompts in the terminal's unfinished current line. Prompts deliberately do
/// not end in a newline, so transcript-line speech alone never announces them.
/// </summary>
public sealed class PromptNews
{
    private static readonly Regex SecretWord = new(
        @"\b(?:password|passphrase|passcode|pin)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private string? _lastAnnounced;

    /// <summary>Returns a new, complete prompt once, or no speech for partial ordinary text.</summary>
    public IReadOnlyList<string> News(string liveText)
    {
        string prompt = liveText.Trim();
        if (prompt.Length == 0)
        {
            _lastAnnounced = null;
            return [];
        }

        if (!LooksComplete(prompt) || prompt.Equals(_lastAnnounced, StringComparison.Ordinal))
            return [];

        _lastAnnounced = prompt;
        return [prompt];
    }

    /// <summary>Whether input for this prompt must be hidden from display and keyboard echo.</summary>
    public static bool RequestsSecret(string liveText) => SecretWord.IsMatch(liveText);

    private static bool LooksComplete(string prompt)
        => RequestsSecret(prompt) || prompt[^1] is '?' or ':' or '>' or ']';
}
