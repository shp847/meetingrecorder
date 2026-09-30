using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public enum MeetingSummaryAttributionState { Current, AttributionChanged, TranscriptChanged, Unavailable, Generating, Failed }
public sealed record SummaryAttributionSegment(string StableSegmentId, string EffectiveSpeakerId, string DisplayLabel, string Source);
public sealed record MeetingSummaryAttributionSnapshot(string TranscriptFingerprint, string AttributionFingerprint, int SchemaVersion);
public sealed record MeetingSummaryAttributionStateInput(bool HasSummary, bool IsGenerating, bool HasFailed, string CurrentTranscriptFingerprint, string CurrentAttributionFingerprint, MeetingSummaryAttributionSnapshot? GeneratedSnapshot);
public sealed record MeetingSummaryAttributionStateResult(MeetingSummaryAttributionState State, string Explanation, bool CanRegenerate);

public static class MeetingSummaryAttributionStateResolver
{
    public const int AttributionSchemaVersion = 1;
    public static string ComputeAttributionFingerprint(string artifactRevision, IReadOnlyList<SummaryAttributionSegment>? segments, string? overrideOrMergeVersion = null)
    {
        var payload = new { artifactRevision = artifactRevision?.Trim() ?? string.Empty, overrideOrMergeVersion = overrideOrMergeVersion?.Trim() ?? string.Empty, segments = (segments ?? []).Select(s => new { id = s.StableSegmentId?.Trim(), speaker = s.EffectiveSpeakerId?.Trim(), label = s.DisplayLabel?.Trim(), source = s.Source?.Trim() }) };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))).ToLowerInvariant();
    }

    public static MeetingSummaryAttributionStateResult Resolve(MeetingSummaryAttributionStateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.IsGenerating) return new(MeetingSummaryAttributionState.Generating, "Generating a summary from the current meeting snapshot.", false);
        if (input.HasFailed) return new(MeetingSummaryAttributionState.Failed, "Summary generation did not finish. The prior summary remains unchanged.", true);
        if (!input.HasSummary || input.GeneratedSnapshot is null) return new(MeetingSummaryAttributionState.Unavailable, "No generated summary is available for this meeting.", false);
        if (!string.Equals(input.GeneratedSnapshot.TranscriptFingerprint, input.CurrentTranscriptFingerprint, StringComparison.Ordinal)) return new(MeetingSummaryAttributionState.TranscriptChanged, "This summary is historic because the transcript changed. Regenerate it after review.", true);
        if (!string.Equals(input.GeneratedSnapshot.AttributionFingerprint, input.CurrentAttributionFingerprint, StringComparison.Ordinal)) return new(MeetingSummaryAttributionState.AttributionChanged, "This summary is historic because speaker attribution changed. Regenerate it with current speaker names.", true);
        return new(MeetingSummaryAttributionState.Current, "This summary uses the current transcript and speaker attribution.", false);
    }
}
