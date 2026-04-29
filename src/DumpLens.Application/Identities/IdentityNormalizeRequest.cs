namespace DumpLens.Application.Identities;

public sealed record IdentityNormalizeRequest
{
    public required string IdentityType { get; init; }

    public string? RawValue { get; init; }

    public string? DisplayValue { get; init; }
}
