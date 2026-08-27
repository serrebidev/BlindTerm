using BlindTerm.Core.Net;
using BlindTerm.Core.Sound;

namespace BlindTerm.Tests;

public class MspPlayerTests
{
    /// <summary>A sound card that keeps a note of what it was asked to do.</summary>
    private sealed class FakeOutput : ISoundOutput
    {
        private int _next;

        public sealed record Voice(int Handle, string Path, int Volume)
        {
            public bool Playing { get; set; } = true;
            public int Plays { get; set; } = 1;
            public bool Closed { get; set; }
        }

        public List<Voice> Voices { get; } = new();
        public bool Disposed { get; private set; }

        /// <summary>Set to refuse everything, as a missing codec or a locked file would.</summary>
        public bool Broken { get; set; }

        public Voice this[int handle] => Voices.Single(v => v.Handle == handle);
        public IEnumerable<Voice> Live => Voices.Where(v => !v.Closed);

        public int? Play(string path, int volume)
        {
            if (Broken) return null;
            var voice = new Voice(++_next, path, volume);
            Voices.Add(voice);
            return voice.Handle;
        }

        public bool IsPlaying(int handle)
        {
            Voice? voice = Voices.SingleOrDefault(v => v.Handle == handle);
            return voice is { Closed: false, Playing: true };
        }

        public void Replay(int handle)
        {
            Voice voice = this[handle];
            voice.Playing = true;
            voice.Plays++;
        }

        public void Stop(int handle)
        {
            Voice? voice = Voices.SingleOrDefault(v => v.Handle == handle);
            if (voice is null) return;
            voice.Playing = false;
            voice.Closed = true;
        }

        public void Finish(int handle) => this[handle].Playing = false;

        public void Dispose() => Disposed = true;
    }

    private const string Root = @"C:\sounds";

    private static MspTrigger Trigger(string body, MspKind kind = MspKind.Sound)
    {
        Assert.True(MspTrigger.TryParse(kind, body, out MspTrigger? trigger));
        return trigger!;
    }

    private static SoundLibrary Library(params string[] present)
    {
        var files = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return new SoundLibrary(Root, exists: files.Contains, matchingFiles: _ => []);
    }

    private static (MspPlayer Player, FakeOutput Output) Build(params string[] present)
    {
        var output = new FakeOutput();
        return (new MspPlayer(output, Library(present)), output);
    }

    [Fact]
    public void ASoundThatExistsIsPlayed()
    {
        var (player, output) = Build(@"C:\sounds\sword.wav");

        Assert.True(player.Handle(Trigger("sword.wav")));

        FakeOutput.Voice voice = Assert.Single(output.Voices);
        Assert.Equal(@"C:\sounds\sword.wav", voice.Path);
        Assert.Equal(100, voice.Volume);
    }

    [Fact]
    public void ASoundThisMachineDoesNotHaveIsQuietlySkipped()
    {
        var (player, output) = Build();

        Assert.False(player.Handle(Trigger("missing.wav")));
        Assert.Empty(output.Voices);
    }

    [Fact]
    public void TheMasterVolumeScalesTheTriggersOwn()
    {
        var (player, output) = Build(@"C:\sounds\sword.wav");
        player.MasterVolume = 50;

        player.Handle(Trigger("sword.wav V=80"));

        Assert.Equal(40, Assert.Single(output.Voices).Volume);
    }

    [Fact]
    public void AMasterVolumeOfZeroSilencesWithoutTurningTheProtocolOff()
    {
        var (player, output) = Build(@"C:\sounds\sword.wav");
        player.MasterVolume = 0;

        player.Handle(Trigger("sword.wav"));

        Assert.Equal(0, Assert.Single(output.Voices).Volume);
    }

    [Fact]
    public void ASoundPlayedOnceIsLetGoWhenItFinishes()
    {
        var (player, output) = Build(@"C:\sounds\sword.wav");
        player.Handle(Trigger("sword.wav"));
        int handle = output.Voices[0].Handle;

        player.Tick();
        Assert.Equal(1, player.PlayingSounds);

        output.Finish(handle);
        player.Tick();

        Assert.Equal(0, player.PlayingSounds);
        Assert.True(output[handle].Closed);
    }

