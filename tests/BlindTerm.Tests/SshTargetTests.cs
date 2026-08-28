using BlindTerm.App;
using BlindTerm.Core;
using BlindTerm.Core.Pty;

namespace BlindTerm.Tests;

public class SshTargetTests
{
    [Theory]
    [InlineData("test@serrebiradio.com", "test", "serrebiradio.com", 22)]
    [InlineData("test@serrebiradio.com:2222", "test", "serrebiradio.com", 2222)]
    [InlineData("ssh://test@serrebiradio.com:2200", "test", "serrebiradio.com", 2200)]
    [InlineData("[2001:db8::1]:2222", "", "[2001:db8::1]", 2222)]
    public void ParsesOrdinarySshDestinations(
        string written, string username, string host, int port)
    {
        Assert.True(SshTarget.TryParse(written, out SshTarget? target));
        Assert.NotNull(target);
        Assert.Equal(username, target.Username);
        Assert.Equal(host, target.Host);
        Assert.Equal(port, target.Port);
    }

    [Fact]
    public void BuildsAForcedTerminalOpenSshCommand()
    {
        var target = new SshTarget("serrebiradio.com", 2222, "test");

        Assert.Equal("ssh.exe -tt -p 2222 test@serrebiradio.com", target.CommandLine);
        Assert.Equal("test@serrebiradio.com:2222", target.Address);
    }

    [Theory]
    [InlineData("host -o ProxyCommand=bad")]
    [InlineData("-oBad")]
    [InlineData("host\"bad")]
    public void RejectsTextThatCouldBecomeAnotherSshArgument(string host)
        => Assert.Throws<ArgumentException>(() => new SshTarget(host));

    [Fact]
    public void AnSshPtyIsASeparateKindAndAlwaysOwnsItsInput()
    {
        using var session = new PtySession(TerminalSessionKind.Ssh, alwaysOwnsInput: true);

        Assert.Equal(TerminalSessionKind.Ssh, session.Kind);
        Assert.True(session.ProgramOwnsInput);
    }

    [Theory]
    [InlineData(new[] { "--ssh", "test@serrebiradio.com" }, "test", "serrebiradio.com", 22)]
    [InlineData(new[] { "--ssh", "test@serrebiradio.com", "2222" }, "test", "serrebiradio.com", 2222)]
    public void StartupAcceptsAnSshDestination(
        string[] arguments, string username, string host, int port)
    {
        SshTarget target = Assert.IsType<SshTarget>(Program.SshArgument(arguments));
        Assert.Equal(username, target.Username);
        Assert.Equal(host, target.Host);
        Assert.Equal(port, target.Port);
    }
}
