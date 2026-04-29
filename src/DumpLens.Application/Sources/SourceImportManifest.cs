using System.Text.Json.Serialization;

namespace DumpLens.Application.Sources;

public sealed record SourceImportManifest
{
    [JsonPropertyName("manifest_version")]
    public required string ManifestVersion { get; init; }

    [JsonPropertyName("source_import_id")]
    public required string SourceImportId { get; init; }

    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("source_name")]
    public required string SourceName { get; init; }

    [JsonPropertyName("source_type")]
    public required string SourceType { get; init; }

    [JsonPropertyName("platform")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Platform { get; init; }

    [JsonPropertyName("original_filename")]
    public required string OriginalFilename { get; init; }

    [JsonPropertyName("stored_relative_path")]
    public required string StoredRelativePath { get; init; }

    [JsonPropertyName("file_size_bytes")]
    public required long FileSizeBytes { get; init; }

    [JsonPropertyName("file_sha256")]
    public required string FileSha256 { get; init; }

    [JsonPropertyName("imported_at_utc")]
    public required string ImportedAtUtc { get; init; }

    [JsonPropertyName("source_folder_relative_path")]
    public required string SourceFolderRelativePath { get; init; }

    [JsonPropertyName("sha256_relative_path")]
    public required string Sha256RelativePath { get; init; }

    [JsonPropertyName("app_name")]
    public required string AppName { get; init; }

    [JsonPropertyName("copy_mode")]
    public required string CopyMode { get; init; }
}
