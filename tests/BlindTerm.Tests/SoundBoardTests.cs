using BlindTerm.Core.Sound;

namespace BlindTerm.Tests;

public class SoundBoardTests
{
    /// <summary>A sound card that keeps a note of what it was asked to do.</summary>
    private sealed class FakeOutput : ISoundOutput
    {
        private int _next;

        public sealed record Voice(int Handle, string Path, int Volume)
        {
            public bool Playing { get; set; } = true;
            public bool Closed { get; set; }
        }

        public List<Voice> Voices { get; } = new();

        /// <summary>Set to refuse the next file, the way a missing one is refused.</summary>
        public bool RefuseEverything { get; set; }

        public bool Disposed { get; private set; }

        public int? Play(string path, int volume)
        {
            if (RefuseEverything) return null;
            var voice = new Voice(++_next, path, volume);
            Voices.Add(voice);
            return voice.Handle;
        }

        public bool IsPlaying(int handle)
            => Voices.FirstOrDefault(v => v.Handle == handle) is { Playing: true, Closed: false };

        public void Replay(int handle) { }

        public void Stop(int handle)
        {
            if (Voices.FirstOrDefault(v => v.Handle == handle) is { } voice)
            {
                voice.Playing = false;
                voice.Closed = true;
            }
        }

        public void Dispose() => Disposed = true;

        public Voice Last => Voices[^1];
    }

    [Fact]
    public void PlayingAFileOpensAVoiceAtTheBoardVolume()
    {
        var output = new FakeOutput();
        using var board = new SoundBoard(output) { Volume = 60 };

        Assert.True(board.Play(@"C:\sounds\alarm.wav"));
        Assert.Equal(@"C:\sounds\alarm.wav", output.Last.Path);
        Assert.Equal(60, output.Last.Volume);
        Assert.True(board.IsPlaying);
    }

    [Fact]
    public void AFileThatWillNotOpenIsReportedRatherThanThrown()
    {
        var output = new FakeOutput { RefuseEverything = true };
        using var board = new SoundBoard(output);

        Assert.False(board.Play(@"C:\sounds\gone.wav"));
        Assert.False(board.IsPlaying);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingIsPlayedForATriggerWithNoSoundOnIt(string? path)
        => Assert.False(new SoundBoard(new FakeOutput()).Play(path));

    [Fact]
    public void VolumeIsKeptInsideItsRange()
    {
        var output = new FakeOutput();
        using var board = new SoundBoard(output) { Volume = 400 };
        board.Play(@"C:\sounds\alarm.wav");
        Assert.Equal(100, output.Last.Volume);
    }

    /// <summary>
    /// A trigger firing on every line of a busy MUD is the ordinary way this fills up, and a
    /// voice that has finished is one the next firing can have.
    /// </summary>
    [Fact]
    public void AVoiceThatHasFinishedIsClosedAndReused()
    {
        var output = new FakeOutput();
        using var board = new SoundBoard(output);

        for (int i = 0; i < SoundBoard.Voices; i++) Assert.True(board.Play(@"C:\sounds\a.wav"));
        Assert.False(board.Play(@"C:\sounds\a.wav"));

        foreach (FakeOutput.Voice voice in output.Voices) voice.Playing = false;
        board.Tick();

        Assert.False(board.IsPlaying);
        Assert.All(output.Voices, voice => Assert.True(voice.Closed));
        Assert.True(board.Play(@"C:\sounds\a.wav"));
    }

    [Fact]
    public void StoppingEverythingClosesEveryVoiceAtOnce()
    {
        var output = new FakeOutput();
        using var board = new SoundBoard(output);
        board.Play(@"C:\sounds\a.wav");
        board.Play(@"C:\sounds\b.wav");

        board.StopAll();

        Assert.False(board.IsPlaying);
        Assert.All(output.Voices, voice => Assert.True(voice.Closed));
    }

    [Fact]
    public void DisposingStopsWhatIsPlayingAndClosesTheOutput()
    {
        var output = new FakeOutput();
        var board = new SoundBoard(output);
        board.Play(@"C:\sounds\a.wav");

        board.Dispose();

        Assert.True(output.Voices[0].Closed);
        Assert.True(output.Disposed);
        Assert.False(board.Play(@"C:\sounds\a.wav"));
    }
}
