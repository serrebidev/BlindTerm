namespace BlindTerm.Core.Sound;

/// <summary>
/// What came of asking for a sound this machine did not have.
///
/// Three answers rather than a path or nothing, because "not yet" and "not ever" are
/// different things to say out loud. A sound being fetched is going to play in a moment and
/// nobody needs telling; a sound that could not be fetched is worth a sentence, because the
/// person hearing nothing otherwise has no way to tell a broken address from a setting left
/// off from a sound folder that is simply empty.
/// </summary>
/// <param name="Path">Where it is now, when it is here at all.</param>
/// <param name="Pending">Whether it is on its way, so it will be here next time.</param>
public readonly record struct MspFetch(string? Path, bool Pending)
{
    /// <summary>It is here, and can be played now.</summary>
    public static MspFetch Here(string path) => new(path, false);

    /// <summary>
    /// It is not here yet, and is being fetched.
    ///
    /// Never the same as failure, and never waited on: see <see cref="SoundDownloader.Fetch"/>
    /// for why the fetch cannot happen on the thread that asked.
    /// </summary>
    public static MspFetch Fetching() => new(null, true);

    /// <summary>It is not here, and nothing is bringing it.</summary>
    public static MspFetch No() => new(null, false);
}
