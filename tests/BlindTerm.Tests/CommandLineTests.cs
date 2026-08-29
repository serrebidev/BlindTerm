using BlindTerm.Core.Pty;

namespace BlindTerm.Tests;

public class CommandLineTests
{
    private const string Path = @"C:\Windows\system32;C:\Users\admin\AppData\Roaming\npm";
    private const string PathExt = ".COM;.EXE;.BAT;.CMD;.VBS;.PS1";
    private const string Npm = @"C:\Users\admin\AppData\Roaming\npm";

    /// <summary>A file system holding exactly the named files, compared as Windows does.</summary>
    private static Func<string, bool> Holding(params string[] files)
        => candidate => files.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    private static string Adapt(string commandLine, params string[] files)
        => CommandLine.ForCreateProcess(commandLine, Path, PathExt, Holding(files));

    [Fact]
    public void AnNpmShimIsRunThroughCmd()
    {
        // The case that closed the window: codex is installed, is on PATH, and has no .exe.
        Assert.Equal(
            $"cmd.exe /s /c \"\"{Npm}\\codex.cmd\" --no-alt-screen\"",
            Adapt("codex --no-alt-screen", $@"{Npm}\codex.cmd", $@"{Npm}\codex.ps1"));
    }

    [Fact]
    public void NamingTheShimAsAnExeStillFindsIt()
    {
        // "codex.exe" is what people type for a command they think of as a program.
        Assert.Equal(
            $"cmd.exe /s /c \"\"{Npm}\\codex.cmd\" -c tui.animations=false\"",
            Adapt("codex.exe -c tui.animations=false", $@"{Npm}\codex.cmd"));
    }

    [Fact]
    public void ARealExeIsLeftAlone()
    {
        // CreateProcess finds this one by itself; rewriting it would only obscure it.
        Assert.Equal("pwsh.exe -NoLogo", Adapt("pwsh.exe -NoLogo", @"C:\Windows\system32\pwsh.exe"));
    }

    [Fact]
    public void AnExeOnPathWinsOverAShimOfTheSameName()
    {
        // PATHEXT puts .EXE before .CMD, and so does this.
        Assert.Equal("codex", Adapt("codex", $@"{Npm}\codex.exe", $@"{Npm}\codex.cmd"));
    }

    [Fact]
    public void SomethingThatIsNotInstalledIsLeftForCreateProcessToRefuse()
    {
        // Unchanged, so the error names what was actually typed.
        Assert.Equal("nosuchtool --help", Adapt("nosuchtool --help"));
    }

    [Fact]
    public void AShimGivenByFullPathIsStillRunThroughCmd()
    {
        Assert.Equal(
            $"cmd.exe /s /c \"\"{Npm}\\codex.cmd\" run\"",
            Adapt($@"{Npm}\codex.cmd run"));
    }

    [Fact]
    public void AProgramGivenByFullPathIsLeftAlone()
    {
        Assert.Equal(@"C:\Windows\system32\cmd.exe /c dir", Adapt(@"C:\Windows\system32\cmd.exe /c dir"));
    }

    [Fact]
    public void AQuotedPathWithSpacesSurvives()
    {
        const string shim = @"C:\Program Files\tools\agent.cmd";
        Assert.Equal($"cmd.exe /s /c \"\"{shim}\" --go\"", Adapt($"\"{shim}\" --go"));
    }

    [Fact]
    public void LeadingSpaceIsKept()
    {
        Assert.Equal(
            $"  cmd.exe /s /c \"\"{Npm}\\codex.cmd\"\"",
            Adapt("  codex", $@"{Npm}\codex.cmd"));
    }

    [Fact]
    public void AnEmptyCommandLineIsReturnedUnchanged()
    {
        Assert.Equal(string.Empty, Adapt(string.Empty));
        Assert.Equal("   ", Adapt("   "));
    }

    [Fact]
    public void APs1ShimIsNotChosen()
    {
        // cmd.exe cannot run one, so picking it would only trade one failure for another.
        Assert.Equal("codex", Adapt("codex", $@"{Npm}\codex.ps1"));
    }

    [Fact]
    public void MissingPathOrPathExtDoesNotThrow()
    {
        Assert.Equal("codex", CommandLine.ForCreateProcess("codex", null, null, _ => false));
        Assert.Equal(
            $"cmd.exe /s /c \"\"{Npm}\\codex.cmd\"\"",
            CommandLine.ForCreateProcess("codex", Path, null, Holding($@"{Npm}\codex.cmd")));
    }

    [Theory]
    [InlineData("codex --help", "codex")]
    [InlineData("  pwsh.exe -NoLogo", "pwsh.exe")]
    [InlineData("\"C:\\Program Files\\a b\\x.exe\" -q", "C:\\Program Files\\a b\\x.exe")]
    [InlineData("", "")]
    public void ProgramNamesWhatWouldBeStarted(string commandLine, string expected)
        => Assert.Equal(expected, CommandLine.Program(commandLine));
}
