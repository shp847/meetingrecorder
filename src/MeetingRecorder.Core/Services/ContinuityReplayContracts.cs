using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ContinuityCorpusSplit
{
    Regression = 0,
    Negative = 1,
    Holdout = 2,
}

public enum ContinuityFixtureConsent
{
    PublicSynthetic = 0,
    RestrictedLocal = 1,
}

public enum ContinuityEvidenceTier
{
    None = 0,
    Weak = 1,
    Medium = 2,
    Strong = 3,
}

public enum ContinuityReplayDecision
{
    SameMeeting = 0,
    DifferentMeeting = 1,
    UnknownGrace = 2,
    ManualReview = 3,
}

public sealed record ContinuityReplaySnapshot(
    int SchemaVersion,
    string ScenarioId,
    ContinuityCorpusSplit Split,
    ContinuityFixtureConsent Consent,
    MeetingPlatform Platform,
    ContinuityEvidenceTier EvidenceTier,
    bool HasStableIdentity,
    bool HasContradictoryStableIdentity,
    bool HasRecentCaptureActivity,
    bool WasManuallyStopped,
    bool Restarted,
    bool Published,
    TimeSpan ElapsedSinceLastPositiveSignal,
    ContinuityReplayDecision ExpectedDecision,
    string ExpectedReasonCode)
{
    public const int CurrentSchemaVersion = 1;
    private static readonly Regex ScenarioIdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    // Deliberately absent: title, attendee, endpoint, process, window, path, transcript, and audio data.
    public string IntegrityHash { get; init; } = string.Empty;

    public ContinuityReplaySnapshot WithComputedIntegrityHash() => this with
    {
        IntegrityHash = ComputeIntegrityHash(),
    };

    public string ComputeIntegrityHash()
    {
        var canonical = string.Join("|", [
            SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ScenarioId,
            ((int)Split).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)Consent).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)Platform).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)EvidenceTier).ToString(System.Globalization.CultureInfo.InvariantCulture),
            HasStableIdentity ? "1" : "0",
            HasContradictoryStableIdentity ? "1" : "0",
            HasRecentCaptureActivity ? "1" : "0",
            WasManuallyStopped ? "1" : "0",
            Restarted ? "1" : "0",
            Published ? "1" : "0",
            ElapsedSinceLastPositiveSignal.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)ExpectedDecision).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExpectedReasonCode,
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public void ValidateForPublicReplay()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported continuity replay schema '{SchemaVersion}'.");
        }

        if (!ScenarioIdPattern.IsMatch(ScenarioId))
        {
            throw new InvalidOperationException("Continuity replay scenario ids must be opaque lowercase tokens.");
        }

        if (Consent != ContinuityFixtureConsent.PublicSynthetic)
        {
            throw new InvalidOperationException("Only consented public synthetic fixtures may enter the tracked continuity corpus.");
        }

        if (Platform == MeetingPlatform.Unknown || ElapsedSinceLastPositiveSignal < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Continuity replay fixture contains invalid platform or timing metadata.");
        }

        if (string.IsNullOrWhiteSpace(ExpectedReasonCode) ||
            !string.Equals(IntegrityHash, ComputeIntegrityHash(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Continuity replay fixture integrity validation failed.");
        }
    }
}

public sealed record ContinuityReplayDecisionResult(
    string ScenarioId,
    ContinuityReplayDecision Decision,
    string ReasonCode,
    bool EntersGrace,
    bool SupportsAutomaticMerge);

public sealed class ContinuityReplayDecisionContract
{
    public static readonly TimeSpan MaximumGrace = TimeSpan.FromMinutes(5);

    public ContinuityReplayDecisionResult Evaluate(ContinuityReplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.ValidateForPublicReplay();

        if (snapshot.WasManuallyStopped)
        {
            return Result(snapshot, ContinuityReplayDecision.ManualReview, "manual-stop-authority", false, false);
        }

        if (snapshot.HasContradictoryStableIdentity && snapshot.EvidenceTier == ContinuityEvidenceTier.Strong)
        {
            return Result(snapshot, ContinuityReplayDecision.DifferentMeeting, "contradictory-strong-identity", false, false);
        }

        if (snapshot.HasStableIdentity && snapshot.EvidenceTier == ContinuityEvidenceTier.Strong)
        {
            return Result(snapshot, ContinuityReplayDecision.SameMeeting, "stable-identity-continuity", false, false);
        }

        if (snapshot.HasRecentCaptureActivity &&
            snapshot.ElapsedSinceLastPositiveSignal <= MaximumGrace &&
            snapshot.EvidenceTier is ContinuityEvidenceTier.Weak or ContinuityEvidenceTier.Medium)
        {
            return Result(snapshot, ContinuityReplayDecision.UnknownGrace, "bounded-ambiguous-grace", true, false);
        }

        return Result(snapshot, ContinuityReplayDecision.ManualReview, "insufficient-identity-evidence", false, false);
    }

