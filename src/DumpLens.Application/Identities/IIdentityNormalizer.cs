namespace DumpLens.Application.Identities;

public interface IIdentityNormalizer
{
    IdentityNormalizeResult Normalize(IdentityNormalizeRequest request);
}
