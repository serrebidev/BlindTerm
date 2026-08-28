using System.Text;
using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MsdpMessageTests
{
    private const byte Var = 1, Val = 2, TableOpen = 3, TableClose = 4,
        ArrayOpen = 5, ArrayClose = 6;

    private static byte[] Bytes(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (object part in parts)
        {
            if (part is byte value) bytes.Add(value);
            else bytes.AddRange(Encoding.UTF8.GetBytes((string)part));
        }
        return [.. bytes];
    }

    [Fact]
    public void SeveralScalarVariablesShareOneMessage()
    {
        Assert.True(MsdpMessage.TryParse(
            Bytes(Var, "HEALTH", Val, "75", Var, "HEALTH_MAX", Val, "100"),
            out MsdpMessage? message));

        Assert.Equal(2, message!.Variables.Count);
        Assert.Equal("HEALTH", message.Variables[0].Key);
        Assert.Equal("75", message.Variables[0].Value.Text);
        Assert.Equal("100", Assert.Single(message.Find("health_max")).Text);
    }

    [Fact]
    public void AnArrayKeepsItsOrderedValues()
    {
        Assert.True(MsdpMessage.TryParse(
            Bytes(Var, "REPORTABLE_VARIABLES", Val, ArrayOpen,
                  Val, "HEALTH", Val, "ROOM_NAME", Val, "ROOM_EXITS", ArrayClose),
            out MsdpMessage? message));

        MsdpValue value = Assert.Single(message!.Find("REPORTABLE_VARIABLES"));
        Assert.Equal(MsdpValueKind.Array, value.Kind);
        Assert.Equal(["HEALTH", "ROOM_NAME", "ROOM_EXITS"], value.ScalarValues());
    }

    [Fact]
    public void ARoomTableAndItsNestedExitsArePreserved()
    {
        Assert.True(MsdpMessage.TryParse(
            Bytes(Var, "ROOM", Val, TableOpen,
                  Var, "VNUM", Val, "6008",
                  Var, "NAME", Val, "The forest clearing",
                  Var, "EXITS", Val, TableOpen,
                      Var, "n", Val, "6011", Var, "e", Val, "6007",
                  TableClose, TableClose),
            out MsdpMessage? message));

        MsdpValue room = Assert.Single(message!.Find("ROOM"));
        Assert.True(room.TryGetField("name", out MsdpValue? name));
        Assert.Equal("The forest clearing", name.Text);
        Assert.True(room.TryGetField("EXITS", out MsdpValue? exits));
        Assert.Equal(["n", "e"], exits.Fields.Keys);
    }

    [Fact]
    public void RepeatedValuesBecomeAnArray()
    {
        Assert.True(MsdpMessage.TryParse(
            Bytes(Var, "REPORT", Val, "HEALTH", Val, "MANA"),
            out MsdpMessage? message));

        Assert.Equal(["HEALTH", "MANA"],
                     Assert.Single(message!.Find("REPORT")).ScalarValues());
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedDataIsIgnoredRatherThanEscapingIntoTheTranscript(byte[] payload)
        => Assert.False(MsdpMessage.TryParse(payload, out _));

    public static TheoryData<byte[]> Malformed => new()
    {
        Array.Empty<byte>(),
        Bytes(Var, "HEALTH"),
        Bytes(Var, "9INVALID", Val, "1"),
        Bytes(Var, "ROOM", Val, TableOpen, Var, "NAME", Val, "Lost"),
        Bytes(Var, "LIST", Val, "REPORT", ArrayClose),
        Bytes(Var, "HEALTH", Val, (byte)0),
    };

    [Fact]
    public void AnUnreasonablyLargePacketIsRejectedBeforeParsing()
        => Assert.False(MsdpMessage.TryParse(new byte[MsdpMessage.MaximumLength + 1], out _));
}
