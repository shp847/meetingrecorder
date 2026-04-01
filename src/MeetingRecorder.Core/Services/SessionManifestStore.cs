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

    public SessionManifestStore(ArtifactPathBuilder pathBuilder)
    {
        PathBuilder = pathBuilder;
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
        var normalizedManifest = NormalizeManifest(manifest);
        var json = JsonSerializer.Serialize(normalizedManifest, SerializerOptions);
        File.WriteAllText(manifestPath, json);
        return Task.CompletedTask;
    }

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
            var manifest = await LoadAsync(manifestPath, cancellationToken);
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
        return NormalizeManifest(manifest);
    }

    private static MeetingSessionManifest NormalizeManifest(MeetingSessionManifest manifest)
    {
        var normalizedMicrophoneCaptureSegments = NormalizeMicrophoneCaptureSegments(manifest);
        return manifest with
        {
            MicrophoneCaptureSegments = normalizedMicrophoneCaptureSegments,
            MicrophoneChunkPaths = normalizedMicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            KeyAttendees = MeetingMetadataNameMatcher.MergeNames(manifest.KeyAttendees, Array.Empty<string>()),
            Attendees = NormalizeAttendees(manifest.Attendees),
        };
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
