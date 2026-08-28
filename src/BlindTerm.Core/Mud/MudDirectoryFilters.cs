namespace BlindTerm.Core.Mud;

/// <summary>One thing that can be filtered on, with the directory's own identifier for it.</summary>
public sealed record MudTag(string Id, string Name);

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
