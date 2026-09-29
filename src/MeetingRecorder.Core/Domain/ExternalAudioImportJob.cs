using System.Security.Cryptography;
using System.Text;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Domain;

public enum ExternalAudioImportJobState
{
    PendingReview = 0,
    Probing = 1,
    ReadyToQueue = 2,
    BlockedBySetup = 3,
    Queued = 4,
    Processing = 5,
    Published = 6,
    Failed = 7,
    Removed = 8,
    Changing = 9,
    SourceMissing = 10,
}

public enum ExternalAudioImportJobReason
{
    None = 0,
    SourceChanged = 1,
    SourceMissing = 2,
    SourceUnreadable = 3,
    SetupRequired = 4,
    Duplicate = 5,
    ProcessingFailed = 6,
    RemovedByUser = 7,
    ReadinessUnknown = 8,
    StagedWorkUnavailable = 9,
}

public enum ExternalAudioImportQueueIntent
{
    None = 0,
    UserRequested = 1,
}

public enum ExternalAudioSourceLocatorClass
{
    LocalFile = 0,
}

public enum ExternalAudioImportSourceOperation
{
    Read = 0,
    CopyToAppOwnedStaging = 1,
    Delete = 2,
    Move = 3,
    Truncate = 4,
    Rename = 5,
}

public enum ExternalAudioImportJobTransitionFailure
{
    None = 0,
    RevisionConflict = 1,
    IllegalStateChange = 2,
    ReadOnly = 3,
    UnsupportedSchema = 4,
}

public sealed record ExternalAudioImportSourceObservation
{
    public Guid ObservationId { get; init; }

    public string OriginalLocator { get; init; } = string.Empty;

    public ExternalAudioSourceLocatorClass LocatorClass { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public ExternalAudioImportMethod ImportMethod { get; init; }

    public long SourceSizeBytes { get; init; }

    public DateTimeOffset SourceLastWriteUtc { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public int ObservationRevision { get; init; }

    public string? ContentHash { get; init; }

    public bool SourceRetained { get; init; } = true;

    public string ObservationKey => ExternalAudioImportIdentity.BuildObservationKey(
        OriginalLocator,
        SourceSizeBytes,
        SourceLastWriteUtc);

    public static ExternalAudioImportSourceObservation Create(
        string originalLocator,
        string? displayName,
        ExternalAudioImportMethod importMethod,
        long sourceSizeBytes,
        DateTimeOffset sourceLastWriteUtc,
        DateTimeOffset observedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(originalLocator))
        {
            throw new ArgumentException("An original source locator is required.", nameof(originalLocator));
        }

        if (sourceSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSizeBytes));
        }

        var canonicalLocator = Path.GetFullPath(originalLocator);
        return new ExternalAudioImportSourceObservation
        {
            ObservationId = Guid.NewGuid(),
            OriginalLocator = canonicalLocator,
            LocatorClass = ExternalAudioSourceLocatorClass.LocalFile,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileName(canonicalLocator)
                : displayName.Trim(),
            ImportMethod = importMethod,
            SourceSizeBytes = sourceSizeBytes,
            SourceLastWriteUtc = sourceLastWriteUtc.ToUniversalTime(),
            ObservedAtUtc = observedAtUtc.ToUniversalTime(),
            ObservationRevision = 0,
            ContentHash = null,
            SourceRetained = true,
        };
    }
}

public sealed record ExternalAudioImportMetadataOverrides(
    string? Title,
    DateTimeOffset? StartedAtUtc,
    string? ProjectName);

/// <summary>
/// Immutable, metadata-only evidence that the exact source observation and its
/// app-owned staged copy passed the local preparation contract. The enclosing
/// import job supplies the bound job id; neither locator is persisted here.
/// </summary>
public sealed record ExternalAudioImportProbeReceipt
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string SourceObservationKey { get; init; } = string.Empty;

    public int SourceObservationRevision { get; init; }

    public string StagedObservationKey { get; init; } = string.Empty;

    public string ResultCode { get; init; } = "Ready";

    public string DecoderVersion { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public static ExternalAudioImportProbeReceipt CreateReady(
        ExternalAudioImportSourceObservation source,
        string stagedObservationKey,
        string decoderVersion,
        TimeSpan duration,
        int sampleRate,
        int channels,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(stagedObservationKey))
        {
            throw new ArgumentException("A staged observation key is required.", nameof(stagedObservationKey));
        }

        if (string.IsNullOrWhiteSpace(decoderVersion))
        {
            throw new ArgumentException("A decoder version is required.", nameof(decoderVersion));
        }

        if (duration <= TimeSpan.Zero || sampleRate <= 0 || channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        return new ExternalAudioImportProbeReceipt
        {
            SourceObservationKey = source.ObservationKey,
            SourceObservationRevision = source.ObservationRevision,
            StagedObservationKey = stagedObservationKey.Trim(),
            DecoderVersion = decoderVersion.Trim(),
            Duration = duration,
            SampleRate = sampleRate,
            Channels = channels,
            CompletedAtUtc = completedAtUtc.ToUniversalTime(),
        };
    }
}

