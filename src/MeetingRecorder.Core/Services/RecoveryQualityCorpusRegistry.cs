namespace MeetingRecorder.Core.Services;

public sealed record RecoveryCorpusClip(
    string OpaqueClipId,
    string SourceSha256,
    string Classification,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc = null);

public sealed record RecoveryCorpusAggregate(
    int EligibleClipCount,
    int ExcludedClipCount,
    IReadOnlyDictionary<string, int> EligibleByClassification);

/// <summary>Local-only corpus eligibility and aggregate reporting; it never accepts audio, text, names, or paths.</summary>
public static class RecoveryQualityCorpusRegistry
{
    public static RecoveryCorpusAggregate Summarize(IEnumerable<RecoveryCorpusClip> clips, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var snapshot = clips.ToArray();
        var eligible = snapshot.Where(clip =>
                !string.IsNullOrWhiteSpace(clip.OpaqueClipId) &&
                IsSha256(clip.SourceSha256) &&
                !string.IsNullOrWhiteSpace(clip.Classification) &&
                clip.RevokedAtUtc is null && clip.ExpiresAtUtc > nowUtc)
            .ToArray();
        return new RecoveryCorpusAggregate(
            eligible.Length,
            snapshot.Length - eligible.Length,
            eligible.GroupBy(clip => clip.Classification.Trim(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
