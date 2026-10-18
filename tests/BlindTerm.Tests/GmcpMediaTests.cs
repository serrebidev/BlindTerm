using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class GmcpMediaTests
{
    private static GmcpMediaRequest? Parse(string text)
    {
        Assert.True(GmcpMessage.TryParse(text, out GmcpMessage? message));
        return GmcpMediaRequest.TryParse(message!, out GmcpMediaRequest? request)
            ? request
            : null;
    }

    [Fact]
    public void APlayAsksForWhatItNamesAtTheVolumeItNames()
    {
        GmcpMediaRequest? request = Parse(
            """Client.Media.Play {"name":"sword.wav","url":"https://mud.example/sounds/","type":"sound","volume":80,"loops":3,"priority":70}""");

        Assert.NotNull(request);
        Assert.Equal(GmcpMediaKind.Play, request.Kind);
        MspTrigger trigger = Assert.IsType<MspTrigger>(request.Trigger);
        Assert.Equal(MspKind.Sound, trigger.Kind);
        Assert.Equal("sword.wav", trigger.FileName);
        Assert.Equal("https://mud.example/sounds/", trigger.Url);
        Assert.Equal(80, trigger.Volume);
        Assert.Equal(3, trigger.Loops);
        Assert.Equal(70, trigger.Priority);
    }

    [Fact]
    public void APlayThatSaysLittleTakesTheProtocolDefaults()
    {
        GmcpMediaRequest? request = Parse(
            """Client.Media.Play {"name":"theme.ogg","type":"music"}""");

        MspTrigger trigger = Assert.IsType<MspTrigger>(request!.Trigger);
        Assert.Equal(MspKind.Music, trigger.Kind);
        Assert.Equal(GmcpMediaRequest.DefaultVolume, trigger.Volume);
        Assert.Equal(1, trigger.Loops);
        Assert.Equal(MspTrigger.DefaultPriority, trigger.Priority);
        Assert.True(trigger.Continue);
        Assert.Null(trigger.Url);
    }

    [Fact]
    public void APlaySetToLoopForeverLoopsForever()
    {
        GmcpMediaRequest? request = Parse(
            """Client.Media.Play {"name":"rain.wav","loops":-1}""");

        Assert.Equal(MspTrigger.Forever, Assert.IsType<MspTrigger>(request!.Trigger).Loops);
    }

    [Fact]
    public void NumbersOutsideTheirRangesAreBroughtInside()
    {
        GmcpMediaRequest? request = Parse(
            """Client.Media.Play {"name":"sword.wav","volume":250,"loops":0}""");

        MspTrigger trigger = Assert.IsType<MspTrigger>(request!.Trigger);
        Assert.Equal(100, trigger.Volume);
        Assert.Equal(1, trigger.Loops);
    }

    [Fact]
    public void ATagIsNotReadAsTheFolderASoundLivesIn()
    {
        // A tag groups sounds so a Stop can aim at the group. The trigger's Type is the
        // MUD Sound Protocol's subfolder; a tag carried into it would send the library
        // looking for the sound somewhere it was never put.
        GmcpMediaRequest? request = Parse(
            """Client.Media.Play {"name":"sword.wav","tag":"combat"}""");

        Assert.Null(Assert.IsType<MspTrigger>(request!.Trigger).Type);
    }

    [Fact]
    public void APlayWithNothingNamedAsksForNothing()
    {
        Assert.Null(Parse("""Client.Media.Play {"volume":80}"""));
        Assert.Null(Parse("Client.Media.Play"));
        Assert.Null(Parse("Client.Media.Play not json"));
    }

    [Fact]
    public void ALoadCarriesTheAddressAndPlaysNothing()
    {
        GmcpMediaRequest? request = Parse(
            """Client.Media.Load {"name":"sounds.zip","url":"https://mud.example/media/"}""");

        Assert.NotNull(request);
        Assert.Equal(GmcpMediaKind.Load, request.Kind);
        Assert.Equal("https://mud.example/media/", request.Url);
        Assert.Null(request.Trigger);
    }

    [Fact]
    public void AStopWithNoPayloadStopsEverything()
    {
        GmcpMediaRequest? request = Parse("Client.Media.Stop");

        Assert.NotNull(request);
        Assert.True(request.BothKinds);
        Assert.True(Assert.IsType<MspTrigger>(request.Trigger).IsOff);
    }

    [Fact]
    public void AStopCanAimAtOneKind()
    {
        GmcpMediaRequest? request = Parse("""Client.Media.Stop {"type":"music"}""");

        Assert.NotNull(request);
        Assert.False(request.BothKinds);
        Assert.Equal(MspKind.Music, Assert.IsType<MspTrigger>(request.Trigger).Kind);
    }

    [Fact]
    public void SomethingThatIsNotMediaIsNotReadAsMedia()
    {
        Assert.True(GmcpMessage.TryParse("""Room.Info {"name":"A room"}""",
                                         out GmcpMessage? message));
        Assert.False(GmcpMediaRequest.TryParse(message!, out _));

        Assert.True(GmcpMessage.TryParse("""Client.Media.Default {"url":"https://mud.example/"}""",
                                         out GmcpMessage? other));
        Assert.False(GmcpMediaRequest.TryParse(other!, out _));
    }
}
