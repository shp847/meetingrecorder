using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingReadingResolverTests
{
    private readonly MeetingReadingResolver _resolver = new();

    [Fact]
    public void Resolve_Keeps_Readable_Transcript_First_When_Summary_Fails()
    {
        var state = _resolver.Resolve(new(MeetingReadingTranscriptState.Readable, MeetingReadingSummaryState.Failed, 0, false, true, true));
        Assert.True(state.ShowTranscriptFirst);
        Assert.True(state.CanSearch);
        Assert.True(state.CanRetrySummary);
        Assert.Contains("ready", state.TranscriptStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Explains_Summary_Setup_Without_Exposing_Provider_Detail()
    {
        var state = _resolver.Resolve(new(MeetingReadingTranscriptState.Readable, MeetingReadingSummaryState.Unconfigured, 0, false, false, true));
        Assert.Equal(MeetingRecommendationActionTarget.SettingsSetup, state.SummaryActionTarget);
        Assert.Contains("consent", state.SummaryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", state.AccessibleDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Separates_Search_No_Match_From_Missing_Transcript()
    {
        var noMatch = _resolver.Resolve(new(MeetingReadingTranscriptState.SearchNoMatch, MeetingReadingSummaryState.Disabled, 0, false, true, true));
        var missing = _resolver.Resolve(new(MeetingReadingTranscriptState.Missing, MeetingReadingSummaryState.Disabled, 0, false, true, false));
        Assert.True(noMatch.CanSearch);
        Assert.False(missing.CanSearch);
        Assert.Contains("No transcript search", noMatch.TranscriptStatus);
    }

    [Fact]
    public void Resolve_Offers_Regeneration_Only_For_A_Stale_Summary()
    {
        var state = _resolver.Resolve(new(MeetingReadingTranscriptState.Readable, MeetingReadingSummaryState.Stale, 0, false, true, true));
        Assert.True(state.CanGenerateSummary);
        Assert.False(state.CanRetrySummary);
        Assert.True(state.ShowSummaryExpanded);
    }
}
