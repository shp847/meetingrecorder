using System.Diagnostics;
using System.Text.RegularExpressions;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum ContinuityDecisionBoundary
{
    Continuation = 0,
    RolloverOrReclassify = 1,
    RecentAutoStopRecovery = 2,
    StartupRecovery = 3,
    PublishHeal = 4,
}

public enum ContinuityShadowDivergence
{
    Agreement = 0,
    ReasonDrift = 1,
    ConservativeUnknown = 2,
    ShadowPreventsSplit = 3,
    ShadowWouldCreateSplit = 4,
    PotentialFalseMerge = 5,
    InputMismatch = 6,
    Unavailable = 7,
}

public enum ContinuityShadowScenarioLabel
{
    Unlabeled = 0,
    ProtectedIncident = 1,
    ProtectedNegative = 2,
}

public enum ContinuityShadowReviewDisposition
{
    NeedsCorpus = 0,
    LegacyBug = 1,
    ShadowBug = 2,
    IntentionalRiskReduction = 3,
}

public sealed record ContinuityShadowInput(
    string OpaqueCorrelationId,
    int InputRevision,
    ContinuityDecisionBoundary Boundary,
    MeetingIdentitySnapshot? ExistingIdentity,
    MeetingIdentitySnapshot? ObservedIdentity,
    MeetingIdentityVerdict LegacyVerdict,
    string LegacyReasonCode,
    ContinuityShadowScenarioLabel Label,
    MeetingIdentityVerdict? ExpectedVerdict = null);

public sealed record ContinuityShadowReceipt(
    string OpaqueCorrelationId,
    int InputRevision,
    ContinuityDecisionBoundary Boundary,
    MeetingPlatform Platform,
    MeetingIdentityEvidenceTier EvidenceTier,
    MeetingIdentityVerdict LegacyVerdict,
    string LegacyReasonCode,
    MeetingIdentityVerdict? ShadowVerdict,
    string ShadowReasonCode,
    ContinuityShadowDivergence Divergence,
    ContinuityShadowScenarioLabel Label,
    MeetingIdentityVerdict? ExpectedVerdict,
    int DurationMilliseconds,
    string EngineVersion);

public sealed class ContinuityShadowEngine
{
    public const string EngineVersion = "continuity-shadow-v1";
    private static readonly Regex OpaqueIdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex ReasonCodePattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private readonly MeetingContinuityMatcher _matcher;

    public ContinuityShadowEngine(MeetingContinuityMatcher? matcher = null)
    {
        _matcher = matcher ?? new MeetingContinuityMatcher();
    }

    public ContinuityShadowReceipt Evaluate(ContinuityShadowInput input, DateTimeOffset nowUtc)
    {
        ValidateInput(input);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = _matcher.Compare(
                input.ExistingIdentity,
                input.ObservedIdentity,
                MeetingIdentityComparisonMode.RuntimeToRuntime,
                nowUtc);
            return CreateReceipt(input, result.Verdict, result.EvidenceTier, result.ReasonCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return Unavailable(input, stopwatch.ElapsedMilliseconds, "shadow-input-unavailable");
        }
    }

    public static ContinuityShadowReceipt Unavailable(ContinuityShadowInput input, long durationMilliseconds, string reasonCode)
    {
        ValidateInput(input);
        if (!ReasonCodePattern.IsMatch(reasonCode))
        {
            throw new ArgumentException("Shadow reason codes must be normalized metadata tokens.", nameof(reasonCode));
        }

        return new ContinuityShadowReceipt(
            input.OpaqueCorrelationId,
            input.InputRevision,
            input.Boundary,
            input.ExistingIdentity?.Platform ?? input.ObservedIdentity?.Platform ?? MeetingPlatform.Unknown,
            MeetingIdentityEvidenceTier.None,
            input.LegacyVerdict,
            input.LegacyReasonCode,
            null,
            reasonCode,
            ContinuityShadowDivergence.Unavailable,
            input.Label,
            input.ExpectedVerdict,
            (int)Math.Clamp(durationMilliseconds, 0, int.MaxValue),
            EngineVersion);
    }

    private static ContinuityShadowReceipt CreateReceipt(
        ContinuityShadowInput input,
        MeetingIdentityVerdict shadowVerdict,
        MeetingIdentityEvidenceTier tier,
        string reasonCode,
        long durationMilliseconds) =>
        new(
            input.OpaqueCorrelationId,
            input.InputRevision,
            input.Boundary,
            input.ExistingIdentity?.Platform ?? input.ObservedIdentity?.Platform ?? MeetingPlatform.Unknown,
            tier,
            input.LegacyVerdict,
            input.LegacyReasonCode,
            shadowVerdict,
            reasonCode,
            Classify(input.LegacyVerdict, input.LegacyReasonCode, shadowVerdict, reasonCode),
            input.Label,
            input.ExpectedVerdict,
            (int)Math.Clamp(durationMilliseconds, 0, int.MaxValue),
            EngineVersion);

    public static ContinuityShadowDivergence Classify(
        MeetingIdentityVerdict legacyVerdict,
        string legacyReasonCode,
        MeetingIdentityVerdict shadowVerdict,
        string shadowReasonCode)
    {
        if (legacyVerdict == shadowVerdict)
        {
            return string.Equals(legacyReasonCode, shadowReasonCode, StringComparison.Ordinal)
                ? ContinuityShadowDivergence.Agreement
                : ContinuityShadowDivergence.ReasonDrift;
        }

        if (shadowVerdict == MeetingIdentityVerdict.Unknown)
        {
            return ContinuityShadowDivergence.ConservativeUnknown;
        }

        if (legacyVerdict == MeetingIdentityVerdict.DifferentMeeting && shadowVerdict == MeetingIdentityVerdict.SameMeeting)
        {
            return ContinuityShadowDivergence.PotentialFalseMerge;
        }

        if (legacyVerdict == MeetingIdentityVerdict.SameMeeting && shadowVerdict == MeetingIdentityVerdict.DifferentMeeting)
        {
            return ContinuityShadowDivergence.ShadowWouldCreateSplit;
        }

        return ContinuityShadowDivergence.ShadowPreventsSplit;
    }

