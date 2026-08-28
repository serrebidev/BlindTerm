namespace BlindTerm.Core.Mud;

/// <summary>
/// Picks which directory to read, from what the user has configured.
///
/// The ordinary case is that they have configured nothing, and that is the case this exists
/// to serve: browsing MUDs asks nobody for anything. BlindTerm publishes the list, so the
/// program that reads it needs no credentials of its own.
///
/// A key is the escape hatch for someone who wants the counts live to the minute rather than
/// to the half hour, or who is running at a moment when the published list is not there.
/// </summary>
public static class MudDirectories
{
    /// <param name="key">A MUDVerse API key, or blank for the published list.</param>
    /// <param name="url">
    /// With a key, the MUDVerse API base to send it to. Without one, where the published list
    /// is. Blank means the usual place for whichever of those applies.
    /// </param>
    public static IMudDirectory Open(string? key, string? url)
        => string.IsNullOrWhiteSpace(key)
            ? new MudFeedDirectory(url)
            : new MudVerseDirectory(key, url);

    /// <summary>
    /// Whether a directory is reading BlindTerm's published list rather than MUDVerse live.
    /// The browser says which, because a player count from the list has an age.
    /// </summary>
    public static bool IsPublishedList(IMudDirectory directory) => directory is MudFeedDirectory;
}
