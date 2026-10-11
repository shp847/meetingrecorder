using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class RecoveryQualityCorpusRegistryTests
{
    [Fact]
    public void Summarize_Excludes_Revoked_Expired_And_Malformed_Records_Without_Payloads()
    {
        var now = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        var result = RecoveryQualityCorpusRegistry.Summarize([
            new("clip-a", new string('A', 64), "long-call", now.AddDays(1)),
            new("clip-b", new string('B', 64), "short-call", now.AddDays(1), now),
            new("clip-c", "bad", "low-speech", now.AddDays(1)),
            new("clip-d", new string('D', 64), "endpoint-switch", now)
        ], now);

        Assert.Equal(1, result.EligibleClipCount);
        Assert.Equal(3, result.ExcludedClipCount);
        Assert.Equal(1, result.EligibleByClassification["long-call"]);
    }
}
