using System.Text.Json.Serialization;

namespace DumpLens.Application.CasePackages;

public sealed record CasePackageManifest
{
    [JsonPropertyName("package_version")]
    public required string PackageVersion { get; init; }

    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("case_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaseNumber { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("created_at_utc")]
    public required string CreatedAtUtc { get; init; }

    [JsonPropertyName("app_name")]
    public required string AppName { get; init; }

    [JsonPropertyName("database_relative_path")]
    public required string DatabaseRelativePath { get; init; }

    [JsonPropertyName("preparation_mode")]
    public required string PreparationMode { get; init; }

    [JsonPropertyName("folders")]
    public required IReadOnlyDictionary<string, string> Folders { get; init; }
}
