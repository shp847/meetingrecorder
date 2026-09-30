using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Domain;

/// <summary>Additive, metadata-only recovery contracts. They are intentionally not live workflow authority yet.</summary>
public sealed record CaptureCandidate(
    int SchemaVersion,
    string CandidateId,
    DateTimeOffset ObservedAtUtc,
    string EvidenceClass,
    DateTimeOffset ExpiresAtUtc);

public sealed record CaptureProof(
    int SchemaVersion,
    string CandidateId,
    DateTimeOffset ProvenAtUtc,
    string ProofClass);

public sealed record CaptureHealthSnapshot(
    int SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    string HealthState,
    string? ReasonCode = null);

public sealed record SourceAudioSet(
    int SchemaVersion,
    string SourceSetId,
    IReadOnlyList<string> OpaqueAudioIds,
    DateTimeOffset RetainUntilUtc);

public sealed record ApprovedTranscriptionProfile(
    int SchemaVersion,
    string ProfileId,
    string ProviderId,
    string ModelSha256,
    long MinimumModelBytes,
    string LanguagePolicy,
    string BenchmarkRevision,
    DateTimeOffset ApprovedAtUtc);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessingBlockReason
{
    None = 0,
    BlockedModel = 1,
    BlockedSource = 2,
    BlockedCapacity = 3,
}

public sealed record CleanupJobLease(
    int SchemaVersion,
    string JobId,
    string OpaqueInputId,
    string OwnerId,
    DateTimeOffset ExpiresAtUtc,
    int Attempt);

public sealed record ArtifactPromotionJournal(
    int SchemaVersion,
    string JournalId,
    string OpaqueInputId,
    string State,
    DateTimeOffset UpdatedAtUtc,
    string? ReasonCode = null);

public sealed record HistoricalReprocessingJob(
    int SchemaVersion,
    string JobId,
    string OpaqueSourceId,
    string State,
    int Attempt,
    DateTimeOffset UpdatedAtUtc);
