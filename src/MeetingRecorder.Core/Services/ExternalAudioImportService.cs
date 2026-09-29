using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
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
    // Leave room for Meeting Recorder's session and staging paths on Windows systems
    // that do not have long-path support enabled.
    private const int MaximumExternalSourcePathLength = 240;

    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;
    private readonly ExternalAudioMediaProbe _mediaProbe;

    public ExternalAudioImportService(
        ArtifactPathBuilder pathBuilder,
        ExternalAudioMediaProbe? mediaProbe = null)
    {
        _pathBuilder = pathBuilder;
        _manifestStore = new SessionManifestStore(pathBuilder);
        _mediaProbe = mediaProbe ?? new ExternalAudioMediaProbe();
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
            imported.Add(await QueueImportAsync(config, request, nowUtc, cancellationToken));
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
            var sourceDisplayName = Path.GetFileName(sourcePath);
            var sourceLocationUnsupported = IsUnsupportedRemoteSourcePath(sourcePath);
            var sourcePathTooLong = !IsSupportedExternalSourcePathLength(sourcePath);
            var sourceExistsForPathSafetyCheck = !sourceLocationUnsupported &&
                !sourcePathTooLong &&
                File.Exists(sourcePath);
            var sourcePathHasReparsePoint = sourceExistsForPathSafetyCheck &&
                HasReparsePointInPath(sourcePath);
            var sourcePathUnsafe = sourceLocationUnsupported ||
                sourcePathTooLong ||
                sourcePathHasReparsePoint;
            var probeMetadata = sourcePathUnsafe ? null : TryReadFileMetadata(sourcePath);
            var sourceSizeBytes = probeMetadata?.Length ?? 0L;
            var sourceLastWriteUtc = probeMetadata is null
                ? nowUtc
                : CreateUtcTimestamp(probeMetadata.LastWriteTimeUtc);
            var importedMeetingInfo = ResolveImportedMeetingInfo(sourcePath, sourceLastWriteUtc);
            var duplicateSourceKey = BuildSourceIdentityKey(sourcePath, sourceSizeBytes, sourceLastWriteUtc);

            ExternalAudioImportPreflightResult preflight;
            if (sourceLocationUnsupported)
            {
                preflight = new ExternalAudioImportPreflightResult(
                    ExternalAudioImportPreflightStatus.UnsupportedLocation,
                    "Choose a source stored on this PC before importing.",
                    Duration: null);
            }
            else if (sourcePathTooLong)
            {
                preflight = new ExternalAudioImportPreflightResult(
                    ExternalAudioImportPreflightStatus.UnsupportedLocation,
                    "Choose a shorter source path before importing.",
                    Duration: null);
            }
            else if (sourcePathHasReparsePoint)
            {
                preflight = new ExternalAudioImportPreflightResult(
                    ExternalAudioImportPreflightStatus.UnsupportedLocation,
                    "Choose a source outside linked or redirected folders before importing.",
                    Duration: null);
            }
            else if (!batchSourceKeys.Add(duplicateSourceKey))
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
                if (preflight.IsSuccess)
                {
                    var storageHealth = ImportInboxPathPolicy.CheckImportStorageHealth(
                        config,
                        sourceSizeBytes,
                        preflight.Duration);
                    if (!storageHealth.IsReady)
                    {
                        preflight = new ExternalAudioImportPreflightResult(
                            ExternalAudioImportPreflightStatus.BlockedStorage,
                            storageHealth.Message,
                            preflight.Duration);
                    }
                }
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
        AppConfig config,
        ExternalAudioImportRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await QueueImportAsync(config, request, nowUtc, readinessSnapshot: null, cancellationToken);
    }

    public async Task<ImportedExternalAudioResult> QueueImportAsync(
        AppConfig config,
        ExternalAudioImportRequest request,
        DateTimeOffset nowUtc,
        ExternalAudioImportReadinessSnapshot? readinessSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var storageHealth = ImportInboxPathPolicy.CheckImportStorageHealth(
            config,
            request.SourceSizeBytes,
            request.ProbedDuration);
        if (!storageHealth.IsReady)
        {
            throw new InvalidOperationException(storageHealth.Message);
        }

        return await QueueImportAsync(config.WorkDir, request, nowUtc, readinessSnapshot, cancellationToken);
    }

    public async Task<ImportedExternalAudioResult> QueueImportAsync(
        string workDir,
        ExternalAudioImportRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        return await QueueImportAsync(workDir, request, nowUtc, readinessSnapshot: null, cancellationToken);
    }

    public async Task<ImportedExternalAudioResult> QueueImportAsync(
        string workDir,
        ExternalAudioImportRequest request,
        DateTimeOffset nowUtc,
        ExternalAudioImportReadinessSnapshot? readinessSnapshot,
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

        var normalizedWorkDir = NormalizePath(workDir);
        var sourcePath = NormalizePath(request.SourcePath);
        if (IsUnsupportedRemoteSourcePath(sourcePath))
        {
            throw new InvalidOperationException("Choose a source stored on this PC before queueing.");
        }

        EnsureSupportedExternalSourcePathLength(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The selected source audio file no longer exists.", sourcePath);
        }

        EnsureSourcePathHasNoReparsePoints(sourcePath);
        EnsureSourceIsOutsideAppOwnedWorkRoot(sourcePath, normalizedWorkDir);
        EnsureSourceIsUnchangedSincePreflight(sourcePath, request);
        var sourceObservation = new ImportedSourceAudioInfo(
            sourcePath,
            request.SourceSizeBytes,
            request.SourceLastWriteUtc,
            request.SourceDisplayName,
            request.ImportMethod,
            request.ProbedDuration,
            sourceRetained: true);
        var existingImports = await LoadKnownImportsAsync(normalizedWorkDir, cancellationToken);
        if (existingImports.Any(existing => SourceMatches(existing, sourceObservation)))
        {
            throw new InvalidOperationException(
                "This unchanged source file already has an imported work session. Review the existing import instead.");
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

        var extension = Path.GetExtension(sourcePath);
        var stagingRoot = Path.Combine(normalizedWorkDir, ".import-staging");
        var stagingPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}{extension}.tmp");
        string? sessionRoot = null;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            File.Copy(sourcePath, stagingPath, overwrite: false);
            EnsureStagedCopyMatchesSourceObservation(stagingPath, request);
            EnsureSourceIsUnchangedSincePreflight(sourcePath, request);
            cancellationToken.ThrowIfCancellationRequested();

            var importedMeetingInfo = ResolveImportedMeetingInfo(sourcePath, request.StartedAtUtc);
            var manifest = await _manifestStore.CreateAsync(
                normalizedWorkDir,
                importedMeetingInfo.Platform,
                title,
                detectionEvidence,
                cancellationToken);

            sessionRoot = _pathBuilder.BuildSessionRoot(normalizedWorkDir, manifest.SessionId);
            var manifestPath = Path.Combine(sessionRoot, "manifest.json");
            var copiedAudioPath = Path.Combine(
                sessionRoot,
                "processing",
                $"imported-source{extension}");
            File.Move(stagingPath, copiedAudioPath);
            var stagedFile = new FileInfo(copiedAudioPath);
            var stagedObservationKey = ExternalAudioImportIdentity.BuildObservationKey(
                copiedAudioPath,
                stagedFile.Length,
                new DateTimeOffset(stagedFile.LastWriteTimeUtc));
            var stagedProbe = await _mediaProbe.ProbeAsync(
                new ExternalAudioMediaProbeRequest(
                    copiedAudioPath,
                    stagedObservationKey,
                    stagedFile.Length,
                    new DateTimeOffset(stagedFile.LastWriteTimeUtc)),
                cancellationToken);
            if (!stagedProbe.IsReady)
            {
                throw new InvalidOperationException(BuildStagedProbeFailureMessage(stagedProbe));
            }

            var importedSourceMetadata = new ImportedSourceAudioInfo(
                sourcePath,
                request.SourceSizeBytes,
                request.SourceLastWriteUtc,
                request.SourceDisplayName,
                request.ImportMethod,
                stagedProbe.Duration,
                // This importer only reads the source and copies it into app-owned work storage.
                // Retention is an observed invariant, not a caller-selected policy.
                sourceRetained: true);

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
            var jobSourceObservation = ExternalAudioImportSourceObservation.Create(
                sourcePath,
                request.SourceDisplayName,
                request.ImportMethod,
                request.SourceSizeBytes,
                request.SourceLastWriteUtc,
                nowUtc);
            var probeReceipt = ExternalAudioImportProbeReceipt.CreateReady(
                jobSourceObservation,
                stagedObservationKey,
                stagedProbe.DecoderVersion,
                stagedProbe.Duration ?? throw new InvalidOperationException("The app-owned import copy did not report a readable duration."),
                stagedProbe.SampleRate ?? throw new InvalidOperationException("The app-owned import copy did not report a readable sample rate."),
                stagedProbe.Channels ?? throw new InvalidOperationException("The app-owned import copy did not report readable audio channels."),
                nowUtc);
            var importJob = ExternalAudioImportJobFactory.CreateQueued(
                jobSourceObservation,
                manifest.SessionId,
                Path.Combine("processing", $"imported-source{extension}"),
                nowUtc,
                probeReceipt,
                readinessSnapshot);
            await new ExternalAudioImportJobStore(Path.Combine(sessionRoot, "import-job.json"))
                .SaveAsync(importJob, cancellationToken);

            return new ImportedExternalAudioResult(
                manifestPath,
                sourcePath,
                updatedManifest.DetectedTitle)
            {
                ImportJobState = importJob.State,
                RecoveryText = importJob.ReadinessSnapshot?.RecoveryText,
            };
        }
        catch
        {
            TryDeleteFile(stagingPath);
            if (!string.IsNullOrWhiteSpace(sessionRoot))
            {
                TryDeleteDirectory(sessionRoot);
            }

            throw;
        }
        finally
        {
            TryDeleteEmptyDirectory(stagingRoot);
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

        var mediaProbe = await _mediaProbe.ProbeAsync(
            new ExternalAudioMediaProbeRequest(
                sourcePath,
                ExternalAudioImportIdentity.BuildObservationKey(
                    sourcePath,
                    sourceMetadata.SourceSizeBytes,
                    sourceMetadata.SourceLastWriteUtc),
                sourceMetadata.SourceSizeBytes,
                sourceMetadata.SourceLastWriteUtc),
            cancellationToken);
        return MapMediaProbeResult(mediaProbe);
    }

    private static ExternalAudioImportPreflightResult MapMediaProbeResult(ExternalAudioMediaProbeResult result)
    {
        var status = result.Status switch
        {
            ExternalAudioMediaProbeStatus.Ready => ExternalAudioImportPreflightStatus.Ready,
            ExternalAudioMediaProbeStatus.WaitingForSettle => ExternalAudioImportPreflightStatus.StillCopying,
            ExternalAudioMediaProbeStatus.UnsupportedExtension => ExternalAudioImportPreflightStatus.UnsupportedExtension,
            ExternalAudioMediaProbeStatus.UnsupportedCodec => ExternalAudioImportPreflightStatus.UnsupportedCodec,
            ExternalAudioMediaProbeStatus.NoAudio or ExternalAudioMediaProbeStatus.Empty => ExternalAudioImportPreflightStatus.EmptyAudio,
            ExternalAudioMediaProbeStatus.TooShort => ExternalAudioImportPreflightStatus.TooShort,
            ExternalAudioMediaProbeStatus.Missing => ExternalAudioImportPreflightStatus.MissingFile,
            ExternalAudioMediaProbeStatus.Offline => ExternalAudioImportPreflightStatus.OfflinePlaceholder,
            ExternalAudioMediaProbeStatus.Changing => ExternalAudioImportPreflightStatus.Changing,
            ExternalAudioMediaProbeStatus.ResourceLimit => ExternalAudioImportPreflightStatus.ResourceLimit,
            ExternalAudioMediaProbeStatus.BlockedStorage => ExternalAudioImportPreflightStatus.BlockedStorage,
            ExternalAudioMediaProbeStatus.PotentialDuplicate => ExternalAudioImportPreflightStatus.Duplicate,
            ExternalAudioMediaProbeStatus.UnsafePath => ExternalAudioImportPreflightStatus.UnsupportedLocation,
            _ => ExternalAudioImportPreflightStatus.DecodeFailed,
        };
        var message = status == ExternalAudioImportPreflightStatus.DecodeFailed
            ? "Meeting Recorder could not read this file with the local transcription audio stack. Choose another supported file or fix the source, then review it again."
            : result.Message;
        return new ExternalAudioImportPreflightResult(status, message, result.Duration);
    }

    private static string BuildStagedProbeFailureMessage(ExternalAudioMediaProbeResult result) => result.Status switch
    {
        ExternalAudioMediaProbeStatus.Changing or
        ExternalAudioMediaProbeStatus.WaitingForSettle or
        ExternalAudioMediaProbeStatus.Missing =>
            "The app-owned import copy could not be verified. Review the source and try again.",
        ExternalAudioMediaProbeStatus.ResourceLimit =>
            "The app-owned import copy is too large or complex for a safe local preflight.",
        _ => "The app-owned import copy could not be prepared with the local transcription audio stack. Review the source and try again.",
    };

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

    private static void EnsureSourceIsUnchangedSincePreflight(
        string sourcePath,
        ExternalAudioImportRequest request)
    {
        var sourceFile = new FileInfo(sourcePath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("The selected source audio file no longer exists.", sourcePath);
        }

        var observedLastWriteUtc = CreateUtcTimestamp(sourceFile.LastWriteTimeUtc);
        if (sourceFile.Length != request.SourceSizeBytes ||
            observedLastWriteUtc.UtcDateTime != request.SourceLastWriteUtc.UtcDateTime)
        {
            throw new InvalidOperationException(
                "The selected source file changed after review. Review it again before queueing.");
        }
    }

    private static void EnsureStagedCopyMatchesSourceObservation(
        string stagingPath,
        ExternalAudioImportRequest request)
    {
        var stagedFile = new FileInfo(stagingPath);
        if (!stagedFile.Exists || stagedFile.Length != request.SourceSizeBytes)
        {
            throw new InvalidOperationException(
                "The selected source file changed while it was being copied. Review it again before queueing.");
        }
    }

    private static void EnsureSourceIsOutsideAppOwnedWorkRoot(string sourcePath, string workDir)
    {
        var canonicalSourcePath = NormalizePath(sourcePath);
        var canonicalWorkRoot = NormalizePath(workDir);
        if (string.Equals(canonicalSourcePath, canonicalWorkRoot, StringComparison.OrdinalIgnoreCase) ||
            canonicalSourcePath.StartsWith(AppendDirectorySeparator(canonicalWorkRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Choose a source outside Meeting Recorder work storage before queueing.");
        }
    }

    private static string AppendDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool IsUnsupportedRemoteSourcePath(string sourcePath) =>
        sourcePath.StartsWith(@"\\", StringComparison.Ordinal);

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
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
            "zoom" => MeetingPlatform.Zoom,
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

    private static bool IsSupportedExternalSourcePathLength(string sourcePath) =>
        sourcePath.Length <= MaximumExternalSourcePathLength;

    private static void EnsureSupportedExternalSourcePathLength(string sourcePath)
    {
        if (!IsSupportedExternalSourcePathLength(sourcePath))
        {
            throw new InvalidOperationException("Choose a shorter source path before queueing.");
        }
    }

    private static void EnsureSourcePathHasNoReparsePoints(string sourcePath)
    {
        if (HasReparsePointInPath(sourcePath))
        {
            throw new InvalidOperationException(
                "Choose a source outside linked or redirected folders before queueing.");
        }
    }

    private static bool HasReparsePointInPath(string sourcePath)
    {
        var currentPath = NormalizePath(sourcePath);
        while (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch
            {
                // An attribute failure must not grant a path admission bypass.
                return true;
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) ||
                string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentPath = parentPath;
        }

        return false;
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
            ExternalAudioImportMethod.ImportInbox => "Queued from Import Inbox.",
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
    UnsupportedLocation = 8,
    BlockedStorage = 9,
    UnsupportedCodec = 10,
    TooShort = 11,
    Changing = 12,
    ResourceLimit = 13,
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
    string Title)
{
    public ExternalAudioImportJobState ImportJobState { get; init; } = ExternalAudioImportJobState.Queued;

    public string? RecoveryText { get; init; }
}
