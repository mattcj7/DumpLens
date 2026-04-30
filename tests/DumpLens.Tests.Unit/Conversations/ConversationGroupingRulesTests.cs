using DumpLens.Application.Conversations;

namespace DumpLens.Tests.Unit.Conversations;

public sealed class ConversationGroupingRulesTests
{
    [Fact]
    public void NormalizeParticipantKey_SortsDistinctValuesAndIgnoresWhitespace()
    {
        var key = ConversationGroupingRules.NormalizeParticipantKey(
            [" b-id ", "a-id", "b-id", null, string.Empty, "c-id"]);

        Assert.Equal("a-id|b-id|c-id", key);
    }

    [Fact]
    public void BuildParticipantGroupKey_NormalizesPlatformAndUsesStablePrefix()
    {
        var key = ConversationGroupingRules.BuildParticipantGroupKey(" SMS ", "a-id|b-id");

        Assert.Equal("participant_set|sms|a-id|b-id", key);
    }

    [Fact]
    public void BuildThreadGroupKey_NormalizesPlatformAndPreservesThreadValue()
    {
        var key = ConversationGroupingRules.BuildThreadGroupKey("Signal", " thread-001 ");

        Assert.Equal("source_thread|signal|thread-001", key);
    }

    [Fact]
    public void BuildSafeTitle_UsesGenericEvidenceSafeTitle()
    {
        Assert.Equal("signal conversation (3 participants)", ConversationGroupingRules.BuildSafeTitle("signal", 3));
        Assert.Equal("Conversation", ConversationGroupingRules.BuildSafeTitle(null, 0));
    }
}
