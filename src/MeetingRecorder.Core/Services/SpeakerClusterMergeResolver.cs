namespace MeetingRecorder.Core.Services;

public enum SpeakerClusterMergeDisposition
{
    Accepted = 0,
    Invalid = 1,
    Redundant = 2,
    ConflictingExistingMerge = 3,
}

/// <summary>
/// Metadata-only request for a user-confirmed merge. It deliberately contains
/// no audio, transcript text, profile, or display-name data.
/// </summary>
public sealed record SpeakerClusterMergeRecord(
    string OperationId,
    string MeetingStableIdentity,
    string ArtifactRevision,
    IReadOnlyList<string> SourceSpeakerIds,
    string CanonicalSpeakerId,
    string EvidenceFingerprint);

public sealed record SpeakerClusterMergeDecision(
    SpeakerClusterMergeDisposition Disposition,
    string Explanation,
    IReadOnlyDictionary<string, string> CanonicalBySpeakerId);

public static class SpeakerClusterMergeResolver
{
    public static SpeakerClusterMergeDecision Resolve(
        SpeakerClusterMergeRecord request,
        IReadOnlyDictionary<string, string>? existingCanonicalBySpeakerId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = Normalize(request.CanonicalSpeakerId);
        var sourceIds = request.SourceSpeakerIds
            .Select(Normalize)
            .Where(static value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.MeetingStableIdentity) ||
            string.IsNullOrWhiteSpace(request.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(request.EvidenceFingerprint) ||
            canonical is null ||
            sourceIds.Length < 2 ||
            !sourceIds.Contains(canonical, StringComparer.Ordinal))
        {
            return Result(SpeakerClusterMergeDisposition.Invalid, "Choose at least two current speakers and one of them as the canonical speaker.");
        }

        var normalizedExisting = NormalizeMappings(existingCanonicalBySpeakerId);
        if (HasCycle(normalizedExisting))
        {
            return Result(SpeakerClusterMergeDisposition.Invalid, "Existing speaker merge data is inconsistent. Reload and repair it before merging speakers.");
        }

        var canonicalBySpeakerId = new Dictionary<string, string>(normalizedExisting, StringComparer.Ordinal);
        var changes = 0;
        foreach (var sourceId in sourceIds)
        {
            var resolved = ResolveCanonical(sourceId, normalizedExisting);
            if (!string.Equals(resolved, sourceId, StringComparison.Ordinal) &&
                !string.Equals(resolved, canonical, StringComparison.Ordinal))
            {
                return Result(SpeakerClusterMergeDisposition.ConflictingExistingMerge, "A selected speaker already belongs to a different merge. Reload before choosing a canonical speaker.");
            }

            if (string.Equals(resolved, canonical, StringComparison.Ordinal))
            {
                canonicalBySpeakerId[sourceId] = canonical;
                continue;
            }

            canonicalBySpeakerId[sourceId] = canonical;
            changes++;
        }

        return changes == 0
            ? Result(SpeakerClusterMergeDisposition.Redundant, "These speakers are already merged into the selected canonical speaker.", canonicalBySpeakerId)
            : Result(SpeakerClusterMergeDisposition.Accepted, "Selected speakers can be merged without changing local voice profiles.", canonicalBySpeakerId);
    }

    private static Dictionary<string, string> NormalizeMappings(IReadOnlyDictionary<string, string>? mappings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (mappings is null)
        {
            return result;
        }

        foreach (var pair in mappings)
        {
            var source = Normalize(pair.Key);
            var target = Normalize(pair.Value);
            if (source is not null && target is not null && !string.Equals(source, target, StringComparison.Ordinal))
            {
                result[source] = target;
            }
        }

        return result;
    }

    private static bool HasCycle(IReadOnlyDictionary<string, string> mappings) =>
        mappings.Keys.Any(source => ResolveCanonical(source, mappings) is null);

    private static string? ResolveCanonical(string source, IReadOnlyDictionary<string, string> mappings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = source;
        while (mappings.TryGetValue(current, out var next))
        {
            if (!seen.Add(current))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static SpeakerClusterMergeDecision Result(
        SpeakerClusterMergeDisposition disposition,
        string explanation,
        IReadOnlyDictionary<string, string>? canonicalBySpeakerId = null) =>
        new(disposition, explanation, canonicalBySpeakerId ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
