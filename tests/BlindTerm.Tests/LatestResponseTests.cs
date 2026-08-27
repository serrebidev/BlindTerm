using BlindTerm.App;
using BlindTerm.Core;
using Xunit;

namespace BlindTerm.Tests;

public class LatestResponseTests
{
    [Fact]
    public void ReturnsOnlyLinesAfterTheLatestCommand()
    {
        var transcript = new Transcript();
        transcript.Append("Welcome");
        transcript.Append("Password:");
        var response = new LatestResponse();

        response.Begin(transcript);
        transcript.Append("Apartment");
        transcript.Append("Exits: north");

        Assert.Equal(new[] { "Apartment", "Exits: north" }, response.Lines(transcript));
    }

    [Fact]
    public void StartingAnotherCommandDropsThePreviousResponse()
    {
        var transcript = new Transcript();
        var response = new LatestResponse();
        response.Begin(transcript);
        transcript.Append("Apartment");

        response.Begin(transcript);
        transcript.Append("Hallway");
        transcript.Append("Exits: east, west");

        Assert.Equal(new[] { "Hallway", "Exits: east, west" }, response.Lines(transcript));
    }

    [Fact]
    public void ReflectsAResponseLineThatWasRewritten()
    {
        var transcript = new Transcript();
        var response = new LatestResponse();
        response.Begin(transcript);
        transcript.Append("Loading");
        transcript.Revise(0, "Ready");

        Assert.Equal("Ready", response.Text(transcript));
    }
}