/// <summary>
/// Bounded, metadata-only evidence of an explicit recovery action. It never
/// includes source locators, staging paths, file hashes, or audio content.
/// </summary>
public sealed record ExternalAudioImportRecoveryActionReceipt(
    Guid ActionId,
    ExternalAudioImportRecoveryActionKind Action,
    int ExpectedRevision,
    int ResultingRevision,
    string Result,
    DateTimeOffset CompletedAtUtc,
    string Message);

public sealed record ExternalAudioImportJob
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid JobId { get; init; }

    public int Revision { get; init; }

    public string? SessionId { get; init; }

    public string? StagedWorkIdentity { get; init; }

    public ExternalAudioImportJobState State { get; init; }

    public ExternalAudioImportJobReason Reason { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public ExternalAudioImportSourceObservation Source { get; init; } = new();

    public ExternalAudioImportMetadataOverrides? MetadataOverrides { get; init; }

    public ExternalAudioImportProbeReceipt? ProbeReceipt { get; init; }

    public ExternalAudioImportReadinessSnapshot? ReadinessSnapshot { get; init; }

    public ExternalAudioImportQueueIntent QueueIntent { get; init; }

    public int RetryCount { get; init; }

    public IReadOnlyList<ExternalAudioImportRecoveryActionReceipt> RecoveryReceipts { get; init; } = [];

    public bool IsReadOnly { get; init; }
}

public sealed record ExternalAudioImportJobTransitionResult(
    bool Applied,
    ExternalAudioImportJob? Job,
    ExternalAudioImportJobTransitionFailure Failure);

public sealed record ExternalAudioImportJobPublicProjection(
    Guid JobId,
    ExternalAudioImportJobState State,
    ExternalAudioImportJobReason Reason,
    string SourceDisplayName,
    ExternalAudioImportMethod ImportMethod,
    bool SourceRetained)
{
    public static ExternalAudioImportJobPublicProjection Create(ExternalAudioImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new ExternalAudioImportJobPublicProjection(
            job.JobId,
            job.State,
            job.Reason,
            job.Source.DisplayName,
            job.Source.ImportMethod,
            job.Source.SourceRetained);
    }
}

public sealed record ExternalAudioImportJobSchemaCompatibility(
    bool CanRead,
    bool CanWrite,
    string RecoveryText)
{
    public static ExternalAudioImportJobSchemaCompatibility Resolve(ExternalAudioImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.SchemaVersion > ExternalAudioImportJob.CurrentSchemaVersion)
        {
            return new ExternalAudioImportJobSchemaCompatibility(
                CanRead: true,
                CanWrite: false,
                RecoveryText: "Update Meeting Recorder before changing this import record.");
        }

        if (job.SchemaVersion != ExternalAudioImportJob.CurrentSchemaVersion)
        {
            return new ExternalAudioImportJobSchemaCompatibility(
                CanRead: false,
                CanWrite: false,
                RecoveryText: "This import record needs recovery before it can be changed.");
        }

        return new ExternalAudioImportJobSchemaCompatibility(
            CanRead: true,
            CanWrite: !job.IsReadOnly,
            RecoveryText: job.IsReadOnly
                ? "This legacy import record is read-only until it is safely migrated."
                : string.Empty);
    }
}

public static class ExternalAudioImportJobFactory
{
    public static ExternalAudioImportJob CreateNew(
        ExternalAudioImportSourceObservation source,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        var normalizedNowUtc = nowUtc.ToUniversalTime();
        return new ExternalAudioImportJob
        {
            SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion,
            JobId = Guid.NewGuid(),
            Revision = 0,
            State = ExternalAudioImportJobState.PendingReview,
            Reason = ExternalAudioImportJobReason.None,
            CreatedAtUtc = normalizedNowUtc,
            UpdatedAtUtc = normalizedNowUtc,
            Source = source with { SourceRetained = true },
            IsReadOnly = false,
        };
    }

