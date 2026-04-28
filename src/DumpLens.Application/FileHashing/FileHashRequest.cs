namespace DumpLens.Application.FileHashing;

public sealed record FileHashRequest
{
    public required string FilePath { get; init; }

    public FileHashAlgorithm Algorithm { get; init; } = FileHashAlgorithm.Sha256;

    public string? CorrelationId { get; init; }
}
