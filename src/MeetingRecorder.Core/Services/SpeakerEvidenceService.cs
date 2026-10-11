using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public enum SpeakerEvidenceState
{
    Available = 0,
    Limited = 1,
    TranscriptUnavailable = 2,
    TimestampsUnavailable = 3,
    NoTurnsForSpeaker = 4,
    TurnsTooShort = 5,
    DiarizationChurn = 6,
    AudioUnavailable = 7,
    ArtifactStale = 8,
}

public enum SpeakerEvidenceSourceKind
{
    StructuredTranscript = 0,
    MarkdownFallback = 1,
}

public enum SpeakerEvidenceAction
{
    None = 0,
    ReadTranscript = 1,
    Reload = 2,
    RepairSpeakerLabels = 3,
}

public sealed record SpeakerEvidenceRequest(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    string SpeakerLabel,
    IReadOnlyList<TranscriptSegment>? Segments,
    bool HasTranscript,
    bool HasAudioReference,
    bool IsArtifactStale,
    SpeakerEvidenceSourceKind SourceKind);

public sealed record SpeakerEvidenceCue(
    TimeSpan Start,
    TimeSpan End,
    string Excerpt,
    SpeakerEvidenceSourceKind SourceKind,
    int SegmentIndex)
{
    public string Timestamp => FormatTimestamp(Start, End);

    public string TranscriptTimestamp => Format(Start);

    private static string FormatTimestamp(TimeSpan start, TimeSpan end) =>
        $"{Format(start)}–{Format(end)}";

    private static string Format(TimeSpan value) => value.TotalHours >= 1d
        ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}

public sealed record SpeakerEvidenceSnapshot(
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    SpeakerEvidenceState State,
    IReadOnlyList<SpeakerEvidenceState> Weaknesses,
    SpeakerEvidenceAction Action,
    string Summary,
    IReadOnlyList<SpeakerEvidenceCue> Cues)
{
    public bool HasCues => Cues.Count > 0;
}

/// <summary>
/// Selects bounded, local transcript context for one speaker. The service accepts
/// already-read segments only; callers must never pass this result to logs or output artifacts.
/// </summary>
public sealed class SpeakerEvidenceService
{
    private const int MaximumCueCount = 3;
    private const int MaximumExcerptTextElements = 180;
    private const int MinimumExcerptTextElements = 12;
    private static readonly TimeSpan MinimumCueDuration = TimeSpan.FromSeconds(1);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SpeakerEvidenceSnapshot Select(SpeakerEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var meetingIdentity = request.MeetingStableIdentity?.Trim() ?? string.Empty;
        var revision = request.ArtifactRevision?.Trim() ?? string.Empty;
        var speakerId = request.SpeakerId?.Trim() ?? string.Empty;
        var speakerLabel = request.SpeakerLabel?.Trim() ?? string.Empty;

        if (request.IsArtifactStale || string.IsNullOrWhiteSpace(meetingIdentity) ||
            string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(speakerId))
        {
            return Unavailable(meetingIdentity, revision, speakerId, SpeakerEvidenceState.ArtifactStale, SpeakerEvidenceAction.Reload,
                "Speaker evidence changed. Reload this meeting before reviewing local transcript context.");
        }

        var segments = request.Segments ?? Array.Empty<TranscriptSegment>();
        if (!request.HasTranscript || segments.Count == 0)
        {
            return Unavailable(meetingIdentity, revision, speakerId, SpeakerEvidenceState.TranscriptUnavailable, SpeakerEvidenceAction.ReadTranscript,
                "No readable local transcript context is available for this speaker.");
        }

        var speakerSegments = segments
            .Select((segment, index) => new IndexedSegment(segment, index))
            .Where(candidate => MatchesSpeaker(candidate.Segment, speakerId, speakerLabel, request.SourceKind))
            .ToArray();
        if (speakerSegments.Length == 0)
        {
            return Unavailable(meetingIdentity, revision, speakerId, SpeakerEvidenceState.NoTurnsForSpeaker, SpeakerEvidenceAction.RepairSpeakerLabels,
                "No transcript turns match this speaker label. Review speaker labels before naming this speaker.");
        }

        if (request.SourceKind == SpeakerEvidenceSourceKind.MarkdownFallback &&
            speakerSegments.Any(candidate => candidate.Segment.End <= candidate.Segment.Start))
        {
            return Unavailable(meetingIdentity, revision, speakerId, SpeakerEvidenceState.TimestampsUnavailable, SpeakerEvidenceAction.ReadTranscript,
                "Transcript timestamps are unavailable, so speaker context cannot be shown safely.");
        }

        var candidates = speakerSegments
            .Where(candidate => candidate.Segment.End - candidate.Segment.Start >= MinimumCueDuration)
            .Select(candidate => candidate with { NormalizedText = NormalizeText(candidate.Segment.Text) })
            .Where(candidate => TextElementCount(candidate.NormalizedText) >= MinimumExcerptTextElements)
            .GroupBy(candidate => candidate.NormalizedText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.Segment.Start).ThenBy(candidate => candidate.Index).First())
            .OrderBy(candidate => candidate.Segment.Start)
            .ThenBy(candidate => candidate.Index)
            .ToArray();
        if (candidates.Length == 0)
        {
            var weakState = speakerSegments.Length >= 4 ? SpeakerEvidenceState.DiarizationChurn : SpeakerEvidenceState.TurnsTooShort;
            var summary = weakState == SpeakerEvidenceState.DiarizationChurn
                ? "Speaker turns are too fragmented to provide reliable local context. Review speaker labels before naming this speaker."
                : "Speaker turns are too short to provide reliable local context. Review speaker labels before naming this speaker.";
            return Unavailable(meetingIdentity, revision, speakerId, weakState, SpeakerEvidenceAction.RepairSpeakerLabels, summary);
        }

