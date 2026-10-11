using System.Security.Cryptography;
using System.Text;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Immutable, metadata-only proof that a complete catalog scan produced the
/// recommendations an automation run is allowed to consider. It deliberately
/// contains neither meeting content nor filesystem locations.
/// </summary>
internal enum AutomationCatalogRefreshMode
{
    SelectionOnly = 0,
    Fast = 1,
    Full = 2,
}

internal sealed record AutomationCatalogSnapshot(
    int RefreshVersion,
    AutomationCatalogRefreshMode RefreshMode,
    DateTimeOffset CompletedAtUtc,
    string InputRevision,
    IReadOnlyList<string> RecommendationFingerprints,
    int PolicyRevision,
    Guid CancellationIdentity)
{
    public static AutomationCatalogSnapshot Create(
        int refreshVersion,
        AutomationCatalogRefreshMode refreshMode,
        DateTimeOffset completedAtUtc,
        IEnumerable<string> inputKeys,
        IEnumerable<string> recommendationFingerprints,
        int policyRevision,
        Guid cancellationIdentity)
    {
        ArgumentNullException.ThrowIfNull(inputKeys);
        ArgumentNullException.ThrowIfNull(recommendationFingerprints);

        var normalizedInputKeys = Normalize(inputKeys);
        var normalizedFingerprints = Normalize(recommendationFingerprints);
        var revisionSource = string.Join(
            "\n",
            normalizedInputKeys.Concat(normalizedFingerprints.Select(fingerprint => $"recommendation:{fingerprint}")));
        var revisionBytes = SHA256.HashData(Encoding.UTF8.GetBytes(revisionSource));

        return new AutomationCatalogSnapshot(
            refreshVersion,
            refreshMode,
            completedAtUtc,
            Convert.ToHexString(revisionBytes),
            normalizedFingerprints,
            policyRevision,
            cancellationIdentity);
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}

internal enum AutomationCoordinatorState
{
    Idle = 0,
    Scanning = 1,
    Waiting = 2,
    Dispatching = 3,
    AwaitingWorkerCompletion = 4,
    Degraded = 5,
}

internal enum AutomationWaitReason
{
    None = 0,
    SnapshotUnavailable = 1,
    FullSnapshotRequired = 2,
    SnapshotStale = 3,
    SnapshotCancelled = 4,
    Recording = 5,
    UserAction = 6,
    QueuePressure = 7,
    ProviderUnavailable = 8,
    Backoff = 9,
    Shutdown = 10,
}

internal sealed record AutomationCoordinatorInput(
    bool IsScanInProgress,
    bool HasSnapshot,
    bool IsFullSnapshot,
    bool IsSnapshotCurrent,
    bool IsSnapshotCancelled,
    bool IsShutdownRequested,
    bool IsRecording,
    bool IsUserActionInProgress,
    bool IsQueueUnderPressure,
    bool IsProviderAvailable,
    bool IsBackoffActive,
    bool HasInFlightWorkerWork,
    int EligibleRecommendationCount);

internal sealed record AutomationCoordinatorDecision(
    AutomationCoordinatorState State,
    AutomationWaitReason WaitReason,
    string StatusText)
{
    public bool CanDispatch => State == AutomationCoordinatorState.Dispatching;
}

/// <summary>
/// Pure dispatch gate for low-pressure automation. The UI may refresh with any
/// scan, but side effects require one current, successful full snapshot.
/// </summary>
internal static class AutomationSchedulerCoordinator
{
    public static AutomationCoordinatorDecision Resolve(AutomationCoordinatorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.IsShutdownRequested)
        {
            return Waiting(AutomationCoordinatorState.Degraded, AutomationWaitReason.Shutdown, "Automation is stopping.");
        }

        if (input.IsScanInProgress)
        {
            return new AutomationCoordinatorDecision(AutomationCoordinatorState.Scanning, AutomationWaitReason.None, "Checking safe automatic work.");
        }

        if (!input.HasSnapshot)
        {
            return Waiting(AutomationCoordinatorState.Idle, AutomationWaitReason.SnapshotUnavailable, "Automatic work waits for a full meeting refresh.");
        }

        if (!input.IsFullSnapshot)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.FullSnapshotRequired, "Automatic work waits for a complete meeting refresh.");
        }

        if (!input.IsSnapshotCurrent)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.SnapshotStale, "Automatic work waits for current meeting results.");
        }

        if (input.IsSnapshotCancelled)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.SnapshotCancelled, "Automatic work waits for the replacement refresh.");
        }

        if (input.IsRecording)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.Recording, "Automatic work is paused while recording.");
        }

        if (input.IsUserActionInProgress)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.UserAction, "Automatic work waits for your current meeting action.");
        }

        if (input.IsQueueUnderPressure || input.HasInFlightWorkerWork)
        {
            return Waiting(AutomationCoordinatorState.AwaitingWorkerCompletion, AutomationWaitReason.QueuePressure, "Automatic work waits for current queued work.");
        }

        if (!input.IsProviderAvailable)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.ProviderUnavailable, "Automatic work waits for the required provider.");
        }

        if (input.IsBackoffActive)
        {
            return Waiting(AutomationCoordinatorState.Waiting, AutomationWaitReason.Backoff, "Automatic work is waiting before retrying the catalog.");
        }

        return input.EligibleRecommendationCount > 0
            ? new AutomationCoordinatorDecision(AutomationCoordinatorState.Dispatching, AutomationWaitReason.None, "Automatic safe work is ready.")
            : new AutomationCoordinatorDecision(AutomationCoordinatorState.Idle, AutomationWaitReason.None, "No safe automatic work is ready.");
    }

    private static AutomationCoordinatorDecision Waiting(
        AutomationCoordinatorState state,
        AutomationWaitReason reason,
        string statusText) => new(state, reason, statusText);
}
