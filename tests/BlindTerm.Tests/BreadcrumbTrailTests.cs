using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class BreadcrumbTrailTests
{
    [Fact]
    public void TheWayBackIsTheTrailInReverseAndFlipped()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("n");
        trail.Record("east");

        Assert.Equal(["w", "s"], trail.Retrace());
    }

    [Fact]
    public void AStepBackCancelsTheStepOut()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("n");
        trail.Record("s");

        Assert.Equal(0, trail.Count);
        Assert.Empty(trail.Retrace());
    }

    [Fact]
    public void DetoursFoldHoweverDeepTheyWere()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("e");
        trail.Record("e");
        trail.Record("w");

        Assert.Equal(1, trail.Count);
        Assert.Equal(["w"], trail.Retrace());

        trail.Record("west");
        Assert.Equal(0, trail.Count);
    }

    [Fact]
    public void MovesThatAreNotCompassStepsAreNotInTheTrail()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("enter cave");
        trail.Record("climb");

        Assert.Equal(0, trail.Count);
    }

    [Fact]
    public void DroppingABreadcrumbStartsMeasuringFromThere()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("n");
        trail.Record("n");
        trail.DropBreadcrumb();

        Assert.Equal(0, trail.Count);
        trail.Record("e");
        Assert.Equal(["w"], trail.Retrace());
    }

    [Fact]
    public void RetracingLeavesTheTrailForTheCallerToClear()
    {
        var trail = new BreadcrumbTrail();
        trail.Record("n");

        Assert.Equal(["s"], trail.Retrace());
        Assert.Equal(1, trail.Count);

        trail.Clear();
        Assert.Equal(0, trail.Count);
    }
}