    private static ContinuityReplayDecisionResult Result(
        ContinuityReplaySnapshot snapshot,
        ContinuityReplayDecision decision,
        string reasonCode,
        bool entersGrace,
        bool supportsAutomaticMerge) =>
        new(snapshot.ScenarioId, decision, reasonCode, entersGrace, supportsAutomaticMerge);
}

public sealed record ContinuityReplayMetrics(
    int ScenarioCount,
    int ExactDecisionCount,
    int FalseMergeCount,
    int FalseSplitCount,
    int UnknownGraceCount,
    int ManualReviewCount)
{
    public static ContinuityReplayMetrics Calculate(
        IEnumerable<ContinuityReplaySnapshot> snapshots,
        ContinuityReplayDecisionContract contract)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(contract);

        var evaluated = snapshots.Select(snapshot => (Snapshot: snapshot, Result: contract.Evaluate(snapshot))).ToArray();
        return new ContinuityReplayMetrics(
            evaluated.Length,
            evaluated.Count(item => item.Snapshot.ExpectedDecision == item.Result.Decision),
            evaluated.Count(item => item.Snapshot.ExpectedDecision == ContinuityReplayDecision.DifferentMeeting && item.Result.Decision == ContinuityReplayDecision.SameMeeting),
            evaluated.Count(item => item.Snapshot.ExpectedDecision == ContinuityReplayDecision.SameMeeting && item.Result.Decision == ContinuityReplayDecision.DifferentMeeting),
            evaluated.Count(item => item.Result.Decision == ContinuityReplayDecision.UnknownGrace),
            evaluated.Count(item => item.Result.Decision == ContinuityReplayDecision.ManualReview));
    }
}

public static class ContinuityReplayCorpus
{
    public static IReadOnlyList<ContinuityReplaySnapshot> CreatePublicSyntheticFixtures() =>
    [
        Fixture("incident-autostop-continuation-a", ContinuityCorpusSplit.Regression, MeetingPlatform.Teams, ContinuityEvidenceTier.Strong, true, false, true, false, false, false, TimeSpan.FromSeconds(30), ContinuityReplayDecision.SameMeeting, "stable-identity-continuity"),
        Fixture("incident-crash-recovery-split-a", ContinuityCorpusSplit.Regression, MeetingPlatform.GoogleMeet, ContinuityEvidenceTier.Strong, true, false, true, false, true, true, TimeSpan.FromMinutes(1), ContinuityReplayDecision.SameMeeting, "stable-identity-continuity"),
        Fixture("generic-teams-false-start-a", ContinuityCorpusSplit.Negative, MeetingPlatform.Teams, ContinuityEvidenceTier.Weak, false, false, false, false, false, false, TimeSpan.Zero, ContinuityReplayDecision.ManualReview, "insufficient-identity-evidence"),
        Fixture("quiet-continuation-a", ContinuityCorpusSplit.Regression, MeetingPlatform.Teams, ContinuityEvidenceTier.Medium, false, false, true, false, false, false, TimeSpan.FromMinutes(2), ContinuityReplayDecision.UnknownGrace, "bounded-ambiguous-grace"),
        Fixture("same-title-different-meeting-a", ContinuityCorpusSplit.Negative, MeetingPlatform.Teams, ContinuityEvidenceTier.Strong, true, true, true, false, false, true, TimeSpan.FromSeconds(20), ContinuityReplayDecision.DifferentMeeting, "contradictory-strong-identity"),
    ];

    private static ContinuityReplaySnapshot Fixture(
        string scenarioId,
        ContinuityCorpusSplit split,
        MeetingPlatform platform,
        ContinuityEvidenceTier tier,
        bool stableIdentity,
        bool contradictoryStableIdentity,
        bool recentCapture,
        bool manuallyStopped,
        bool restarted,
        bool published,
        TimeSpan elapsed,
        ContinuityReplayDecision expectedDecision,
        string expectedReasonCode) =>
        new ContinuityReplaySnapshot(
            ContinuityReplaySnapshot.CurrentSchemaVersion,
            scenarioId,
            split,
            ContinuityFixtureConsent.PublicSynthetic,
            platform,
            tier,
            stableIdentity,
            contradictoryStableIdentity,
            recentCapture,
            manuallyStopped,
            restarted,
            published,
            elapsed,
            expectedDecision,
            expectedReasonCode)
        .WithComputedIntegrityHash();
}
