using BlindTerm.App;

namespace BlindTerm.Tests;

/// <summary>
/// Recognising the ssh client on a typed command line.
///
/// This is the only place a client started at a prompt can be told apart from a program on this
/// machine: the session kind stays Shell either way. Getting it wrong in the permissive
/// direction hands a local program's arrow keys to the command line's own history; getting it
/// wrong in the other direction is the bug this was written for, where every arrow at an SSH
/// prompt was sent to the far end, which answered with its bell.
/// </summary>
public class SshCommandTests
{
    [Theory]
    [InlineData("ssh root@serrebiradio.com")]
    [InlineData("ssh serrebiradio.com")]
    [InlineData("ssh")]
    [InlineData("ssh.exe root@host")]
    [InlineData("SSH root@host")]
    [InlineData("  ssh   root@host")]
    [InlineData("ssh -p 2222 root@host")]
    [InlineData("ssh -i ~/.ssh/id_ed25519 root@host uptime")]
    [InlineData("ssh -o StrictHostKeyChecking=accept-new root@host")]
    public void ALineThatStartsTheClientIsAClient(string command)
        => Assert.True(SshCommand.IsSshLaunch(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sshd")]
    [InlineData("sshfs host:/srv /mnt")]
    [InlineData("ssh-keygen -t ed25519")]
    [InlineData("sshpass -p hunter2 ssh host")]
    [InlineData("scp file.txt host:/tmp")]
    [InlineData("cat ~/.ssh/config")]
    [InlineData("echo ssh host")]
    [InlineData("sshpass")]
    [InlineData("git clone ssh://host/repo.git")]
    [InlineData("man ssh")]
    public void NothingElseIs(string? command)
        => Assert.False(SshCommand.IsSshLaunch(command));

    /// <summary>
    /// A line the shell still has work to do on is the shell's, and what it eventually runs is
    /// not something BlindTerm can see from here.
    /// </summary>
    [Theory]
    [InlineData("ssh host | tee log")]
    [InlineData("ssh host; ls")]
    [InlineData("ssh host && ls")]
    [InlineData("ssh host > out.txt")]
    [InlineData("ssh host &")]
    public void ALineTheShellHasWorkToDoOnIsNotOurs(string command)
        => Assert.False(SshCommand.IsSshLaunch(command));
}
