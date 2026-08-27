using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;

namespace BlindTerm.Tests;

public class SoundLibraryTests
{
    private const string Root = @"C:\sounds";

    private static MspTrigger Trigger(string body, MspKind kind = MspKind.Sound)
    {
        Assert.True(MspTrigger.TryParse(kind, body, out MspTrigger? trigger));
        return trigger!;
    }

    private static SoundLibrary Library(params string[] present)
    {
        var files = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return new SoundLibrary(
            Root,
            exists: files.Contains,
            matchingFiles: pattern =>
            {
                string? folder = Path.GetDirectoryName(pattern);
                string name = Path.GetFileName(pattern);
                string prefix = name[..name.IndexOf('*')];
                return files.Where(f =>
                    string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            },
            random: new Random(1));
    }

    [Fact]
    public void ASoundInTheFolderIsFound()
        => Assert.Equal(@"C:\sounds\sword.wav",
                        Library(@"C:\sounds\sword.wav").Resolve(Trigger("sword.wav")));

    [Fact]
    public void TheTypeNamesASubfolder()
        => Assert.Equal(@"C:\sounds\combat\sword.wav",
                        Library(@"C:\sounds\combat\sword.wav").Resolve(Trigger("sword.wav T=combat")));

    [Fact]
    public void ASoundThatIsNotHereIsNotPlayed()
        => Assert.Null(Library().Resolve(Trigger("missing.wav")));

    [Fact]
    public void AWildcardPicksOneOfTheMatches()
    {
        string? chosen = Library(@"C:\sounds\hit1.wav", @"C:\sounds\hit2.wav", @"C:\sounds\miss.wav")
            .Resolve(Trigger("hit*.wav"));

        Assert.Contains(chosen, new[] { @"C:\sounds\hit1.wav", @"C:\sounds\hit2.wav" });
    }

    [Fact]
    public void AWildcardThatMatchesNothingPlaysNothing()
        => Assert.Null(Library(@"C:\sounds\miss.wav").Resolve(Trigger("hit*.wav")));

    [Theory]
    [InlineData(@"..\..\Startup\evil.exe")]
    [InlineData("../../etc/passwd")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData(@"sub\nested.wav")]
    [InlineData("sub/nested.wav")]
    [InlineData("..")]
    public void AServerNamesASoundAndNeverAPath(string name)
    {
        // A MUD sends the name of a sound. It does not get to say where on this disk to look.
        Assert.False(SoundLibrary.IsSafeName(name));
        Assert.Null(Library(@"C:\sounds\sword.wav").Resolve(Trigger(name)));
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("script.ps1")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void SomethingThatIsNotASoundIsNotOpened(string name)
        => Assert.False(SoundLibrary.IsSafeName(name));

    [Theory]
    [InlineData("sword.wav")]
    [InlineData("theme.mp3")]
    [InlineData("song.mid")]
    [InlineData("HIT.WAV")]
    public void TheOrdinarySoundFormatsAreAccepted(string name)
        => Assert.True(SoundLibrary.IsSafeName(name));

    [Fact]
    public void ATypeThatIsAPathIsRefusedTheSameWay()
        => Assert.Null(Library(@"C:\sounds\sword.wav").Resolve(Trigger(@"sword.wav T=..\..\Windows")));

    [Fact]
    public void ADownloadAddressIsBuiltFromTheUrlTypeAndName()
    {
        Uri? uri = SoundLibrary.DownloadFor(
            Trigger("sword.wav T=combat U=http://mud.example/sounds"));

        Assert.Equal("http://mud.example/sounds/combat/sword.wav", uri?.AbsoluteUri);
    }

    [Fact]
    public void ADownloadAddressWorksWithoutAType()
    {
        Uri? uri = SoundLibrary.DownloadFor(Trigger("sword.wav U=https://mud.example/snd/"));

        Assert.Equal("https://mud.example/snd/sword.wav", uri?.AbsoluteUri);
    }

    [Theory]
    [InlineData(@"sword.wav U=file:///C:/Windows")]
    [InlineData("sword.wav U=ftp://mud.example")]
    [InlineData(@"..\evil.exe U=http://mud.example")]
    [InlineData("evil.exe U=http://mud.example")]
    [InlineData("sword.wav")]
    public void AnAddressThatIsNotAnOrdinaryWebSoundIsRefused(string body)
        => Assert.Null(SoundLibrary.DownloadFor(Trigger(body)));

    [Fact]
    public void AWildcardNamesAChoiceAmongLocalFilesAndIsNeverFetched()
    {
        // There is no one file to ask for, and inventing one would write a file called
        // "hit*.wav" onto the disk.
        Assert.Null(SoundLibrary.DownloadFor(Trigger("hit*.wav U=http://mud.example")));
        Assert.Null(Library().DestinationFor(Trigger("hit*.wav")));
    }

    [Fact]
    public void ADownloadLandsInsideTheSoundFolder()
    {
        string? destination = Library().DestinationFor(Trigger("sword.wav T=combat"));

        Assert.Equal(@"C:\sounds\combat\sword.wav", destination);
    }
}
