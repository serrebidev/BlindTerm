using System.Text;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class TelnetAccessibilityFilterTests
{
    private const string CoreOpening =
        "\x1b[0m\r\n" +
        "        o                             Welcome to\x1b[0m\r\n" +
        "      ..|..\x1b[0m\r\n" +
        "    ..  |  ..                          Core MUD\x1b[0m\r\n" +
        "   .o   |   o.         \"Company mining colony, Hermes-571G system\"\x1b[0m\r\n" +
        "  .   \\ | /   .      telnet coremud.org 4000 or 4022 for TLS\x1b[0m\r\n" +
        " o------o------o\x1b[0m\r\n" +
        "  .    /|\\    .      They came from all corners of the galaxy.\x1b[0m\r\n" +
        "       o            All help files online at https://coremud.org/wiki\x1b[0m\r\n" +
        "\r\n" +
        " Driver: fluffos, Mudlib: Colony 2.1\r\n" +
        "Type new if you are a new player. \r\n";

    [Fact]
    public void CoreMudOpeningBecomesReadableProseBeforeThePrompt()
    {
        var filter = new TelnetAccessibilityFilter("coremud.org", 4000);
        byte[] opening = Encoding.UTF8.GetBytes(CoreOpening);
        int split = opening.Length - 12;

        Assert.Empty(filter.Process(opening.AsSpan(0, split)));
        byte[] result = filter.Process(
            [.. opening.AsSpan(split).ToArray(), .. "By what name is your character known? "u8.ToArray()]);
        string text = Encoding.UTF8.GetString(result);

        Assert.Contains("Welcome to Core MUD\r\n", text);
        Assert.Contains("\"Company mining colony, Hermes-571G system\"", text);
        Assert.Contains("They came from all corners of the galaxy.", text);
        Assert.Contains("All help files online at https://coremud.org/wiki", text);
        Assert.Contains("Type new if you are a new player.\r\n", text);
        Assert.EndsWith("By what name is your character known? ", text);
        Assert.DoesNotContain("..|..", text);
        Assert.DoesNotContain("o------o", text);
        Assert.DoesNotContain("\x1b[", text);
    }

    [Fact]
    public void TrafficAfterTheOpeningPassesThroughByteForByte()
    {
        var filter = new TelnetAccessibilityFilter("coremud.org", 4000);
        filter.Process(Encoding.UTF8.GetBytes(CoreOpening));
        byte[] room = "Caf\u00e9 deck \u2014 north.\r\n"u8.ToArray();

        Assert.Equal(room, filter.Process(room));
    }

    [Fact]
    public void OtherHostsAreNeverBufferedOrChanged()
    {
        var filter = new TelnetAccessibilityFilter("mud.example", 4000);
        byte[] opening = "  /\\  Welcome\r\nPrompt: "u8.ToArray();

        Assert.False(filter.IsActive);
        Assert.Equal(opening, filter.Process(opening));
    }

    [Fact]
    public void AnUnrecognizedCoreOpeningIsReturnedOnDisconnect()
    {
        var filter = new TelnetAccessibilityFilter("coremud.org", 4000);
        byte[] changed = "A future Core MUD opening without the known marker"u8.ToArray();

        Assert.Empty(filter.Process(changed));
        Assert.Equal(changed, filter.Flush());
    }
}
