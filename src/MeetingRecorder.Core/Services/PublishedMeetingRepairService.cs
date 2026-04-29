using MeetingRecorder.Core.Domain;
using System.Text;

namespace MeetingRecorder.Core.Services;

public static partial class PublishedMeetingRepairService
{
    private const string RepairMarkerFileName = "published-meeting-repair-v6.done";
    private const string RepairArchiveDirectoryName = "published-meeting-repair-v6";
    private const string EchoRepairReportFileName = "echo-repair-report.txt";
    private const string EchoRepairPublishMessage = "Published audio was republished after one-time echo repair v6.";
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
        CloudFileStorageOptimizer.MarkUnpinnedRecursive(archiveDirectory);

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
        var echoRepairResult = EchoRepairPassResult.Empty;

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

        echoRepairResult = await RepairPublishedMicrophoneEchoAsync(
            audioOutputDir,
            workDir,
            archiveDirectory,
            manifestStore,
            pathBuilder,
            cancellationToken);
        var archivedMeetingCount = archivedMeetingStems.Count;

        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        await File.WriteAllTextAsync(
            markerPath,
            $"mergedSplitPairCount={mergedSplitPairCount};archivedMeetingCount={archivedMeetingCount};archivedEditorArtifactCount={archivedEditorArtifactCount};archivedShortGenericTeamsMeetingCount={archivedShortGenericTeamsMeetingCount};echoRepairedCount={echoRepairResult.RepairedCount};echoSkippedCount={echoRepairResult.SkippedCount}",
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

    private static async Task<EchoRepairPassResult> RepairPublishedMicrophoneEchoAsync(
        string audioOutputDir,
        string workDir,
        string archiveDirectory,
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        CancellationToken cancellationToken)
    {
        var repaired = new List<string>();
        var skipped = new List<string>();
        var repairedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waveChunkMerger = new WaveChunkMerger();
        var publishService = new FilePublishService();

        if (Directory.Exists(workDir))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken);
                    if (manifest.State != SessionState.Published || manifest.MicrophoneCaptureSegments.Count == 0)
                    {
                        continue;
                    }

                    var stem = ResolvePublishedStem(manifest, pathBuilder);
                    if (!repairedStems.Add(stem))
                    {
                        skipped.Add($"{stem}: duplicate published stem");
                        continue;
                    }

                    var publishedAudioPath = Path.Combine(audioOutputDir, $"{stem}.wav");
                    if (!File.Exists(publishedAudioPath))
                    {
                        skipped.Add($"{stem}: missing published audio");
                        continue;
                    }

                    var sessionRoot = Path.GetDirectoryName(manifestPath)
                        ?? throw new InvalidOperationException($"Manifest path has no session root: {manifestPath}");
                    var resolvedLoopbackSegments = ResolveLoopbackCaptureSegments(sessionRoot, manifest.LoopbackCaptureSegments);
                    if (resolvedLoopbackSegments.MissingChunkPath is { } missingLoopbackChunk)
                    {
                        skipped.Add($"{stem}: missing loopback chunk '{missingLoopbackChunk}'");
                        continue;
                    }

                    var resolvedMicrophoneSegments = ResolveMicrophoneCaptureSegments(sessionRoot, manifest.MicrophoneCaptureSegments);
                    if (resolvedMicrophoneSegments.MissingChunkPath is { } missingMicrophoneChunk)
                    {
                        skipped.Add($"{stem}: missing microphone chunk '{missingMicrophoneChunk}'");
                        continue;
                    }

                    if (resolvedLoopbackSegments.Segments.Count == 0)
                    {
                        skipped.Add($"{stem}: no loopback chunks");
                        continue;
                    }

                    if (resolvedMicrophoneSegments.Segments.Count == 0)
                    {
                        skipped.Add($"{stem}: no microphone segments");
                        continue;
                    }

                    var processingDirectory = Path.Combine(sessionRoot, "processing");
                    Directory.CreateDirectory(processingDirectory);
                    var repairedMergedAudioPath = Path.Combine(processingDirectory, $"{stem}.wav");
                    var meetingArchiveDirectory = Path.Combine(archiveDirectory, stem);
                    Directory.CreateDirectory(meetingArchiveDirectory);

                    BackupIfPresent(
                        publishedAudioPath,
                        Path.Combine(meetingArchiveDirectory, Path.GetFileName(publishedAudioPath)));
                    BackupIfPresent(
                        ResolveExistingProcessingAudioPath(sessionRoot, manifest.MergedAudioPath, repairedMergedAudioPath),
                        Path.Combine(meetingArchiveDirectory, $"processing-{Path.GetFileName(repairedMergedAudioPath)}"));

                    if (File.Exists(repairedMergedAudioPath))
                    {
                        File.Delete(repairedMergedAudioPath);
                    }

                    await waveChunkMerger.MergeAsync(
                        resolvedLoopbackSegments.Segments.SelectMany(segment => segment.ChunkPaths).ToArray(),
                        resolvedMicrophoneSegments.Segments,
                        manifest.StartedAtUtc,
                        manifest.EndedAtUtc,
                        repairedMergedAudioPath,
                        cancellationToken);
                    await publishService.PublishAudioAsync(
                        repairedMergedAudioPath,
                        audioOutputDir,
                        stem,
                        cancellationToken);

                    await manifestStore.SaveAsync(
                        manifest with
                        {
                            LoopbackCaptureSegments = resolvedLoopbackSegments.Segments,
                            MicrophoneCaptureSegments = resolvedMicrophoneSegments.Segments,
                            MergedAudioPath = repairedMergedAudioPath,
                            State = SessionState.Published,
                            PublishStatus = new ProcessingStageStatus(
                                "publish",
                                StageExecutionState.Succeeded,
                                DateTimeOffset.UtcNow,
                                EchoRepairPublishMessage),
                        },
                        manifestPath,
                        cancellationToken);

                    repaired.Add($"{stem}: {manifest.DetectedTitle}");
                }
                catch (Exception exception)
                {
                    skipped.Add($"{manifestPath}: repair failed: {exception.Message}");
                }
            }
        }

        var reportPath = Path.Combine(archiveDirectory, EchoRepairReportFileName);
        await File.WriteAllTextAsync(
            reportPath,
            BuildEchoRepairReport(workDir, archiveDirectory, repaired, skipped),
            cancellationToken);
        CloudFileStorageOptimizer.MarkUnpinnedRecursive(archiveDirectory);
        return new EchoRepairPassResult(repaired.Count, skipped.Count, reportPath);
    }

    private static string BuildEchoRepairReport(
        string workDir,
        string archiveDirectory,
        IReadOnlyList<string> repaired,
        IReadOnlyList<string> skipped)
    {
        var reportBuilder = new StringBuilder();
        reportBuilder.AppendLine($"Echo repair v6 run at {DateTimeOffset.Now:O}");
        reportBuilder.AppendLine($"Work directory: {workDir}");
        reportBuilder.AppendLine($"Archive: {archiveDirectory}");
        reportBuilder.AppendLine();
        reportBuilder.AppendLine($"Repaired: {repaired.Count}");
        foreach (var repairedLine in repaired)
        {
            reportBuilder.AppendLine($"- {repairedLine}");
        }

        reportBuilder.AppendLine();
        reportBuilder.AppendLine($"Skipped: {skipped.Count}");
        foreach (var skippedLine in skipped)
        {
            reportBuilder.AppendLine($"- {skippedLine}");
        }

        return reportBuilder.ToString();
    }

    private static string ResolvePublishedStem(MeetingSessionManifest manifest, ArtifactPathBuilder pathBuilder)
    {
        var mergedAudioPath = manifest.MergedAudioPath;
        if (!string.IsNullOrWhiteSpace(mergedAudioPath))
        {
            var mergedAudioStem = Path.GetFileNameWithoutExtension(mergedAudioPath);
            if (!string.IsNullOrWhiteSpace(mergedAudioStem))
            {
                return mergedAudioStem;
            }
        }

        return pathBuilder.BuildFileStem(
            manifest.Platform,
            manifest.StartedAtUtc,
            string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle);
    }

    private static ResolvedLoopbackSegmentCollection ResolveLoopbackCaptureSegments(
        string sessionRoot,
        IReadOnlyList<LoopbackCaptureSegment> segments)
    {
        var resolvedSegments = new List<LoopbackCaptureSegment>(segments.Count);
        foreach (var segment in segments)
        {
            var resolvedChunkPaths = ResolveChunkPaths(sessionRoot, segment.ChunkPaths);
            if (resolvedChunkPaths.MissingChunkPath is { } missingChunkPath)
            {
                return new ResolvedLoopbackSegmentCollection(Array.Empty<LoopbackCaptureSegment>(), missingChunkPath);
            }

            if (resolvedChunkPaths.Paths.Count == 0)
            {
                continue;
            }

            resolvedSegments.Add(segment with { ChunkPaths = resolvedChunkPaths.Paths });
        }

        return new ResolvedLoopbackSegmentCollection(resolvedSegments, null);
    }

    private static ResolvedMicrophoneSegmentCollection ResolveMicrophoneCaptureSegments(
        string sessionRoot,
        IReadOnlyList<MicrophoneCaptureSegment> segments)
    {
        var resolvedSegments = new List<MicrophoneCaptureSegment>(segments.Count);
        foreach (var segment in segments)
        {
            var resolvedChunkPaths = ResolveChunkPaths(sessionRoot, segment.ChunkPaths);
            if (resolvedChunkPaths.MissingChunkPath is { } missingChunkPath)
            {
                return new ResolvedMicrophoneSegmentCollection(Array.Empty<MicrophoneCaptureSegment>(), missingChunkPath);
            }

            if (resolvedChunkPaths.Paths.Count == 0)
            {
                continue;
            }

            resolvedSegments.Add(segment with { ChunkPaths = resolvedChunkPaths.Paths });
        }

        return new ResolvedMicrophoneSegmentCollection(resolvedSegments, null);
    }

    private static ResolvedChunkPaths ResolveChunkPaths(string sessionRoot, IReadOnlyList<string> chunkPaths)
    {
        var resolvedPaths = new List<string>(chunkPaths.Count);
        foreach (var chunkPath in chunkPaths)
        {
            if (string.IsNullOrWhiteSpace(chunkPath))
            {
                return new ResolvedChunkPaths(Array.Empty<string>(), "(blank)");
            }

            if (File.Exists(chunkPath))
            {
                resolvedPaths.Add(chunkPath);
                continue;
            }

            var localRawCandidate = Path.Combine(sessionRoot, "raw", Path.GetFileName(chunkPath));
            if (File.Exists(localRawCandidate))
            {
                resolvedPaths.Add(localRawCandidate);
                continue;
            }

            return new ResolvedChunkPaths(Array.Empty<string>(), chunkPath);
        }

        return new ResolvedChunkPaths(resolvedPaths, null);
    }

    private static string ResolveExistingProcessingAudioPath(
        string sessionRoot,
        string? mergedAudioPath,
        string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(mergedAudioPath) && File.Exists(mergedAudioPath))
        {
            return mergedAudioPath;
        }

        var processingFileName = string.IsNullOrWhiteSpace(mergedAudioPath)
            ? Path.GetFileName(fallbackPath)
            : Path.GetFileName(mergedAudioPath);
        var localProcessingCandidate = Path.Combine(sessionRoot, "processing", processingFileName);
        return File.Exists(localProcessingCandidate) ? localProcessingCandidate : fallbackPath;
    }

    private static void BackupIfPresent(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static int CountHelpfulPunctuation(string title)
    {
        return title.Count(character => character is ',' or '&' or '/' or '(' or ')');
    }

    private sealed record EchoRepairPassResult(int RepairedCount, int SkippedCount, string ReportPath)
    {
        public static EchoRepairPassResult Empty { get; } = new(0, 0, string.Empty);
    }

    private sealed record ResolvedChunkPaths(IReadOnlyList<string> Paths, string? MissingChunkPath);

    private sealed record ResolvedLoopbackSegmentCollection(IReadOnlyList<LoopbackCaptureSegment> Segments, string? MissingChunkPath);

    private sealed record ResolvedMicrophoneSegmentCollection(IReadOnlyList<MicrophoneCaptureSegment> Segments, string? MissingChunkPath);
}

public sealed record PublishedMeetingRepairResult(
    string MarkerPath,
    string ArchiveDirectory,
    int MergedSplitPairCount,
    int ArchivedArtifactCount,
    int ArchivedEditorArtifactCount,
    int ArchivedShortGenericTeamsMeetingCount,
    bool AlreadyApplied);
