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

        // A prompt that follows another on the same unfinished line -- "Password:" printed
        // after "By what name is your character known?" without a newline between them -- is
        // new only in its tail. Reading the whole line again makes every further question
        // repeat everything already asked and answered on it.
        string news = _lastAnnounced is { Length: > 0 } said
                      && prompt.StartsWith(said, StringComparison.Ordinal)
            ? prompt[said.Length..].Trim()
            : prompt;

        _lastAnnounced = prompt;
        return news.Length == 0 ? [] : [news];
    }

    /// <summary>Whether input for this prompt must be hidden from display and keyboard echo.</summary>
    public static bool RequestsSecret(string liveText) => SecretWord.IsMatch(liveText);

    /// <summary>
    /// Whether an unfinished line reads as a prompt waiting for an answer, rather than as a
    /// line still being printed. Independent of what has been announced: a prompt the user was
    /// told about a minute ago is still the prompt they are sitting at.
    /// </summary>
    public static bool IsPrompt(string liveText)
    {
        string prompt = liveText.Trim();
        return prompt.Length > 0 && LooksComplete(prompt);
    }

    private static bool LooksComplete(string prompt)
    {
        if (RequestsSecret(prompt) || prompt[^1] is '?' or ':' or '>' or ']') return true;
        if (prompt[^1] != ')') return false;

        // Bash scripts commonly put an answer hint after the question mark, which makes the
        // unfinished line end in a closing parenthesis: "Continue? (y/N)" or
        // "Continue? (default: no)". The punctuation before a nonempty hint distinguishes it
        // from ordinary progress such as "Downloading package (1/4)".
        int hintStart = prompt.LastIndexOf('(');
        if (hintStart <= 0 || prompt[(hintStart + 1)..^1].Trim().Length == 0) return false;

        string question = prompt[..hintStart].TrimEnd();
        return question.Length > 0 && question[^1] is '?' or ':';
    }
}
