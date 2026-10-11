using MeetingRecorder.Core.Domain;
using System.Linq;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed class SessionManifestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly MeetingIdentitySnapshotService _identitySnapshotService;

    public SessionManifestStore(ArtifactPathBuilder pathBuilder, MeetingIdentitySnapshotService? identitySnapshotService = null)
    {
        PathBuilder = pathBuilder;
        _identitySnapshotService = identitySnapshotService ?? new MeetingIdentitySnapshotService(
            new MeetingIdentityKeyStore(AppDataPaths.GetMeetingIdentityKeyPath()));
    }

    public ArtifactPathBuilder PathBuilder { get; }

    public Task<MeetingSessionManifest> CreateAsync(
        string workDir,
        MeetingPlatform platform,
        string title,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var sessionId = $"{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var sessionRoot = PathBuilder.BuildSessionRoot(workDir, sessionId);

        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(Path.Combine(sessionRoot, "raw"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "processing"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "logs"));

        var manifest = new MeetingSessionManifest
        {
            SessionId = sessionId,
            Platform = platform,
            DetectedTitle = title,
            StartedAtUtc = now,
            State = SessionState.Queued,
            DetectionEvidence = detectionEvidence,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            MergedAudioPath = null,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.NotStarted, now, null),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
            SummarizationStatus = new ProcessingStageStatus("summarization", StageExecutionState.NotStarted, now, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
        };

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        return SaveAndReturnAsync(manifest, manifestPath, cancellationToken);
    }

    public Task<MeetingSessionManifest> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<MeetingSessionManifest>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize manifest '{manifestPath}'.");

        return Task.FromResult(NormalizeManifest(manifest));
    }

    public Task SaveAsync(MeetingSessionManifest manifest, string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? throw new InvalidOperationException("Manifest path must include a directory."));
        var normalizedManifest = NormalizeManifest(_identitySnapshotService.EnsureForNormalSave(manifest));
        var json = JsonSerializer.Serialize(normalizedManifest, SerializerOptions);
        var temporaryPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backupPath = temporaryPath + ".bak";
        try
        {
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(manifestPath))
            {
                File.Replace(temporaryPath, manifestPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, manifestPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        return Task.CompletedTask;
    }

    public MeetingIdentitySnapshot? GetIdentitySnapshotForComparison(MeetingSessionManifest manifest) =>
        _identitySnapshotService.GetForComparison(manifest);

    public MeetingIdentitySnapshot? CreateIdentitySnapshotForComparison(
        MeetingPlatform platform,
        string? meetingTitle,
        DetectedAudioSource? audioSource,
        DateTimeOffset capturedAtUtc) =>
        _identitySnapshotService.CreateForRuntime(platform, meetingTitle, audioSource, capturedAtUtc);

    public async Task<IReadOnlyList<string>> FindPendingManifestPathsAsync(string workDir, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(workDir))
        {
            return Array.Empty<string>();
        }

        var manifestPaths = Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories).ToArray();
        var pending = new List<(string Path, MeetingSessionManifest Manifest)>(manifestPaths.Length);

        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeetingSessionManifest manifest;
            try
            {
                manifest = await LoadAsync(manifestPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (manifest.ImportedSourceAudio is not null &&
                await IsImportedWorkBlockedBySetupAsync(manifestPath, cancellationToken))
            {
                continue;
            }

            if (manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing)
            {
                pending.Add((manifestPath, manifest));
            }
        }

        return pending
            .OrderBy(candidate => GetPendingResumePriority(candidate.Manifest))
            .ThenBy(candidate => candidate.Manifest.StartedAtUtc)
            .Select(candidate => candidate.Path)
            .ToArray();
    }

    private static async Task<bool> IsImportedWorkBlockedBySetupAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var sessionRoot = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(sessionRoot))
        {
            return true;
        }

        var jobPath = Path.Combine(sessionRoot, "import-job.json");
        if (!File.Exists(jobPath))
        {
            // Legacy imported manifests did not have a companion job. Preserve
            // their pre-S5 resume behavior rather than silently reclassifying them.
            return false;
        }

        try
        {
            var loaded = await new ExternalAudioImportJobStore(jobPath).LoadAsync(cancellationToken);
            return loaded.Job is null ||
                   !loaded.Schema.CanRead ||
                   loaded.Job.State == ExternalAudioImportJobState.BlockedBySetup;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // An unreadable companion job is unknown safety state. Keep staged
            // work intact for repair; never start a worker from incomplete truth.
            return true;
        }
    }

    internal static int GetPendingResumePriority(MeetingSessionManifest manifest)
    {
        if (manifest.TranscriptionStatus.State == StageExecutionState.Succeeded &&
            manifest.PublishStatus.State != StageExecutionState.Succeeded)
        {
            return 0;
        }

        if (manifest.State is SessionState.Processing or SessionState.Finalizing)
        {
            return 1;
        }

        return 2;
    }

    private async Task<MeetingSessionManifest> SaveAndReturnAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await SaveAsync(manifest, manifestPath, cancellationToken);
        return NormalizeManifest(_identitySnapshotService.EnsureForNormalSave(manifest));
    }

    private static MeetingSessionManifest NormalizeManifest(MeetingSessionManifest manifest)
    {
        var normalizedLoopbackCaptureSegments = NormalizeLoopbackCaptureSegments(manifest);
        var normalizedMicrophoneCaptureSegments = NormalizeMicrophoneCaptureSegments(manifest);
        return manifest with
        {
            LoopbackCaptureSegments = normalizedLoopbackCaptureSegments,
            RawChunkPaths = normalizedLoopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            MicrophoneCaptureSegments = normalizedMicrophoneCaptureSegments,
            MicrophoneChunkPaths = normalizedMicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            CaptureTimeline = NormalizeCaptureTimeline(manifest.CaptureTimeline),
            ImportedSourceAudio = NormalizeImportedSourceAudio(manifest.ImportedSourceAudio),
            KeyAttendees = MeetingMetadataNameMatcher.MergeNames(manifest.KeyAttendees, Array.Empty<string>()),
            Attendees = NormalizeAttendees(manifest.Attendees),
            SummarizationStatus = NormalizeSummarizationStatus(manifest.SummarizationStatus),
        };
    }

    private static ImportedSourceAudioInfo? NormalizeImportedSourceAudio(ImportedSourceAudioInfo? importedSourceAudio)
    {
        if (importedSourceAudio is null)
        {
            return null;
        }

        return importedSourceAudio with
        {
            OriginalPath = string.IsNullOrWhiteSpace(importedSourceAudio.OriginalPath)
                ? string.Empty
                : importedSourceAudio.OriginalPath,
            SourceDisplayName = string.IsNullOrWhiteSpace(importedSourceAudio.SourceDisplayName)
                ? Path.GetFileName(importedSourceAudio.OriginalPath)
                : importedSourceAudio.SourceDisplayName.Trim(),
            ImportMethod = importedSourceAudio.ImportMethod,
            SourceRetained = importedSourceAudio.SourceRetained,
        };
    }

    private static ProcessingStageStatus NormalizeSummarizationStatus(ProcessingStageStatus? status)
    {
        return status is null
            ? new ProcessingStageStatus("summarization", StageExecutionState.NotStarted, DateTimeOffset.UtcNow, null)
            : status with
            {
                StageName = "summarization",
            };
    }

    private static IReadOnlyList<LoopbackCaptureSegment> NormalizeLoopbackCaptureSegments(MeetingSessionManifest manifest)
    {
        if (manifest.LoopbackCaptureSegments.Count > 0)
        {
            return manifest.LoopbackCaptureSegments
                .Where(segment => segment.ChunkPaths.Count > 0)
                .Select(segment => segment with
                {
                    EndpointDeviceId = string.IsNullOrWhiteSpace(segment.EndpointDeviceId)
                        ? string.Empty
                        : segment.EndpointDeviceId.Trim(),
                    EndpointName = string.IsNullOrWhiteSpace(segment.EndpointName)
                        ? "Unknown endpoint"
                        : segment.EndpointName.Trim(),
                    EndpointRole = string.IsNullOrWhiteSpace(segment.EndpointRole)
                        ? "Unknown"
                        : segment.EndpointRole.Trim(),
                })
                .ToArray();
        }

        if (manifest.RawChunkPaths.Count == 0)
        {
            return Array.Empty<LoopbackCaptureSegment>();
        }

        return
        [
            new LoopbackCaptureSegment(
                manifest.StartedAtUtc,
                manifest.EndedAtUtc,
                manifest.RawChunkPaths.ToArray(),
                string.Empty,
                "Unknown endpoint",
                "Unknown"),
        ];
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> NormalizeMicrophoneCaptureSegments(MeetingSessionManifest manifest)
    {
        if (manifest.MicrophoneCaptureSegments.Count > 0)
        {
            return manifest.MicrophoneCaptureSegments
                .Where(segment => segment.ChunkPaths.Count > 0)
                .ToArray();
        }

        if (manifest.MicrophoneChunkPaths.Count == 0)
        {
            return Array.Empty<MicrophoneCaptureSegment>();
        }

        return
        [
            new MicrophoneCaptureSegment(
                manifest.StartedAtUtc,
                manifest.EndedAtUtc,
                manifest.MicrophoneChunkPaths.ToArray()),
        ];
    }

    private static IReadOnlyList<CaptureTimelineEntry> NormalizeCaptureTimeline(IReadOnlyList<CaptureTimelineEntry>? captureTimeline)
    {
        if (captureTimeline is null || captureTimeline.Count == 0)
        {
            return Array.Empty<CaptureTimelineEntry>();
        }

        return captureTimeline
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Summary))
            .Select(entry => entry with
            {
                Summary = entry.Summary.Trim(),
                Detail = string.IsNullOrWhiteSpace(entry.Detail)
                    ? null
                    : entry.Detail.Trim(),
            })
            .ToArray();
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
}
