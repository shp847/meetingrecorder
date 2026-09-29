using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AutomationCatalogSnapshotTests
{
    [Fact]
    public void Create_Normalizes_Metadata_And_Derives_A_Stable_Input_Revision()
    {
        var cancellationIdentity = Guid.Parse("84B4E6AA-5FC7-42F8-B9B9-3BF5F364F56E");
        var first = AutomationCatalogSnapshot.Create(
            7,
            AutomationCatalogRefreshMode.Full,
            DateTimeOffset.Parse("2026-09-27T12:00:00Z"),
            [" meeting-b ", "meeting-a", "meeting-a"],
            ["fix-b", "fix-a", "fix-a"],
            policyRevision: 1,
            cancellationIdentity);
        var second = AutomationCatalogSnapshot.Create(
            7,
            AutomationCatalogRefreshMode.Full,
            DateTimeOffset.Parse("2026-09-27T12:01:00Z"),
            ["meeting-a", "meeting-b"],
            ["fix-a", "fix-b"],
            policyRevision: 1,
            cancellationIdentity);

        Assert.Equal(["fix-a", "fix-b"], first.RecommendationFingerprints);
        Assert.Equal(first.InputRevision, second.InputRevision);
        Assert.Equal(cancellationIdentity, first.CancellationIdentity);
        Assert.Equal(64, first.InputRevision.Length);
    }

    [Theory]
    [InlineData(false, true, true, false, (int)AutomationCoordinatorState.Idle, (int)AutomationWaitReason.SnapshotUnavailable)]
    [InlineData(true, false, true, false, (int)AutomationCoordinatorState.Waiting, (int)AutomationWaitReason.FullSnapshotRequired)]
    [InlineData(true, true, false, false, (int)AutomationCoordinatorState.Waiting, (int)AutomationWaitReason.SnapshotStale)]
    [InlineData(true, true, true, true, (int)AutomationCoordinatorState.Waiting, (int)AutomationWaitReason.SnapshotCancelled)]
    public void Resolve_Rejects_NonCurrent_Or_NonFull_Snapshots(
        bool hasSnapshot,
        bool isFullSnapshot,
        bool isSnapshotCurrent,
        bool isSnapshotCancelled,
        int expectedState,
        int expectedReason)
    {
        var result = AutomationSchedulerCoordinator.Resolve(Input(
            hasSnapshot: hasSnapshot,
            isFullSnapshot: isFullSnapshot,
            isSnapshotCurrent: isSnapshotCurrent,
            isSnapshotCancelled: isSnapshotCancelled));

        Assert.Equal((AutomationCoordinatorState)expectedState, result.State);
        Assert.Equal((AutomationWaitReason)expectedReason, result.WaitReason);
        Assert.False(result.CanDispatch);
    }

    [Theory]
    [InlineData(true, false, false, false, (int)AutomationWaitReason.Recording)]
    [InlineData(false, true, false, false, (int)AutomationWaitReason.UserAction)]
    [InlineData(false, false, true, false, (int)AutomationWaitReason.QueuePressure)]
    [InlineData(false, false, false, true, (int)AutomationWaitReason.ProviderUnavailable)]
    public void Resolve_Waits_For_LowPressure_Authority(
        bool recording,
        bool userAction,
        bool queuePressure,
        bool providerUnavailable,
        int expectedReason)
    {
        var result = AutomationSchedulerCoordinator.Resolve(Input(
            isRecording: recording,
            isUserActionInProgress: userAction,
            isQueueUnderPressure: queuePressure,
            isProviderAvailable: !providerUnavailable));

        Assert.False(result.CanDispatch);
        Assert.Equal((AutomationWaitReason)expectedReason, result.WaitReason);
    }

    [Fact]
    public void Resolve_Allows_Only_A_Current_Full_Unblocked_Snapshot_To_Dispatch()
    {
        var result = AutomationSchedulerCoordinator.Resolve(Input(eligibleRecommendationCount: 2));

        Assert.True(result.CanDispatch);
        Assert.Equal(AutomationCoordinatorState.Dispatching, result.State);
        Assert.Equal(AutomationWaitReason.None, result.WaitReason);
    }

    private static AutomationCoordinatorInput Input(
        bool hasSnapshot = true,
        bool isFullSnapshot = true,
        bool isSnapshotCurrent = true,
        bool isSnapshotCancelled = false,
        bool isRecording = false,
        bool isUserActionInProgress = false,
        bool isQueueUnderPressure = false,
        bool isProviderAvailable = true,
        int eligibleRecommendationCount = 1) =>
        new(
            IsScanInProgress: false,
            hasSnapshot,
            isFullSnapshot,
            isSnapshotCurrent,
            isSnapshotCancelled,
            IsShutdownRequested: false,
            isRecording,
            isUserActionInProgress,
            isQueueUnderPressure,
            isProviderAvailable,
            IsBackoffActive: false,
            HasInFlightWorkerWork: false,
            eligibleRecommendationCount);
}
