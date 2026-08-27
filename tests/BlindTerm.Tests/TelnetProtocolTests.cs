using System.Text;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class TelnetProtocolTests
{
    private const byte Iac = 255, Dont = 254, Do = 253, Wont = 252, Will = 251, Sb = 250, Se = 240;
    private const byte OptEcho = 1, OptSga = 3, OptTerminalType = 24, OptNaws = 31;
    private const byte OptCompress2 = 86, OptGmcp = 201, OptMudSound = 90;

    private static (string Text, byte[] Reply) Feed(TelnetProtocol protocol, params byte[] received)
    {
        var text = new byte[Math.Max(1, received.Length)];
        var reply = new List<byte>();
        int written = protocol.Receive(received, text, reply);
        return (Encoding.UTF8.GetString(text, 0, written), [.. reply]);
    }

    private static TelnetProtocol New() => new("BLINDTERM");

    [Fact]
    public void PlainTextPassesThroughUntouched()
    {
        var (text, reply) = Feed(New(), Encoding.UTF8.GetBytes("By what name is your character known?"));

        Assert.Equal("By what name is your character known?", text);
        Assert.Empty(reply);
    }

    [Fact]
    public void ADoubledInterpretAsCommandIsALiteralByte()
    {
        var text = new byte[4];
        var reply = new List<byte>();
        int written = New().Receive([(byte)'a', Iac, Iac, (byte)'b'], text, reply);

        Assert.Equal(new byte[] { (byte)'a', 255, (byte)'b' }, text[..written]);
        Assert.Empty(reply);
    }

    [Fact]
    public void ACommandSplitAcrossTwoReadsIsStillOneCommand()
    {
        var protocol = New();

        var (firstText, firstReply) = Feed(protocol, (byte)'h', (byte)'i', Iac);
        var (secondText, secondReply) = Feed(protocol, Do, OptTerminalType);

        Assert.Equal("hi", firstText);
        Assert.Empty(firstReply);
        Assert.Equal(string.Empty, secondText);
        Assert.Equal(new byte[] { Iac, Will, OptTerminalType }, secondReply);
    }

    [Fact]
    public void ACarriageReturnPaddedWithNulLosesThePadding()
    {
        var (text, _) = Feed(New(), (byte)'a', (byte)'\r', 0, (byte)'b');

        Assert.Equal("a\rb", text);
    }

    [Fact]
    public void AGoAheadIsNotText()
    {
        // 249 is Go Ahead, which MUDs send to mark the end of a prompt. It carries nothing to
        // print, and printing its bytes would put rubbish in the middle of the prompt.
        var (text, reply) = Feed(New(), (byte)'>', (byte)' ', Iac, 249);

        Assert.Equal("> ", text);
        Assert.Empty(reply);
    }

    [Theory]
    [InlineData(OptTerminalType)]
    [InlineData(OptNaws)]
    [InlineData(OptSga)]
    public void TheOptionsThisTerminalPerformsAreAgreedTo(byte option)
    {
        var (_, reply) = Feed(New(), Iac, Do, option);

        Assert.Equal(new byte[] { Iac, Will, option }, reply);
    }

    [Theory]
    [InlineData(OptCompress2)]
    [InlineData(OptGmcp)]
    [InlineData((byte)34)]
    public void OptionsThatWouldPutSomethingOtherThanTextOnTheWireAreRefused(byte option)
    {
        var (_, reply) = Feed(New(), Iac, Do, option);

        Assert.Equal(new byte[] { Iac, Wont, option }, reply);
    }

    [Fact]
    public void CompressionOfferedByTheServerIsDeclined()
    {
        // Accepting would turn the rest of the stream into deflate, which nothing downstream
        // of this can read.
        var (_, reply) = Feed(New(), Iac, Will, OptCompress2);

        Assert.Equal(new byte[] { Iac, Dont, OptCompress2 }, reply);
    }

    [Fact]
    public void RemoteEchoIsAccepted()
    {
        // A password prompt works by the server turning echo off, so this end has to let it.
        var (_, reply) = Feed(New(), Iac, Will, OptEcho);

        Assert.Equal(new byte[] { Iac, Do, OptEcho }, reply);
    }

    [Fact]
    public void AnAgreementAlreadyReachedIsNotAnnouncedAgain()
    {
        var protocol = New();
        Feed(protocol, Iac, Do, OptTerminalType);

        var (_, reply) = Feed(protocol, Iac, Do, OptTerminalType);

        Assert.Empty(reply);
    }

    [Fact]
    public void SoundIsAcceptedSoTheMudWillSendItsTriggers()
    {
        var (_, reply) = Feed(New(), Iac, Will, OptMudSound);

        Assert.Equal(new byte[] { Iac, Do, OptMudSound }, reply);
    }

    [Fact]
    public void ATriggerSentOutOfBandIsHandedOverRatherThanDropped()
    {
        // Core MUD sends its triggers inside a subnegotiation, so a client that does not speak
        // the protocol never sees them. Dropping them with the rest of the telnet traffic is
        // how a MUD that plays sounds appears to play none.
        var protocol = New();
        byte[] received =
        [
            Iac, Sb, OptMudSound,
            .. Encoding.ASCII.GetBytes("!!SOUND(off U=https://coremud.org/sounds/)"),
            Iac, Se,
        ];

        var (text, _) = Feed(protocol, received);

        Assert.Equal(string.Empty, text);
        var requests = new List<string>();
        protocol.DrainMudSoundRequests(requests);
        string request = Assert.Single(requests);
        Assert.True(MspTrigger.TryParseLine(request, out MspTrigger? trigger));
        Assert.True(trigger!.IsOff);
        Assert.Equal("https://coremud.org/sounds/", trigger.Url);
    }

    [Fact]
    public void OutOfBandTriggersAreHandedOverOnlyOnce()
    {
        var protocol = New();
        Feed(protocol, [Iac, Sb, OptMudSound, .. Encoding.ASCII.GetBytes("!!SOUND(a.wav)"), Iac, Se]);

        var first = new List<string>();
        protocol.DrainMudSoundRequests(first);
        var second = new List<string>();
        protocol.DrainMudSoundRequests(second);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public void PlainTextGetsNoUnsolicitedTelnetCommands()
    {
        // Not every host on a port speaks telnet. A client that announces itself on connect
        // puts three commands in front of a mail or web server's first line, and that server
        // reads them as the start of the request.
        var (text, reply) = Feed(New(), Encoding.UTF8.GetBytes("220 mail.example.org ESMTP"));

        Assert.Equal("220 mail.example.org ESMTP", text);
        Assert.Empty(reply);
    }

    [Fact]
    public void TheTerminalTypeCycleEndsByDeclaringAScreenReader()
    {
        var protocol = New();
        byte[] send = [Iac, Sb, OptTerminalType, 1, Iac, Se];

        Assert.Equal("BLINDTERM", TerminalType(Feed(protocol, send).Reply));
        Assert.Equal("ANSI", TerminalType(Feed(protocol, send).Reply));
        // MTTS bit 64 is SCREEN READER. This is the whole reason for answering at all: a MUD
        // that honours it leaves out its maps and ASCII art without anyone finding a setting.
        Assert.Equal(TelnetProtocol.MttsAnswer, TerminalType(Feed(protocol, send).Reply));
        Assert.Equal(TelnetProtocol.MttsAnswer, TerminalType(Feed(protocol, send).Reply));
    }

    private static string TerminalType(byte[] reply)
    {
        Assert.Equal(new byte[] { Iac, Sb, OptTerminalType, 0 }, reply[..4]);
        Assert.Equal(new byte[] { Iac, Se }, reply[^2..]);
        return Encoding.ASCII.GetString(reply[4..^2]);
    }

    [Fact]
    public void AgreeingTheWindowSizeLeavesTheSizeItselfOwed()
    {
        var protocol = New();
        Assert.False(protocol.TakeWindowSizeRequest());

        Feed(protocol, Iac, Do, OptNaws);

        Assert.True(protocol.WindowSizeAgreed);
        Assert.True(protocol.TakeWindowSizeRequest());
        // Owed once, not forever.
        Assert.False(protocol.TakeWindowSizeRequest());
    }

    [Fact]
    public void BeingAskedTwiceForTheWindowSizeAnswersTwice()
    {
        var protocol = New();
        Feed(protocol, Iac, Do, OptNaws);
        Assert.True(protocol.TakeWindowSizeRequest());

        Feed(protocol, Iac, Do, OptNaws);

        Assert.True(protocol.TakeWindowSizeRequest());
    }

    [Fact]
    public void TheWindowSizeGoesOutAsTwoBigEndianNumbers()
    {
        var reply = new List<byte>();

        TelnetProtocol.AppendWindowSize(reply, 120, 30);

        Assert.Equal<byte[]>([Iac, Sb, OptNaws, 0, 120, 0, 30, Iac, Se], [.. reply]);
    }

    [Fact]
    public void AWidthOf255IsEscapedSoItIsNotReadAsACommand()
    {
        var reply = new List<byte>();

        TelnetProtocol.AppendWindowSize(reply, 255, 30);

        Assert.Equal<byte[]>([Iac, Sb, OptNaws, 0, 255, 255, 0, 30, Iac, Se], [.. reply]);
    }

    [Fact]
    public void ATypedByteOf255IsDoubledOnTheWay()
    {
        Assert.Equal(new byte[] { (byte)'a', 255, 255, (byte)'b' },
                     TelnetProtocol.Escape([(byte)'a', 255, (byte)'b']));
        Assert.Equal(Encoding.UTF8.GetBytes("look"), TelnetProtocol.Escape(Encoding.UTF8.GetBytes("look")));
    }

    [Fact]
    public void ASubnegotiationDoesNotSwallowTheTextAfterIt()
    {
        var protocol = New();
        byte[] received =
        [
            Iac, Sb, OptTerminalType, 1, Iac, Se,
            .. Encoding.UTF8.GetBytes("You are in a room."),
        ];

        var (text, _) = Feed(protocol, received);

        Assert.Equal("You are in a room.", text);
    }

    [Fact]
    public void AWholeMudLoginArrivesAsNothingButItsText()
    {
        var protocol = New();
        byte[] received =
        [
            Iac, Will, OptEcho,
            Iac, Do, OptTerminalType,
            Iac, Sb, OptTerminalType, 1, Iac, Se,
            Iac, Do, OptNaws,
            Iac, Will, OptCompress2,
            .. Encoding.UTF8.GetBytes("By what name is your character known? "),
            Iac, 249,
        ];

        var (text, reply) = Feed(protocol, received);

        Assert.Equal("By what name is your character known? ", text);
        Assert.Contains(Iac, reply);
        Assert.True(protocol.WindowSizeAgreed);
    }
}
