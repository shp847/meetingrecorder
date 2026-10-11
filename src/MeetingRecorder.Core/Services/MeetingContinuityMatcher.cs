using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum MeetingIdentityComparisonMode
{
    RuntimeToRuntime = 0,
    RuntimeToManifest = 1,
    ManifestToManifest = 2,
}

public enum MeetingIdentityVerdict
{
    SameMeeting = 0,
    DifferentMeeting = 1,
    Unknown = 2,
}

public sealed record MeetingIdentityMatchResult(
    MeetingIdentityVerdict Verdict,
    MeetingIdentityEvidenceTier EvidenceTier,
    string ReasonCode,
    bool HasConflict,
    bool IsExpired);

/// <summary>Pure identity comparison. Callers decide grace and capture action.</summary>
public sealed class MeetingContinuityMatcher
{
    public static readonly TimeSpan MaximumStrongCaptureGap = TimeSpan.FromHours(4);

    public MeetingIdentityMatchResult Compare(
        MeetingIdentitySnapshot? left,
        MeetingIdentitySnapshot? right,
        MeetingIdentityComparisonMode mode,
        DateTimeOffset nowUtc)
    {
        _ = mode; // Modes are intentionally input parity only; no mode-specific policy.
        nowUtc = nowUtc.ToUniversalTime();
        if (!IsCurrent(left, nowUtc) || !IsCurrent(right, nowUtc))
        {
            return Result(MeetingIdentityVerdict.Unknown, MeetingIdentityEvidenceTier.None, "identity-missing-or-expired", false, true);
        }

        if (left!.Platform != right!.Platform && IsStrong(left) && IsStrong(right))
        {
            return Result(MeetingIdentityVerdict.DifferentMeeting, MeetingIdentityEvidenceTier.Strong, "contradictory-strong-platform", true, false);
        }

        if (KeysMatch(left, right) && DurableTokensConflict(left, right) && IsStrong(left) && IsStrong(right))
        {
            return Result(MeetingIdentityVerdict.DifferentMeeting, MeetingIdentityEvidenceTier.Strong, "contradictory-strong-durable-identity", true, false);
        }

        if (KeysMatch(left, right) && TitleTokensConflict(left, right) && IsStrong(left) && IsStrong(right))
        {
            return Result(MeetingIdentityVerdict.DifferentMeeting, MeetingIdentityEvidenceTier.Strong, "contradictory-strong-specific-title", true, false);
        }

        if (StrongTokensMatch(left, right) && IsCaptureProximate(left, right))
        {
            return Result(MeetingIdentityVerdict.SameMeeting, MeetingIdentityEvidenceTier.Strong, "compatible-strong-identity", false, false);
        }

        if (string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.Ordinal))
        {
            return Result(MeetingIdentityVerdict.Unknown, MinimumTier(left, right), "fingerprint-is-not-identity", false, false);
        }

        return Result(MeetingIdentityVerdict.Unknown, MinimumTier(left, right), "insufficient-identity-evidence", false, false);
    }

    private static bool IsCurrent(MeetingIdentitySnapshot? snapshot, DateTimeOffset nowUtc) =>
        snapshot is not null &&
        MeetingIdentitySnapshotValidator.IsReadable(snapshot) &&
        snapshot.CapturedAtUtc <= nowUtc.AddMinutes(1) &&
        snapshot.ExpiresAtUtc > nowUtc;

    private static bool IsStrong(MeetingIdentitySnapshot snapshot) =>
        snapshot.EvidenceTier == MeetingIdentityEvidenceTier.Strong && !snapshot.IsAmbiguous && snapshot.HasAttributedCapture;

    private static bool DurableTokensConflict(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        left.DurableIdentityToken is not null && right.DurableIdentityToken is not null &&
        !string.Equals(left.DurableIdentityToken, right.DurableIdentityToken, StringComparison.Ordinal);

    private static bool TitleTokensConflict(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        left.SpecificTitleToken is not null && right.SpecificTitleToken is not null &&
        !string.Equals(left.SpecificTitleToken, right.SpecificTitleToken, StringComparison.Ordinal);

    private static bool StrongTokensMatch(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        IsStrong(left) && IsStrong(right) && left.Platform == right.Platform && KeysMatch(left, right) &&
        ((left.DurableIdentityToken is not null && string.Equals(left.DurableIdentityToken, right.DurableIdentityToken, StringComparison.Ordinal)) ||
         (left.SpecificTitleToken is not null && string.Equals(left.SpecificTitleToken, right.SpecificTitleToken, StringComparison.Ordinal)));

    private static bool IsCaptureProximate(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        (left.CapturedAtUtc - right.CapturedAtUtc).Duration() <= MaximumStrongCaptureGap;

    private static bool KeysMatch(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        string.Equals(left.KeyId, right.KeyId, StringComparison.Ordinal);

    private static MeetingIdentityEvidenceTier MinimumTier(MeetingIdentitySnapshot left, MeetingIdentitySnapshot right) =>
        (MeetingIdentityEvidenceTier)Math.Min((int)left.EvidenceTier, (int)right.EvidenceTier);

    private static MeetingIdentityMatchResult Result(MeetingIdentityVerdict verdict, MeetingIdentityEvidenceTier tier, string reason, bool conflict, bool expired) =>
        new(verdict, tier, reason, conflict, expired);
}
