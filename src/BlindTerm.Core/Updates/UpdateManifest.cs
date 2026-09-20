using System.Text.Json.Serialization;

namespace BlindTerm.Core.Updates;

/// <summary>Release metadata published beside a BlindTerm GitHub release.</summary>
/// <remarks>
/// Every field is nullable, and deliberately so. These are read from a file somebody else
/// publishes, and JSON has a null where the property initializer below would have put an
/// empty string -- the initializer only applies when the key is absent, not when it is
/// written out as null. Declaring them non-null would be a claim about a document this
/// program does not control, and the first such null would be a NullReferenceException in
/// whichever check ran first.
/// </remarks>
public sealed record UpdateManifest
{
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("asset")] public string? Asset { get; init; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; init; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
    [JsonPropertyName("published_at")] public string? PublishedAt { get; init; }
    [JsonPropertyName("notes_summary")] public string? NotesSummary { get; init; }
}
