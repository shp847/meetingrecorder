using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ContinuityCutoverMode
{
    Legacy = 0,
    Matcher = 1,
}

public enum ContinuityLifecycleAction
{
    Continue = 0,
    DifferentMeeting = 1,
    Grace = 2,
    Stop = 3,
}

public sealed record ContinuityGraceReceipt(
    int SessionRevision,
    string EvidenceFingerprint,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DeadlineUtc,
    int ObservationCount);

public sealed record ContinuityCutoverInput(
    ContinuityCutoverMode Mode,
    int SessionRevision,
    MeetingIdentitySnapshot? ExistingIdentity,
    MeetingIdentitySnapshot? ObservedIdentity,
    MeetingIdentityVerdict LegacyVerdict,
    bool IsManualStop,
    ContinuityGraceReceipt? ExistingGrace,
    DateTimeOffset NowUtc);

public sealed record ContinuityCutoverDecision(
    ContinuityLifecycleAction Action,
    MeetingIdentityVerdict Verdict,
    string ReasonCode,
    ContinuityGraceReceipt? GraceReceipt,
    bool UsedMatcher);

/// <summary>
/// Small, deterministic adapter for the live cutover. It cannot start a new
/// recording and does not own capture transitions; callers must recheck their
/// session revision before acting on a result.
/// </summary>
public sealed class ContinuityCutoverPolicy
{
    public static readonly TimeSpan MaximumGrace = TimeSpan.FromMinutes(5);
    private readonly MeetingContinuityMatcher _matcher;

    public ContinuityCutoverPolicy(MeetingContinuityMatcher? matcher = null)
    {
        _matcher = matcher ?? new MeetingContinuityMatcher();
    }

    public ContinuityCutoverDecision Evaluate(ContinuityCutoverInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var nowUtc = input.NowUtc.ToUniversalTime();
        if (input.IsManualStop)
        {
            return new ContinuityCutoverDecision(
                ContinuityLifecycleAction.Stop,
                MeetingIdentityVerdict.Unknown,
                "manual-stop-authority",
                null,
                false);
        }

        if (input.Mode == ContinuityCutoverMode.Legacy)
        {
            return FromLegacy(input.LegacyVerdict);
        }

        var match = _matcher.Compare(
            input.ExistingIdentity,
            input.ObservedIdentity,
            MeetingIdentityComparisonMode.RuntimeToRuntime,
            nowUtc);
        return match.Verdict switch
        {
            MeetingIdentityVerdict.SameMeeting => new ContinuityCutoverDecision(
                ContinuityLifecycleAction.Continue,
                match.Verdict,
                match.ReasonCode,
                null,
                true),
            MeetingIdentityVerdict.DifferentMeeting => new ContinuityCutoverDecision(
                ContinuityLifecycleAction.DifferentMeeting,
                match.Verdict,
                match.ReasonCode,
                null,
                true),
            _ => CreateGrace(input, nowUtc, match.ReasonCode),
        };
    }

    private static ContinuityCutoverDecision FromLegacy(MeetingIdentityVerdict legacyVerdict) => legacyVerdict switch
    {
        MeetingIdentityVerdict.SameMeeting => new(ContinuityLifecycleAction.Continue, legacyVerdict, "legacy-continuation", null, false),
        MeetingIdentityVerdict.DifferentMeeting => new(ContinuityLifecycleAction.DifferentMeeting, legacyVerdict, "legacy-different-meeting", null, false),
        _ => new(ContinuityLifecycleAction.Grace, legacyVerdict, "legacy-unknown", null, false),
    };

    private static ContinuityCutoverDecision CreateGrace(
        ContinuityCutoverInput input,
        DateTimeOffset nowUtc,
        string reasonCode)
    {
        var fingerprint = input.ObservedIdentity?.Fingerprint ?? "missing-identity";
        var existing = input.ExistingGrace;
        if (existing is not null &&
            existing.SessionRevision == input.SessionRevision &&
            string.Equals(existing.EvidenceFingerprint, fingerprint, StringComparison.Ordinal) &&
            nowUtc <= existing.DeadlineUtc)
        {
            return new ContinuityCutoverDecision(
                ContinuityLifecycleAction.Grace,
                MeetingIdentityVerdict.Unknown,
                reasonCode,
                existing with { ObservationCount = existing.ObservationCount + 1 },
                true);
        }

        var receipt = new ContinuityGraceReceipt(
            input.SessionRevision,
            fingerprint,
            nowUtc,
            nowUtc + MaximumGrace,
            1);
        return new ContinuityCutoverDecision(
            ContinuityLifecycleAction.Grace,
            MeetingIdentityVerdict.Unknown,
            reasonCode,
            receipt,
            true);
    }
}
