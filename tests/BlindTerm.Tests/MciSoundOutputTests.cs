using System.Runtime.Versioning;
using BlindTerm.Core.Sound;

namespace BlindTerm.Tests;

/// <summary>
/// The real Windows multimedia path, driven with a file of pure silence. Everything that can
/// go wrong about opening, playing and closing a sound goes wrong here rather than in front of
/// someone in a MUD, and nothing is heard while it does.
/// </summary>
[SupportedOSPlatform("windows")]
public class MciSoundOutputTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "BlindTerm.Sound." + Guid.NewGuid().ToString("N"));

    public MciSoundOutputTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>A valid WAV of the requested length whose every sample is silence.</summary>
    private string Silence(string name, double seconds = 1.0)
    {
        const int rate = 8000;
        int samples = (int)(rate * seconds);
        string path = Path.Combine(_folder, name);

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                      // PCM header length
        writer.Write((short)1);                // PCM
        writer.Write((short)1);                // mono
        writer.Write(rate);
        writer.Write(rate);                    // bytes per second
        writer.Write((short)1);                // block align
        writer.Write((short)8);                // bits per sample
        writer.Write("data"u8);
        writer.Write(samples);
        // 8-bit PCM silence is the midpoint, not zero.
        writer.Write(Enumerable.Repeat((byte)128, samples).ToArray());
        return path;
    }

    [Fact]
    public void ASoundOpensPlaysAndCloses()
    {
        using var output = new MciSoundOutput();

        int? handle = output.Play(Silence("quiet.wav"), volume: 0);

        Assert.NotNull(handle);
        Assert.True(output.IsPlaying(handle!.Value));

        output.Stop(handle.Value);
        Assert.False(output.IsPlaying(handle.Value));
    }

    [Fact]
    public void SeveralSoundsPlayAtOnce()
    {
        // SoundPlayer cannot do this, which is why MCI is here at all: a MUD asks for a hit,
        // a grunt and a footstep in the same breath.
        using var output = new MciSoundOutput();
        int?[] handles =
        [
            output.Play(Silence("a.wav"), 0),
            output.Play(Silence("b.wav"), 0),
            output.Play(Silence("c.wav"), 0),
        ];

        Assert.All(handles, handle => Assert.NotNull(handle));
        Assert.All(handles, handle => Assert.True(output.IsPlaying(handle!.Value)));

        foreach (int? handle in handles) output.Stop(handle!.Value);
    }

    [Fact]
    public void AFolderWrittenWithForwardSlashesStillPlays()
    {
        // Somebody will type one into the settings box, and MCI wants backslashes. It says
        // nothing useful when it does not get them; the sound simply never plays.
        using var output = new MciSoundOutput();
        string path = Silence("slashes.wav").Replace(Path.DirectorySeparatorChar, '/');

        int? handle = output.Play(path, volume: 0);

        Assert.NotNull(handle);
        output.Stop(handle!.Value);
    }

    [Fact]
    public void ASoundUnderALongPathStillPlays()
    {
        // MCI parses a fixed-length command string, and a sound pack unpacked somewhere deep
        // simply never plays: it reports only that the file name is invalid.
        string deep = _folder;
        while (deep.Length < 180) deep = Path.Combine(deep, "a-reasonably-long-folder-name");
        Directory.CreateDirectory(deep);
        string path = Path.Combine(deep, "far.wav");
        File.Copy(Silence("source.wav"), path);

        using var output = new MciSoundOutput();
        int? handle = output.Play(path, volume: 0);

        Assert.NotNull(handle);
        Assert.True(output.IsPlaying(handle!.Value));
        output.Stop(handle.Value);
    }

    [Fact]
    public void TwoDistantSoundsWithTheSameNameStayDistinct()
    {
        string first = Path.Combine(_folder, new string('x', 120), "same.wav");
        string second = Path.Combine(_folder, new string('y', 120), "same.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.Copy(Silence("a.wav", 0.2), first);
        File.Copy(Silence("b.wav", 5), second);

        using var output = new MciSoundOutput();
        int one = output.Play(first, 0)!.Value;
        int two = output.Play(second, 0)!.Value;

        // The short one runs out while the long one is still going, which it could not do if
        // they had collapsed into a single staged copy.
        Thread.Sleep(700);
        Assert.False(output.IsPlaying(one));
        Assert.True(output.IsPlaying(two));
        output.Stop(two);
    }

    [Fact]
    public void AFileThatIsNotThereIsNotPlayed()
    {
        using var output = new MciSoundOutput();

        Assert.Null(output.Play(Path.Combine(_folder, "nothing.wav"), 0));
    }

    [Fact]
    public void AFileThatIsNotASoundIsNotPlayed()
    {
        string path = Path.Combine(_folder, "rubbish.wav");
        File.WriteAllText(path, "this is not a wave file");

        using var output = new MciSoundOutput();

        Assert.Null(output.Play(path, 0));
    }

    [Fact]
    public void AFinishedSoundCanBePlayedAgain()
    {
        using var output = new MciSoundOutput();
        int handle = output.Play(Silence("short.wav", 0.05), 0)!.Value;

        // Let it run out, which is what a repeat waits for.
        Thread.Sleep(400);
        Assert.False(output.IsPlaying(handle));

        output.Replay(handle);

        Assert.True(output.IsPlaying(handle));
        output.Stop(handle);
    }

    [Fact]
    public void StoppingSomethingTwiceIsNotAnError()
    {
        using var output = new MciSoundOutput();
        int handle = output.Play(Silence("twice.wav"), 0)!.Value;

        output.Stop(handle);
        output.Stop(handle);
        output.Stop(handle + 1000);
    }

    [Fact]
    public void DisposingSilencesWhateverIsStillPlaying()
    {
        var output = new MciSoundOutput();
        int handle = output.Play(Silence("long.wav", 5), 0)!.Value;
        Assert.True(output.IsPlaying(handle));

        output.Dispose();

        Assert.False(output.IsPlaying(handle));
        Assert.Null(output.Play(Silence("after.wav"), 0));
    }
}
