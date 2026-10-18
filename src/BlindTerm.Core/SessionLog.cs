using System.Text;

namespace BlindTerm.Core;

/// <summary>
/// A session written to a file as it happens, for reading back later.
///
/// The log holds what arrived -- the lines of the transcript, as they were finished --
/// and nothing else. BlindTerm never writes what was typed into it: typing lives in the
/// command line, not the transcript, and a password being asked for is not echoed by the
/// MUD either, which is the whole of how passwords are asked for. The one thing to know is
/// that a MUD which echoes a password anyway would put it here, exactly as it already put
/// it in the transcript on the screen.
///
/// Writing must never be able to break the session it is recording, so every failure --
/// a full disk, a folder taken away -- is swallowed and remembered as <see cref="Failed"/>
/// rather than thrown at a session that was working until it tried to keep a note of
/// itself.
/// </summary>
public sealed class SessionLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private bool _disposed;

    private SessionLog(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    /// <summary>The file being written, in full.</summary>
    public string FilePath { get; }

    /// <summary>Whether writing has failed. A failed log writes nothing more.</summary>
    public bool Failed { get; private set; }

    /// <summary>Where session logs live when nobody has chosen anywhere else.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlindTerm", "logs");

    /// <summary>
    /// Starts a log in a folder, named for the session and the moment it started:
    /// "coremud.org 4000-20261018-103045.log". Says whether it could.
    /// </summary>
    /// <param name="directory">The folder to write in. Created if it is not there.</param>
    /// <param name="slug">
    /// What the session is called -- a host and port, usually. Made safe for a file name
    /// here, so a window title can be handed over as it stands.
    /// </param>
    /// <param name="started">When the log starts, which is part of its name.</param>
    public static bool TryStart(string directory, string slug, DateTimeOffset started,
        out SessionLog? log)
    {
        log = null;
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                $"{Slug(slug)}-{started:yyyyMMdd-HHmmss}.log");
            // Readable while it is being written, so the log can be followed from another
            // window -- the point of keeping one during a session rather than after it.
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read);
            log = new SessionLog(path, new StreamWriter(stream, new UTF8Encoding(false)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Writes one line and whatever newline ends it.</summary>
    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_disposed || Failed) return;
            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Failed = true;
            }
        }
    }

    /// <summary>Writes a batch of finished lines, in order.</summary>
    public void Write(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        lock (_gate)
        {
            if (_disposed || Failed) return;
            try
            {
                foreach (string line in lines) _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Failed = true;
            }
        }
    }

    /// <summary>
    /// A session's name made into something a file can be called: letters, digits, dots
    /// and dashes kept, everything else a dash, and never longer than a name needs to be.
    /// </summary>
    private static string Slug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return "session";
        var safe = new StringBuilder(slug.Length);
        foreach (char c in slug.Trim())
            safe.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        string name = safe.ToString().Trim('-');
        if (name.Length > 60) name = name[..60].TrimEnd('-');
        return name.Length == 0 ? "session" : name;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _writer.Dispose(); }
            catch (IOException) { }
        }
    }
}
