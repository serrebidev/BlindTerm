using System.Text.Json.Serialization;

namespace BlindTerm.Core.Mud;

/// <summary>One thing that can be filtered on, with the directory's own identifier for it.</summary>
/// <param name="Count">
/// How many listings actually carry this, where the source can count them. Zero means nobody
/// counted, not that nothing matches.
///
/// It is here because a filter that returns nothing is worse than no filter at all. A list of
/// thirty-one genres where twenty-eight of them match one game apiece reads as thirty-one
/// equal choices, and every wrong one costs a fetch and an empty list. Saying the number
/// beside the name turns that into a decision anyone can make before pressing anything. It is
/// worked out from the listings in hand, so it is never written into the published file.
/// </param>
public sealed record MudTag(string Id, string Name, [property: JsonIgnore] int Count = 0);

/// <summary>
/// What a directory will let a list be narrowed by, as it describes itself.
///
/// Fetched rather than compiled in. A genre list written into BlindTerm would be wrong the
/// first time a directory added one, and there is no version of this program that ships fast
/// enough to keep up with somebody else's taxonomy.
/// </summary>
public sealed record MudDirectoryFilters
{
    public IReadOnlyList<MudTag> Themes { get; init; } = [];
    public IReadOnlyList<MudTag> GameTypes { get; init; } = [];
    public IReadOnlyList<MudTag> Roleplaying { get; init; } = [];

    public static MudDirectoryFilters None { get; } = new();
}
