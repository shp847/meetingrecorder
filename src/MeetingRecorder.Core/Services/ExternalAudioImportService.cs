using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class ExternalAudioImportService
{
    private static readonly Regex PublishedStemPattern = new(
        "^(?<date>\\d{4}-\\d{2}-\\d{2})_(?<time>\\d{6})_(?<platform>[a-z]+)_(?<slug>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".mp4",
    };

    private static readonly TimeSpan MinimumFileQuietPeriod = TimeSpan.FromSeconds(15);

    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;
    private readonly TranscriptionAudioPreparer _audioPreparer;

    public ExternalAudioImportService(ArtifactPathBuilder pathBuilder)
    {
        _pathBuilder = pathBuilder;
        _manifestStore = new SessionManifestStore(pathBuilder);
        _audioPreparer = new TranscriptionAudioPreparer();
    }

    public async Task<IReadOnlyList<ImportedExternalAudioResult>> ImportPendingAudioFilesAsync(
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = await ScanWatchedAudioFolderAsync(config, nowUtc, cancellationToken);
        var imported = new List<ImportedExternalAudioResult>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.CanQueue)
            {
                continue;
            }

            var request = new ExternalAudioImportRequest(
                candidate.SourcePath,
                candidate.SourceDisplayName,
                candidate.SourceSizeBytes,
                candidate.SourceLastWriteUtc,
                candidate.ImportMethod,
                candidate.Title,
                candidate.StartedAtUtc,
                ProjectName: null,
                candidate.Preflight.Duration,
                SourceRetained: true);
            imported.Add(await QueueImportAsync(config.WorkDir, request, nowUtc, cancellationToken));
        }

        return imported;
    }

    public async Task<IReadOnlyList<ExternalAudioImportCandidate>> ScanWatchedAudioFolderAsync(
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(config.AudioOutputDir) || !Directory.Exists(config.AudioOutputDir))
        {
            return Array.Empty<ExternalAudioImportCandidate>();
        }

        var sourcePaths = Directory
            .EnumerateFiles(config.AudioOutputDir)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return await BuildImportCandidatesAsync(config, sourcePaths, ExternalAudioImportMethod.WatchedFolder, nowUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalAudioImportCandidate>> BuildImportCandidatesAsync(
        AppConfig config,
        IEnumerable<string> sourcePaths,
        ExternalAudioImportMethod importMethod,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(config.WorkDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(config));
        }

        Directory.CreateDirectory(config.WorkDir);
        var knownImports = await LoadKnownImportsAsync(config.WorkDir, cancellationToken);
        var knownAppOwnedMeetings = await LoadKnownAppOwnedMeetingsAsync(config.WorkDir, cancellationToken);
        var importCandidates = new List<ExternalAudioImportCandidate>();
        var batchSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawSourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = NormalizePath(rawSourcePath);
            var probeMetadata = TryReadFileMetadata(sourcePath);
            var sourceDisplayName = Path.GetFileName(sourcePath);
            var sourceSizeBytes = probeMetadata?.Length ?? 0L;
            var sourceLastWriteUtc = probeMetadata is null
                ? nowUtc
                : CreateUtcTimestamp(probeMetadata.LastWriteTimeUtc);
            var importedMeetingInfo = ResolveImportedMeetingInfo(sourcePath, sourceLastWriteUtc);
            var duplicateSourceKey = BuildSourceIdentityKey(sourcePath, sourceSizeBytes, sourceLastWriteUtc);

            ExternalAudioImportPreflightResult preflight;
            if (!batchSourceKeys.Add(duplicateSourceKey))
            {
                preflight = new ExternalAudioImportPreflightResult(
                    ExternalAudioImportPreflightStatus.Duplicate,
                    "This file is already included in the current import review.",
                    Duration: null);
            }
            else
            {
                preflight = await PreflightSourceAsync(
                    config,
                    sourcePath,
                    importMethod,
                    nowUtc,
                    knownImports,
                    knownAppOwnedMeetings,
                    cancellationToken);
            }

            importCandidates.Add(new ExternalAudioImportCandidate(
                sourcePath,
                sourceDisplayName,
                importMethod,
                importedMeetingInfo.Title,
                importedMeetingInfo.StartedAtUtc,
                sourceSizeBytes,
                sourceLastWriteUtc,
                preflight));
        }

        return importCandidates;
    }

    public async Task<ImportedExternalAudioResult> QueueImportAsync(
        string workDir,
        ExternalAudioImportRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            throw new ArgumentException("A source audio path is required.", nameof(request));
        }

        var sourcePath = NormalizePath(request.SourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected source audio file no longer exists.", sourcePath);
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? BuildImportedTitle(sourcePath)
            : request.Title.Trim();
        var detectionEvidence = new[]
        {
            new DetectionSignal(
                "external-audio-import",
                Path.GetFileName(sourcePath),
                1d,
                nowUtc),
        };

        var importedMeetingInfo = ResolveImportedMeetingInfo(sourcePath, request.StartedAtUtc);
        var manifest = await _manifestStore.CreateAsync(
            workDir,
            importedMeetingInfo.Platform,
            title,
            detectionEvidence,
            cancellationToken);

        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var copiedAudioPath = Path.Combine(
            sessionRoot,
            "processing",
            $"imported-source{Path.GetExtension(sourcePath)}");

        try
        {
            File.Copy(sourcePath, copiedAudioPath, overwrite: false);

            var importedSourceMetadata = new ImportedSourceAudioInfo(
                sourcePath,
                request.SourceSizeBytes,
                request.SourceLastWriteUtc,
                request.SourceDisplayName,
                request.ImportMethod,
                request.ProbedDuration,
                request.SourceRetained);

            var updatedManifest = manifest with
            {
                Platform = importedMeetingInfo.Platform,
                DetectedTitle = title,
                StartedAtUtc = request.StartedAtUtc,
                MergedAudioPath = copiedAudioPath,
                ImportedSourceAudio = importedSourceMetadata,
                ProjectName = string.IsNullOrWhiteSpace(request.ProjectName)
                    ? null
                    : request.ProjectName.Trim(),
                TranscriptionStatus = new ProcessingStageStatus(
                    "transcription",
                    StageExecutionState.NotStarted,
                    nowUtc,
                    BuildQueuedStatusMessage(request.ImportMethod)),
            };

            await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);

            return new ImportedExternalAudioResult(
                manifestPath,
                sourcePath,
                updatedManifest.DetectedTitle);
        }
        catch
        {
            TryDeleteFile(copiedAudioPath);
            TryDeleteDirectory(sessionRoot);
            throw;
        }
    }

    private async Task<ExternalAudioImportPreflightResult> PreflightSourceAsync(
        AppConfig config,
        string sourcePath,
        ExternalAudioImportMethod importMethod,
        DateTimeOffset nowUtc,
        IReadOnlyList<ImportedSourceAudioInfo> knownImports,
        IReadOnlyList<AppOwnedMeetingIdentity> knownMeetings,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedSourceAudioPath(sourcePath))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.UnsupportedExtension,
                "Unsupported file type. Import .wav, .mp3, .m4a, .aac, or .mp4 audio.",
                Duration: null);
        }

        var sourceFile = new FileInfo(sourcePath);
        if (!sourceFile.Exists)
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.MissingFile,
                "The source file is no longer available.",
                Duration: null);
        }

        if (CloudFileStorageOptimizer.IsCloudPlaceholderOrOffline(sourcePath))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.OfflinePlaceholder,
                "This source file is still offline. Make it available on this PC before importing.",
                Duration: null);
        }

        if (importMethod == ExternalAudioImportMethod.WatchedFolder && !HasSettled(sourceFile, nowUtc))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.StillCopying,
                "Waiting for the file to finish copying before import begins.",
                Duration: null);
        }

        var sourceMetadata = new ImportedSourceAudioInfo(
            NormalizePath(sourcePath),
            sourceFile.Length,
            CreateUtcTimestamp(sourceFile.LastWriteTimeUtc),
            Path.GetFileName(sourcePath),
            importMethod,
            probedDuration: null,
            sourceRetained: true);
        if (knownImports.Any(existing => SourceMatches(existing, sourceMetadata)))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.Duplicate,
                "This source file already has an imported work session.",
                Duration: null);
        }

        if (RepresentsKnownAppOwnedMeeting(sourceMetadata.OriginalPath, knownMeetings))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.Duplicate,
                "Meeting Recorder already owns a published meeting for this app-generated recording.",
                Duration: null);
        }

        if (importMethod == ExternalAudioImportMethod.WatchedFolder &&
            HasTranscriptArtifactForSource(config.TranscriptOutputDir, sourcePath))
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.Duplicate,
                "Transcript artifacts already exist for this watched-folder file.",
                Duration: null);
        }

        try
        {
            var duration = await ProbeDurationAsync(sourcePath, cancellationToken);
            if (duration <= TimeSpan.Zero)
            {
                return new ExternalAudioImportPreflightResult(
                    ExternalAudioImportPreflightStatus.EmptyAudio,
                    "This source file does not contain readable audio.",
                    Duration: duration);
            }

            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.Ready,
                "Ready to queue.",
                duration);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ExternalAudioImportPreflightResult(
                ExternalAudioImportPreflightStatus.DecodeFailed,
                $"Meeting Recorder could not read this file with the local transcription audio stack. {exception.Message}",
                Duration: null);
        }
    }

    private async Task<TimeSpan> ProbeDurationAsync(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var preparedAudioPath = BuildPreparedAudioPath(sourcePath);
        try
        {
            await _audioPreparer.PrepareAsync(sourcePath, preparedAudioPath, cancellationToken);
            using var reader = new AudioFileReader(preparedAudioPath);
            return reader.TotalTime;
        }
        finally
        {
            TryDeleteFile(preparedAudioPath);
        }
    }

    private static string BuildPreparedAudioPath(string sourcePath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderImportPreflight");
        Directory.CreateDirectory(tempDirectory);
        return Path.Combine(
            tempDirectory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}-{Guid.NewGuid():N}.wav");
    }

    private async Task<List<ImportedSourceAudioInfo>> LoadKnownImportsAsync(string workDir, CancellationToken cancellationToken)
    {
        var knownImports = new List<ImportedSourceAudioInfo>();
        if (!Directory.Exists(workDir))
        {
            return knownImports;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
                if (manifest.ImportedSourceAudio is null)
                {
                    continue;
                }

                knownImports.Add(manifest.ImportedSourceAudio with
                {
                    OriginalPath = NormalizePath(manifest.ImportedSourceAudio.OriginalPath),
                });
            }
            catch
            {
                // Ignore malformed or partially-written manifests while scanning for imports.
            }
        }

        return knownImports;
    }

    private async Task<List<AppOwnedMeetingIdentity>> LoadKnownAppOwnedMeetingsAsync(string workDir, CancellationToken cancellationToken)
    {
        var knownMeetings = new List<AppOwnedMeetingIdentity>();
        if (!Directory.Exists(workDir))
        {
            return knownMeetings;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
                if (manifest.ImportedSourceAudio is not null)
                {
                    continue;
                }

                knownMeetings.Add(new AppOwnedMeetingIdentity(
                    manifest.Platform,
                    NormalizeMeetingTitle(manifest.DetectedTitle),
                    manifest.StartedAtUtc));
            }
            catch
            {
                // Ignore malformed or partially-written manifests while scanning for known meetings.
            }
        }

        return knownMeetings;
    }

    private static FileInfo? TryReadFileMetadata(string sourcePath)
    {
        try
        {
            var fileInfo = new FileInfo(sourcePath);
            return fileInfo.Exists ? fileInfo : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasSettled(FileInfo sourceFile, DateTimeOffset nowUtc)
    {
        var lastWriteUtc = CreateUtcTimestamp(sourceFile.LastWriteTimeUtc);
        return nowUtc - lastWriteUtc >= MinimumFileQuietPeriod;
    }

    private static bool HasTranscriptArtifactForSource(string transcriptOutputDir, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(transcriptOutputDir) || !Directory.Exists(transcriptOutputDir))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        var sidecarDir = ArtifactPathBuilder.BuildTranscriptSidecarRoot(transcriptOutputDir);
        return File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.md")) ||
            File.Exists(Path.Combine(sidecarDir, $"{stem}.json")) ||
            File.Exists(Path.Combine(sidecarDir, $"{stem}.ready")) ||
            File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.json")) ||
            File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.ready"));
    }

    private static bool IsSupportedSourceAudioPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SourceMatches(ImportedSourceAudioInfo existing, ImportedSourceAudioInfo current)
    {
        return string.Equals(existing.OriginalPath, current.OriginalPath, StringComparison.OrdinalIgnoreCase) &&
            existing.SourceSizeBytes == current.SourceSizeBytes &&
            existing.SourceLastWriteUtc.UtcDateTime == current.SourceLastWriteUtc.UtcDateTime;
    }

    private static string BuildSourceIdentityKey(string sourcePath, long sourceSizeBytes, DateTimeOffset sourceLastWriteUtc)
    {
        return $"{NormalizePath(sourcePath)}\n{sourceSizeBytes}\n{sourceLastWriteUtc.UtcTicks}";
    }

    private static bool RepresentsKnownAppOwnedMeeting(
        string sourcePath,
        IReadOnlyList<AppOwnedMeetingIdentity> knownMeetings)
    {
        if (!TryParsePublishedStem(Path.GetFileNameWithoutExtension(sourcePath), out var meetingInfo))
        {
            return false;
        }

        var normalizedTitle = NormalizeMeetingTitle(meetingInfo.Title);
        return knownMeetings.Any(existing =>
            existing.Platform == meetingInfo.Platform &&
            string.Equals(existing.NormalizedTitle, normalizedTitle, StringComparison.OrdinalIgnoreCase) &&
            existing.StartedAtUtc.UtcDateTime == meetingInfo.StartedAtUtc.UtcDateTime);
    }

    private static string BuildImportedTitle(string sourcePath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath)?.Trim();
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return "Imported Audio";
        }

        return fileNameWithoutExtension
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
    }

    private static ImportedMeetingInfo ResolveImportedMeetingInfo(string sourcePath, DateTimeOffset fallbackStartedAtUtc)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (TryParsePublishedStem(stem, out var parsedInfo))
        {
            return parsedInfo;
        }

        return new ImportedMeetingInfo(
            MeetingPlatform.Manual,
            BuildImportedTitle(sourcePath),
            fallbackStartedAtUtc);
    }

    private static bool TryParsePublishedStem(string? stem, out ImportedMeetingInfo meetingInfo)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            meetingInfo = default;
            return false;
        }

        var match = PublishedStemPattern.Match(stem);
        if (!match.Success)
        {
            meetingInfo = default;
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
            meetingInfo = default;
            return false;
        }

        var platform = match.Groups["platform"].Value switch
        {
            "teams" => MeetingPlatform.Teams,
            "gmeet" => MeetingPlatform.GoogleMeet,
            "manual" => MeetingPlatform.Manual,
            _ => MeetingPlatform.Unknown,
        };

        meetingInfo = new ImportedMeetingInfo(
            platform,
            HumanizeSlug(match.Groups["slug"].Value),
            startedAtUtc);
        return true;
    }

    private static string HumanizeSlug(string slug)
    {
        var words = slug
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
            .ToArray();
        return words.Length == 0 ? "Imported Audio" : string.Join(' ', words);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static string NormalizeMeetingTitle(string title)
    {
        return string.Join(
            ' ',
            title
                .Trim()
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static DateTimeOffset CreateUtcTimestamp(DateTime utcDateTime)
    {
        return new(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
    }

    private static string BuildQueuedStatusMessage(ExternalAudioImportMethod importMethod)
    {
        return importMethod switch
        {
            ExternalAudioImportMethod.FilePicker => "Queued from Add Audio Files.",
            ExternalAudioImportMethod.DragDrop => "Queued from a dropped audio file.",
            _ => "Queued from the watched audio folder.",
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private readonly record struct ImportedMeetingInfo(
        MeetingPlatform Platform,
        string Title,
        DateTimeOffset StartedAtUtc);

    private readonly record struct AppOwnedMeetingIdentity(
        MeetingPlatform Platform,
        string NormalizedTitle,
        DateTimeOffset StartedAtUtc);
}

public enum ExternalAudioImportPreflightStatus
{
    Ready = 0,
    Duplicate = 1,
    MissingFile = 2,
    OfflinePlaceholder = 3,
    UnsupportedExtension = 4,
    StillCopying = 5,
    DecodeFailed = 6,
    EmptyAudio = 7,
}

public sealed record ExternalAudioImportPreflightResult(
    ExternalAudioImportPreflightStatus Status,
    string Message,
    TimeSpan? Duration)
{
    public bool IsSuccess => Status == ExternalAudioImportPreflightStatus.Ready;
}

public sealed record ExternalAudioImportCandidate(
    string SourcePath,
    string SourceDisplayName,
    ExternalAudioImportMethod ImportMethod,
    string Title,
    DateTimeOffset StartedAtUtc,
    long SourceSizeBytes,
    DateTimeOffset SourceLastWriteUtc,
    ExternalAudioImportPreflightResult Preflight)
{
    public bool CanQueue => Preflight.IsSuccess;
}

public sealed record ExternalAudioImportRequest(
    string SourcePath,
    string SourceDisplayName,
    long SourceSizeBytes,
    DateTimeOffset SourceLastWriteUtc,
    ExternalAudioImportMethod ImportMethod,
    string Title,
    DateTimeOffset StartedAtUtc,
    string? ProjectName,
    TimeSpan? ProbedDuration,
    bool SourceRetained);

public sealed record ImportedExternalAudioResult(
    string ManifestPath,
    string OriginalSourcePath,
    string Title);