    [Fact]
    public void ASoundAskedForThreeTimesPlaysThreeTimes()
    {
        var (player, output) = Build(@"C:\sounds\drip.wav");
        player.Handle(Trigger("drip.wav L=3"));
        int handle = output.Voices[0].Handle;

        for (int i = 0; i < 5; i++)
        {
            output.Finish(handle);
            player.Tick();
        }

        Assert.Equal(3, output[handle].Plays);
        Assert.Equal(0, player.PlayingSounds);
    }

    [Fact]
    public void ASoundAskedForForEverKeepsGoing()
    {
        var (player, output) = Build(@"C:\sounds\rain.wav");
        player.Handle(Trigger("rain.wav L=-1"));
        int handle = output.Voices[0].Handle;

        for (int i = 0; i < 20; i++)
        {
            output.Finish(handle);
            player.Tick();
        }

        Assert.Equal(21, output[handle].Plays);
        Assert.Equal(1, player.PlayingSounds);
    }

    [Fact]
    public void OffStopsEverySoundAtOnce()
    {
        var (player, output) = Build(@"C:\sounds\a.wav", @"C:\sounds\b.wav");
        player.Handle(Trigger("a.wav"));
        player.Handle(Trigger("b.wav"));

        Assert.True(player.Handle(Trigger("Off")));

        Assert.Equal(0, player.PlayingSounds);
        Assert.All(output.Voices, voice => Assert.True(voice.Closed));
    }

    [Fact]
    public void MusicOffLeavesTheSoundEffectsAlone()
    {
        var (player, _) = Build(@"C:\sounds\a.wav", @"C:\sounds\theme.mid");
        player.Handle(Trigger("a.wav"));
        player.Handle(Trigger("theme.mid", MspKind.Music));

        player.Handle(Trigger("Off", MspKind.Music));

        Assert.Null(player.PlayingMusic);
        Assert.Equal(1, player.PlayingSounds);
    }

    [Fact]
    public void OnlyOnePieceOfMusicPlaysAtATime()
    {
        var (player, output) = Build(@"C:\sounds\one.mid", @"C:\sounds\two.mid");
        player.Handle(Trigger("one.mid", MspKind.Music));

        player.Handle(Trigger("two.mid", MspKind.Music));

        Assert.Equal(@"C:\sounds\two.mid", player.PlayingMusic);
        Assert.True(output.Voices[0].Closed);
        Assert.False(output.Voices[1].Closed);
    }

    [Fact]
    public void TheSameMusicAgainIsLeftPlayingRatherThanRestarted()
    {
        // Otherwise every room description starts the theme again from the top.
        var (player, output) = Build(@"C:\sounds\theme.mid");
        player.Handle(Trigger("theme.mid", MspKind.Music));

        Assert.False(player.Handle(Trigger("theme.mid", MspKind.Music)));

        Assert.Single(output.Voices);
        Assert.Equal(1, output.Voices[0].Plays);
    }

    [Fact]
    public void TheSameMusicWithContinueOffStartsAgain()
    {
        var (player, output) = Build(@"C:\sounds\theme.mid");
        player.Handle(Trigger("theme.mid", MspKind.Music));

        Assert.True(player.Handle(Trigger("theme.mid C=0", MspKind.Music)));

        Assert.Equal(2, output.Voices.Count);
        Assert.True(output.Voices[0].Closed);
    }

    [Fact]
    public void OnlySoManySoundsPlayAtOnce()
    {
        var (player, output) = Build([.. Enumerable.Range(0, 12).Select(i => $@"C:\sounds\s{i}.wav")]);

        for (int i = 0; i < 12; i++) player.Handle(Trigger($"s{i}.wav P=50"));

        Assert.Equal(MspPlayer.Voices, player.PlayingSounds);
        Assert.Equal(MspPlayer.Voices, output.Live.Count());
    }

