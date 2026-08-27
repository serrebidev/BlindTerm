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
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public SoundDownloader(SoundLibrary library, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>
    /// Fetches the trigger's file if it is missing, and returns where it now is, or null if it
    /// could not or should not be fetched.
    /// </summary>
    public string? Fetch(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        Uri? source = SoundLibrary.DownloadFor(trigger);
        string? destination = _library.DestinationFor(trigger);
        if (source is null || destination is null) return null;

        lock (_gate)
        {
            // One attempt per address. A MUD that names a sound it does not have would
            // otherwise send this back to the network on every room description.
            if (!_failed.Add(source.AbsoluteUri)) return null;
        }

        try
        {
            string? folder = Path.GetDirectoryName(destination);
            if (folder is null) return null;
            Directory.CreateDirectory(folder);
            if (File.Exists(destination)) return destination;

            using HttpResponseMessage response =
                _http.Send(new HttpRequestMessage(HttpMethod.Get, source), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > SoundLibrary.MaximumDownloadBytes) return null;

            using Stream body = response.Content.ReadAsStream();
            // Written beside the destination and moved into place, so an interrupted download
            // never leaves half a sound to be played next time.
            string temporary = destination + ".part";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (!CopyCapped(body, file)) { file.Dispose(); TryDelete(temporary); return null; }
            }

            File.Move(temporary, destination, overwrite: false);
            lock (_gate) _failed.Remove(source.AbsoluteUri);
            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException
                                   or UnauthorizedAccessException or NotSupportedException
                                   or InvalidOperationException)
        {
            // A sound that cannot be fetched is a sound that does not play. Nothing about a
            // MUD session should stop because a web server did.
            return null;
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
