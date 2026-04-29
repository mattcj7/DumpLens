namespace DumpLens.Application.Identities;

public sealed record IdentityNormalizeWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
