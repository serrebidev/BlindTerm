using BlindTerm.App;

namespace BlindTerm.Tests;

public class TelnetCommandTests
{
    [Theory]
    [InlineData("telnet coremud.org 4000", "coremud.org", 4000)]
    [InlineData("telnet coremud.org:4000", "coremud.org", 4000)]
    [InlineData("telnet coremud.org", "coremud.org", 23)]
    [InlineData("  telnet   coremud.org   4000  ", "coremud.org", 4000)]
    [InlineData("TELNET CoreMUD.org 4000", "CoreMUD.org", 4000)]
    [InlineData("telnet.exe coremud.org 4000", "coremud.org", 4000)]
    [InlineData("telnet 100.103.9.33 4000", "100.103.9.33", 4000)]
    [InlineData("telnet [2001:db8::1]:4000", "2001:db8::1", 4000)]
    public void APlainDialIsRecognised(string command, string host, int port)
    {
        var parsed = TelnetCommand.Parse(command);
        Assert.NotNull(parsed);
        Assert.Equal(host, parsed!.Value.Host);
        Assert.Equal(port, parsed.Value.Port);
    }

    [Theory]
    [InlineData("telnet")]                              // its own interactive prompt
    [InlineData("telnet -a coremud.org")]               // switches BlindTerm does not implement
    [InlineData("telnet -l root coremud.org")]
    [InlineData("telnet /f log.txt coremud.org")]
    [InlineData("telnet coremud.org smtp")]             // a service name is telnet.exe's to resolve
    [InlineData("telnet coremud.org 4000 4022")]
    [InlineData("telnet coremud.org:4000 4022")]        // contradicts itself
    [InlineData("telnet coremud.org 0")]
    [InlineData("telnet coremud.org 70000")]
    public void AnythingElseTelnetExeUnderstandsIsLeftToIt(string command)
        => Assert.Null(TelnetCommand.Parse(command));

    [Theory]
    [InlineData("telnet coremud.org 4000 | Tee-Object log.txt")]
    [InlineData("telnet coremud.org 4000 > log.txt")]
    [InlineData("telnet coremud.org 4000; exit")]
    [InlineData("telnet coremud.org 4000 & echo done")]
    [InlineData("telnet \"coremud.org\" 4000")]
    [InlineData("echo telnet coremud.org 4000")]
    [InlineData("ssh host telnet coremud.org 4000")]
    [InlineData("C:\\Windows\\System32\\telnet.exe coremud.org 4000")]
    [InlineData("telnetd coremud.org")]
    [InlineData("")]
    [InlineData(null)]
    public void CommandsWhoseMeaningCouldChangeAreLeftAlone(string? command)
        => Assert.Null(TelnetCommand.Parse(command));
}