    [Fact]
    public void AMoreImportantSoundTakesTheQuietestClaimsPlace()
    {
        var (player, output) = Build([.. Enumerable.Range(0, 9).Select(i => $@"C:\sounds\s{i}.wav")]);
        for (int i = 0; i < MspPlayer.Voices; i++) player.Handle(Trigger($"s{i}.wav P=10"));
        // Make one of them plainly the least important.
        player.Handle(Trigger("Off"));
        for (int i = 0; i < MspPlayer.Voices; i++) player.Handle(Trigger($"s{i}.wav P={20 + i}"));

        Assert.True(player.Handle(Trigger("s8.wav P=99")));

        Assert.Equal(MspPlayer.Voices, player.PlayingSounds);
        Assert.Contains(output.Live, voice => voice.Path.EndsWith(@"s8.wav", StringComparison.Ordinal));
        // The one that gave way is the one that was easiest to spare.
        Assert.DoesNotContain(output.Live, voice => voice.Path.EndsWith(@"s0.wav", StringComparison.Ordinal));
    }

    [Fact]
    public void ALessImportantSoundDoesNotCutOffWhatIsAlreadyBeingHeard()
    {
        var (player, _) = Build([.. Enumerable.Range(0, 9).Select(i => $@"C:\sounds\s{i}.wav")]);
        for (int i = 0; i < MspPlayer.Voices; i++) player.Handle(Trigger($"s{i}.wav P=80"));

        Assert.False(player.Handle(Trigger("s8.wav P=10")));

        Assert.Equal(MspPlayer.Voices, player.PlayingSounds);
    }

    [Fact]
    public void ASoundIsFetchedOnlyWhenItIsMissingAndFetchingIsOn()
    {
        var output = new FakeOutput();
        var asked = new List<string>();
        var player = new MspPlayer(output, Library(@"C:\sounds\here.wav"))
        {
            Download = trigger =>
            {
                asked.Add(trigger.FileName);
                return @"C:\sounds\fetched.wav";
            },
        };

        player.Handle(Trigger("here.wav"));
        player.Handle(Trigger("elsewhere.wav"));

        Assert.Equal(["elsewhere.wav"], asked);
        Assert.Equal(2, output.Voices.Count);
    }

    [Fact]
    public void TheAddressAMudGivesOnceIsRememberedForEveryTriggerAfterIt()
    {
        // Core MUD names its sound folder on the "!!SOUND(off U=...)" it sends at connect, and
        // every trigger after that is a bare file name.
        var output = new FakeOutput();
        var asked = new List<MspTrigger>();
        var player = new MspPlayer(output, Library())
        {
            Download = trigger => { asked.Add(trigger); return null; },
        };

        player.Handle(Trigger("Off U=https://mud.example/sounds/"));
        player.Handle(Trigger("sword.wav"));

        Assert.Equal("https://mud.example/sounds/", player.SoundUrl);
        Assert.Equal("https://mud.example/sounds/", Assert.Single(asked).Url);
    }

    [Fact]
    public void ATriggerWithItsOwnAddressKeepsIt()
    {
        var output = new FakeOutput();
        var asked = new List<MspTrigger>();
        var player = new MspPlayer(output, Library())
        {
            Download = trigger => { asked.Add(trigger); return null; },
        };

        player.Handle(Trigger("Off U=https://mud.example/sounds/"));
        player.Handle(Trigger("sword.wav U=https://elsewhere.example/"));

        Assert.Equal("https://elsewhere.example/", Assert.Single(asked).Url);
    }

    [Fact]
    public void NothingIsFetchedWhenFetchingIsOff()
    {
        var (player, output) = Build();

        Assert.False(player.Handle(Trigger("elsewhere.wav U=http://mud.example")));
        Assert.Empty(output.Voices);
    }

    [Fact]
    public void ASoundTheMachineCannotPlayIsNotCountedAsPlaying()
    {
        var (player, output) = Build(@"C:\sounds\sword.wav");
        output.Broken = true;

        Assert.False(player.Handle(Trigger("sword.wav")));
        Assert.Equal(0, player.PlayingSounds);
    }

    [Fact]
    public void ClosingTheWindowSilencesEverything()
    {
        var (player, output) = Build(@"C:\sounds\a.wav", @"C:\sounds\theme.mid");
        player.Handle(Trigger("a.wav"));
        player.Handle(Trigger("theme.mid", MspKind.Music));

        player.Dispose();

        Assert.All(output.Voices, voice => Assert.True(voice.Closed));
        Assert.True(output.Disposed);
        Assert.False(player.Handle(Trigger("a.wav")));
    }
}
