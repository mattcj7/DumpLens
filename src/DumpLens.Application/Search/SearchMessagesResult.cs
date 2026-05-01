namespace DumpLens.Application.Search;

public sealed record SearchMessagesResult
{
    public required string CaseId { get; init; }

    public required bool IsQueryValid { get; init; }

    public string? ValidationErrorCode { get; init; }

    public string? ValidationMessage { get; init; }

    public required int ResultCount { get; init; }

    public IReadOnlyList<MessageSearchResult> Results { get; init; } = [];

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
