using BlindTerm.App;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class TelnetAddressTests
{
    [Theory]
    [InlineData("coremud.org:4000", "coremud.org", 4000)]
    [InlineData("  coremud.org:4000  ", "coremud.org", 4000)]
    [InlineData("coremud.org", "coremud.org", 23)]
    [InlineData("192.168.1.10:2323", "192.168.1.10", 2323)]
    [InlineData("[::1]:4000", "::1", 4000)]
    [InlineData("[::1]", "::1", 23)]
    public void AnAddressIsReadTheWayAMudPrintsIt(string text, string host, int port)
    {
        Assert.True(TelnetAddress.TryParse(text, out string parsedHost, out int parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Fact]
    public void AnUnbracketedIpv6AddressIsAHostAndNotAHostAndPort()
    {
        // Its own notation is full of colons, so the last one is not a port and guessing that
        // it is would connect to the wrong place entirely.
        Assert.True(TelnetAddress.TryParse("2001:db8::1", out string host, out int port));
        Assert.Equal("2001:db8::1", host);
        Assert.Equal(23, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(":4000")]
    [InlineData("coremud.org:")]
    [InlineData("coremud.org:0")]
    [InlineData("coremud.org:70000")]
    [InlineData("coremud.org:mud")]
    public void SomethingThatIsNotAnAddressIsRefused(string? text)
        => Assert.False(TelnetAddress.TryParse(text, out _, out _));

    [Theory]
    [InlineData("coremud.org", 4000, "coremud.org:4000")]
    [InlineData("coremud.org", 23, "coremud.org")]
    [InlineData("::1", 4000, "[::1]:4000")]
    public void RememberingAnAddressKeepsOnlyThePortWorthKeeping(string host, int port, string expected)
        => Assert.Equal(expected, TelnetAddress.Format(host, port));

    [Fact]
    public void FormattingAndParsingAreEachOthersOpposite()
    {
        string formatted = TelnetAddress.Format("coremud.org", 4000);

        Assert.True(TelnetAddress.TryParse(formatted, out string host, out int port));
        Assert.Equal("coremud.org", host);
        Assert.Equal(4000, port);
    }

    [Theory]
    [InlineData(new[] { "--telnet", "coremud.org:4000" }, "coremud.org", 4000)]
    [InlineData(new[] { "--telnet", "coremud.org", "4000" }, "coremud.org", 4000)]
    [InlineData(new[] { "--telnet", "coremud.org" }, "coremud.org", 23)]
    [InlineData(new[] { "--TELNET", "coremud.org", "4000" }, "coremud.org", 4000)]
    public void TheCommandLineAcceptsEitherSpelling(string[] args, string host, int port)
    {
        var parsed = Program.TelnetArgument(args);

        Assert.NotNull(parsed);
        Assert.Equal(host, parsed!.Host);
        Assert.Equal(port, parsed.Port);
    }

    [Fact]
    public void AnOrdinaryLaunchIsNotATelnetLaunch()
    {
        Assert.Null(Program.TelnetArgument([]));
        Assert.Null(Program.TelnetArgument(["pwsh.exe"]));
        Assert.Null(Program.TelnetArgument(["wsl.exe", "--", "bash"]));
        // The switch with nothing after it names no host, and must not be read as one.
        Assert.Null(Program.TelnetArgument(["--telnet"]));
    }

    [Fact]
    public void AnAddressThatAlreadyCarriesAPortKeepsIt()
    {
        // "--telnet coremud.org:4000 something" must not read the next word as a port.
        var parsed = Program.TelnetArgument(["--telnet", "coremud.org:4000", "9999"]);

        Assert.NotNull(parsed);
        Assert.Equal(4000, parsed!.Port);
    }
}
