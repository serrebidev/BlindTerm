using System.Diagnostics;
using System.Runtime.Versioning;
using BlindTerm.Core.Pty;

namespace BlindTerm.Tests;

[SupportedOSPlatform("windows")]
public class ProcessTreeTests
{
    private static bool Eventually(Func<bool> condition, int seconds = 10)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASessionWithNoProcessYetIsNotRunningAnything(int processId)
        => Assert.False(ProcessTree.HasChild(processId));

    [Fact]
    public void AProgramTheShellStartedIsVisibleToTheShell()
    {
        using Process shell = Process.Start(new ProcessStartInfo(
            "cmd.exe", "/c ping -n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");

        try
        {
            Assert.True(Eventually(() => ProcessTree.HasChild(shell.Id)),
                "ping should be visible as a child of the cmd that started it.");
        }
        finally
        {
            try { shell.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            shell.WaitForExit(10_000);
        }
    }

    [Fact]
    public void AProcessThatIsGoneIsNotRunningAnything()
    {
        using Process shell = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");

        int processId = shell.Id;
        Assert.True(Eventually(() => ProcessTree.HasChild(processId)));

        shell.Kill(entireProcessTree: true);
        shell.WaitForExit(10_000);

        // Keep the process handle open through the assertion so Windows cannot recycle its
        // numeric id for an unrelated process with a child between these two snapshots.
        // The shell and everything under it are gone, so the terminal hands the keys back.
        Assert.True(Eventually(() => !ProcessTree.HasChild(processId)));
    }
}
