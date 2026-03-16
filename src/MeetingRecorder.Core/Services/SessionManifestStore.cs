using MeetingRecorder.Core.Domain;
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

        return Task.FromResult(manifest);
    }

    public Task SaveAsync(MeetingSessionManifest manifest, string manifestPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? throw new InvalidOperationException("Manifest path must include a directory."));
        var json = JsonSerializer.Serialize(manifest, SerializerOptions);
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
        var pending = new List<string>(manifestPaths.Length);

        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await LoadAsync(manifestPath, cancellationToken);
            if (manifest.State is SessionState.Queued or SessionState.Processing or SessionState.Finalizing)
            {
                pending.Add(manifestPath);
            }
        }

        return pending;
    }

    private async Task<MeetingSessionManifest> SaveAndReturnAsync(
        MeetingSessionManifest manifest,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await SaveAsync(manifest, manifestPath, cancellationToken);
        return manifest;
    }
}