    private static void ValidateInput(ContinuityShadowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OpaqueIdPattern.IsMatch(input.OpaqueCorrelationId) ||
            input.InputRevision < 0 ||
            !ReasonCodePattern.IsMatch(input.LegacyReasonCode))
        {
            throw new ArgumentException("Shadow input must contain only opaque identifiers and normalized metadata.", nameof(input));
        }
    }
}

/// <summary>Bounded in-memory sampling; callers may drop instead of delaying live work.</summary>
public sealed class ContinuityShadowMeter
{
    private readonly Queue<ContinuityShadowReceipt> _receipts = [];
    private readonly int _capacity;
    private int _dropped;

    public ContinuityShadowMeter(int capacity = 256)
    {
        if (capacity is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public bool TryRecord(ContinuityShadowReceipt receipt, TimeSpan maximumLatency)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (maximumLatency <= TimeSpan.Zero || receipt.DurationMilliseconds > maximumLatency.TotalMilliseconds)
        {
            _dropped++;
            return false;
        }

        if (_receipts.Count == _capacity)
        {
            _receipts.Dequeue();
            _dropped++;
        }

        _receipts.Enqueue(receipt);
        return true;
    }

    public ContinuityShadowReport CreateReport() => ContinuityShadowReport.Create(_receipts, _dropped);
}

public sealed record ContinuityShadowReportRow(
    ContinuityDecisionBoundary Boundary,
    MeetingPlatform Platform,
    MeetingIdentityEvidenceTier EvidenceTier,
    string EngineVersion,
    int Count,
    int LabeledCount,
    int UnavailableCount,
    int PotentialFalseMergeCount,
    int DisagreementCount,
    int MaximumLatencyMilliseconds);

public sealed record ContinuityShadowReport(
    IReadOnlyList<ContinuityShadowReportRow> Rows,
    int DroppedCount,
    int TotalCount)
{
    public static ContinuityShadowReport Create(IEnumerable<ContinuityShadowReceipt> receipts, int droppedCount = 0)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var captured = receipts.ToArray();
        var rows = captured
            .GroupBy(receipt => (receipt.Boundary, receipt.Platform, receipt.EvidenceTier, receipt.EngineVersion))
            .OrderBy(group => group.Key.Boundary)
            .ThenBy(group => group.Key.Platform)
            .ThenBy(group => group.Key.EvidenceTier)
            .Select(group => new ContinuityShadowReportRow(
                group.Key.Boundary,
                group.Key.Platform,
                group.Key.EvidenceTier,
                group.Key.EngineVersion,
                group.Count(),
                group.Count(item => item.Label != ContinuityShadowScenarioLabel.Unlabeled),
                group.Count(item => item.Divergence == ContinuityShadowDivergence.Unavailable),
                group.Count(item => item.Divergence == ContinuityShadowDivergence.PotentialFalseMerge),
                group.Count(item => item.Divergence is not ContinuityShadowDivergence.Agreement and not ContinuityShadowDivergence.Unavailable),
                group.Max(item => item.DurationMilliseconds)))
            .ToArray();
        return new ContinuityShadowReport(rows, Math.Max(0, droppedCount), captured.Length);
    }
}

public sealed record ContinuityShadowCutoverGateResult(bool CanCutOver, IReadOnlyList<string> BlockingReasonCodes);

public static class ContinuityShadowCutoverGate
{
    public static ContinuityShadowCutoverGateResult Evaluate(
        IEnumerable<ContinuityShadowReceipt> receipts,
        IReadOnlyDictionary<string, ContinuityShadowReviewDisposition> reviews,
        TimeSpan maximumLatency,
        double maximumUnavailableRate = 0.05d)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(reviews);
        var captured = receipts.ToArray();
        var blockers = new List<string>();
        if (captured.Length == 0 || captured.All(item => item.Label == ContinuityShadowScenarioLabel.Unlabeled))
        {
            blockers.Add("insufficient-labeled-coverage");
        }

        if (captured.Any(item => item.Label == ContinuityShadowScenarioLabel.ProtectedIncident && item.ShadowVerdict != item.ExpectedVerdict))
        {
            blockers.Add("protected-incident-not-correct");
        }

        if (captured.Any(item => item.Label == ContinuityShadowScenarioLabel.ProtectedNegative &&
                                 item.Divergence == ContinuityShadowDivergence.PotentialFalseMerge &&
                                 !reviews.TryGetValue(item.OpaqueCorrelationId, out var review)))
        {
            blockers.Add("unreviewed-potential-false-merge");
        }

        if (captured.Any(item => item.DurationMilliseconds > maximumLatency.TotalMilliseconds))
        {
            blockers.Add("shadow-latency-exceeded");
        }

        if (captured.Length > 0 && captured.Count(item => item.Divergence == ContinuityShadowDivergence.Unavailable) / (double)captured.Length > maximumUnavailableRate)
        {
            blockers.Add("shadow-unavailability-exceeded");
        }

        return new ContinuityShadowCutoverGateResult(blockers.Count == 0, blockers);
    }
}
