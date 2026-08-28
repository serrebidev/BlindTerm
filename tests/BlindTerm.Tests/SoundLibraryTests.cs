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
    [InlineData("sounds/../../evil.wav")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData(@"sub\nested.wav")]
    [InlineData("/etc/sound.wav")]
    [InlineData("sub//nested.wav")]
    [InlineData("a/b/c/d/e/f/g/h/i/deep.wav")]
    [InlineData("mp3*/test.wav")]
    [InlineData("..")]
    public void AServerNamesASoundAndNeverAPath(string name)
    {
        // A MUD names a sound under its own folder. It does not get to leave that folder, name
        // a drive, or point a wildcard at the folders themselves.
        Assert.False(SoundLibrary.IsSafeName(name));
        Assert.Null(Library(@"C:\sounds\sword.wav").Resolve(Trigger(name)));
        Assert.Null(Library().DestinationFor(Trigger(name)));
        Assert.Null(SoundLibrary.DownloadFor(Trigger($"{name} U=https://mud.example/s")));
    }

    [Fact]
    public void AMudMayKeepItsSoundsInFolders()
    {
        // Core MUD's own test sound is named "mp3/msptest.mp3". Refusing that refuses the
        // sound the MUD plays to prove sound is working.
        Assert.True(SoundLibrary.IsSafeName("mp3/msptest.mp3"));

        Assert.Equal(@"C:\sounds\mp3\msptest.mp3",
                     Library(@"C:\sounds\mp3\msptest.mp3").Resolve(Trigger("mp3/msptest.mp3")));
        Assert.Equal(@"C:\sounds\mp3\msptest.mp3",
                     Library().DestinationFor(Trigger("mp3/msptest.mp3")));
        Assert.Equal("https://coremud.org/sounds/mp3/msptest.mp3",
                     SoundLibrary.DownloadFor(
                         Trigger("mp3/msptest.mp3 U=https://coremud.org/sounds/"))?.AbsoluteUri);
    }

    [Fact]
    public void AFolderInTheNameSitsUnderTheTypeFolder()
        => Assert.Equal(@"C:\sounds\combat\swords\hit.wav",
                        Library(@"C:\sounds\combat\swords\hit.wav")
                            .Resolve(Trigger("swords/hit.wav T=combat")));

    [Fact]
    public void AWildcardMayNameTheFileInsideAFolder()
    {
        string? chosen = Library(@"C:\sounds\mp3\hit1.mp3", @"C:\sounds\mp3\hit2.mp3")
            .Resolve(Trigger("mp3/hit*.mp3"));

        Assert.Contains(chosen, new[] { @"C:\sounds\mp3\hit1.mp3", @"C:\sounds\mp3\hit2.mp3" });
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
