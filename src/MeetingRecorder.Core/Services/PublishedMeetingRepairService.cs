using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public static partial class PublishedMeetingRepairService
{
    private const string RepairMarkerFileName = "published-meeting-repair-v3.done";

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
        var archiveDirectory = Path.Combine(
            MeetingCleanupExecutionService.GetArchiveRoot(audioOutputDir),
            "published-meeting-repair-v3");
        Directory.CreateDirectory(archiveDirectory);

        var pathBuilder = new ArtifactPathBuilder();
        var catalog = new MeetingOutputCatalogService(pathBuilder);
        var executionService = new MeetingCleanupExecutionService(pathBuilder, catalog);
        var manifestStore = new SessionManifestStore(pathBuilder);
        var meetings = catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir: null)
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

        var recommendations = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(
                MeetingCleanupRecommendationEngine.Analyze(inspections))
            .ToArray();
        var archivedMeetingStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedSplitPairCount = 0;
        var archivedEditorArtifactCount = 0;
        var archivedShortGenericTeamsMeetingCount = 0;

        foreach (var recommendation in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var meetingsByStem = catalog.ListMeetings(audioOutputDir, transcriptOutputDir, workDir: null)
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
}

public sealed record PublishedMeetingRepairResult(
    string MarkerPath,
    string ArchiveDirectory,
    int MergedSplitPairCount,
    int ArchivedArtifactCount,
    int ArchivedEditorArtifactCount,
    int ArchivedShortGenericTeamsMeetingCount,
    bool AlreadyApplied);
