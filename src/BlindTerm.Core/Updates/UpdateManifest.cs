using System.Text.Json.Serialization;

namespace BlindTerm.Core.Updates;

/// <summary>Release metadata published beside a BlindTerm GitHub release.</summary>
public sealed record UpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("asset")] public string Asset { get; init; } = string.Empty;
    [JsonPropertyName("download_url")] public string DownloadUrl { get; init; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = string.Empty;
    [JsonPropertyName("published_at")] public string PublishedAt { get; init; } = string.Empty;
    [JsonPropertyName("notes_summary")] public string NotesSummary { get; init; } = string.Empty;
}
