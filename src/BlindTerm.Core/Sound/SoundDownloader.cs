using BlindTerm.Core.Net;

namespace BlindTerm.Core.Sound;

/// <summary>
/// Fetches a sound a MUD has offered but this machine does not have.
///
/// This is off unless it is turned on, and deliberately so. A trigger's U parameter is an
/// address chosen by the server, and acting on it means fetching a file that server picked and
/// writing it to this disk. With it on, the rules are narrow: the address must be ordinary
/// web, the name must be a plain sound file name that <see cref="SoundLibrary"/> has already
/// accepted, the destination is inside the sound folder and nowhere else, the size is capped,
/// and a file already here is never overwritten -- a sound pack someone installed is theirs,
/// not the server's to replace.
/// </summary>
public sealed class SoundDownloader : IDisposable
{
    private readonly HttpClient _http;
    private readonly SoundLibrary _library;

    /// <summary>URLs already tried, so a MUD naming something it does not have is not a loop.</summary>
    private readonly HashSet<string> _attempted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>
    /// How many addresses are remembered before no more are attempted.
    ///
    /// A bound on a set that only ever grows: every distinct address the server names is
    /// remembered for the life of the session, and the addresses are the server's to choose.
    /// Past this, a session has already been asked to fetch far more than any sound pack
    /// holds, and refusing the rest costs nothing a user would notice.
    /// </summary>
    private const int MaximumAttempts = 512;

    public SoundDownloader(SoundLibrary library, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// The file for a trigger if it is here, starting a fetch in the background when it is
    /// not, and reporting which of those happened.
    ///
    /// Nothing is ever waited for. This is reached from the window's own thread at the moment
    /// a trigger arrives, and the address being fetched is the server's to choose: waiting on
    /// it there means a MUD that names a host which never answers freezes the terminal for
    /// the whole of the timeout, with the sound board locked behind the same gate. So the
    /// fetch runs on its own thread and the sound plays the next time the MUD asks -- which
    /// for the MUDs that use sound packs is after every room description.
    /// </summary>
    public MspFetch Fetch(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        Uri? source = SoundLibrary.DownloadFor(trigger);
        string? destination = _library.DestinationFor(trigger);
        if (source is null || destination is null) return MspFetch.No();

        if (File.Exists(destination)) return MspFetch.Here(destination);

        lock (_gate)
        {
            // One attempt per address. A MUD that names a sound it does not have would
            // otherwise send this back to the network on every room description.
            if (_attempted.Count >= MaximumAttempts || !_attempted.Add(source.AbsoluteUri))
                return MspFetch.No();
        }

        _ = Task.Run(() => FetchNow(source, destination));
        return MspFetch.Fetching();
    }

    /// <summary>
    /// Does the fetch and writes the file. Runs on a thread pool thread, off the window's own.
    /// </summary>
    private void FetchNow(Uri source, string destination)
    {
        string? temporary = null;
        try
        {
            string? folder = Path.GetDirectoryName(destination);
            if (folder is null) return;
            Directory.CreateDirectory(folder);
            if (File.Exists(destination)) return;

            using HttpResponseMessage response =
                _http.Send(new HttpRequestMessage(HttpMethod.Get, source), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return;
            if (response.Content.Headers.ContentLength > SoundLibrary.MaximumDownloadBytes) return;

            using Stream body = response.Content.ReadAsStream();
            // Written beside the destination and moved into place, so an interrupted download
            // never leaves half a sound to be played next time.
            temporary = destination + ".part";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (!CopyCapped(body, file)) return;
            }

            File.Move(temporary, destination, overwrite: false);
            temporary = null;
        }
        catch (Exception)
        {
            // A sound that cannot be fetched is a sound that does not play, and nothing about
            // a MUD session should stop because a web server did. Caught as everything rather
            // than as the likely set: this is a thread-pool task, where an exception nobody
            // observed is a silent leak rather than a message anybody reads.
        }
        finally
        {
            // Two ways to arrive here with a part file: the cap was reached mid-copy, or the
            // move failed because something else put a file there first. Either way it is
            // half a sound under a name nothing will ever look for, and left alone it
            // accumulates one per address the server names.
            if (temporary is not null) TryDelete(temporary);
        }
    }

    /// <summary>Copies up to the cap, and reports false if the source ran past it.</summary>
    private static bool CopyCapped(Stream source, Stream destination)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read <= 0) return true;
            total += read;
            if (total > SoundLibrary.MaximumDownloadBytes) return false;
            destination.Write(buffer, 0, read);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => _http.Dispose();
}
