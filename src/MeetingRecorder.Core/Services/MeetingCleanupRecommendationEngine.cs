using MeetingRecorder.Core.Domain;
using System.Security.Cryptography;
using System.Text;

namespace MeetingRecorder.Core.Services;

public enum MeetingCleanupAction
{
    Archive = 0,
    Merge = 1,
    Split = 2,
    Rename = 3,
    RegenerateTranscript = 4,
    GenerateSpeakerLabels = 5,
}

public enum MeetingCleanupConfidence
{
    High = 0,
    Medium = 1,
    Low = 2,
}

public sealed record MeetingCleanupRecommendation(
    string Fingerprint,
    MeetingCleanupAction Action,
    MeetingCleanupConfidence Confidence,
    string Title,
    string Description,
    string PrimaryStem,
    IReadOnlyList<string> RelatedStems,
    bool CanApplyAutomatically,
    string? SuggestedTitle,
    TimeSpan? SuggestedSplitPoint,
    string ReasonCode = "");

internal sealed record MeetingInspectionRecord(
    MeetingOutputRecord Meeting,
    MeetingSessionManifest? Manifest,
    string? SuggestedTitle,
    string? SuggestedTitleSource,
    bool IsDiarizationReady = false);

internal static class MeetingCleanupRecommendationEngine
{
    private static readonly TimeSpan MaximumMergeGap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumShortGenericTeamsDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaximumShortSplitSegmentDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumSplitEligibleDuration = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<MeetingCleanupRecommendation> Analyze(IReadOnlyList<MeetingInspectionRecord> inspections)
    {
        if (inspections.Count == 0)
        {
            return Array.Empty<MeetingCleanupRecommendation>();
        }

        var recommendations = new List<MeetingCleanupRecommendation>();
        var blockedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byStem = inspections.ToDictionary(item => item.Meeting.Stem, StringComparer.OrdinalIgnoreCase);

        foreach (var inspection in inspections)
        {
            var archiveRecommendation = TryBuildArchiveRecommendation(inspection);
            if (archiveRecommendation is null)
            {
                continue;
            }

            recommendations.Add(archiveRecommendation);
            blockedStems.Add(inspection.Meeting.Stem);
        }

        foreach (var duplicateRecommendation in BuildDuplicateRecommendations(inspections, blockedStems))
        {
            recommendations.Add(duplicateRecommendation);
            blockedStems.Add(duplicateRecommendation.PrimaryStem);
        }

        foreach (var mergeRecommendation in BuildMergeRecommendations(inspections, blockedStems))
        {
            recommendations.Add(mergeRecommendation);
            foreach (var stem in mergeRecommendation.RelatedStems)
            {
                blockedStems.Add(stem);
            }
        }

        foreach (var inspection in inspections)
        {
            if (blockedStems.Contains(inspection.Meeting.Stem))
            {
                continue;
            }

            if (TryBuildRenameRecommendation(inspection) is { } renameRecommendation)
            {
                recommendations.Add(renameRecommendation);
            }

            if (TryBuildRegenerateRecommendation(inspection) is { } regenerateRecommendation)
            {
                recommendations.Add(regenerateRecommendation);
            }

            if (TryBuildGenerateSpeakerLabelsRecommendation(inspection) is { } generateSpeakerLabelsRecommendation)
            {
                recommendations.Add(generateSpeakerLabelsRecommendation);
            }

            if (TryBuildSplitRecommendation(inspection) is { } splitRecommendation)
            {
                recommendations.Add(splitRecommendation);
            }
        }

        return recommendations
            .OrderBy(recommendation => GetPriority(recommendation.Action))
            .ThenBy(recommendation => recommendation.Confidence)
            .ThenBy(recommendation => ResolveStartedAtUtc(recommendation, byStem))
            .ToArray();
    }

