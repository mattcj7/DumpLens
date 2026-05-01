namespace DumpLens.Application.Search;

public sealed record RebuildMessageSearchIndexResult
{
    public required string CaseId { get; init; }

    public required int IndexedCount { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
