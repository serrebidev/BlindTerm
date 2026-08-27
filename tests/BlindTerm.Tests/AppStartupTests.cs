using BlindTerm.App;
using BlindTerm.App.Defterm;
using BlindTerm.Core;

namespace BlindTerm.Tests;

/// <summary>
/// How BlindTerm decides what to be when it starts.
///
/// There are two ways in now. A user opens it, and it runs a shell. Or Windows opens it,
/// because a command-line program needed a terminal and BlindTerm is the default one -- and
/// then it must not run a shell at all, because the console it is about to be handed already
/// has a program in it.
/// </summary>
public class AppStartupTests
{
    [Theory]
    [InlineData("-Embedding")]
    [InlineData("/Embedding")]
    [InlineData("-embedding")]
    [InlineData("-EMBEDDING")]
    public void WindowsStartingUsForAConsoleIsRecognised(string argument)
    {
        // COM appends this to the registered command line, and has spelled it with either
        // prefix at various points in its life.
        Assert.True(Program.IsEmbedding([argument]));
    }

    [Fact]
    public void AnEmbeddingSwitchIsFoundWhereverItSits()
    {
        Assert.True(Program.IsEmbedding(["something", "-Embedding"]));
    }

    [Fact]
    public void AnOrdinaryCommandLineIsNotMistakenForOne()
    {
        // A false positive here is a window that never opens: BlindTerm would sit waiting for
        // a handoff that nobody is going to send, and the shell the user asked for never runs.
        Assert.False(Program.IsEmbedding([]));
        Assert.False(Program.IsEmbedding(["pwsh.exe"]));
        Assert.False(Program.IsEmbedding(["wsl.exe", "--distribution", "Ubuntu"]));
        Assert.False(Program.IsEmbedding(["cmd.exe", "/k", "echo -Embedding inside a word"]));
    }

    [Fact]
    public void AConfiguredShellIsUsedVerbatim()
    {
        Assert.Equal("wsl.exe -d Ubuntu", Program.ShellFor("wsl.exe -d Ubuntu"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoShellConfiguredSomethingRunnableIsChosen(string configured)
    {
        string shell = Program.ShellFor(configured);

        Assert.NotEqual(string.Empty, shell.Trim());

        // PowerShell 7 when it is installed, and Windows PowerShell with PSReadLine put back
        // when it is not -- never a bare powershell.exe, which disables PSReadLine as soon as
        // it notices a screen reader.
        if (!shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("Import-Module PSReadLine", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuestionIsNotAskedOnceItHasBeenAnswered()
    {
        Assert.False(DefaultTerminalPrompt.ShouldAsk(new AppSettings { AskAboutDefaultTerminal = false }));
    }

    [Fact]
    public void ANewInstallationHasNotBeenAskedYet()
    {
        // Whether it actually asks also depends on the Windows version and on BlindTerm not
        // already being the default, which are machine facts rather than preferences.
        Assert.True(new AppSettings().AskAboutDefaultTerminal);
    }
}