    public static ExternalAudioImportJob CreateLegacyReadOnlyProjection(
        MeetingSessionManifest manifest,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var importedSource = manifest.ImportedSourceAudio
            ?? throw new ArgumentException("A legacy import projection requires imported source metadata.", nameof(manifest));
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        var source = new ExternalAudioImportSourceObservation
        {
            ObservationId = ExternalAudioImportIdentity.CreateStableGuid(
                $"legacy-source\n{manifest.SessionId}\n{importedSource.OriginalPath}\n{importedSource.SourceSizeBytes}\n{importedSource.SourceLastWriteUtc.UtcTicks}"),
            OriginalLocator = importedSource.OriginalPath,
            LocatorClass = ExternalAudioSourceLocatorClass.LocalFile,
            DisplayName = importedSource.SourceDisplayName,
            ImportMethod = importedSource.ImportMethod,
            SourceSizeBytes = importedSource.SourceSizeBytes,
            SourceLastWriteUtc = importedSource.SourceLastWriteUtc.ToUniversalTime(),
            ObservedAtUtc = normalizedNowUtc,
            ObservationRevision = 0,
            ContentHash = null,
            SourceRetained = true,
        };

        return new ExternalAudioImportJob
        {
            SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion,
            JobId = ExternalAudioImportIdentity.CreateStableGuid($"legacy-job\n{manifest.SessionId}"),
            Revision = 0,
            SessionId = manifest.SessionId,
            StagedWorkIdentity = manifest.MergedAudioPath,
            State = MapLegacyState(manifest.State),
            Reason = manifest.State == SessionState.Failed
                ? ExternalAudioImportJobReason.ProcessingFailed
                : ExternalAudioImportJobReason.None,
            CreatedAtUtc = normalizedNowUtc,
            UpdatedAtUtc = normalizedNowUtc,
            Source = source,
            MetadataOverrides = new ExternalAudioImportMetadataOverrides(
                manifest.DetectedTitle,
                manifest.StartedAtUtc,
                manifest.ProjectName),
            IsReadOnly = true,
        };
    }

    public static ExternalAudioImportJob CreateQueued(
        ExternalAudioImportSourceObservation source,
        string sessionId,
        string stagedWorkIdentity,
        DateTimeOffset nowUtc,
        ExternalAudioImportProbeReceipt? probeReceipt = null,
        ExternalAudioImportReadinessSnapshot? readinessSnapshot = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("A session id is required.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(stagedWorkIdentity))
        {
            throw new ArgumentException("A staged work identity is required.", nameof(stagedWorkIdentity));
        }

        var pending = CreateNew(source, nowUtc) with
        {
            SessionId = sessionId.Trim(),
            StagedWorkIdentity = stagedWorkIdentity.Trim(),
            ProbeReceipt = probeReceipt,
            ReadinessSnapshot = readinessSnapshot,
            QueueIntent = ExternalAudioImportQueueIntent.UserRequested,
        };
        var probing = Transition(pending, ExternalAudioImportJobState.Probing, nowUtc);
        var ready = Transition(probing, ExternalAudioImportJobState.ReadyToQueue, nowUtc);
        if (readinessSnapshot is { CanQueue: false })
        {
            return Transition(
                ready,
                ExternalAudioImportJobState.BlockedBySetup,
                nowUtc,
                
                reason:
                readinessSnapshot.State == ExternalAudioImportReadinessState.Unknown
                    ? ExternalAudioImportJobReason.ReadinessUnknown
                    : ExternalAudioImportJobReason.SetupRequired);
        }

        return Transition(ready, ExternalAudioImportJobState.Queued, nowUtc);
    }

    private static ExternalAudioImportJob Transition(
        ExternalAudioImportJob job,
        ExternalAudioImportJobState state,
        DateTimeOffset nowUtc,
        ExternalAudioImportJobReason reason = ExternalAudioImportJobReason.None)
    {
        var result = ExternalAudioImportJobTransitions.TryTransition(
            job,
            job.Revision,
            state,
            reason,
            nowUtc);
        return result.Job ?? throw new InvalidOperationException("The import job could not enter its initial state.");
    }

    private static ExternalAudioImportJobState MapLegacyState(SessionState state) => state switch
    {
        SessionState.Queued => ExternalAudioImportJobState.Queued,
        SessionState.Processing or SessionState.Finalizing => ExternalAudioImportJobState.Processing,
        SessionState.Published => ExternalAudioImportJobState.Published,
        SessionState.Failed => ExternalAudioImportJobState.Failed,
        _ => ExternalAudioImportJobState.PendingReview,
    };
}

public static class ExternalAudioImportJobTransitions
{
    public static ExternalAudioImportJobTransitionResult TryTransition(
        ExternalAudioImportJob job,
        int expectedRevision,
        ExternalAudioImportJobState targetState,
        ExternalAudioImportJobReason reason,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(job);

        var schema = ExternalAudioImportJobSchemaCompatibility.Resolve(job);
        if (!schema.CanWrite && job.SchemaVersion != ExternalAudioImportJob.CurrentSchemaVersion)
        {
            return new ExternalAudioImportJobTransitionResult(
                false,
                null,
                ExternalAudioImportJobTransitionFailure.UnsupportedSchema);
        }

        if (job.IsReadOnly)
        {
            return new ExternalAudioImportJobTransitionResult(
                false,
                null,
                ExternalAudioImportJobTransitionFailure.ReadOnly);
        }

        if (job.Revision != expectedRevision)
        {
            return new ExternalAudioImportJobTransitionResult(
                false,
                null,
                ExternalAudioImportJobTransitionFailure.RevisionConflict);
        }

        if (!IsLegalTransition(job.State, targetState))
        {
            return new ExternalAudioImportJobTransitionResult(
                false,
                null,
                ExternalAudioImportJobTransitionFailure.IllegalStateChange);
        }

        var updated = job with
        {
            State = targetState,
            Reason = reason,
            Revision = job.Revision + 1,
            UpdatedAtUtc = nowUtc.ToUniversalTime(),
            RetryCount = targetState == ExternalAudioImportJobState.Probing &&
                         job.State is ExternalAudioImportJobState.Failed or
                                      ExternalAudioImportJobState.Changing or
                                      ExternalAudioImportJobState.SourceMissing
                ? job.RetryCount + 1
                : job.RetryCount,
        };
        return new ExternalAudioImportJobTransitionResult(
            true,
            updated,
            ExternalAudioImportJobTransitionFailure.None);
    }