        var nonOverlappingCandidates = DiscardOverlappingCandidates(candidates);
        var selected = SelectSpreadCues(nonOverlappingCandidates)
            .Select(candidate => new SpeakerEvidenceCue(
                candidate.Segment.Start,
                candidate.Segment.End,
                ToBoundedExcerpt(candidate.NormalizedText),
                request.SourceKind,
                candidate.Index))
            .ToArray();
        var weaknesses = new List<SpeakerEvidenceState>();
        if (selected.Length < 2)
        {
            weaknesses.Add(SpeakerEvidenceState.Limited);
        }

        if (!request.HasAudioReference)
        {
            weaknesses.Add(SpeakerEvidenceState.AudioUnavailable);
        }

        var state = weaknesses.Count == 0 ? SpeakerEvidenceState.Available : SpeakerEvidenceState.Limited;
        var sourceDescription = request.SourceKind == SpeakerEvidenceSourceKind.MarkdownFallback
            ? "Markdown fallback"
            : "local transcript";
        var summaryText = selected.Length == 1
            ? $"1 {sourceDescription} cue is available. It is context, not identity proof."
            : $"{selected.Length} {sourceDescription} cues are available. They are context, not identity proof.";
        if (!request.HasAudioReference)
        {
            summaryText += " Audio review is unavailable.";
        }

        return new SpeakerEvidenceSnapshot(
            meetingIdentity,
            revision,
            speakerId,
            state,
            weaknesses,
            SpeakerEvidenceAction.None,
            summaryText,
            selected);
    }

    private static SpeakerEvidenceSnapshot Unavailable(
        string meetingIdentity,
        string revision,
        string speakerId,
        SpeakerEvidenceState state,
        SpeakerEvidenceAction action,
        string summary) =>
        new(meetingIdentity, revision, speakerId, state, [state], action, summary, Array.Empty<SpeakerEvidenceCue>());

    private static bool MatchesSpeaker(
        TranscriptSegment segment,
        string speakerId,
        string speakerLabel,
        SpeakerEvidenceSourceKind sourceKind) =>
        string.Equals(segment.SpeakerId?.Trim(), speakerId, StringComparison.Ordinal) ||
        (sourceKind == SpeakerEvidenceSourceKind.MarkdownFallback &&
         !string.IsNullOrWhiteSpace(speakerLabel) &&
         string.Equals(segment.SpeakerLabel?.Trim(), speakerLabel, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<IndexedSegment> SelectSpreadCues(IReadOnlyList<IndexedSegment> candidates)
    {
        var selected = new List<IndexedSegment>(MaximumCueCount);
        var first = candidates[0].Segment.Start;
        var last = candidates[^1].Segment.End;
        var range = last - first;
        foreach (var fraction in new[] { 0d, 0.5d, 1d })
        {
            var target = first + TimeSpan.FromTicks((long)(range.Ticks * fraction));
            var choice = candidates
                .Where(candidate => !selected.Contains(candidate) && !OverlapsSelected(candidate, selected))
                .OrderBy(candidate => Math.Abs((candidate.Segment.Start - target).Ticks))
                .ThenBy(candidate => candidate.Segment.Start)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault();
            if (choice is not null)
            {
                selected.Add(choice);
            }
        }

        return selected
            .OrderBy(candidate => candidate.Segment.Start)
            .ThenBy(candidate => candidate.Index)
            .Take(MaximumCueCount)
            .ToArray();
    }

    private static bool OverlapsSelected(IndexedSegment candidate, IReadOnlyList<IndexedSegment> selected) => selected.Any(existing =>
        candidate.Segment.Start < existing.Segment.End && candidate.Segment.End > existing.Segment.Start);

    private static IReadOnlyList<IndexedSegment> DiscardOverlappingCandidates(IReadOnlyList<IndexedSegment> candidates)
    {
        var result = new List<IndexedSegment>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!OverlapsSelected(candidate, result))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static string NormalizeText(string? text) => string.IsNullOrWhiteSpace(text)
        ? string.Empty
        : WhitespacePattern.Replace(text.Trim(), " ");

    private static int TextElementCount(string text) => StringInfo.ParseCombiningCharacters(text).Length;

    private static string ToBoundedExcerpt(string normalizedText)
    {
        var textElements = StringInfo.ParseCombiningCharacters(normalizedText);
        if (textElements.Length <= MaximumExcerptTextElements)
        {
            return normalizedText;
        }

        return normalizedText[..textElements[MaximumExcerptTextElements]].TrimEnd() + "…";
    }

    private sealed record IndexedSegment(TranscriptSegment Segment, int Index, string NormalizedText = "");
}
