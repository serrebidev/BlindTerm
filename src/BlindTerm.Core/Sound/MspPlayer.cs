using BlindTerm.Core.Net;

namespace BlindTerm.Core.Sound;

/// <summary>
/// Acts on MUD Sound Protocol triggers: which sound plays, how loud, how many times, and what
/// gives way to what.
///
/// Sound effects share a small number of voices and music has one of its own, because that is
/// what the protocol describes and because a MUD in a busy room will ask for more at once than
/// anyone can hear. When the voices are full the quietest claim wins: a trigger arriving with
/// a higher priority than something already playing takes its place, and one arriving with a
/// lower priority is dropped rather than cutting off what is already being heard.
///
/// Repeats are counted here rather than asked of the sound layer, which has no notion of
/// playing something three times. A tick looks at what has finished and either starts it again
/// or lets it go.
/// </summary>
public sealed class MspPlayer : IDisposable
{
    /// <summary>How many sound effects may play at once.</summary>
    public const int Voices = 8;

    private sealed class Voice
    {
        public required int Handle { get; init; }
        public required string Path { get; init; }
        public required int Priority { get; init; }
        public required int Volume { get; init; }
        public int Remaining { get; set; }
    }

    private readonly ISoundOutput _output;
    private readonly SoundLibrary _library;
    private readonly Lock _gate = new();
    private readonly List<Voice> _sounds = new();

    private Voice? _music;
    private string? _musicPath;
    private string? _soundUrl;
    private bool _disposed;

    /// <summary>
    /// Fetches a missing sound and returns where it was written, or null. Left unset, nothing
    /// is downloaded and a sound the machine does not have is simply not played.
    /// </summary>
    public Func<MspTrigger, string?>? Download { get; init; }

    /// <summary>Scales every sound. 0 silences without turning the protocol off.</summary>
    public int MasterVolume { get; set; } = 100;

    public MspPlayer(ISoundOutput output, SoundLibrary library)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(library);
        _output = output;
        _library = library;
    }

    /// <summary>What is playing now, for tests and for anything that wants to report it.</summary>
    public int PlayingSounds { get { lock (_gate) return _sounds.Count; } }

    public string? PlayingMusic { get { lock (_gate) return _music is null ? null : _musicPath; } }

    /// <summary>
    /// Acts on one trigger.
    /// </summary>
    /// <returns>Whether anything was started or stopped.</returns>
    public bool Handle(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        lock (_gate)
        {
            if (_disposed) return false;

            // A MUD names where its sounds live once and then leaves it out. Core MUD says it
            // on the "!!SOUND(off U=...)" it sends the moment the connection is up, and every
            // trigger after that carries a file name and nothing else.
            if (trigger.Url is not null) _soundUrl = trigger.Url;

            if (trigger.IsOff) return StopAll(trigger.Kind);
            return trigger.Kind == MspKind.Music ? StartMusic(trigger) : StartSound(trigger);
        }
    }

    /// <summary>
    /// Starts again anything that has finished and still has repeats owed, and forgets
    /// anything that has finished for good. Call this a few times a second.
    /// </summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;

            for (int i = _sounds.Count - 1; i >= 0; i--)
            {
                if (!Continue(_sounds[i])) _sounds.RemoveAt(i);
            }

            if (_music is not null && !Continue(_music))
            {
                _music = null;
                _musicPath = null;
            }
        }
    }

    /// <summary>Whether a voice should stay. Restarts it if it has finished and owes a repeat.</summary>
    private bool Continue(Voice voice)
    {
        if (_output.IsPlaying(voice.Handle)) return true;

        if (voice.Remaining == MspTrigger.Forever)
        {
            _output.Replay(voice.Handle);
            return true;
        }
        if (voice.Remaining > 0)
        {
            voice.Remaining--;
            _output.Replay(voice.Handle);
            return true;
        }

        _output.Stop(voice.Handle);
        return false;
    }

    private bool StartSound(MspTrigger trigger)
    {
        string? path = Locate(trigger);
        if (path is null) return false;

        if (_sounds.Count >= Voices)
        {
            // The one giving way is the least important thing currently playing, and only if
            // it is less important than what has just been asked for.
            Voice quietest = _sounds.MinBy(voice => voice.Priority)!;
            if (quietest.Priority >= trigger.Priority) return false;
            _output.Stop(quietest.Handle);
            _sounds.Remove(quietest);
        }

        int volume = Scale(trigger.Volume);
        int? handle = _output.Play(path, volume);
        if (handle is not int started) return false;

        _sounds.Add(new Voice
        {
            Handle = started,
            Path = path,
            Priority = trigger.Priority,
            Volume = volume,
            Remaining = Repeats(trigger.Loops),
        });
        return true;
    }

    private bool StartMusic(MspTrigger trigger)
    {
        string? path = Locate(trigger);
        if (path is null) return false;

        // "Continue" means exactly this: the same music, still playing, is left alone. Without
        // it every room description would start the theme again from the top.
        if (trigger.Continue && _music is not null &&
            string.Equals(_musicPath, path, StringComparison.OrdinalIgnoreCase) &&
            _output.IsPlaying(_music.Handle))
        {
            return false;
        }

        if (_music is not null)
        {
            _output.Stop(_music.Handle);
            _music = null;
            _musicPath = null;
        }

        int volume = Scale(trigger.Volume);
        int? handle = _output.Play(path, volume);
        if (handle is not int started) return false;

        _music = new Voice
        {
            Handle = started,
            Path = path,
            Priority = trigger.Priority,
            Volume = volume,
            Remaining = Repeats(trigger.Loops),
        };
        _musicPath = path;
        return true;
    }

    private bool StopAll(MspKind kind)
    {
        if (kind == MspKind.Music)
        {
            if (_music is null) return false;
            _output.Stop(_music.Handle);
            _music = null;
            _musicPath = null;
            return true;
        }

        if (_sounds.Count == 0) return false;
        foreach (Voice voice in _sounds) _output.Stop(voice.Handle);
        _sounds.Clear();
        return true;
    }

    private string? Locate(MspTrigger trigger)
    {
        string? here = _library.Resolve(trigger);
        if (here is not null || Download is null) return here;

        MspTrigger asked = trigger.Url is null && _soundUrl is not null
            ? trigger with { Url = _soundUrl }
            : trigger;
        return Download(asked);
    }

    /// <summary>Where this MUD has said its sounds live, if it has said.</summary>
    public string? SoundUrl { get { lock (_gate) return _soundUrl; } }

    /// <summary>Repeats still owed after the first play. -1 stays -1, and means for ever.</summary>
    private static int Repeats(int loops)
        => loops == MspTrigger.Forever ? MspTrigger.Forever : Math.Max(0, loops - 1);

    private int Scale(int volume)
        => Math.Clamp(volume * Math.Clamp(MasterVolume, 0, 100) / 100, 0, 100);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Voice voice in _sounds) _output.Stop(voice.Handle);
            _sounds.Clear();
            if (_music is not null) _output.Stop(_music.Handle);
            _music = null;
            _musicPath = null;
            _output.Dispose();
        }
    }
}