    private static bool IsLegalTransition(
        ExternalAudioImportJobState currentState,
        ExternalAudioImportJobState targetState) => currentState switch
    {
        ExternalAudioImportJobState.PendingReview => targetState is
            ExternalAudioImportJobState.Probing or ExternalAudioImportJobState.Removed,
        ExternalAudioImportJobState.Probing => targetState is
            ExternalAudioImportJobState.ReadyToQueue or
            ExternalAudioImportJobState.BlockedBySetup or
            ExternalAudioImportJobState.Changing or
            ExternalAudioImportJobState.SourceMissing or
            ExternalAudioImportJobState.Failed or
            ExternalAudioImportJobState.PendingReview,
        ExternalAudioImportJobState.ReadyToQueue => targetState is
            ExternalAudioImportJobState.Probing or
            ExternalAudioImportJobState.BlockedBySetup or
            ExternalAudioImportJobState.Queued or
            ExternalAudioImportJobState.Removed,
        ExternalAudioImportJobState.BlockedBySetup => targetState is
            ExternalAudioImportJobState.Probing or
            ExternalAudioImportJobState.ReadyToQueue or
            ExternalAudioImportJobState.Removed,
        ExternalAudioImportJobState.Changing => targetState is
            ExternalAudioImportJobState.Probing or ExternalAudioImportJobState.Removed,
        ExternalAudioImportJobState.SourceMissing => targetState is
            ExternalAudioImportJobState.Probing or ExternalAudioImportJobState.Removed,
        ExternalAudioImportJobState.Queued => targetState is
            ExternalAudioImportJobState.Processing or ExternalAudioImportJobState.Failed,
        ExternalAudioImportJobState.Processing => targetState is
            ExternalAudioImportJobState.Published or ExternalAudioImportJobState.Failed,
        ExternalAudioImportJobState.Failed => targetState is
            ExternalAudioImportJobState.Probing or ExternalAudioImportJobState.Removed,
        _ => false,
    };
}

public static class ExternalAudioImportSourceOwnershipPolicy
{
    public static bool IsAllowed(ExternalAudioImportSourceOperation operation) => operation is
        ExternalAudioImportSourceOperation.Read or
        ExternalAudioImportSourceOperation.CopyToAppOwnedStaging;
}

public static class ExternalAudioImportIdentity
{
    public static string BuildObservationKey(
        string originalLocator,
        long sourceSizeBytes,
        DateTimeOffset sourceLastWriteUtc)
    {
        if (string.IsNullOrWhiteSpace(originalLocator))
        {
            throw new ArgumentException("An original source locator is required.", nameof(originalLocator));
        }

        var canonicalLocator = Path.GetFullPath(originalLocator)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var keyMaterial = $"{canonicalLocator}\n{sourceSizeBytes}\n{sourceLastWriteUtc.UtcTicks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial)));
    }

    internal static Guid CreateStableGuid(string material)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new Guid(hash.AsSpan(0, 16));
    }
}
