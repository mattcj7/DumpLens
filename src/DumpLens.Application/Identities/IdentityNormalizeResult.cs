namespace DumpLens.Application.Identities;

public sealed record IdentityNormalizeResult
{
    public required string IdentityType { get; init; }

    public string? RawValue { get; init; }

    public required string DisplayValue { get; init; }

    public required string NormalizedValue { get; init; }

    public required string Confidence { get; init; }

    public required IReadOnlyList<IdentityNormalizeWarning> Warnings { get; init; }
}
