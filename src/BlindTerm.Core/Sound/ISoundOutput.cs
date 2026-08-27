namespace BlindTerm.Core.Sound;

/// <summary>
/// Somewhere to send a sound. Behind this is the Windows multimedia layer; in front of it is
/// everything about the MUD Sound Protocol worth testing, which is why it is an interface.
/// </summary>
public interface ISoundOutput : IDisposable
{
    /// <summary>
    /// Starts playing a file and returns a handle for it, or null if it could not be opened.
    /// </summary>
    /// <param name="path">A file on this machine.</param>
    /// <param name="volume">0 to 100.</param>
    int? Play(string path, int volume);

    /// <summary>Whether a handle is still making sound.</summary>
    bool IsPlaying(int handle);

    /// <summary>Plays a handle that has finished from the beginning again.</summary>
    void Replay(int handle);

    /// <summary>Stops a handle and releases it. A handle already stopped is not an error.</summary>
    void Stop(int handle);
}
