using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class MeetingOutputCatalogService
{
    private const int MaxJsonTitlePreviewBytes = 64 * 1024;
    private const string CleanupSessionIdPrefix = "- Session ID: cleanup-";

    private static readonly Regex StemPattern = new(
        "^(?<date>\\d{4}-\\d{2}-\\d{2})_(?<time>\\d{6})_(?<platform>[a-z]+)_(?<slug>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JsonTitlePattern = new(
        "\"(?<name>title|Title)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownSpeakerLinePattern = new(
        "(?m)(?<prefix>^\\[\\d{2}:\\d{2}:\\d{2}\\s-\\s\\d{2}:\\d{2}:\\d{2}\\]\\s\\*\\*)(?<label>.+?)(?<suffix>:\\*\\*\\s)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ArtifactPathBuilder _pathBuilder;

    public MeetingOutputCatalogService(ArtifactPathBuilder pathBuilder)
    {
        _pathBuilder = pathBuilder;
    }

    public IReadOnlyList<MeetingOutputRecord> ListMeetings(string audioOutputDir, string transcriptOutputDir)
    {
        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir: null);
    }

    public IReadOnlyList<MeetingOutputRecord> ListMeetings(string audioOutputDir, string transcriptOutputDir, string? workDir)
    {
        var pathsByStem = new Dictionary<string, MeetingOutputMutableRecord>(StringComparer.OrdinalIgnoreCase);
        AddArtifacts(pathsByStem, audioOutputDir);
        AddArtifacts(pathsByStem, transcriptOutputDir);
        var manifestInfoByStem = LoadManifestInfoByStem(workDir);
        AddManifestOnlyRecords(pathsByStem, manifestInfoByStem);

        return pathsByStem.Values
            .Select(record => BuildRecord(
                record,
                manifestInfoByStem.TryGetValue(record.Stem, out var manifestInfo)
                    ? manifestInfo
                    : null))
            .OrderByDescending(record => record.StartedAtUtc)
            .ThenBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<MeetingOutputRecord> RenameMeetingAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        return await RenameMeetingAsync(
            audioOutputDir,
            transcriptOutputDir,
            existingStem,
            newTitle,
            workDir: null,
            cancellationToken);
    }

    public async Task<MeetingOutputRecord> RenameMeetingAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        string newTitle,
        string? workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(newTitle));
        }

        if (!TryParseStem(existingStem, out var stemInfo))
        {
            throw new InvalidOperationException($"The output stem '{existingStem}' does not match the expected format.");
        }

        var newStem = _pathBuilder.BuildFileStem(stemInfo.Platform, stemInfo.StartedAtUtc, newTitle);
        var meetings = ListMeetings(audioOutputDir, transcriptOutputDir, workDir);
        var existing = meetings.SingleOrDefault(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Unable to locate published artifacts for stem '{existingStem}'.");

        var renamePairs = BuildRenamePairs(existing, newStem);
        foreach (var pair in renamePairs)
        {
            if (!string.Equals(pair.SourcePath, pair.DestinationPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(pair.DestinationPath))
            {
                throw new IOException($"Cannot rename '{pair.SourcePath}' because '{pair.DestinationPath}' already exists.");
            }
        }

        foreach (var pair in renamePairs)
        {
            if (string.Equals(pair.SourcePath, pair.DestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Move(pair.SourcePath, pair.DestinationPath, overwrite: false);
        }

        if (!string.IsNullOrWhiteSpace(existing.MarkdownPath))
        {
            await UpdateMarkdownTitleAsync(GetRenamedArtifactPath(existing.MarkdownPath, newStem), newTitle, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existing.JsonPath))
        {
            await UpdateJsonTitleAsync(GetRenamedArtifactPath(existing.JsonPath, newStem), newTitle, cancellationToken);
        }

        await UpdateManifestTitleAsync(workDir, existingStem, newTitle, cancellationToken);

        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .Single(record => string.Equals(record.Stem, newStem, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MeetingOutputRecord> UpdateMeetingProjectAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        string? projectName,
        string? workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProjectName = NormalizeOptionalMetadataValue(projectName);
        var existing = ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .SingleOrDefault(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Unable to locate published artifacts for stem '{existingStem}'.");

        if (!string.IsNullOrWhiteSpace(existing.MarkdownPath) && File.Exists(existing.MarkdownPath))
        {
            await UpdateMarkdownProjectAsync(existing.MarkdownPath, normalizedProjectName, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existing.JsonPath) && File.Exists(existing.JsonPath))
        {
            await UpdateJsonProjectAsync(existing.JsonPath, normalizedProjectName, cancellationToken);
        }

        await UpdateManifestAsync(
            workDir,
            existingStem,
            manifest => manifest with
            {
                ProjectName = normalizedProjectName,
            },
            cancellationToken);

        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .Single(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MeetingOutputRecord> MergeMeetingAttendeesAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        IReadOnlyList<MeetingAttendee> attendees,
        string? workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .SingleOrDefault(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Unable to locate published artifacts for stem '{existingStem}'.");

        var mergedAttendees = NormalizeAttendees(existing.Attendees.Concat(attendees).ToArray());
        var mergedKeyAttendees = NormalizeKeyAttendees(
            (existing.KeyAttendees ?? Array.Empty<string>()).Concat(mergedAttendees.Select(attendee => attendee.Name)).ToArray());
        if (AreEquivalent(existing.Attendees, mergedAttendees) &&
            (existing.KeyAttendees ?? Array.Empty<string>()).SequenceEqual(mergedKeyAttendees, StringComparer.Ordinal))
        {
            return existing;
        }

        if (!string.IsNullOrWhiteSpace(existing.JsonPath) && File.Exists(existing.JsonPath))
        {
            await UpdateJsonAttendeesAsync(existing.JsonPath, mergedAttendees, cancellationToken);
            await UpdateJsonKeyAttendeesAsync(existing.JsonPath, mergedKeyAttendees, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existing.MarkdownPath) && File.Exists(existing.MarkdownPath))
        {
            await UpdateMarkdownKeyAttendeesAsync(existing.MarkdownPath, mergedKeyAttendees, cancellationToken);
        }

        await UpdateManifestAsync(
            workDir,
            existingStem,
            manifest => manifest with
            {
                Attendees = mergedAttendees,
                KeyAttendees = mergedKeyAttendees,
            },
            cancellationToken);

        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .Single(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ListSpeakerLabels(MeetingOutputRecord record)
    {
        var labels = TryReadJsonSpeakerLabels(record.JsonPath);
        if (labels.Count > 0)
        {
            return labels;
        }

        return TryReadMarkdownSpeakerLabels(record.MarkdownPath);
    }

    public async Task RenameSpeakerLabelsAsync(
        MeetingOutputRecord record,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedMap = speakerLabelMap
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Key) &&
                !string.IsNullOrWhiteSpace(entry.Value) &&
                !string.Equals(entry.Key.Trim(), entry.Value.Trim(), StringComparison.Ordinal))
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => entry.Value.Trim(),
                StringComparer.Ordinal);

        if (normalizedMap.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.JsonPath) && File.Exists(record.JsonPath))
        {
            await UpdateJsonSpeakerLabelsAsync(record.JsonPath, normalizedMap, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(record.MarkdownPath) && File.Exists(record.MarkdownPath))
        {
            await UpdateMarkdownSpeakerLabelsAsync(record.MarkdownPath, normalizedMap, cancellationToken);
        }
    }

    public async Task<string> CreateSyntheticManifestForPublishedMeetingAsync(
        MeetingOutputRecord record,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(record.AudioPath) || !File.Exists(record.AudioPath))
        {
            throw new FileNotFoundException("A published audio file is required to synthesize a work manifest.", record.AudioPath);
        }

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        if (!string.IsNullOrWhiteSpace(record.ManifestPath) && File.Exists(record.ManifestPath))
        {
            return record.ManifestPath;
        }

        var now = DateTimeOffset.UtcNow;
        var manifestStore = new SessionManifestStore(_pathBuilder);
        var sessionId = $"imported-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, sessionId);

        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(Path.Combine(sessionRoot, "raw"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "processing"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "logs"));

        var manifest = new MeetingSessionManifest
        {
            SessionId = sessionId,
            Platform = record.Platform,
            DetectedTitle = string.IsNullOrWhiteSpace(record.Title) ? record.Stem : record.Title,
            StartedAtUtc = record.StartedAtUtc == DateTimeOffset.MinValue ? now : record.StartedAtUtc,
            State = SessionState.Queued,
            DetectionEvidence = Array.Empty<DetectionSignal>(),
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            MergedAudioPath = record.AudioPath,
            ProjectName = record.ProjectName,
            KeyAttendees = NormalizeKeyAttendees(record.KeyAttendees ?? Array.Empty<string>()),
            DetectedAudioSource = NormalizeDetectedAudioSource(record.DetectedAudioSource),
            Attendees = record.Attendees,
            CaptureTimeline = record.CaptureTimeline ?? Array.Empty<CaptureTimelineEntry>(),
            LoopbackCaptureSegments = record.LoopbackCaptureSegments ?? Array.Empty<LoopbackCaptureSegment>(),
            ProcessingMetadata = new MeetingProcessingMetadata(
                record.TranscriptionModelFileName,
                record.HasSpeakerLabels),
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Queued, now, "Queued from an existing published audio file."),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
        };

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
        return manifestPath;
    }

    public async Task<MergedMeetingResult> MergeMeetingsAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        string mergedTitle,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(mergedTitle))
        {
            throw new ArgumentException("A merged meeting title is required.", nameof(mergedTitle));
        }

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        var orderedRecords = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Stem))
            .GroupBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(record => record.StartedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.MaxValue : record.StartedAtUtc)
            .ThenBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedRecords.Length < 2)
        {
            throw new InvalidOperationException("Select at least two published meetings to merge them.");
        }

        var audioPaths = orderedRecords
            .Select(record => !string.IsNullOrWhiteSpace(record.AudioPath) && File.Exists(record.AudioPath)
                ? record.AudioPath
                : throw new FileNotFoundException($"Meeting '{record.Title}' is missing a published audio file.", record.AudioPath))
            .ToArray();

        var mergedPlatform = ResolveMergedPlatform(orderedRecords);
        var manifestStore = new SessionManifestStore(_pathBuilder);
        var mergedManifest = await manifestStore.CreateAsync(
            workDir,
            mergedPlatform,
            mergedTitle.Trim(),
            Array.Empty<DetectionSignal>(),
            cancellationToken);

        var startedAtUtc = orderedRecords
            .Select(record => record.StartedAtUtc)
            .Where(value => value != DateTimeOffset.MinValue)
            .DefaultIfEmpty(mergedManifest.StartedAtUtc)
            .Min();
        var projectName = GetMergedProjectName(orderedRecords);
        var keyAttendees = NormalizeKeyAttendees(orderedRecords.SelectMany(record => record.KeyAttendees ?? Array.Empty<string>()).ToArray());
        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, mergedManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var mergedAudioPath = Path.Combine(sessionRoot, "processing", "merged.wav");

        await new WaveChunkMerger().MergePublishedAudioFilesAsync(audioPaths, mergedAudioPath, cancellationToken);

        mergedManifest = mergedManifest with
        {
            StartedAtUtc = startedAtUtc,
            MergedAudioPath = mergedAudioPath,
            ProjectName = projectName,
            KeyAttendees = keyAttendees,
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };
        await manifestStore.SaveAsync(mergedManifest, manifestPath, cancellationToken);

        return new MergedMeetingResult(
            manifestPath,
            _pathBuilder.BuildFileStem(mergedPlatform, startedAtUtc, mergedManifest.DetectedTitle),
            mergedManifest.DetectedTitle);
    }

    public async Task<SplitMeetingResult> SplitMeetingAsync(
        MeetingOutputRecord record,
        TimeSpan splitPoint,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        if (string.IsNullOrWhiteSpace(record.AudioPath) || !File.Exists(record.AudioPath))
        {
            throw new FileNotFoundException("A published audio file is required to split a meeting.", record.AudioPath);
        }

        using var durationReader = new AudioFileReader(record.AudioPath);
        if (splitPoint <= TimeSpan.Zero || splitPoint >= durationReader.TotalTime)
        {
            throw new InvalidOperationException("The split point must fall inside the published meeting duration.");
        }

        var baseTitle = string.IsNullOrWhiteSpace(record.Title) ? record.Stem : record.Title.Trim();
        var firstTitle = $"{baseTitle} part 1";
        var secondTitle = $"{baseTitle} part 2";
        var startedAtUtc = record.StartedAtUtc == DateTimeOffset.MinValue
            ? DateTimeOffset.UtcNow
            : record.StartedAtUtc;
        var secondStartedAtUtc = startedAtUtc + splitPoint;
        var platform = record.Platform;
        var manifestStore = new SessionManifestStore(_pathBuilder);

        var firstManifest = await manifestStore.CreateAsync(
            workDir,
            platform,
            firstTitle,
            Array.Empty<DetectionSignal>(),
            cancellationToken);
        var secondManifest = await manifestStore.CreateAsync(
            workDir,
            platform,
            secondTitle,
            Array.Empty<DetectionSignal>(),
            cancellationToken);

        var firstSessionRoot = _pathBuilder.BuildSessionRoot(workDir, firstManifest.SessionId);
        var secondSessionRoot = _pathBuilder.BuildSessionRoot(workDir, secondManifest.SessionId);
        var firstManifestPath = Path.Combine(firstSessionRoot, "manifest.json");
        var secondManifestPath = Path.Combine(secondSessionRoot, "manifest.json");
        var firstAudioPath = Path.Combine(firstSessionRoot, "processing", "merged.wav");
        var secondAudioPath = Path.Combine(secondSessionRoot, "processing", "merged.wav");

        await new WaveChunkMerger().SplitPublishedAudioFileAsync(
            record.AudioPath,
            splitPoint,
            firstAudioPath,
            secondAudioPath,
            cancellationToken);

        firstManifest = firstManifest with
        {
            DetectedTitle = firstTitle,
            StartedAtUtc = startedAtUtc,
            MergedAudioPath = firstAudioPath,
            ProjectName = record.ProjectName,
            KeyAttendees = NormalizeKeyAttendees(record.KeyAttendees ?? Array.Empty<string>()),
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };
        secondManifest = secondManifest with
        {
            DetectedTitle = secondTitle,
            StartedAtUtc = secondStartedAtUtc,
            MergedAudioPath = secondAudioPath,
            ProjectName = record.ProjectName,
            KeyAttendees = NormalizeKeyAttendees(record.KeyAttendees ?? Array.Empty<string>()),
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };

        await manifestStore.SaveAsync(firstManifest, firstManifestPath, cancellationToken);
        await manifestStore.SaveAsync(secondManifest, secondManifestPath, cancellationToken);

        return new SplitMeetingResult(
            firstManifestPath,
            _pathBuilder.BuildFileStem(platform, firstManifest.StartedAtUtc, firstManifest.DetectedTitle),
            firstManifest.DetectedTitle,
            secondManifestPath,
            _pathBuilder.BuildFileStem(platform, secondManifest.StartedAtUtc, secondManifest.DetectedTitle),
            secondManifest.DetectedTitle,
            splitPoint);
    }

    private static void AddArtifacts(IDictionary<string, MeetingOutputMutableRecord> pathsByStem, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(stem))
            {
                continue;
            }

            if (!pathsByStem.TryGetValue(stem, out var record))
            {
                record = new MeetingOutputMutableRecord(stem);
                pathsByStem.Add(stem, record);
            }

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                case ".mp3":
                case ".m4a":
                    record.AudioPath = path;
                    break;
                case ".md":
                    record.MarkdownPath = path;
                    break;
                case ".json":
                    record.JsonPath = path;
                    break;
                case ".ready":
                    record.ReadyMarkerPath = path;
                    break;
            }
        }
    }

    private static MeetingOutputRecord BuildRecord(MeetingOutputMutableRecord record, ManifestInfo? manifestInfo)
    {
        var jsonMetadata = TryReadJsonMetadata(record.JsonPath);
        var manifestState = ResolveManifestState(record, manifestInfo);

        if (TryParseStem(record.Stem, out var stemInfo))
        {
            var storedTitle = manifestInfo?.Title ?? jsonMetadata?.Title ?? TryReadMarkdownTitle(record.MarkdownPath);
            var projectName = manifestInfo?.ProjectName ?? jsonMetadata?.ProjectName ?? TryReadMarkdownProject(record.MarkdownPath);
            var keyAttendees = manifestInfo?.KeyAttendees ?? jsonMetadata?.KeyAttendees ?? TryReadMarkdownKeyAttendees(record.MarkdownPath);
            return new MeetingOutputRecord(
                record.Stem,
                string.IsNullOrWhiteSpace(storedTitle) ? HumanizeSlug(stemInfo.TitleSlug) : storedTitle,
                stemInfo.StartedAtUtc,
                stemInfo.Platform,
                ResolveDuration(stemInfo.StartedAtUtc, record.AudioPath, record.MarkdownPath, manifestInfo),
                record.AudioPath,
                record.MarkdownPath,
                record.JsonPath,
                record.ReadyMarkerPath,
                manifestInfo?.ManifestPath,
                manifestState,
                manifestInfo?.Attendees ?? jsonMetadata?.Attendees ?? Array.Empty<MeetingAttendee>(),
                manifestInfo?.ProcessingMetadata?.HasSpeakerLabels ?? jsonMetadata?.HasSpeakerLabels ?? false,
                manifestInfo?.ProcessingMetadata?.TranscriptionModelFileName ?? jsonMetadata?.TranscriptionModelFileName,
                projectName,
                manifestInfo?.DetectedAudioSource ?? jsonMetadata?.DetectedAudioSource,
                keyAttendees,
                manifestInfo?.CaptureTimeline,
                manifestInfo?.LoopbackCaptureSegments);
        }

        var fallbackTimestamp = record.AudioPath is not null
            ? File.GetCreationTimeUtc(record.AudioPath)
            : DateTimeOffset.MinValue;
        return new MeetingOutputRecord(
            record.Stem,
            record.Stem,
            fallbackTimestamp,
            MeetingPlatform.Unknown,
            ResolveDuration(fallbackTimestamp, record.AudioPath, record.MarkdownPath, manifestInfo),
            record.AudioPath,
            record.MarkdownPath,
            record.JsonPath,
            record.ReadyMarkerPath,
            manifestInfo?.ManifestPath,
            manifestState,
            manifestInfo?.Attendees ?? jsonMetadata?.Attendees ?? Array.Empty<MeetingAttendee>(),
            manifestInfo?.ProcessingMetadata?.HasSpeakerLabels ?? jsonMetadata?.HasSpeakerLabels ?? false,
            manifestInfo?.ProcessingMetadata?.TranscriptionModelFileName ?? jsonMetadata?.TranscriptionModelFileName,
            manifestInfo?.ProjectName ?? jsonMetadata?.ProjectName ?? TryReadMarkdownProject(record.MarkdownPath),
            manifestInfo?.DetectedAudioSource ?? jsonMetadata?.DetectedAudioSource,
            manifestInfo?.KeyAttendees ?? jsonMetadata?.KeyAttendees ?? TryReadMarkdownKeyAttendees(record.MarkdownPath),
            manifestInfo?.CaptureTimeline,
            manifestInfo?.LoopbackCaptureSegments);
    }

    private static SessionState? ResolveManifestState(MeetingOutputMutableRecord record, ManifestInfo? manifestInfo)
    {
        if (manifestInfo is null)
        {
            return null;
        }

        if (HasPublishedArtifacts(record) &&
            manifestInfo.IsImportedSource &&
            manifestInfo.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing or SessionState.Failed)
        {
            return null;
        }

        return manifestInfo.State;
    }

    private static void AddManifestOnlyRecords(
        IDictionary<string, MeetingOutputMutableRecord> pathsByStem,
        IDictionary<string, ManifestInfo> manifestInfoByStem)
    {
        foreach (var pair in manifestInfoByStem)
        {
            if (pathsByStem.ContainsKey(pair.Key) ||
                !ShouldIncludeManifestOnlyRecord(pair.Value))
            {
                continue;
            }

            pathsByStem.Add(pair.Key, new MeetingOutputMutableRecord(pair.Key));
        }
    }

    private IDictionary<string, ManifestInfo> LoadManifestInfoByStem(string? workDir)
    {
        var infoByStem = new Dictionary<string, ManifestInfo>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return infoByStem;
        }

        var manifestStore = new SessionManifestStore(_pathBuilder);
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = manifestStore.LoadAsync(manifestPath).GetAwaiter().GetResult();
                var title = string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle;
                var stem = _pathBuilder.BuildFileStem(manifest.Platform, manifest.StartedAtUtc, title);
                var candidate = new ManifestInfo(
                    title,
                    manifestPath,
                    manifest.State,
                    manifest.StartedAtUtc,
                    manifest.EndedAtUtc,
                    manifest.ProjectName,
                    NormalizeKeyAttendees(manifest.KeyAttendees),
                    NormalizeAttendees(manifest.Attendees),
                    manifest.ProcessingMetadata,
                    NormalizeDetectedAudioSource(manifest.DetectedAudioSource),
                    manifest.CaptureTimeline,
                    manifest.LoopbackCaptureSegments,
                    manifest.ImportedSourceAudio is not null);
                if (!infoByStem.TryGetValue(stem, out var existing) || ShouldReplaceManifestInfo(existing, candidate))
                {
                    infoByStem[stem] = candidate;
                }
            }
            catch
            {
                // Ignore malformed or partially-written manifests when listing published outputs.
            }
        }

        return infoByStem;
    }

    private static bool ShouldReplaceManifestInfo(ManifestInfo existing, ManifestInfo candidate)
    {
        var existingPriority = GetManifestSelectionPriority(existing);
        var candidatePriority = GetManifestSelectionPriority(candidate);
        if (candidatePriority != existingPriority)
        {
            return candidatePriority < existingPriority;
        }

        if (candidate.State != existing.State)
        {
            if (candidate.State == SessionState.Published)
            {
                return true;
            }

            if (existing.State == SessionState.Published)
            {
                return false;
            }
        }

        return string.Compare(candidate.ManifestPath, existing.ManifestPath, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int GetManifestSelectionPriority(ManifestInfo manifestInfo)
    {
        if (!manifestInfo.IsImportedSource && manifestInfo.State == SessionState.Published)
        {
            return 0;
        }

        if (!manifestInfo.IsImportedSource)
        {
            return 1;
        }

        if (manifestInfo.State == SessionState.Published)
        {
            return 2;
        }

        return 3;
    }

    private static bool HasPublishedArtifacts(MeetingOutputMutableRecord record)
    {
        var hasAudio = !string.IsNullOrWhiteSpace(record.AudioPath);
        var hasTranscript = !string.IsNullOrWhiteSpace(record.MarkdownPath) || !string.IsNullOrWhiteSpace(record.JsonPath);
        return !string.IsNullOrWhiteSpace(record.ReadyMarkerPath) || (hasAudio && hasTranscript);
    }

    private static bool ShouldIncludeManifestOnlyRecord(ManifestInfo manifestInfo)
    {
        return manifestInfo.State is SessionState.Finalizing or SessionState.Queued or SessionState.Processing or SessionState.Failed;
    }

    private static bool TryParseStem(string stem, out StemInfo stemInfo)
    {
        var match = StemPattern.Match(stem);
        if (!match.Success)
        {
            stemInfo = default;
            return false;
        }

        var timestampText = $"{match.Groups["date"].Value}_{match.Groups["time"].Value}";
        if (!DateTimeOffset.TryParseExact(
            timestampText,
            "yyyy-MM-dd_HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var startedAtUtc))
        {
            stemInfo = default;
            return false;
        }

        var platform = match.Groups["platform"].Value switch
        {
            "teams" => MeetingPlatform.Teams,
            "gmeet" => MeetingPlatform.GoogleMeet,
            "manual" => MeetingPlatform.Manual,
            _ => MeetingPlatform.Unknown,
        };

        stemInfo = new StemInfo(startedAtUtc, platform, match.Groups["slug"].Value);
        return true;
    }

    private static string HumanizeSlug(string slug)
    {
        var words = slug
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
            .ToArray();
        return words.Length == 0 ? "Session" : string.Join(' ', words);
    }

    private static JsonTranscriptMetadata? TryReadJsonMetadata(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            if (node is null)
            {
                return null;
            }

            var hasSpeakerLabels = TryGetJsonBoolean(node, "HasSpeakerLabels", "hasSpeakerLabels")
                ?? TryInferHasSpeakerLabels(node);

            return new JsonTranscriptMetadata(
                GetJsonString(node, "Title", "title"),
                NormalizeAttendees(TryReadJsonAttendees(node)),
                GetJsonString(node, "TranscriptionModelFileName", "transcriptionModelFileName"),
                NormalizeOptionalMetadataValue(GetJsonString(node, "ProjectName", "projectName")),
                NormalizeKeyAttendees(TryReadJsonKeyAttendees(node)),
                hasSpeakerLabels,
                TryReadDetectedAudioSource(node));
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TryReadJsonKeyAttendees(JsonObject node)
    {
        var keyAttendeesNode = node["KeyAttendees"] as JsonArray ?? node["keyAttendees"] as JsonArray;
        if (keyAttendeesNode is null)
        {
            return Array.Empty<string>();
        }

        var keyAttendees = new List<string>();
        foreach (var attendeeNode in keyAttendeesNode)
        {
            if (attendeeNode is JsonValue value &&
                value.TryGetValue<string>(out var attendeeName) &&
                !string.IsNullOrWhiteSpace(attendeeName))
            {
                keyAttendees.Add(attendeeName);
            }
        }

        return keyAttendees;
    }

    private static IReadOnlyList<MeetingAttendee> TryReadJsonAttendees(JsonObject node)
    {
        var attendeesNode = node["Attendees"] as JsonArray ?? node["attendees"] as JsonArray;
        if (attendeesNode is null)
        {
            return Array.Empty<MeetingAttendee>();
        }

        var attendees = new List<MeetingAttendee>();
        foreach (var attendeeNode in attendeesNode)
        {
            if (attendeeNode is not JsonObject attendeeObject)
            {
                continue;
            }

            var name = GetJsonString(attendeeObject, "Name", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var sourcesNode = attendeeObject["Sources"] as JsonArray ?? attendeeObject["sources"] as JsonArray;
            var sources = new List<MeetingAttendeeSource>();
            if (sourcesNode is not null)
            {
                foreach (var sourceNode in sourcesNode)
                {
                    if (TryParseAttendeeSource(sourceNode, out var source))
                    {
                        sources.Add(source);
                    }
                }
            }

            attendees.Add(new MeetingAttendee(
                name.Trim(),
                sources.Count == 0 ? [MeetingAttendeeSource.Unknown] : sources.ToArray()));
        }

        return attendees;
    }

    private static bool TryParseAttendeeSource(JsonNode? sourceNode, out MeetingAttendeeSource source)
    {
        if (sourceNode is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text) &&
                Enum.TryParse<MeetingAttendeeSource>(text, ignoreCase: true, out source) &&
                Enum.IsDefined(source))
            {
                return true;
            }

            if (value.TryGetValue<int>(out var numericValue) &&
                Enum.IsDefined(typeof(MeetingAttendeeSource), numericValue))
            {
                source = (MeetingAttendeeSource)numericValue;
                return true;
            }
        }

        source = MeetingAttendeeSource.Unknown;
        return false;
    }

    private static bool TryInferHasSpeakerLabels(JsonObject node)
    {
        var speakers = GetSpeakersArray(node);
        if (speakers is { Count: > 0 })
        {
            return true;
        }

        var segments = GetSegmentsArray(node);
        if (segments is null)
        {
            return false;
        }

        foreach (var segmentNode in segments)
        {
            if (segmentNode is not JsonObject segmentObject)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(GetJsonString(segmentObject, "SpeakerLabel", "speakerLabel", "DisplaySpeakerLabel", "displaySpeakerLabel")) ||
                !string.IsNullOrWhiteSpace(GetJsonString(segmentObject, "SpeakerId", "speakerId")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool? TryGetJsonBoolean(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<bool>(out var boolean))
            {
                return boolean;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> TryReadJsonSpeakerLabels(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            var speakers = GetSpeakersArray(node);
            if (speakers is { Count: > 0 })
            {
                var speakerLabels = new List<string>();
                var seenSpeakerLabels = new HashSet<string>(StringComparer.Ordinal);
                foreach (var speakerNode in speakers)
                {
                    if (speakerNode is not JsonObject speakerObject)
                    {
                        continue;
                    }

                    var displayName = GetJsonString(speakerObject, "DisplayName", "displayName");
                    if (string.IsNullOrWhiteSpace(displayName) || !seenSpeakerLabels.Add(displayName))
                    {
                        continue;
                    }

                    speakerLabels.Add(displayName);
                }

                if (speakerLabels.Count > 0)
                {
                    return speakerLabels;
                }
            }

            var segments = GetSegmentsArray(node);
            if (segments is null)
            {
                return Array.Empty<string>();
            }

            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var segmentNode in segments)
            {
                if (segmentNode is not JsonObject segmentObject)
                {
                    continue;
                }

                var label = GetJsonString(segmentObject, "SpeakerLabel", "speakerLabel", "DisplaySpeakerLabel", "displaySpeakerLabel");
                if (string.IsNullOrWhiteSpace(label) || !seen.Add(label))
                {
                    continue;
                }

                labels.Add(label);
            }

            return labels;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> TryReadMarkdownSpeakerLabels(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var content = File.ReadAllText(markdownPath);
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in MarkdownSpeakerLinePattern.Matches(content))
            {
                var label = match.Groups["label"].Value.Trim();
                if (string.IsNullOrWhiteSpace(label) || !seen.Add(label))
                {
                    continue;
                }

                labels.Add(label);
            }

            return labels;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static TimeSpan? ResolveDuration(
        DateTimeOffset startedAtUtc,
        string? audioPath,
        string? markdownPath,
        ManifestInfo? manifestInfo)
    {
        var audioDuration = TryReadAudioDuration(audioPath);
        if (IsRepairGeneratedPublishedArtifact(markdownPath) && audioDuration is not null)
        {
            return audioDuration;
        }

        var durationStart = manifestInfo?.StartedAtUtc ?? startedAtUtc;
        if (manifestInfo?.EndedAtUtc is { } endedAtUtc &&
            durationStart != DateTimeOffset.MinValue &&
            endedAtUtc >= durationStart)
        {
            return endedAtUtc - durationStart;
        }

        return audioDuration;
    }

    private static bool IsRepairGeneratedPublishedArtifact(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return false;
        }

        try
        {
            foreach (var line in File.ReadLines(markdownPath).Take(12))
            {
                if (line.StartsWith(CleanupSessionIdPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static TimeSpan? TryReadAudioDuration(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return null;
        }

        try
        {
            using var reader = new AudioFileReader(audioPath);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadJsonTitle(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(jsonPath);
            var previewLength = (int)Math.Min(stream.Length, MaxJsonTitlePreviewBytes);
            if (previewLength <= 0)
            {
                return null;
            }

            var previewBuffer = new byte[previewLength];
            var bytesRead = stream.Read(previewBuffer, 0, previewBuffer.Length);
            if (bytesRead <= 0)
            {
                return null;
            }

            var previewText = System.Text.Encoding.UTF8.GetString(previewBuffer, 0, bytesRead);
            var match = JsonTitlePattern.Match(previewText);
            if (!match.Success)
            {
                return null;
            }

            return Regex.Unescape(match.Groups["value"].Value);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadMarkdownTitle(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return null;
        }

        try
        {
            var firstLine = File.ReadLines(markdownPath).FirstOrDefault();
            return firstLine is not null && firstLine.StartsWith("# ", StringComparison.Ordinal)
                ? firstLine[2..].Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadMarkdownProject(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(markdownPath))
            {
                if (!line.StartsWith("- Project:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return NormalizeOptionalMetadataValue(line["- Project:".Length..]);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TryReadMarkdownKeyAttendees(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            foreach (var line in File.ReadLines(markdownPath))
            {
                if (!line.StartsWith("- Key Attendees:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return NormalizeKeyAttendees(
                    line["- Key Attendees:".Length..]
                        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            return Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<RenamePair> BuildRenamePairs(MeetingOutputRecord existing, string newStem)
    {
        var pairs = new List<RenamePair>();
        AddPairIfPresent(existing.AudioPath, newStem, pairs);
        AddPairIfPresent(existing.MarkdownPath, newStem, pairs);
        AddPairIfPresent(existing.JsonPath, newStem, pairs);
        AddPairIfPresent(existing.ReadyMarkerPath, newStem, pairs);
        return pairs;
    }

    private static void AddPairIfPresent(string? sourcePath, string newStem, ICollection<RenamePair> pairs)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Artifact path must include a directory.");
        pairs.Add(new RenamePair(sourcePath, Path.Combine(directory, $"{newStem}{Path.GetExtension(sourcePath)}")));
    }

    private static string GetRenamedArtifactPath(string existingPath, string newStem)
    {
        var directory = Path.GetDirectoryName(existingPath)
            ?? throw new InvalidOperationException("Artifact path must include a directory.");
        return Path.Combine(directory, $"{newStem}{Path.GetExtension(existingPath)}");
    }

    private static async Task UpdateMarkdownTitleAsync(string markdownPath, string newTitle, CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(markdownPath, cancellationToken);
        if (lines.Length == 0)
        {
            lines = new[] { $"# {newTitle}" };
        }
        else
        {
            lines[0] = $"# {newTitle}";
        }

        await File.WriteAllLinesAsync(markdownPath, lines, cancellationToken);
    }

    private static async Task UpdateJsonTitleAsync(string jsonPath, string newTitle, CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        await UpdateJsonAsync(
            jsonPath,
            node =>
            {
                var propertyName = node.ContainsKey("title") ? "title" : "Title";
                node[propertyName] = newTitle;
            },
            cancellationToken);
    }

    private static async Task UpdateJsonProjectAsync(
        string jsonPath,
        string? projectName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        await UpdateJsonAsync(
            jsonPath,
            node => SetJsonStringProperty(node, "projectName", "ProjectName", projectName),
            cancellationToken);
    }

    private static async Task UpdateJsonAttendeesAsync(
        string jsonPath,
        IReadOnlyList<MeetingAttendee> attendees,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        await UpdateJsonAsync(
            jsonPath,
            node => node["attendees"] = BuildAttendeesJsonArray(attendees),
            cancellationToken);
    }

    private static async Task UpdateJsonKeyAttendeesAsync(
        string jsonPath,
        IReadOnlyList<string> keyAttendees,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        await UpdateJsonAsync(
            jsonPath,
            node => node["keyAttendees"] = BuildKeyAttendeesJsonArray(keyAttendees),
            cancellationToken);
    }

    private static async Task UpdateJsonSpeakerLabelsAsync(
        string jsonPath,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        await UpdateJsonAsync(
            jsonPath,
            node =>
            {
                var speakers = GetSpeakersArray(node);
                if (speakers is not null)
                {
                    foreach (var speakerNode in speakers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (speakerNode is not JsonObject speakerObject)
                        {
                            continue;
                        }

                        var propertyName = speakerObject.ContainsKey("displayName") ? "displayName" : "DisplayName";
                        var label = GetJsonString(speakerObject, "DisplayName", "displayName");
                        if (string.IsNullOrWhiteSpace(label) || !speakerLabelMap.TryGetValue(label, out var updatedLabel))
                        {
                            continue;
                        }

                        speakerObject[propertyName] = updatedLabel;
                    }
                }

                var segments = GetSegmentsArray(node);
                if (segments is null)
                {
                    return;
                }

                foreach (var segmentNode in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (segmentNode is not JsonObject segmentObject)
                    {
                        continue;
                    }

                    foreach (var propertyName in new[]
                             {
                                 segmentObject.ContainsKey("speakerLabel") ? "speakerLabel" : "SpeakerLabel",
                                 segmentObject.ContainsKey("displaySpeakerLabel") ? "displaySpeakerLabel" : "DisplaySpeakerLabel",
                             }.Distinct(StringComparer.Ordinal))
                    {
                        var label = GetJsonString(segmentObject, propertyName);
                        if (string.IsNullOrWhiteSpace(label) || !speakerLabelMap.TryGetValue(label, out var updatedLabel))
                        {
                            continue;
                        }

                        segmentObject[propertyName] = updatedLabel;
                    }
                }
            },
            cancellationToken);
    }

    private static async Task UpdateMarkdownProjectAsync(
        string markdownPath,
        string? projectName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var lines = (await File.ReadAllLinesAsync(markdownPath, cancellationToken)).ToList();
        var existingProjectLineIndex = lines.FindIndex(line => line.StartsWith("- Project:", StringComparison.OrdinalIgnoreCase));
        var normalizedProjectName = NormalizeOptionalMetadataValue(projectName);

        if (string.IsNullOrWhiteSpace(normalizedProjectName))
        {
            if (existingProjectLineIndex >= 0)
            {
                lines.RemoveAt(existingProjectLineIndex);
            }
        }
        else
        {
            var projectLine = $"- Project: {normalizedProjectName}";
            if (existingProjectLineIndex >= 0)
            {
                lines[existingProjectLineIndex] = projectLine;
            }
            else
            {
                var insertAfterIndex = lines.FindLastIndex(line =>
                    line.StartsWith("- Started", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("- Platform:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("- Session ID:", StringComparison.OrdinalIgnoreCase));
                if (insertAfterIndex < 0)
                {
                    insertAfterIndex = lines.FindIndex(line => line.StartsWith("# ", StringComparison.Ordinal));
                }

                var insertIndex = insertAfterIndex >= 0 ? insertAfterIndex + 1 : 0;
                lines.Insert(insertIndex, projectLine);
            }
        }

        await File.WriteAllLinesAsync(markdownPath, lines, cancellationToken);
    }

    private static async Task UpdateMarkdownKeyAttendeesAsync(
        string markdownPath,
        IReadOnlyList<string> keyAttendees,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var lines = (await File.ReadAllLinesAsync(markdownPath, cancellationToken)).ToList();
        var existingKeyAttendeesLineIndex = lines.FindIndex(line => line.StartsWith("- Key Attendees:", StringComparison.OrdinalIgnoreCase));
        var normalizedKeyAttendees = NormalizeKeyAttendees(keyAttendees);

        if (normalizedKeyAttendees.Count == 0)
        {
            if (existingKeyAttendeesLineIndex >= 0)
            {
                lines.RemoveAt(existingKeyAttendeesLineIndex);
            }
        }
        else
        {
            var keyAttendeesLine = $"- Key Attendees: {string.Join(", ", normalizedKeyAttendees)}";
            if (existingKeyAttendeesLineIndex >= 0)
            {
                lines[existingKeyAttendeesLineIndex] = keyAttendeesLine;
            }
            else
            {
                var insertAfterIndex = lines.FindLastIndex(line =>
                    line.StartsWith("- Project:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("- Started", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("- Platform:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("- Session ID:", StringComparison.OrdinalIgnoreCase));
                if (insertAfterIndex < 0)
                {
                    insertAfterIndex = lines.FindIndex(line => line.StartsWith("# ", StringComparison.Ordinal));
                }

                var insertIndex = insertAfterIndex >= 0 ? insertAfterIndex + 1 : 0;
                lines.Insert(insertIndex, keyAttendeesLine);
            }
        }

        await File.WriteAllLinesAsync(markdownPath, lines, cancellationToken);
    }

    private static async Task UpdateJsonAsync(
        string jsonPath,
        Action<JsonObject> update,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"Unable to parse transcript JSON '{jsonPath}'.");
        update(node);
        await File.WriteAllTextAsync(
            jsonPath,
            node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }),
            cancellationToken);
    }

    private static async Task UpdateMarkdownSpeakerLabelsAsync(
        string markdownPath,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        var updated = MarkdownSpeakerLinePattern.Replace(
            markdown,
            match =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var label = match.Groups["label"].Value.Trim();
                if (!speakerLabelMap.TryGetValue(label, out var updatedLabel))
                {
                    return match.Value;
                }

                return $"{match.Groups["prefix"].Value}{updatedLabel}{match.Groups["suffix"].Value}";
            });

        await File.WriteAllTextAsync(markdownPath, updated, cancellationToken);
    }

    private static JsonArray? GetSegmentsArray(JsonObject? node)
    {
        if (node is null)
        {
            return null;
        }

        return node["Segments"] as JsonArray ?? node["segments"] as JsonArray;
    }

    private static JsonArray? GetSpeakersArray(JsonObject? node)
    {
        if (node is null)
        {
            return null;
        }

        return node["Speakers"] as JsonArray ?? node["speakers"] as JsonArray;
    }

    private static void SetJsonStringProperty(
        JsonObject node,
        string camelCasePropertyName,
        string pascalCasePropertyName,
        string? value)
    {
        var normalizedValue = NormalizeOptionalMetadataValue(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            node.Remove(camelCasePropertyName);
            node.Remove(pascalCasePropertyName);
            return;
        }

        var propertyName = node.ContainsKey(camelCasePropertyName)
            ? camelCasePropertyName
            : node.ContainsKey(pascalCasePropertyName)
                ? pascalCasePropertyName
                : camelCasePropertyName;
        node[propertyName] = normalizedValue;
    }

    private static JsonArray BuildAttendeesJsonArray(IReadOnlyList<MeetingAttendee> attendees)
    {
        return new JsonArray(
            NormalizeAttendees(attendees)
                .Select(attendee => (JsonNode)new JsonObject
                {
                    ["name"] = attendee.Name,
                    ["sources"] = new JsonArray(attendee.Sources.Select(source => JsonValue.Create(source.ToString())).ToArray()),
                })
                .ToArray());
    }

    private static DetectedAudioSource? TryReadDetectedAudioSource(JsonObject node)
    {
        var audioSourceNode = node["DetectedAudioSource"] as JsonObject ?? node["detectedAudioSource"] as JsonObject;
        if (audioSourceNode is null)
        {
            return null;
        }

        var appName = NormalizeOptionalMetadataValue(GetJsonString(audioSourceNode, "AppName", "appName"));
        if (string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        if (!TryParseJsonEnum(audioSourceNode, out AudioSourceMatchKind matchKind, "MatchKind", "matchKind"))
        {
            matchKind = AudioSourceMatchKind.EndpointFallback;
        }

        if (!TryParseJsonEnum(audioSourceNode, out AudioSourceConfidence confidence, "Confidence", "confidence"))
        {
            confidence = AudioSourceConfidence.Low;
        }

        var observedAtUtc = TryGetJsonDateTimeOffset(audioSourceNode, "ObservedAtUtc", "observedAtUtc")
            ?? DateTimeOffset.MinValue;

        return NormalizeDetectedAudioSource(new DetectedAudioSource(
            appName,
            NormalizeOptionalMetadataValue(GetJsonString(audioSourceNode, "WindowTitle", "windowTitle")),
            NormalizeOptionalMetadataValue(GetJsonString(audioSourceNode, "BrowserTabTitle", "browserTabTitle")),
            matchKind,
            confidence,
            observedAtUtc));
    }

    private static string? GetJsonString(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetJsonDateTimeOffset(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is not JsonValue value)
            {
                continue;
            }

            if (value.TryGetValue<DateTimeOffset>(out var timestamp))
            {
                return timestamp;
            }

            if (value.TryGetValue<string>(out var text) &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
            {
                return timestamp;
            }
        }

        return null;
    }

    private static bool TryParseJsonEnum<TEnum>(JsonObject node, out TEnum value, params string[] propertyNames)
        where TEnum : struct, Enum
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is not JsonValue jsonValue)
            {
                continue;
            }

            if (jsonValue.TryGetValue<string>(out var text) &&
                Enum.TryParse<TEnum>(text, ignoreCase: true, out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<int>(out var numericValue) &&
                Enum.IsDefined(typeof(TEnum), numericValue))
            {
                value = (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
                return true;
            }
        }

        value = default;
        return false;
    }

    private async Task UpdateManifestTitleAsync(
        string? workDir,
        string existingStem,
        string newTitle,
        CancellationToken cancellationToken)
    {
        await UpdateManifestAsync(
            workDir,
            existingStem,
            manifest => manifest with
            {
                DetectedTitle = newTitle,
            },
            cancellationToken);
    }

    private async Task UpdateManifestAsync(
        string? workDir,
        string existingStem,
        Func<MeetingSessionManifest, MeetingSessionManifest> update,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return;
        }

        var manifestStore = new SessionManifestStore(_pathBuilder);
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MeetingSessionManifest manifest;
            try
            {
                manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken);
            }
            catch
            {
                continue;
            }

            var currentTitle = string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle;
            var manifestStem = _pathBuilder.BuildFileStem(manifest.Platform, manifest.StartedAtUtc, currentTitle);
            if (!string.Equals(manifestStem, existingStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var updatedManifest = update(manifest);
            if (ReferenceEquals(updatedManifest, manifest) || updatedManifest == manifest)
            {
                return;
            }

            await manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
            return;
        }
    }

    private sealed class MeetingOutputMutableRecord
    {
        public MeetingOutputMutableRecord(string stem)
        {
            Stem = stem;
        }

        public string Stem { get; }

        public string? AudioPath { get; set; }

        public string? MarkdownPath { get; set; }

        public string? JsonPath { get; set; }

        public string? ReadyMarkerPath { get; set; }
    }

    private static IReadOnlyList<MeetingAttendee> NormalizeAttendees(IReadOnlyList<MeetingAttendee>? attendees)
    {
        if (attendees is null || attendees.Count == 0)
        {
            return Array.Empty<MeetingAttendee>();
        }

        var merged = new List<(string Name, List<MeetingAttendeeSource> Sources)>();
        foreach (var attendee in attendees)
        {
            if (string.IsNullOrWhiteSpace(attendee.Name))
            {
                continue;
            }

            var normalizedName = MeetingMetadataNameMatcher.NormalizeDisplayName(attendee.Name);
            var existingIndex = merged.FindIndex(existing =>
                MeetingMetadataNameMatcher.AreReasonableMatch(existing.Name, normalizedName));
            if (existingIndex < 0)
            {
                var newSources = attendee.Sources.Distinct().ToList();
                if (newSources.Count == 0)
                {
                    newSources.Add(MeetingAttendeeSource.Unknown);
                }

                merged.Add((normalizedName, newSources));
                continue;
            }

            var existing = merged[existingIndex];
            var preferredName = MeetingMetadataNameMatcher.ChoosePreferredDisplayName(existing.Name, normalizedName);
            var existingSources = existing.Sources;
            foreach (var source in attendee.Sources.Distinct())
            {
                if (!existingSources.Contains(source))
                {
                    existingSources.Add(source);
                }
            }

            if (existingSources.Count == 0)
            {
                existingSources.Add(MeetingAttendeeSource.Unknown);
            }

            merged[existingIndex] = (preferredName, existingSources);
        }

        return merged
            .Select(item => new MeetingAttendee(item.Name, item.Sources.ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeKeyAttendees(IReadOnlyList<string>? keyAttendees)
    {
        return MeetingMetadataNameMatcher.MergeNames(keyAttendees, Array.Empty<string>());
    }

    private static bool AreEquivalent(
        IReadOnlyList<MeetingAttendee> left,
        IReadOnlyList<MeetingAttendee> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal) ||
                !left[index].Sources.SequenceEqual(right[index].Sources))
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeOptionalMetadataValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static JsonArray BuildKeyAttendeesJsonArray(IReadOnlyList<string> keyAttendees)
    {
        var array = new JsonArray();
        foreach (var attendee in NormalizeKeyAttendees(keyAttendees))
        {
            array.Add(attendee);
        }

        return array;
    }

    private static DetectedAudioSource? NormalizeDetectedAudioSource(DetectedAudioSource? audioSource)
    {
        if (audioSource is null)
        {
            return null;
        }

        var appName = NormalizeOptionalMetadataValue(audioSource.AppName);
        if (string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        return audioSource with
        {
            AppName = appName,
            WindowTitle = NormalizeOptionalMetadataValue(audioSource.WindowTitle),
            BrowserTabTitle = NormalizeOptionalMetadataValue(audioSource.BrowserTabTitle),
        };
    }

    private sealed record ManifestInfo(
        string Title,
        string ManifestPath,
        SessionState State,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string? ProjectName,
        IReadOnlyList<string> KeyAttendees,
        IReadOnlyList<MeetingAttendee> Attendees,
        MeetingProcessingMetadata? ProcessingMetadata,
        DetectedAudioSource? DetectedAudioSource,
        IReadOnlyList<CaptureTimelineEntry> CaptureTimeline,
        IReadOnlyList<LoopbackCaptureSegment> LoopbackCaptureSegments,
        bool IsImportedSource);

    private sealed record JsonTranscriptMetadata(
        string? Title,
        IReadOnlyList<MeetingAttendee> Attendees,
        string? TranscriptionModelFileName,
        string? ProjectName,
        IReadOnlyList<string> KeyAttendees,
        bool HasSpeakerLabels,
        DetectedAudioSource? DetectedAudioSource);

    private readonly record struct StemInfo(DateTimeOffset StartedAtUtc, MeetingPlatform Platform, string TitleSlug);

    private readonly record struct RenamePair(string SourcePath, string DestinationPath);

    private static MeetingPlatform ResolveMergedPlatform(IReadOnlyList<MeetingOutputRecord> records)
    {
        var distinctPlatforms = records
            .Select(record => record.Platform)
            .Where(platform => platform != MeetingPlatform.Unknown)
            .Distinct()
            .ToArray();

        return distinctPlatforms.Length == 1
            ? distinctPlatforms[0]
            : MeetingPlatform.Manual;
    }

    private static string? GetMergedProjectName(IReadOnlyList<MeetingOutputRecord> records)
    {
        var distinctProjectNames = records
            .Select(record => NormalizeOptionalMetadataValue(record.ProjectName))
            .Where(projectName => !string.IsNullOrWhiteSpace(projectName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinctProjectNames.Length == 1
            ? distinctProjectNames[0]
            : null;
    }
}

public sealed record MeetingOutputRecord(
    string Stem,
    string Title,
    DateTimeOffset StartedAtUtc,
    MeetingPlatform Platform,
    TimeSpan? Duration,
    string? AudioPath,
    string? MarkdownPath,
    string? JsonPath,
    string? ReadyMarkerPath,
    string? ManifestPath,
    SessionState? ManifestState,
    IReadOnlyList<MeetingAttendee> Attendees,
    bool HasSpeakerLabels,
    string? TranscriptionModelFileName,
    string? ProjectName = null,
    DetectedAudioSource? DetectedAudioSource = null,
    IReadOnlyList<string>? KeyAttendees = null,
    IReadOnlyList<CaptureTimelineEntry>? CaptureTimeline = null,
    IReadOnlyList<LoopbackCaptureSegment>? LoopbackCaptureSegments = null);

public sealed record MergedMeetingResult(
    string ManifestPath,
    string ExpectedStem,
    string Title);

public sealed record SplitMeetingResult(
    string FirstManifestPath,
    string FirstExpectedStem,
    string FirstTitle,
    string SecondManifestPath,
    string SecondExpectedStem,
    string SecondTitle,
    TimeSpan SplitPoint);
