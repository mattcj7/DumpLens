using DumpLens.Core.Audit;

namespace DumpLens.Tests.Unit.Core.Audit;

public sealed class AuditEventCanonicalizerTests
{
    [Fact]
    public void CreateCanonicalJson_NormalizesJsonPayloadsAndUtcTimestampDeterministically()
    {
        var input = new AuditEventHashInput(
            "audit-1",
            "case-1",
            "user-1",
            "case_update",
            "case",
            "case-1",
            "Updated synthetic case",
            """
            { "b": 2, "a": { "y": 2, "x": 1 } }
            """,
            """
            { "list": [3, {"z": 9, "a": 1}], "a": { "y": 2, "x": 1 } }
            """,
            "synthetic reason",
            new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(2)),
            "WS-01",
            "1.0.0");

        var canonicalJson = AuditEventCanonicalizer.CreateCanonicalJson(input);

        Assert.Equal(
            """{"id":"audit-1","case_id":"case-1","user_id":"user-1","action_type":"case_update","entity_type":"case","entity_id":"case-1","summary":"Updated synthetic case","old_value_json":{"a":{"x":1,"y":2},"b":2},"new_value_json":{"a":{"x":1,"y":2},"list":[3,{"a":1,"z":9}]},"reason":"synthetic reason","event_time_utc":"2026-02-03T02:05:06.0000000+00:00","workstation":"WS-01","app_version":"1.0.0"}""",
            canonicalJson);
    }

    [Fact]
    public void CreateCanonicalJson_TreatsEquivalentJsonPropertyOrderAsIdentical()
    {
        var left = new AuditEventHashInput(
            "audit-2",
            "case-1",
            null,
            "entity_update",
            "artifact",
            "artifact-1",
            "Updated synthetic artifact",
            """{"b":2,"a":1}""",
            """{"c":[{"y":2,"x":1}]}""",
            null,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            null);
        var right = new AuditEventHashInput(
            "audit-2",
            "case-1",
            null,
            "entity_update",
            "artifact",
            "artifact-1",
            "Updated synthetic artifact",
            """{ "a": 1, "b": 2 }""",
            """{"c":[{"x":1,"y":2}]}""",
            null,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            null);

        Assert.Equal(
            AuditEventCanonicalizer.CreateCanonicalJson(left),
            AuditEventCanonicalizer.CreateCanonicalJson(right));
    }

    [Fact]
    public void ComputeHash_UsesGenesisForFirstEventAndChangesWhenInputsChange()
    {
        const string canonicalJson = """{"id":"audit-3","summary":"Synthetic"}""";

        var firstHash = AuditChainHash.ComputeHash(null, canonicalJson);
        var repeatedHash = AuditChainHash.ComputeHash(string.Empty, canonicalJson);
        var differentPreviousHash = AuditChainHash.ComputeHash("prior-hash", canonicalJson);
        var differentContentHash = AuditChainHash.ComputeHash(null, """{"id":"audit-3","summary":"Changed"}""");

        Assert.Equal(firstHash, repeatedHash);
        Assert.NotEqual(firstHash, differentPreviousHash);
        Assert.NotEqual(firstHash, differentContentHash);
    }
}