    private static MeetingCleanupRecommendation? TryBuildArchiveRecommendation(MeetingInspectionRecord inspection)
    {
        if (IsEditorGeneratedFalseMeeting(inspection.Meeting))
        {
            return BuildRecommendation(
                inspection.Meeting.Stem,
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive obvious editor-generated false meeting",
                "This meeting appears to have been created from an editor window title instead of a real meeting.",
                [inspection.Meeting.Stem],
                canApplyAutomatically: true,
                suggestedTitle: null,
                suggestedSplitPoint: null,
                reasonCode: "archive-editor-false-meeting");
        }

        if (IsShortGenericTeamsFalseStart(inspection.Meeting))
        {
            return BuildRecommendation(
                inspection.Meeting.Stem,
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive short generic Teams false start",
                "This looks like a brief generic Teams shell false start rather than a real meeting.",
                [inspection.Meeting.Stem],
                canApplyAutomatically: true,
                suggestedTitle: null,
                suggestedSplitPoint: null,
                reasonCode: "archive-short-generic-teams");
        }

        if (IsTinyOrEmptyAudioArtifact(inspection.Meeting))
        {
            return BuildRecommendation(
                inspection.Meeting.Stem,
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive tiny or empty meeting artifact",
                "This meeting has an empty or header-only audio artifact and is unlikely to be useful.",
                [inspection.Meeting.Stem],
                canApplyAutomatically: true,
                suggestedTitle: null,
                suggestedSplitPoint: null,
                reasonCode: "archive-tiny-audio");
        }

        if (IsTranscriptOnlyOrphan(inspection.Meeting))
        {
            return BuildRecommendation(
                inspection.Meeting.Stem,
                MeetingCleanupAction.Archive,
                MeetingCleanupConfidence.High,
                "Archive orphan transcript-only meeting",
                "This meeting has transcript artifacts but no usable audio or manifest context.",
                [inspection.Meeting.Stem],
                canApplyAutomatically: true,
                suggestedTitle: null,
                suggestedSplitPoint: null,
                reasonCode: "archive-orphan-transcript");
        }

        return null;
    }

    private static IReadOnlyList<MeetingCleanupRecommendation> BuildDuplicateRecommendations(
        IReadOnlyList<MeetingInspectionRecord> inspections,
        IReadOnlySet<string> blockedStems)
    {
        var recommendations = new List<MeetingCleanupRecommendation>();

        foreach (var sameStartGroup in inspections
                     .Where(inspection =>
                         !blockedStems.Contains(inspection.Meeting.Stem) &&
                         inspection.Meeting.StartedAtUtc != DateTimeOffset.MinValue &&
                         inspection.Meeting.Platform != MeetingPlatform.Unknown)
                     .GroupBy(inspection => new
                     {
                         inspection.Meeting.Platform,
                         inspection.Meeting.StartedAtUtc,
                     }))
        {
            var candidates = sameStartGroup.ToArray();
            if (candidates.Length < 2)
            {
                continue;
            }

            var hashGroups = candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    AudioHash = ComputeAudioIdentity(candidate.Meeting.AudioPath),
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.AudioHash))
                .GroupBy(item => item.AudioHash!, StringComparer.OrdinalIgnoreCase);

