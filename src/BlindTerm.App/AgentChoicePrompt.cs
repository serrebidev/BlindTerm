using System.Text.RegularExpressions;

namespace BlindTerm.App;

/// <summary>
/// Recognizes a coding-agent question whose visible answers are numbered.
///
/// A remembered agent process is not enough: its ordinary composer must still accept a prompt
/// beginning with a number. The numbered options and the live question/navigation hint together
/// are the boundary at which a bare digit becomes an immediate terminal answer.
/// </summary>
internal static partial class AgentChoicePrompt
{
    public static bool IsVisible(string liveText, IReadOnlyList<string> transcript)
    {
        string live = liveText.Trim();
        if (live.Length == 0 || !LooksLikeChoiceInstruction(live)) return false;

        int numbered = CountNumbered(live.Split('\n'));
        if (numbered >= 2) return true;

        for (int i = Math.Max(0, transcript.Count - 30); i < transcript.Count; i++)
        {
            if (NumberedOption().IsMatch(transcript[i]) && ++numbered >= 2) return true;
        }
        return false;
    }

    private static int CountNumbered(IEnumerable<string> lines)
        => lines.Count(line => NumberedOption().IsMatch(line));

    private static bool LooksLikeChoiceInstruction(string text)
        => ConfirmInstruction().IsMatch(text)
           || NavigationInstruction().IsMatch(text)
           || text.EndsWith('?')
           || text.EndsWith(':');

    [GeneratedRegex(@"^\s*(?:[>›]\s*)?\d{1,2}[.)]\s+\S",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberedOption();

    [GeneratedRegex(@"\b(?:press\s+)?enter\b.*\b(?:confirm|select|choose)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConfirmInstruction();

    [GeneratedRegex(@"\b(?:esc|escape)\b.*\b(?:back|cancel|close)\b|\b(?:up|down|arrow)s?\b.*\b(?:navigate|select|choose)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NavigationInstruction();
}
