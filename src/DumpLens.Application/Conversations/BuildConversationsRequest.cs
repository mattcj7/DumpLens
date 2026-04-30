namespace DumpLens.Application.Conversations;

public sealed record BuildConversationsRequest
{
    public required string CaseId { get; init; }

    public required string CaseDatabasePath { get; init; }

    public string? SourceImportId { get; init; }

    public bool RebuildExisting { get; init; }

    public string? CorrelationId { get; init; }
}
