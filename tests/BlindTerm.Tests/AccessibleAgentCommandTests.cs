using BlindTerm.App;

namespace BlindTerm.Tests;

public class AccessibleAgentCommandTests
{
    [Theory]
    [InlineData("claude", "claude --ax-screen-reader")]
    [InlineData("claude --resume abc", "claude --ax-screen-reader --resume abc")]
    [InlineData("codex", "codex --no-alt-screen -c tui.raw_output_mode=true -c tui.animations=false")]
    [InlineData("codex resume --last",
        "codex --no-alt-screen -c tui.raw_output_mode=true -c tui.animations=false resume --last")]
    [InlineData("opencode", "opencode --mini --no-replay")]
    [InlineData("opencode -c", "opencode --mini --no-replay -c")]
    [InlineData("opencode .", "opencode --mini --no-replay .")]
    public void SimpleInteractiveLaunchesUseTheAccessibleInterface(string command, string expected)
        => Assert.Equal(expected, AccessibleAgentCommand.Adapt(command));

    [Theory]
    [InlineData("claude --ax-screen-reader")]
    [InlineData("codex --no-alt-screen -c tui.raw_output_mode=false -c tui.animations=true")]
    [InlineData("codex -c tui.alternate_screen=true -c tui.raw_output_mode=true -c tui.animations=false")]
    [InlineData("opencode --mini --replay")]
    public void ExplicitChoicesAreRespected(string command)
        => Assert.Equal(command, AccessibleAgentCommand.Adapt(command));

    [Theory]
    [InlineData("opencode run explain this")]
    [InlineData("opencode serve")]
    [InlineData("opencode --print-logs serve")]
    [InlineData("echo codex")]
    [InlineData("codex | Out-File result.txt")]
    [InlineData("& codex")]
    [InlineData("C:\\tools\\codex.exe")]
    public void CommandsWhoseMeaningCouldChangeAreLeftAlone(string command)
        => Assert.Equal(command, AccessibleAgentCommand.Adapt(command));

    [Theory]
    [InlineData("freebuff")]
    [InlineData("freebuff --continue")]
    public void FreebuffGetsNoInventedUnsupportedOption(string command)
        => Assert.Equal(command, AccessibleAgentCommand.Adapt(command));
}
