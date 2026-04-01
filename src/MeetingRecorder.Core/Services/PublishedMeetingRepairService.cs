using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public static partial class PublishedMeetingRepairService
{
    private const string RepairMarkerFileName = "published-meeting-repair-v5.done";
    private const string RepairArchiveDirectoryName = "published-meeting-repair-v5";
    private static readonly TimeSpan MaximumRepeatedSplitChainGap = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumRepeatedSplitChainSegmentDuration = TimeSpan.FromMinutes(3);
    private const int MinimumRepeatedSplitChainLength = 3;

    public static async Task<PublishedMeetingRepairResult> RepairKnownIssuesAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string appRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var markerPath = Path.Combine(appRoot, "repairs", RepairMarkerFileName);
        if (File.Exists(markerPath))
        {
            return new PublishedMeetingRepairResult(markerPath, string.Empty, 0, 0, 0, 0, true);
        }

        MeetingCleanupExecutionService.ConsolidateLegacyArchiveRoots(audioOutputDir);
        var workDir = Path.Combine(appRoot, "work");
        var archiveDirectory = Path.Combine(
            MeetingCleanupExecutionService.GetArchiveRoot(audioOutputDir),
            RepairArchiveDirectoryName);
        Directory.CreateDirectory(archiveDirectory);

        var pathBuilder = new ArtifactPathBuilder();
        var catalog = new MeetingOutputCatalogService(pathBuilder);
        var executionService = new MeetingCleanupExecutionService(pathBuilder, catalog);
        var manifestStore = new SessionManifestStore(pathBuilder);
        var meetings = catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .OrderBy(record => record.StartedAtUtc)
            .ToArray();

        var archivedMeetingStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedSplitPairCount = 0;
        var archivedEditorArtifactCount = 0;
        var archivedShortGenericTeamsMeetingCount = 0;

        foreach (var splitChain in FindRepeatedSplitChains(meetings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await executionService.MergeMeetingsAsync(
                splitChain,
                ChoosePreferredTitle(splitChain),
                audioOutputDir,
                transcriptOutputDir,
                archiveDirectory,
                cancellationToken);
            foreach (var meeting in splitChain)
            {
                archivedMeetingStems.Add(meeting.Stem);
            }

            mergedSplitPairCount += splitChain.Count - 1;
        }

        var recommendations = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(
                MeetingCleanupRecommendationEngine.Analyze(await BuildInspections(
                    catalog,
                    manifestStore,
                    audioOutputDir,
                    transcriptOutputDir,
                    workDir,
                    cancellationToken)))
            .ToArray();

        foreach (var recommendation in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var meetingsByStem = catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
                .ToDictionary(record => record.Stem, StringComparer.OrdinalIgnoreCase);

            switch (recommendation.Action)
            {
                case MeetingCleanupAction.Archive:
                    if (!meetingsByStem.TryGetValue(recommendation.PrimaryStem, out var archiveMeeting))
                    {
                        continue;
                    }

                    await executionService.ArchiveMeetingAsync(
                        archiveMeeting,
                        archiveDirectory,
                        MeetingCleanupExecutionService.GetArchiveCategory(recommendation),
                        cancellationToken);
                    archivedMeetingStems.Add(archiveMeeting.Stem);
                    if (string.Equals(recommendation.ReasonCode, "archive-editor-false-meeting", StringComparison.Ordinal))
                    {
                        archivedEditorArtifactCount++;
                    }

                    if (string.Equals(recommendation.ReasonCode, "archive-short-generic-teams", StringComparison.Ordinal))
                    {
                        archivedShortGenericTeamsMeetingCount++;
                    }
                    break;

                case MeetingCleanupAction.Merge:
                    if (recommendation.RelatedStems.Count < 2 ||
                        !meetingsByStem.TryGetValue(recommendation.RelatedStems[0], out var firstMeeting) ||
                        !meetingsByStem.TryGetValue(recommendation.RelatedStems[1], out var secondMeeting))
                    {
                        continue;
                    }

                    await executionService.MergeMeetingsAsync(
                        firstMeeting,
                        secondMeeting,
                        recommendation.SuggestedTitle ?? firstMeeting.Title,
                        audioOutputDir,
                        transcriptOutputDir,
                        archiveDirectory,
                        cancellationToken);
                    archivedMeetingStems.Add(firstMeeting.Stem);
                    archivedMeetingStems.Add(secondMeeting.Stem);
                    mergedSplitPairCount++;
                    break;
            }
        }

        var archivedMeetingCount = archivedMeetingStems.Count;

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        await File.WriteAllTextAsync(
            markerPath,
            $"mergedSplitPairCount={mergedSplitPairCount};archivedMeetingCount={archivedMeetingCount};archivedEditorArtifactCount={archivedEditorArtifactCount};archivedShortGenericTeamsMeetingCount={archivedShortGenericTeamsMeetingCount}",
            cancellationToken);

        return new PublishedMeetingRepairResult(
            markerPath,
            archiveDirectory,
            mergedSplitPairCount,
            archivedMeetingCount,
            archivedEditorArtifactCount,
            archivedShortGenericTeamsMeetingCount,
            false);
    }

    private static async Task<IReadOnlyList<MeetingInspectionRecord>> BuildInspections(
        MeetingOutputCatalogService catalog,
        SessionManifestStore manifestStore,
        string audioOutputDir,
        string transcriptOutputDir,
        string workDir,
        CancellationToken cancellationToken)
    {
        var meetings = catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .OrderBy(record => record.StartedAtUtc)
            .ToArray();
        var inspections = new List<MeetingInspectionRecord>(meetings.Length);

        foreach (var meeting in meetings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeetingSessionManifest? manifest = null;
            if (!string.IsNullOrWhiteSpace(meeting.ManifestPath) && File.Exists(meeting.ManifestPath))
            {
                try
                {
                    manifest = await manifestStore.LoadAsync(meeting.ManifestPath, cancellationToken);
                }
                catch
                {
                    manifest = null;
                }
            }

            inspections.Add(new MeetingInspectionRecord(meeting, manifest, SuggestedTitle: null, SuggestedTitleSource: null));
        }

        return inspections;
    }

    private static IReadOnlyList<IReadOnlyList<MeetingOutputRecord>> FindRepeatedSplitChains(
        IReadOnlyList<MeetingOutputRecord> meetings)
    {
        var chains = new List<IReadOnlyList<MeetingOutputRecord>>();

        foreach (var groupedMeetings in meetings
                     .Where(IsEligibleRepeatedSplitChainMeeting)
                     .GroupBy(
                         meeting => new
                         {
                             meeting.Platform,
                             ComparableTitle = MeetingTitleNormalizer.NormalizeForComparison(meeting.Title),
                             Day = meeting.StartedAtUtc.UtcDateTime.Date,
                         }))
        {
            var ordered = groupedMeetings
                .OrderBy(meeting => meeting.StartedAtUtc)
                .ThenBy(meeting => meeting.Stem, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ordered.Length < MinimumRepeatedSplitChainLength)
            {
                continue;
            }

            var currentChain = new List<MeetingOutputRecord> { ordered[0] };
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = currentChain[^1];
                var current = ordered[index];
                var previousEnd = previous.StartedAtUtc + (previous.Duration ?? TimeSpan.Zero);
                var gap = current.StartedAtUtc - previousEnd;
                if (gap < TimeSpan.Zero || gap > MaximumRepeatedSplitChainGap)
                {
                    AddChainIfEligible(chains, currentChain);
                    currentChain = new List<MeetingOutputRecord> { current };
                    continue;
                }

                currentChain.Add(current);
            }

            AddChainIfEligible(chains, currentChain);
        }

        return chains;
    }

    private static void AddChainIfEligible(
        ICollection<IReadOnlyList<MeetingOutputRecord>> chains,
        IReadOnlyList<MeetingOutputRecord> candidateChain)
    {
        if (candidateChain.Count < MinimumRepeatedSplitChainLength)
        {
            return;
        }

        chains.Add(candidateChain.ToArray());
    }

    private static bool IsEligibleRepeatedSplitChainMeeting(MeetingOutputRecord meeting)
    {
        return meeting.Platform != MeetingPlatform.Unknown &&
            meeting.StartedAtUtc != DateTimeOffset.MinValue &&
            meeting.Duration is { } duration &&
            duration <= MaximumRepeatedSplitChainSegmentDuration &&
            !string.IsNullOrWhiteSpace(meeting.AudioPath) &&
            !string.IsNullOrWhiteSpace(meeting.MarkdownPath) &&
            File.Exists(meeting.AudioPath) &&
            File.Exists(meeting.MarkdownPath) &&
            !string.IsNullOrWhiteSpace(MeetingTitleNormalizer.NormalizeForComparison(meeting.Title));
    }

    private static string ChoosePreferredTitle(IReadOnlyList<MeetingOutputRecord> meetings)
    {
        return meetings
            .Select(meeting => meeting.Title)
            .Aggregate((bestTitle, candidateTitle) =>
            {
                var bestPunctuationScore = CountHelpfulPunctuation(bestTitle);
                var candidatePunctuationScore = CountHelpfulPunctuation(candidateTitle);
                if (candidatePunctuationScore != bestPunctuationScore)
                {
                    return candidatePunctuationScore > bestPunctuationScore ? candidateTitle.Trim() : bestTitle.Trim();
                }

                return candidateTitle.Length > bestTitle.Length ? candidateTitle.Trim() : bestTitle.Trim();
            });
    }

    private static int CountHelpfulPunctuation(string title)
    {
        return title.Count(character => character is ',' or '&' or '/' or '(' or ')');
    }
}

public sealed record PublishedMeetingRepairResult(
    string MarkerPath,
    string ArchiveDirectory,
    int MergedSplitPairCount,
    int ArchivedArtifactCount,
    int ArchivedEditorArtifactCount,
    int ArchivedShortGenericTeamsMeetingCount,
    bool AlreadyApplied);
