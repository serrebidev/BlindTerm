using BlindTerm.Core.Net;

namespace BlindTerm.Tests;

public class MudRouteTests
{
    [Fact]
    public void ABurstRouteExpandsItsCounts()
    {
        Assert.True(MudRoute.TryParse(".3n2e", out MudRoute? route));
        Assert.NotNull(route);
        Assert.False(route.Careful);
        Assert.Equal(["n", "n", "n", "e", "e"], route.Steps);
        Assert.Equal("3 north, 2 east", route.Summary());
    }

    [Fact]
    public void TwoDotsMakeTheRouteCareful()
    {
        Assert.True(MudRoute.TryParse("..2se", out MudRoute? route));
        Assert.NotNull(route);
        Assert.True(route.Careful);
        Assert.Equal(["se", "se"], route.Steps);
        Assert.Equal("2 southeast", route.Summary());
    }

    [Fact]
    public void CapitalsAndDiagonalsReadTheSame()
    {
        Assert.True(MudRoute.TryParse(".3N2NE", out MudRoute? route));
        Assert.NotNull(route);
        Assert.Equal(["n", "n", "n", "ne", "ne"], route.Steps);
    }

    [Fact]
    public void UpAndDownAreStepsToo()
    {
        Assert.True(MudRoute.TryParse(".u2d", out MudRoute? route));
        Assert.NotNull(route);
        Assert.Equal(["u", "d", "d"], route.Steps);
    }

    [Theory]
    [InlineData("3n2e")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".3north")]
    [InlineData(".0n")]
    [InlineData(".n;n")]
    [InlineData(".3x")]
    [InlineData(". 3n")]
    [InlineData("look")]
    [InlineData("")]
    public void LinesThatAreNotRoutesAreNotRoutes(string line)
    {
        Assert.False(MudRoute.TryParse(line, out _));
    }

    [Fact]
    public void AWalkPastTheCapIsRefusedRatherThanFloodingTheMud()
    {
        Assert.False(MudRoute.TryParse(".1001n", out _));
        Assert.True(MudRoute.TryParse(".1000n", out MudRoute? route));
        Assert.NotNull(route);
        Assert.Equal(MudRoute.MaximumSteps, route.Steps.Count);
    }

    [Fact]
    public void TheCompassReadsEverySpellingOfAStep()
    {
        Assert.True(Compass.TryParse("North", out string north));
        Assert.Equal("n", north);
        Assert.True(Compass.TryParse("UP", out string up));
        Assert.Equal("u", up);
        Assert.False(Compass.TryParse("enter cave", out _));
        Assert.Equal("sw", Compass.Opposite("ne"));
        Assert.Equal("d", Compass.Opposite("u"));
        Assert.Equal("southeast", Compass.Name("se"));
    }
}
