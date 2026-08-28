using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace BlindTerm.Core.Sound;

/// <summary>
/// Playback through the Windows multimedia control interface.
///
/// MCI is chosen over the simpler SoundPlayer because a MUD needs what SoundPlayer cannot do:
/// several sounds at once, a volume per sound, and formats other than WAV -- MUD sound packs
/// are full of MP3 and MIDI. It is in Windows already, so a terminal that plays a sound needs
/// nothing installed alongside it.
///
/// Every call is serialised. MCI keeps one table of open devices per process, and two threads
/// opening aliases at the same moment is how that table gets confused.
///
/// Call this from a thread in a COM apartment. The device that plays MP3 and WMA is
/// DirectShow underneath, and on a thread with no apartment it does not fail to play -- it
/// fails to load, and MCI reports only "unknown problem while loading the specified device
/// driver". BlindTerm's window thread is [STAThread], which is where every one of these calls
/// comes from; anything else calling this has to arrange the same.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MciSoundOutput : ISoundOutput
{
    private readonly Lock _gate = new();
    private readonly HashSet<int> _open = new();
    private readonly Dictionary<string, string> _staged =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _cache;
    private int _next;
    private bool _disposed;

    public int? Play(string path, int volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (_gate)
        {
            if (_disposed) return null;

            int handle = ++_next;
            string alias = Alias(handle);
            string? device = DeviceFor(Path.GetExtension(path));
            string type = device is null ? string.Empty : $" type {device}";

            // MCI is old enough to want backslashes and a full path, and it says nothing
            // useful when it does not get them: a folder typed with forward slashes, or a
            // relative one, simply plays no sound.
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                       or PathTooLongException or IOException
                                       or System.Security.SecurityException)
            {
                return null;
            }

            // MCI parses a fixed-length command string, so a sound sitting under a long path
            // fails to open with nothing said about why. The 8.3 form of the same path fits
            // where the long one does not.
            // MCI parses a fixed-length command string, so a sound under a long path fails to
            // open and says only that the file name is invalid. Rather than guess where the
            // limit falls, ask, and on refusal play a copy from somewhere short instead.
            if (Send(Open(full, type, alias)) != 0)
            {
                string? staged = Stage(full);
                if (staged is null || Send(Open(staged, type, alias)) != 0) return null;
            }
            _open.Add(handle);

            // MIDI has no volume of its own through MCI, so this fails harmlessly for a
            // sequencer file and the sound still plays at the system volume.
            Send($"setaudio {alias} volume to {Math.Clamp(volume, 0, 100) * 10}");

            if (Send($"play {alias}") != 0)
            {
                Send($"close {alias}");
                _open.Remove(handle);
                return null;
            }
            return handle;
        }
    }

    public bool IsPlaying(int handle)
    {
        lock (_gate)
        {
            if (_disposed || !_open.Contains(handle)) return false;
            var answer = new StringBuilder(64);
            if (Send($"status {Alias(handle)} mode", answer) != 0) return false;
            return answer.ToString().Trim().Equals("playing", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Replay(int handle)
    {
        lock (_gate)
        {
            if (_disposed || !_open.Contains(handle)) return;
            string alias = Alias(handle);
            Send($"seek {alias} to start");
            Send($"play {alias}");
        }
    }

    public void Stop(int handle)
    {
        lock (_gate)
        {
            if (!_open.Remove(handle)) return;
            string alias = Alias(handle);
            Send($"stop {alias}");
            Send($"close {alias}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (int handle in _open)
            {
                string alias = Alias(handle);
                Send($"stop {alias}");
                Send($"close {alias}");
            }
            _open.Clear();

            if (_cache is not null)
            {
                try { Directory.Delete(_cache, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                _cache = null;
            }
            _staged.Clear();
        }
    }

    /// <summary>
    /// An alias unique to this process, so two BlindTerm windows -- or a MUD and anything
    /// else using MCI -- cannot end up naming each other's sounds.
    /// </summary>
    private static string Alias(int handle)
        => $"bt{Environment.ProcessId:x}s{handle:x}";

    /// <summary>
    /// Which MCI device plays this. Naming it is more reliable than letting MCI infer one,
    /// and an unknown extension is left for MCI to work out rather than refused here --
    /// the extension allow list has already decided what may be opened at all.
    /// </summary>
    private static string? DeviceFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".wav" or ".au" or ".aif" or ".aiff" => "waveaudio",
        ".mid" or ".midi" or ".rmi" => "sequencer",
        ".mp3" or ".wma" => "mpegvideo",
        _ => null,
    };

    private static string Open(string path, string type, string alias)
        => $"open \"{path}\"{type} alias {alias}";

    /// <summary>
    /// The shortest name Windows has for this file. Falls back to the name given, which is
    /// what a volume with short names turned off gives back -- most of them, these days.
    /// </summary>
    private static string Shortest(string path)
    {
        var buffer = new StringBuilder(320);
        int length = GetShortPathNameW(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    /// <summary>
    /// Puts a copy of a sound somewhere with a short enough name for MCI, and returns it.
    ///
    /// Only reached when opening the real one has already been refused, and only once per
    /// file: a sound pack under a deep folder is copied the first time it is heard and played
    /// from the copy after that. The copies go with the process.
    /// </summary>
    private string? Stage(string path)
    {
        if (_staged.TryGetValue(path, out string? existing)) return existing;

        try
        {
            _cache ??= Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "BT" + Environment.ProcessId.ToString("x"))).FullName;

            // Named for the path it came from, so two sounds with the same file name in
            // different folders do not become one.
            string name = Math.Abs(path.GetHashCode(StringComparison.OrdinalIgnoreCase))
                .ToString("x8") + Path.GetExtension(path);
            string copy = Path.Combine(_cache, name);
            if (!File.Exists(copy)) File.Copy(path, copy);
            _staged[path] = copy;
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static int Send(string command, StringBuilder? answer = null)
        => mciSendStringW(command, answer, answer?.Capacity ?? 0, IntPtr.Zero);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int mciSendStringW(string command, StringBuilder? returnValue,
                                             int returnLength, IntPtr callback);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW",
               SetLastError = true)]
    private static extern int GetShortPathNameW(string longPath, StringBuilder shortPath,
                                                int shortPathLength);
}
