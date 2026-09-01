using BlindTerm.Core.Pty;

namespace BlindTerm.Tests;

public class PtySessionProcessJobTests
{
    [Fact]
    public void CmdChildIsTrackedWithoutScanningTheSystemProcessTable()
    {
        using var session = new PtySession();
        session.Start("cmd.exe /d /q", 80, 25);

        Assert.True(session.UsesProcessJob);
        Assert.False(session.ProgramOwnsInput);

        // A sleeping PowerShell is a real child of cmd and stays alive long enough to observe
        // reliably. It produces no output, so this test measures ownership, not rendering.
        session.Write(
            "powershell.exe -NoLogo -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 2\"\r");
        Assert.True(SpinWait.SpinUntil(
            () => session.ProgramOwnsInput,
            TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(
            () => !session.ProgramOwnsInput,
            TimeSpan.FromSeconds(10)),
            $"The job still reported {session.ActiveJobProcesses?.ToString() ?? "no"} active processes.");

        session.Write("exit\r");
    }
}