            foreach (var hashGroup in hashGroups)
            {
                var duplicateCandidates = hashGroup.Select(item => item.Candidate).ToArray();
                if (duplicateCandidates.Length < 2)
                {
                    continue;
                }

                var canonical = duplicateCandidates
                    .OrderByDescending(GetDuplicateCanonicalScore)
                    .ThenBy(candidate => candidate.Meeting.Stem, StringComparer.OrdinalIgnoreCase)
                    .First();

                foreach (var duplicate in duplicateCandidates)
                {
                    if (ReferenceEquals(duplicate, canonical))
                    {
                        continue;
                    }

                    recommendations.Add(BuildRecommendation(
                        duplicate.Meeting.Stem,
                        MeetingCleanupAction.Archive,
                        MeetingCleanupConfidence.High,
                        "Archive duplicate published meeting",
                        $"This meeting duplicates '{canonical.Meeting.Title}' at the same start time and can be archived safely.",
                        [duplicate.Meeting.Stem, canonical.Meeting.Stem],
                        canApplyAutomatically: true,
                        suggestedTitle: null,
                        suggestedSplitPoint: null,
                        reasonCode: "archive-duplicate-publish"));
                }
            }
        }

        return recommendations;
    }

    private static IReadOnlyList<MeetingCleanupRecommendation> BuildMergeRecommendations(
        IReadOnlyList<MeetingInspectionRecord> inspections,
        IReadOnlySet<string> blockedStems)
    {
        var recommendations = new List<MeetingCleanupRecommendation>();
        var ordered = inspections
            .Where(inspection => !blockedStems.Contains(inspection.Meeting.Stem))
            .OrderBy(inspection => inspection.Meeting.StartedAtUtc)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1].Meeting;
            var current = ordered[index].Meeting;
            if (!TryGetMergeConfidence(previous, current, out var confidence))
            {
                continue;
            }

            recommendations.Add(BuildRecommendation(
                previous.Stem,
                MeetingCleanupAction.Merge,
                confidence,
                "Merge likely split meeting pair",
                $"These meetings look like the same call split into two publishes: '{previous.Title}' and '{current.Title}'.",
                [previous.Stem, current.Stem],
                canApplyAutomatically: confidence == MeetingCleanupConfidence.High,
                suggestedTitle: ChoosePreferredTitle(previous.Title, current.Title),
                suggestedSplitPoint: null,
                reasonCode: "merge-split-pair"));
        }

        return recommendations;
    }

    private static MeetingCleanupRecommendation? TryBuildRenameRecommendation(MeetingInspectionRecord inspection)
    {
        if (string.IsNullOrWhiteSpace(inspection.SuggestedTitle))
        {
            return null;
        }

        return BuildRecommendation(
            inspection.Meeting.Stem,
            MeetingCleanupAction.Rename,
            MeetingCleanupConfidence.Medium,
            "Rename generic meeting title",
            $"A stronger title suggestion is available for '{inspection.Meeting.Title}'.",
            [inspection.Meeting.Stem],
            canApplyAutomatically: false,
            suggestedTitle: inspection.SuggestedTitle.Trim(),
            suggestedSplitPoint: null,
            reasonCode: "rename-generic-title");
    }

    private static MeetingCleanupRecommendation? TryBuildRegenerateRecommendation(MeetingInspectionRecord inspection)
    {
        if (string.IsNullOrWhiteSpace(inspection.Meeting.AudioPath) ||
            !File.Exists(inspection.Meeting.AudioPath) ||
            !string.IsNullOrWhiteSpace(inspection.Meeting.MarkdownPath) ||
            !string.IsNullOrWhiteSpace(inspection.Meeting.JsonPath))
        {
            return null;
        }

        return BuildRecommendation(
            inspection.Meeting.Stem,
            MeetingCleanupAction.RegenerateTranscript,
            MeetingCleanupConfidence.High,
            "Re-generate missing transcript",
            $"Audio exists for '{inspection.Meeting.Title}', but transcript artifacts are missing.",
            [inspection.Meeting.Stem],
            canApplyAutomatically: true,
            suggestedTitle: null,
            suggestedSplitPoint: null,
            reasonCode: "regenerate-missing-transcript");
    }

    private static MeetingCleanupRecommendation? TryBuildSplitRecommendation(MeetingInspectionRecord inspection)
    {
        if (inspection.Manifest is null ||
            inspection.Meeting.Duration is not { } duration ||
            duration < MinimumSplitEligibleDuration ||
            string.IsNullOrWhiteSpace(inspection.Meeting.AudioPath))
        {
            return null;
        }

        var splitPoint = TrySuggestSplitPoint(inspection.Manifest, inspection.Meeting.StartedAtUtc, duration);
        if (splitPoint is null)
        {
            return null;
        }

        return BuildRecommendation(
            inspection.Meeting.Stem,
            MeetingCleanupAction.Split,
            MeetingCleanupConfidence.Medium,
            "Split combined meeting",
            "This meeting looks like two back-to-back calls combined into one published recording.",
            [inspection.Meeting.Stem],
            canApplyAutomatically: false,
            suggestedTitle: null,
            suggestedSplitPoint: splitPoint,
            reasonCode: "split-combined-meeting");
    }

    private static MeetingCleanupRecommendation? TryBuildGenerateSpeakerLabelsRecommendation(MeetingInspectionRecord inspection)
    {
        if (!inspection.IsDiarizationReady ||
            inspection.Meeting.HasSpeakerLabels ||
            string.IsNullOrWhiteSpace(inspection.Meeting.AudioPath) ||
            !File.Exists(inspection.Meeting.AudioPath) ||
            !HasTranscriptArtifacts(inspection.Meeting))
        {
            return null;
        }

        return BuildRecommendation(
            inspection.Meeting.Stem,
            MeetingCleanupAction.GenerateSpeakerLabels,
            MeetingCleanupConfidence.High,
            "Add missing speaker labels",
            $"Speaker labeling is ready for '{inspection.Meeting.Title}', but the published transcript does not contain speaker labels yet.",
            [inspection.Meeting.Stem],
            canApplyAutomatically: true,
            suggestedTitle: null,
            suggestedSplitPoint: null,
            reasonCode: "generate-speaker-labels");
    }

    private static TimeSpan? TrySuggestSplitPoint(
        MeetingSessionManifest manifest,
        DateTimeOffset startedAtUtc,
        TimeSpan duration)
    {
        var distinctSignals = manifest.DetectionEvidence
            .Where(signal => string.Equals(signal.Source, "window-title", StringComparison.OrdinalIgnoreCase))
            .Select(signal => new
            {
                signal.CapturedAtUtc,
                Title = ExtractComparableSignalTitle(signal.Value),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !IsGenericTitle(manifest.Platform, item.Title))
            .ToArray();
        if (distinctSignals.Length < 4)
        {
            return null;
        }

        var firstClusterTitle = distinctSignals[0].Title;
        var firstClusterLastUtc = distinctSignals
            .Where(item => string.Equals(item.Title, firstClusterTitle, StringComparison.OrdinalIgnoreCase))
            .Max(item => item.CapturedAtUtc);
        var secondCluster = distinctSignals
            .Where(item => !string.Equals(item.Title, firstClusterTitle, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Min(item => item.CapturedAtUtc))
            .FirstOrDefault();
        if (secondCluster is null || secondCluster.Count() < 2)
        {
            return null;
        }

        var secondClusterFirstUtc = secondCluster.Min(item => item.CapturedAtUtc);
        if (secondClusterFirstUtc <= firstClusterLastUtc)
        {
            return null;
        }

        var midpointUtc = firstClusterLastUtc + TimeSpan.FromTicks((secondClusterFirstUtc - firstClusterLastUtc).Ticks / 2);
        var splitPoint = midpointUtc - startedAtUtc;
        return splitPoint <= TimeSpan.FromSeconds(1) || splitPoint >= duration - TimeSpan.FromSeconds(1)
            ? null
            : splitPoint;
    }

    private static bool TryGetMergeConfidence(
        MeetingOutputRecord previous,
        MeetingOutputRecord current,
        out MeetingCleanupConfidence confidence)
    {
        confidence = MeetingCleanupConfidence.Low;
        if (previous.Platform == MeetingPlatform.Unknown ||
            previous.Platform != current.Platform ||
            previous.Duration is not { } previousDuration ||
            current.Duration is not { } currentDuration ||
            string.IsNullOrWhiteSpace(previous.AudioPath) ||
            string.IsNullOrWhiteSpace(current.AudioPath) ||
            string.IsNullOrWhiteSpace(previous.MarkdownPath) ||
            string.IsNullOrWhiteSpace(current.MarkdownPath) ||
            !File.Exists(previous.AudioPath) ||
            !File.Exists(current.AudioPath))
        {
            return false;
        }

        var previousComparable = MeetingTitleNormalizer.NormalizeForComparison(previous.Title);
        var currentComparable = MeetingTitleNormalizer.NormalizeForComparison(current.Title);
        if (string.IsNullOrWhiteSpace(previousComparable) ||
            !string.Equals(previousComparable, currentComparable, StringComparison.Ordinal))
        {
            return false;
        }

        var previousEnd = previous.StartedAtUtc + previousDuration;
        var gap = current.StartedAtUtc - previousEnd;
        if (gap < TimeSpan.Zero || gap > MaximumMergeGap)
        {
            return false;
        }

        var titlesDiffer = !string.Equals(previous.Title.Trim(), current.Title.Trim(), StringComparison.OrdinalIgnoreCase);
        var hasShortSegment = previousDuration <= MaximumShortSplitSegmentDuration || currentDuration <= MaximumShortSplitSegmentDuration;
        confidence = titlesDiffer || hasShortSegment
            ? MeetingCleanupConfidence.High
            : MeetingCleanupConfidence.Medium;
        return true;
    }

    private static string BuildFingerprint(
        MeetingCleanupAction action,
        string primaryStem,
        IReadOnlyList<string> relatedStems,
        string reasonCode,
        string? suggestedTitle,
        TimeSpan? suggestedSplitPoint)
    {
        var builder = new StringBuilder();
        builder.Append(action);
        builder.Append('|');
        builder.Append(primaryStem);
        builder.Append('|');
        builder.Append(reasonCode);
        builder.Append('|');
        builder.Append(string.Join(",", relatedStems.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)));
        builder.Append('|');
        builder.Append(suggestedTitle?.Trim() ?? string.Empty);
        builder.Append('|');
        builder.Append(suggestedSplitPoint?.ToString() ?? string.Empty);
        return builder.ToString();
    }

    private static MeetingCleanupRecommendation BuildRecommendation(
        string primaryStem,
        MeetingCleanupAction action,
        MeetingCleanupConfidence confidence,
        string title,
        string description,
        IReadOnlyList<string> relatedStems,
        bool canApplyAutomatically,
        string? suggestedTitle,
        TimeSpan? suggestedSplitPoint,
        string reasonCode)
    {
        return new MeetingCleanupRecommendation(
            BuildFingerprint(action, primaryStem, relatedStems, reasonCode, suggestedTitle, suggestedSplitPoint),
            action,
            confidence,
            title,
            description,
            primaryStem,
            relatedStems,
            canApplyAutomatically,
            suggestedTitle,
            suggestedSplitPoint,
            reasonCode);
    }

    private static int GetPriority(MeetingCleanupAction action)
    {
        return action switch
        {
            MeetingCleanupAction.Archive => 0,
            MeetingCleanupAction.Merge => 1,
            MeetingCleanupAction.Rename => 2,
            MeetingCleanupAction.RegenerateTranscript => 3,
            MeetingCleanupAction.GenerateSpeakerLabels => 4,
            MeetingCleanupAction.Split => 5,
            _ => 10,
        };
    }

    private static DateTimeOffset ResolveStartedAtUtc(
        MeetingCleanupRecommendation recommendation,
        IReadOnlyDictionary<string, MeetingInspectionRecord> byStem)
    {
        return byStem.TryGetValue(recommendation.PrimaryStem, out var inspection)
            ? inspection.Meeting.StartedAtUtc
            : DateTimeOffset.MaxValue;
    }

    private static int GetDuplicateCanonicalScore(MeetingInspectionRecord inspection)
    {
        var score = 0;
        if (!IsGenericTitle(inspection.Meeting.Platform, inspection.Meeting.Title))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(inspection.Meeting.AudioPath) && File.Exists(inspection.Meeting.AudioPath))
        {
            score += 8;
            score += (int)Math.Min(3, new FileInfo(inspection.Meeting.AudioPath).Length / 1024);
        }

        if (!string.IsNullOrWhiteSpace(inspection.Meeting.MarkdownPath))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(inspection.Meeting.JsonPath))
        {
            score += 2;
        }

        return score;
    }

    private static bool IsEditorGeneratedFalseMeeting(MeetingOutputRecord meeting)
    {
        return meeting.Stem.Contains("meeting-recorder-visual-studio-code", StringComparison.OrdinalIgnoreCase) ||
            meeting.Title.Contains("visual studio code", StringComparison.OrdinalIgnoreCase) ||
            meeting.Title.EndsWith(".md - Meeting Recorder - Visual Studio Code", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShortGenericTeamsFalseStart(MeetingOutputRecord meeting)
    {
        return meeting.Platform == MeetingPlatform.Teams &&
            meeting.Duration is { } duration &&
            duration <= MaximumShortGenericTeamsDuration &&
            IsGenericTitle(meeting.Platform, meeting.Title);
    }

    private static bool HasTranscriptArtifacts(MeetingOutputRecord meeting)
    {
        return (!string.IsNullOrWhiteSpace(meeting.MarkdownPath) && File.Exists(meeting.MarkdownPath)) ||
               (!string.IsNullOrWhiteSpace(meeting.JsonPath) && File.Exists(meeting.JsonPath));
    }

    private static bool IsTinyOrEmptyAudioArtifact(MeetingOutputRecord meeting)
    {
        if (string.IsNullOrWhiteSpace(meeting.AudioPath) || !File.Exists(meeting.AudioPath))
        {
            return false;
        }

        var fileLength = new FileInfo(meeting.AudioPath).Length;
        return fileLength <= 128 || meeting.Duration == TimeSpan.Zero;
    }

    private static bool IsTranscriptOnlyOrphan(MeetingOutputRecord meeting)
    {
        return string.IsNullOrWhiteSpace(meeting.AudioPath) &&
            (!string.IsNullOrWhiteSpace(meeting.MarkdownPath) || !string.IsNullOrWhiteSpace(meeting.JsonPath)) &&
            string.IsNullOrWhiteSpace(meeting.ManifestPath);
    }

    private static string? ComputeAudioIdentity(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return null;
        }

        using var stream = File.OpenRead(audioPath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string ChoosePreferredTitle(string firstTitle, string secondTitle)
    {
        var firstPunctuationScore = CountHelpfulPunctuation(firstTitle);
        var secondPunctuationScore = CountHelpfulPunctuation(secondTitle);
        if (secondPunctuationScore != firstPunctuationScore)
        {
            return secondPunctuationScore > firstPunctuationScore ? secondTitle.Trim() : firstTitle.Trim();
        }

        return secondTitle.Length > firstTitle.Length ? secondTitle.Trim() : firstTitle.Trim();
    }

    private static int CountHelpfulPunctuation(string title)
    {
        return title.Count(character => character is ',' or '&' or '/' or '(' or ')');
    }

    private static string ExtractComparableSignalTitle(string value)
    {
        var candidate = value.Trim();
        if (candidate.Contains("visual studio code", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        candidate = candidate
            .Replace("- Microsoft Teams", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("| Microsoft Teams", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('|', ' ');
        return candidate;
    }

    private static bool IsGenericTitle(MeetingPlatform platform, string? title)
    {
        var normalizedComparable = MeetingTitleNormalizer.NormalizeForComparison(title);
        if (normalizedComparable is "" or "meeting" or "detected meeting")
        {
            return true;
        }

        return platform switch
        {
            MeetingPlatform.Teams => normalizedComparable is "microsoft teams" or "teams" or "ms teams" or "sharing control bar" or "search" or "calls" or "chat",
            MeetingPlatform.GoogleMeet => normalizedComparable is "google meet" or "meet",
            MeetingPlatform.Manual => normalizedComparable.StartsWith("manual session ", StringComparison.Ordinal),
            _ => false,
        };
    }
}
