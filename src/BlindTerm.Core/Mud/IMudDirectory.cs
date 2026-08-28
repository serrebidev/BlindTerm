namespace BlindTerm.Core.Mud;

/// <summary>
/// A place BlindTerm can ask what MUDs exist.
///
/// An interface rather than one class because no single directory is going to last. MUDVerse
/// is a week old, MUDStats has the better activity figures and no published API, and The Mud
/// Connector's ranking stopped in 2021. Whichever of them is the right answer in two years,
/// the browser window and the normalising in <see cref="MudGame"/> should not have to change
/// for it.
/// </summary>
/// <remarks>Every source of these holds a connection to somewhere, so every source is
/// disposable and the caller never has to know which kind it got.</remarks>
public interface IMudDirectory : IDisposable
{
    /// <summary>What to call this source out loud. Appears in the window and in errors.</summary>
    string Name { get; }

    /// <summary>
    /// The most listings this source will hand over in one <see cref="SearchAsync"/>.
    ///
    /// Asked rather than assumed, because the two answers are nothing like each other. A
    /// source read over somebody else's API pages at whatever size that API allows; a source
    /// that downloaded its whole list already has all of it in memory and cutting it into
    /// twenty-fives only makes the person browsing press a button eight hundred times. Fifty
    /// is the cautious default for anything that has not said.
    /// </summary>
    int PageSizeLimit => 50;

    /// <summary>The genres, game types and roleplaying policies this source knows about.</summary>
    Task<MudDirectoryFilters> FiltersAsync(CancellationToken cancellationToken = default);

    Task<MudDirectoryPage> SearchAsync(MudDirectoryQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// A directory could not answer, in words that can go straight into a dialog.
///
/// The message is the point. "Response status code does not indicate success: 401" tells
/// somebody nothing they can act on; "MUDVerse did not accept the API key" tells them exactly
/// which thing to go and fix.
/// </summary>
public sealed class MudDirectoryException : Exception
{
    public MudDirectoryException(string message, bool isAuthentication = false,
        bool worthRetrying = false, Exception? inner = null)
        : base(message, inner)
    {
        IsAuthentication = isAuthentication;
        IsWorthRetrying = worthRetrying;
    }

    /// <summary>Whether the fix is a key rather than a retry, so the window can offer the key.</summary>
    public bool IsAuthentication { get; }

    /// <summary>
    /// Whether this is the sort of failure that goes away on its own -- a timeout, a dropped
    /// connection, a server having a moment -- rather than one that will fail identically
    /// however many times it is asked. A rejected key is not worth asking twice.
    /// </summary>
    public bool IsWorthRetrying { get; }
}
