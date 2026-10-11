namespace MeetingRecorder.Core.Services;

public enum MeetingReadingTranscriptState { Readable = 0, Missing = 1, Loading = 2, Corrupt = 3, SearchNoMatch = 4 }
public enum MeetingReadingSummaryState { Generated = 0, Generating = 1, Disabled = 2, Unconfigured = 3, Unavailable = 4, Failed = 5, Stale = 6 }

public sealed record MeetingReadingInput(
    MeetingReadingTranscriptState Transcript,
    MeetingReadingSummaryState Summary,
    int SearchMatchCount,
    bool IsSummaryExpanded,
    bool HasCurrentConsent,
    bool HasStructuredTranscript);

public sealed record MeetingReadingState(
    MeetingReadingTranscriptState Transcript,
    MeetingReadingSummaryState Summary,
    string TranscriptStatus,
    string SummaryStatus,
    bool ShowTranscriptFirst,
    bool ShowSummaryExpanded,
    bool CanSearch,
    bool CanGenerateSummary,
    bool CanRetrySummary,
    MeetingRecommendationActionTarget SummaryActionTarget,
    string AccessibleDescription);

/// <summary>Pure transcript-first detail presentation; it reads no artifact or provider payload.</summary>
public sealed class MeetingReadingResolver
{
    public MeetingReadingState Resolve(MeetingReadingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var transcriptStatus = input.Transcript switch
        {
            MeetingReadingTranscriptState.Readable => input.SearchMatchCount == 0 ? "Transcript is ready." : $"Transcript search has {input.SearchMatchCount} match(es).",
            MeetingReadingTranscriptState.SearchNoMatch => "No transcript search matches.",
            MeetingReadingTranscriptState.Loading => "Transcript is loading.",
            MeetingReadingTranscriptState.Corrupt => "Transcript needs repair.",
            _ => "Transcript is unavailable.",
        };
        var summary = ResolveSummary(input);
        return new(input.Transcript, input.Summary, transcriptStatus, summary.Status,
            ShowTranscriptFirst: true,
            ShowSummaryExpanded: input.Summary == MeetingReadingSummaryState.Generated || input.IsSummaryExpanded || input.Summary is MeetingReadingSummaryState.Failed or MeetingReadingSummaryState.Stale,
            CanSearch: input.Transcript is MeetingReadingTranscriptState.Readable or MeetingReadingTranscriptState.SearchNoMatch,
            summary.CanGenerate, summary.CanRetry, summary.Target,
            $"{transcriptStatus} {summary.Status}");
    }

    private static (string Status, bool CanGenerate, bool CanRetry, MeetingRecommendationActionTarget Target) ResolveSummary(MeetingReadingInput input) => input.Summary switch
    {
        MeetingReadingSummaryState.Generated => ("Summary is ready.", false, false, MeetingRecommendationActionTarget.None),
        MeetingReadingSummaryState.Generating => ("Summary is generating.", false, false, MeetingRecommendationActionTarget.None),
        MeetingReadingSummaryState.Disabled => ("Summaries are off.", false, false, MeetingRecommendationActionTarget.SettingsSetup),
        MeetingReadingSummaryState.Unconfigured => (input.HasCurrentConsent ? "Summary setup is incomplete." : "Summary setup and consent are required.", false, false, MeetingRecommendationActionTarget.SettingsSetup),
        MeetingReadingSummaryState.Unavailable => (input.HasStructuredTranscript ? "Summary is unavailable." : "Summary needs a structured transcript.", false, false, MeetingRecommendationActionTarget.MeetingDetails),
        MeetingReadingSummaryState.Failed => ("Summary failed; retry is available from the current transcript.", false, true, MeetingRecommendationActionTarget.MeetingDetails),
        MeetingReadingSummaryState.Stale => ("Summary is stale for this transcript; regenerate it after review.", true, false, MeetingRecommendationActionTarget.MeetingDetails),
        _ => ("Generate a summary from the current transcript.", true, false, MeetingRecommendationActionTarget.MeetingDetails),
    };
}
