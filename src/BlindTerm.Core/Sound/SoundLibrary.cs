using BlindTerm.Core.Net;

namespace BlindTerm.Core.Sound;

/// <summary>
/// Turns the file name in a trigger into a file on this machine, or decides there is not one.
///
/// Everything here is a rule about what a server is allowed to name. A MUD sends the name of a
/// sound; it does not get to name a path. A trigger asking for "..\..\Startup\evil.exe" names
/// a place outside the sound folder, and one asking for a file the client would run rather
/// than play names something that is not a sound at all. Both are refused by name, before
/// anything is opened, downloaded or played.
/// </summary>
public sealed class SoundLibrary
{
    /// <summary>
    /// What a sound may be. Nothing outside this list is opened, and nothing outside it is
    /// ever written to disk by a download.
    /// </summary>
    public static readonly IReadOnlySet<string> PlayableExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".mid", ".midi", ".rmi", ".wma", ".au", ".aif", ".aiff",
        };

    /// <summary>A download that is larger than this is refused rather than written.</summary>
    public const int MaximumDownloadBytes = 16 * 1024 * 1024;

    private readonly Func<string, IEnumerable<string>> _matchingFiles;
    private readonly Func<string, bool> _exists;
    private readonly Random _random;

    public string Directory { get; }

    /// <param name="directory">Where sound files live.</param>
    /// <param name="exists">Whether a file exists, for tests.</param>
    /// <param name="matchingFiles">Files matching a wildcard path, for tests.</param>
    /// <param name="random">Which of several matches to choose, for tests.</param>
    public SoundLibrary(
        string directory,
        Func<string, bool>? exists = null,
        Func<string, IEnumerable<string>>? matchingFiles = null,
        Random? random = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        _exists = exists ?? File.Exists;
        _matchingFiles = matchingFiles ?? DefaultMatches;
        _random = random ?? Random.Shared;
    }

    /// <summary>The default place sound packs are unpacked into.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlindTerm", "sounds");

    /// <summary>
    /// The file to play for a trigger, or null if the server named something that is not a
    /// sound, or named a sound that is not here.
    ///
    /// A name may contain wildcards, which is how a MUD asks for "one of the sword sounds".
    /// One of the matches is chosen at random, which is the point of asking that way.
    /// </summary>
    public string? Resolve(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (RelativePath(trigger.FileName) is not { } relative) return null;

        string folder = FolderFor(trigger.Type);
        if (folder.Length == 0) return null;

        if (trigger.FileName.Contains('*') || trigger.FileName.Contains('?'))
        {
            string[] matches = [.. _matchingFiles(Path.Combine(folder, relative))
                .Where(match => PlayableExtensions.Contains(Path.GetExtension(match)))
                .OrderBy(match => match, StringComparer.OrdinalIgnoreCase)];
            return matches.Length == 0 ? null : matches[_random.Next(matches.Length)];
        }

        string path = Path.Combine(folder, relative);
        return _exists(path) ? path : null;
    }

    /// <summary>Where a trigger's file would live if it were downloaded.</summary>
    public string? DestinationFor(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        // A wildcard names a choice among files already here, not a file to fetch.
        if (RelativePath(trigger.FileName) is not { } relative) return null;
        if (trigger.FileName.Contains('*') || trigger.FileName.Contains('?')) return null;

        string folder = FolderFor(trigger.Type);
        return folder.Length == 0 ? null : Path.Combine(folder, relative);
    }

    /// <summary>
    /// Where a missing file may be fetched from, or null if the server did not say, or said
    /// something other than an ordinary web address.
    /// </summary>
    public static Uri? DownloadFor(MspTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (trigger.Url is null || !IsSafeName(trigger.FileName)) return null;
        if (trigger.FileName.Contains('*') || trigger.FileName.Contains('?')) return null;
        if (trigger.Type is not null && !IsSafeSegment(trigger.Type)) return null;

        string root = trigger.Url.EndsWith('/') ? trigger.Url : trigger.Url + "/";
        string relative = trigger.Type is null
            ? trigger.FileName
            : $"{trigger.Type}/{trigger.FileName}";

        if (!Uri.TryCreate(root + relative, UriKind.Absolute, out Uri? uri)) return null;
        // A sound comes over the web or not at all. file:// would read this machine's own
        // disk at a path the server chose, which is not a download.
        return uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>
    /// Whether a name is one a server may use: a sound file, named relative to the sound
    /// folder, with an extension this can actually play, and nothing that would step outside
    /// that folder.
    ///
    /// A MUD keeps its sounds in folders and names them that way -- Core MUD's own test sound
    /// is "mp3/msptest.mp3" -- so a name is one or more parts joined by forward slashes.
    /// Every part still has to be an ordinary name. "..", a drive letter, a backslash or an
    /// empty part all describe a path rather than a name, and are refused where they always
    /// were.
    /// </summary>
    public static bool IsSafeName(string? name)
        => Parts(name) is { } parts && PlayableExtensions.Contains(Path.GetExtension(parts[^1]));

    /// <summary>How many folders deep a server may name. Deeper than any sound pack needs.</summary>
    private const int MaximumDepth = 8;

    /// <summary>
    /// The parts of a name a server may use, or null when it is not one.
    ///
    /// Wildcards are allowed only in the last part: a MUD asks for one of the sword sounds,
    /// never for one of the folders, and matching folders would let a pattern walk the disk.
    /// </summary>
    private static string[]? Parts(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        // A backslash is a separator to Windows and an ordinary character to the protocol.
        // Refusing it outright keeps the two readings from ever disagreeing about a name.
        if (name.Contains('\\')) return null;

        string[] parts = name.Split('/');
        if (parts.Length > MaximumDepth) return null;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!IsSafeSegment(parts[i])) return null;
            if (i < parts.Length - 1 && (parts[i].Contains('*') || parts[i].Contains('?')))
                return null;
        }
        return parts;
    }

    /// <summary>The name as a path under the sound folder, or null when it is not allowed.</summary>
    private static string? RelativePath(string? name)
    {
        if (Parts(name) is not { } parts) return null;
        return PlayableExtensions.Contains(Path.GetExtension(parts[^1]))
            ? Path.Combine(parts)
            : null;
    }

    /// <summary>Whether a name is a single ordinary path segment and nothing more.</summary>
    public static bool IsSafeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment.Length > 128) return false;
        if (segment is "." or "..") return false;
        if (segment.Contains("..", StringComparison.Ordinal)) return false;
        if (segment.Contains('/') || segment.Contains('\\')) return false;
        // A drive letter, a device name or a stream would all be a path rather than a name.
        if (segment.Contains(':')) return false;
        return segment.IndexOfAny(Path.GetInvalidFileNameChars()
            .Where(c => c is not '*' and not '?').ToArray()) < 0;
    }

    private string FolderFor(string? type)
    {
        if (type is null) return Directory;
        return IsSafeSegment(type) ? Path.Combine(Directory, type) : string.Empty;
    }

    private static IEnumerable<string> DefaultMatches(string pattern)
    {
        string? folder = Path.GetDirectoryName(pattern);
        string name = Path.GetFileName(pattern);
        if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) return [];
        try
        {
            return System.IO.Directory.EnumerateFiles(folder, name, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException)
        {
            return [];
        }
    }
}
