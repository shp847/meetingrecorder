using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum SpeakerClusterMergePreflightDisposition
{
    Ready = 0,
    Blocked = 1,
    ResolveNameConflict = 2,
    RepairRecommended = 3,
}

public sealed record SpeakerClusterMergeCandidate(
    string SpeakerId,
    string DisplayName,
    SpeakerNameSource NameSource,
    double? Confidence,
    bool HasEvidence,
    int AffectedTurnCount);

public sealed record SpeakerClusterMergePreflightInput(
    string ArtifactRevision,
    IReadOnlyList<SpeakerClusterMergeCandidate>? SelectedSpeakers,
    bool IsBusy,
    bool IsRepairRecommended);

public sealed record SpeakerClusterMergePreflight(
    SpeakerClusterMergePreflightDisposition Disposition,
    string Summary,
    string? RecommendedCanonicalSpeakerId,
    int AffectedTurnCount,
    int EvidenceCount)
{
    public bool CanMerge => Disposition == SpeakerClusterMergePreflightDisposition.Ready;
}

/// <summary>
/// Guards a manual duplicate-cluster merge before any artifact write. It uses only
/// review metadata and never decides a person's name or changes local learning.
/// </summary>
public static class SpeakerClusterMergePreflightResolver
{
    public static SpeakerClusterMergePreflight Resolve(SpeakerClusterMergePreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var speakers = (input.SelectedSpeakers ?? Array.Empty<SpeakerClusterMergeCandidate>())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SpeakerId))
            .GroupBy(candidate => candidate.SpeakerId.Trim(), StringComparer.Ordinal)
            .Select(group => group.First() with { SpeakerId = group.Key })
            .ToArray();
        var affectedTurns = speakers.Sum(candidate => Math.Max(0, candidate.AffectedTurnCount));
        var evidenceCount = speakers.Count(candidate => candidate.HasEvidence);

        if (input.IsBusy || string.IsNullOrWhiteSpace(input.ArtifactRevision))
        {
            return Result(SpeakerClusterMergePreflightDisposition.Blocked, "Meeting speaker data is changing. Reload after processing finishes before merging speakers.", null, affectedTurns, evidenceCount);
        }

        if (speakers.Length < 2)
        {
            return Result(SpeakerClusterMergePreflightDisposition.Blocked, "Select at least two current speaker clusters to merge.", null, affectedTurns, evidenceCount);
        }

        if (input.IsRepairRecommended)
        {
            return Result(SpeakerClusterMergePreflightDisposition.RepairRecommended, "Speaker labels look fragmented. Use Repair Speaker Labels instead of a broad manual merge.", null, affectedTurns, evidenceCount);
        }

        if (evidenceCount != speakers.Length)
        {
            return Result(SpeakerClusterMergePreflightDisposition.Blocked, "Each selected speaker needs local review evidence before a merge can be confirmed.", null, affectedTurns, evidenceCount);
        }

        var userNames = TrustedDistinctNames(speakers.Where(candidate => candidate.NameSource == SpeakerNameSource.UserEdited));
        var profileNames = TrustedDistinctNames(speakers.Where(candidate =>
            candidate.NameSource == SpeakerNameSource.AutoAppliedVoiceProfile && candidate.Confidence >= 0.90d));
        if (userNames.Count > 1 || profileNames.Count > 1)
        {
            return Result(SpeakerClusterMergePreflightDisposition.ResolveNameConflict, "Selected speakers have conflicting confirmed or high-confidence names. Resolve them to one Meeting Display Name first, or cancel the merge.", null, affectedTurns, evidenceCount);
        }

        var recommended = speakers
            .OrderBy(candidate => IsGeneric(candidate) ? 0 : 1)
            .ThenBy(candidate => candidate.SpeakerId, StringComparer.Ordinal)
            .Select(candidate => candidate.SpeakerId)
            .FirstOrDefault();
        var warning = speakers.Any(candidate => candidate.NameSource == SpeakerNameSource.SuggestedVoiceProfile || candidate.Confidence is < 0.90d)
            ? " Some selected names are suggestions or low confidence; review the canonical choice before confirming."
            : string.Empty;
        return Result(SpeakerClusterMergePreflightDisposition.Ready, $"Merge affects {affectedTurns} turn(s). Source speaker ids stay auditable and this merge does not teach local voice profiles.{warning}", recommended, affectedTurns, evidenceCount);
    }

    private static HashSet<string> TrustedDistinctNames(IEnumerable<SpeakerClusterMergeCandidate> candidates) =>
        candidates
            .Select(candidate => candidate.DisplayName?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsGeneric(SpeakerClusterMergeCandidate candidate) =>
        candidate.NameSource == SpeakerNameSource.None ||
        candidate.DisplayName.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase);

    private static SpeakerClusterMergePreflight Result(
        SpeakerClusterMergePreflightDisposition disposition,
        string summary,
        string? recommendedCanonicalSpeakerId,
        int affectedTurns,
        int evidenceCount) =>
        new(disposition, summary, recommendedCanonicalSpeakerId, affectedTurns, evidenceCount);
}
