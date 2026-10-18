using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MudWalkerTests
{
    private static MudRoute Route(string text)
    {
        Assert.True(MudRoute.TryParse(text, out MudRoute? route));
        return route!;
    }

    [Fact]
    public async Task ABurstSendsEveryStepWithoutWaiting()
    {
        var sent = new List<string>();
        bool waited = false;
        var walker = new MudWalker(
            send: step => { sent.Add(step); return Task.CompletedTask; },
            waitForArrival: _ => { waited = true; return Task.CompletedTask; },
            isBlocked: () => false);

        MudWalkResult result = await walker.WalkAsync(Route(".3n"));

        Assert.Equal(["n", "n", "n"], sent);
        Assert.False(waited);
        Assert.Equal(MudWalkOutcome.Arrived, result.Outcome);
        Assert.Equal(3, result.StepsSent);
        Assert.Equal(0, result.StepsAbandoned);
    }

    [Fact]
    public async Task ACarefulWalkWaitsBetweenSteps()
    {
        var order = new List<string>();
        var walker = new MudWalker(
            send: step => { order.Add($"send {step}"); return Task.CompletedTask; },
            waitForArrival: _ => { order.Add("wait"); return Task.CompletedTask; },
            isBlocked: () => false);

        MudWalkResult result = await walker.WalkAsync(Route("..nne"));

        // No wait after the last step: there is nothing left to arrive for.
        Assert.Equal(["send n", "wait", "send ne"], order);
        Assert.Equal(MudWalkOutcome.Arrived, result.Outcome);
    }

    [Fact]
    public async Task ARefusalAbandonsTheRestOfTheRoute()
    {
        var sent = new List<string>();
        var walker = new MudWalker(
            send: step => { sent.Add(step); return Task.CompletedTask; },
            waitForArrival: _ => Task.CompletedTask,
            isBlocked: () => true);

        MudWalkResult result = await walker.WalkAsync(Route("..3n"));

        Assert.Equal(["n"], sent);
        Assert.Equal(MudWalkOutcome.Blocked, result.Outcome);
        Assert.Equal(1, result.StepsSent);
        Assert.Equal(2, result.StepsAbandoned);
    }

    [Fact]
    public async Task StoppingMidWalkSaysWhereItGotTo()
    {
        using var stop = new CancellationTokenSource();
        var walker = new MudWalker(
            send: _ => Task.CompletedTask,
            waitForArrival: cancellationToken =>
            {
                stop.Cancel();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            },
            isBlocked: () => false);

        MudWalkResult result = await walker.WalkAsync(Route("..3n"), stop.Token);

        Assert.Equal(MudWalkOutcome.Stopped, result.Outcome);
        Assert.Equal(1, result.StepsSent);
        Assert.Equal(2, result.StepsAbandoned);
    }

    [Theory]
    [InlineData("You can't go that way.")]
    [InlineData("You can’t go that way.")]
    [InlineData("The door is closed.")]
    [InlineData("There is no exit in that direction.")]
    public void RefusalsAreRecognised(string line)
    {
        Assert.True(MudWalkBlock.IsBlockedLine(line));
    }

    [Theory]
    [InlineData("The door is open.")]
    [InlineData("Exits: north, south.")]
    [InlineData("A closed door stands here, carved with runes.")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryLinesAreNotRefusals(string? line)
    {
        Assert.False(MudWalkBlock.IsBlockedLine(line));
    }
}
