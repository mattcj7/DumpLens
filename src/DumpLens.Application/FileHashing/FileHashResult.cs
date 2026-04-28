namespace DumpLens.Application.FileHashing;

public sealed record FileHashResult
{
    public required string CorrelationId { get; init; }

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required FileHashAlgorithm Algorithm { get; init; }

    public required string HexDigest { get; init; }

    public required long FileSizeBytes { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }

    public required TimeSpan Duration { get; init; }
}
