namespace DumpLens.Application.Timestamps;

public interface ITimestampNormalizer
{
    TimestampNormalizeResult Normalize(TimestampNormalizeRequest request);
}
