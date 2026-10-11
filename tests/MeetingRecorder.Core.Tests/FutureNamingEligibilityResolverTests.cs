using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class FutureNamingEligibilityResolverTests
{
    [Theory]
    [InlineData(false, true, true, true, true, true, true, true, true, false, FutureNamingEligibility.Disabled)]
    [InlineData(true, false, true, true, true, true, true, true, true, false, FutureNamingEligibility.NoSamples)]
    [InlineData(true, true, false, true, true, true, true, true, true, false, FutureNamingEligibility.NoProfiles)]
    [InlineData(true, true, true, true, true, true, true, true, true, true, FutureNamingEligibility.Suppressed)]
    [InlineData(true, true, true, true, true, false, true, true, true, false, FutureNamingEligibility.BelowThreshold)]
    [InlineData(true, true, true, true, true, true, true, false, false, false, FutureNamingEligibility.Ambiguous)]
    public void Resolve_UsesConservativePrecedence(bool enabled, bool sample, bool profile, bool mature, bool duration, bool suggestion, bool auto, bool margin, bool duplicate, bool suppressed, FutureNamingEligibility expected)
    {
        var result = FutureNamingEligibilityResolver.Resolve(new("r1", "speaker-1", "policy-1", enabled, sample, profile, mature, duration, suggestion, auto, margin, duplicate, suppressed));
        Assert.Equal(expected, result.Eligibility);
        Assert.False(result.MayAutoApply);
    }

    [Fact]
    public void Resolve_AutoApplyRequiresEveryGate_AndSuggestionDoesNotRename()
    {
        var auto = FutureNamingEligibilityResolver.Resolve(new("r1", "speaker-1", "policy-1", true, true, true, true, true, true, true, true, false, false));
        var suggestion = FutureNamingEligibilityResolver.Resolve(new("r1", "speaker-1", "policy-1", true, true, true, true, true, true, false, true, false, false));
        Assert.True(auto.MayAutoApply);
        Assert.True(suggestion.MaySuggest);
        Assert.False(suggestion.MayAutoApply);
    }
}
