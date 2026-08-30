using BlindTerm.App;

namespace BlindTerm.Tests;

public class AgentChoicePromptTests
{
    [Fact]
    public void FindsTheNumberedCodexPermissionsPicker()
    {
        string[] transcript =
        [
            "  Update Model Permissions",
            "  1. Read Only",
            "› 2. Ask for approval (current)",
            "  3. Approve for me",
            "  4. Full Access",
        ];

        Assert.True(AgentChoicePrompt.IsVisible(
            "Press enter to confirm or esc to go back", transcript));
    }

    [Fact]
    public void FindsANumberedQuestionWithAQuestionAsItsLiveLine()
        => Assert.True(AgentChoicePrompt.IsVisible("Which approach should I use?",
            ["1. Keep the current format", "2. Convert everything"]));

    [Fact]
    public void FindsClaudeOptionsKeptInsideTheMultilineLivePrompt()
    {
        string live = """
            Permissions  Recently denied   Allow   Ask   Deny
            1. Add a new rule…
            2. Agent
            3. Bash
            Enter selection [1-3], or Escape to cancel:
            ←/→ to switch · ↓ to select · Esc to cancel
            """;

        Assert.True(AgentChoicePrompt.IsVisible(live, []));
    }

    [Fact]
    public void AnOrdinaryComposerAfterANumberedListStillAcceptsLeadingDigits()
        => Assert.False(AgentChoicePrompt.IsVisible("› Ask Codex to do anything",
            ["1. First observation", "2. Second observation"]));

    [Fact]
    public void OneNumberInConversationIsNotAChoicePicker()
        => Assert.False(AgentChoicePrompt.IsVisible("Choose:", ["1. The only line"]));
}
