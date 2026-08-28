namespace BlindTerm.Core.Sound;

/// <summary>
/// Why a sound a MUD asked for was not heard, when the reason is one a user can act on.
///
/// Silence is the wrong answer on its own. A MUD says "attempting test sound", nothing
/// happens, and there is no way to tell an empty sound folder from a setting left off, a
/// download that failed, or a server naming a file that is not a sound at all.
/// </summary>
public enum MspProblem
{
    /// <summary>The sound is not on this machine and BlindTerm is not allowed to fetch it.</summary>
    NotHere,

    /// <summary>It is not here, and could not be fetched from where the MUD said it lives.</summary>
    CouldNotFetch,

    /// <summary>The MUD named something that is not a sound BlindTerm will play.</summary>
    Refused,

    /// <summary>
    /// The file is here and was accepted, and Windows would not play it. A missing codec, a
    /// file that is not really the format its name claims, or an audio device that refused.
    /// </summary>
    CannotPlay,
}
