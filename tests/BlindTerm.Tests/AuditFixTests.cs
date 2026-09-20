using BlindTerm.Core.Mud;
using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;
using BlindTerm.Core.Updates;
using Xunit;

namespace BlindTerm.Tests;

/// <summary>
/// The bugs found in a pass over the whole program, one test each.
///
/// Kept together because they have nothing in common but that: each is a small, specific
/// thing that was wrong, in a different corner, and each would go wrong again quietly if the
/// reasoning behind the fix were lost. Every one of them failed before the fix.
/// </summary>
public class AuditFixTests
{
    // ---- Names two directories are joined on ----

    [Theory]
    // An article is a word. Stripping the letters "the" off the front of a name that merely
    // begins with them sets two unrelated games up to share each other's player counts, which
    // is the one thing this join is written to avoid.
    [InlineData("Thera", "Ra")]
    [InlineData("Threshold RPG", "RPG")]
    [InlineData("Them", "M")]
    public void AnArticleIsOnlyTakenOffWhenItIsTheWholeWord(string one, string other)
        => Assert.NotEqual(MudMerge.Key(one), MudMerge.Key(other));

    [Fact]
    public void AListingWithNoAddressDoesNotLendItsEncryptedPort()
    {
        // This one publishes an encrypted port and no way to reach it: a web-only listing.
        var offline = Game("Webby", host: "", port: 0, tlsPort: 4000);
        var real = Game("Real", host: "mud.example", port: 23, tlsPort: null);

        MudGame merged = MudMerge.Fill(offline, real);

        Assert.Equal("mud.example", merged.Host);
        Assert.Equal(23, merged.Port);
        // The address came from the second game, and so must anything that belongs to it. A
        // port kept from the first offers a connection to a server that was never about this.
        Assert.Null(merged.TlsPort);
    }

    // ---- Addresses ----

    [Theory]
    [InlineData("http://mud.example.com:80")]
    [InlineData("https://mud.example.com")]
    [InlineData("ftp://mud.example.com:21")]
    public void ASchemeThisDoesNotSpeakIsRefusedRatherThanBecomeAHost(string written)
        => Assert.False(TelnetAddress.TryParse(written, out _, out _));

    [Theory]
    [InlineData("ssl://mud.example.com:4000", "mud.example.com", 4000, true)]
    [InlineData("mud.example.com:4000", "mud.example.com", 4000, false)]
    [InlineData("[::1]:4000", "::1", 4000, false)]
    [InlineData("::1", "::1", 23, false)]
    public void TheAddressesThatAreAddressesStillParse(string written, string host, int port, bool secure)
    {
        Assert.True(TelnetAddress.TryParse(written, out string actualHost, out int actualPort,
                                           out bool actualSecure));
        Assert.Equal(host, actualHost);
        Assert.Equal(port, actualPort);
        Assert.Equal(secure, actualSecure);
    }

    // ---- What a server may name ----

    [Theory]
    [InlineData("NUL.wav")]
    [InlineData("nul.wav")]
    [InlineData("COM1.wav")]
    [InlineData("LPT1.mp3")]
    [InlineData("CON.wav")]
    public void ADeviceNameIsNotASound(string name)
        => Assert.False(SoundLibrary.IsSafeName(name));

    [Theory]
    [InlineData("sword.wav")]
    [InlineData("combat/hit.wav")]
    [InlineData("mp3/msptest.mp3")]
    public void OrdinarySoundNamesAreStillSounds(string name)
        => Assert.True(SoundLibrary.IsSafeName(name));

    [Fact]
    public void AWildcardIsNotAllowedToNameASubfolder()
    {
        var library = new SoundLibrary(Path.GetTempPath());

        // A wildcard in the folder would be the disk walked rather than a folder read, and
        // Windows will not create a directory with one in its name either.
        Assert.Null(library.DestinationFor(Trigger("x.wav", type: "a?b")));
        Assert.NotNull(library.DestinationFor(Trigger("x.wav", type: "combat")));
    }

    // ---- Numbers a server sends ----

    [Fact]
    public void AnAbsurdLoopCountIsBroughtInsideWhatASoundCanOutlive()
    {
        Assert.True(MspTrigger.TryParse(MspKind.Sound, "x.wav L=2147483647", out MspTrigger? trigger));
        Assert.Equal(MspTrigger.MaximumLoops, trigger.Loops);
    }

    [Fact]
    public void TheOneNegativeThatMeansSomethingStillMeansIt()
    {
        Assert.True(MspTrigger.TryParse(MspKind.Sound, "x.wav L=-5", out MspTrigger? trigger));
        Assert.Equal(MspTrigger.Forever, trigger.Loops);
    }

    // ---- The published list ----

    [Fact]
    public void AListingWithANullFieldIsReadRatherThanThrownOver()
    {
        const string json = """
        {"Version":1,"Games":[
          {"Source":"a","SourceId":"1","Name":"Good","Host":"mud.example","Port":23,"Genre":null},
          {"Source":"b","SourceId":"2","Name":"Nowhere","Host":null,"Port":23}
        ]}
        """;

        MudFeed feed = MudFeed.FromJson(json);

        // The one with an address is kept, with the missing word filled in rather than left
        // as null for something downstream to read with .Length.
        MudGame kept = Assert.Single(feed.Games);
        Assert.Equal("Good", kept.Name);
        Assert.Equal(string.Empty, kept.Genre);
    }

    // ---- Orders of magnitude ----

    [Fact]
    public void APageFarPastTheEndIsEmptyRatherThanTheFirstPage()
    {
        MudGame[] games = [Game("One", "a.example", 23, null), Game("Two", "b.example", 23, null)];

        MudDirectoryPage page = MudSorting.Page(games, int.MaxValue, 50);

        Assert.Empty(page.Games);
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public void TheFirstPageIsStillTheFirstPage()
    {
        MudGame[] games = [Game("One", "a.example", 23, null), Game("Two", "b.example", 23, null)];

        MudDirectoryPage page = MudSorting.Page(games, 1, 1);

        Assert.Single(page.Games);
        Assert.Equal("One", page.Games[0].Name);
        Assert.True(page.HasMore);
    }

    // ---- The update manifest ----

    [Fact]
    public void AManifestWithNothingInItIsNotNewer()
    {
        // Every one of these used to be a NullReferenceException in Normalize, thrown from a
        // background timer where nothing catches it.
        Assert.False(UpdateClient.IsNewer(null, "0.7.10"));
        Assert.False(UpdateClient.IsNewer(string.Empty, "0.7.10"));
        Assert.False(UpdateClient.IsNewer("not a version", "0.7.10"));
    }

    [Fact]
    public void ARealVersionIsStillComparedAsOne()
    {
        Assert.True(UpdateClient.IsNewer("v0.7.11", "0.7.10"));
        Assert.False(UpdateClient.IsNewer("v0.7.9", "0.7.10"));
    }

    private static MudGame Game(string name, string host, int port, int? tlsPort) => new()
    {
        Source = "a-source",
        SourceId = name,
        Name = name,
        Host = host,
        Port = port,
        TlsPort = tlsPort,
    };

    private static MspTrigger Trigger(string fileName, string? type = null) => new(
        MspKind.Sound,
        fileName,
        Volume: 100,
        Loops: 1,
        Priority: 50,
        Continue: false,
        Type: type,
        Url: null);
}
