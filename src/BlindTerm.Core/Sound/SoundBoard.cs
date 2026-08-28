namespace BlindTerm.Core.Sound;

/// <summary>
/// Plays a file the user chose, once, and clears up after it.
///
/// Separate from <see cref="MspPlayer"/> because the two answer to different people. That one
/// enforces a protocol a server drives: priorities, loops, a folder a MUD may not name its
/// way out of. This one plays what the user picked in a dialog, and the only rules it needs
/// are that a burst of triggers cannot open unlimited devices, and that a sound which has
/// finished is closed rather than left holding one.
/// </summary>
public sealed class SoundBoard : IDisposable
{
    /// <summary>How many trigger sounds may be heard at once.</summary>
    public const int Voices = 6;

    private readonly ISoundOutput _output;
    private readonly Lock _gate = new();
    private readonly List<int> _playing = new();
    private bool _disposed;

    public SoundBoard(ISoundOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Scales every sound played through this, 0 to 100.</summary>
    public int Volume { get; set; } = 100;

    /// <summary>Whether anything is still making a sound.</summary>
    public bool IsPlaying
    {
        get { lock (_gate) return _playing.Count > 0; }
    }

    /// <summary>
    /// Starts a file, and says whether it started.
    ///
    /// A sound that will not open is not an error worth stopping anything for -- the file has
    /// been moved or renamed since the trigger was written -- but the caller is told, so that
    /// it can be said once rather than guessed at.
    /// </summary>
    public bool Play(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        lock (_gate)
        {
            if (_disposed) return false;

            // Reap first: a trigger firing repeatedly is the normal way this fills up, and
            // the voices it used are almost always finished by the time the next one comes.
            Reap();
            if (_playing.Count >= Voices) return false;

            int? handle = _output.Play(path, Math.Clamp(Volume, 0, 100));
            if (handle is not { } started) return false;
            _playing.Add(started);
            return true;
        }
    }

    /// <summary>
    /// Closes anything that has finished. Called from the window's timer, because nothing
    /// below this layer knows when a sound ends.
    /// </summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Reap();
        }
    }

    /// <summary>Stops everything at once. What a master switch being turned off means.</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            foreach (int handle in _playing) _output.Stop(handle);
            _playing.Clear();
        }
    }

    private void Reap()
    {
        for (int i = _playing.Count - 1; i >= 0; i--)
        {
            if (_output.IsPlaying(_playing[i])) continue;
            _output.Stop(_playing[i]);
            _playing.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (int handle in _playing) _output.Stop(handle);
            _playing.Clear();
        }
        _output.Dispose();
    }
}
