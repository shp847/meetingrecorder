using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class HomeCommandCenterServiceTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly NextBestActionResolver _resolver = new();

    [Fact]
    public void Resolve_Active_Degraded_Capture_Wins_Over_Every_Other_Candidate()
    {
        var state = _resolver.Resolve(CreateInput(
            isRecording: true,
            captureTruth: HomeCaptureTruth.Degraded,
            hasModel: false,
            recoveryRequested: true,
            setupRequired: true,
            queuedCount: 3,
            updateAvailable: true,
            providerNeedsAttention: true));

        Assert.Equal("Capture needs attention", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.SettingsRecording, state.Target);
        Assert.Equal(HomeCommandCenterSeverity.Danger, state.Severity);
        Assert.Contains("Local transcription needs setup.", state.SuppressedCandidateReasons);
    }

    [Fact]
    public void Resolve_Active_Live_Recording_Remains_Primary_Over_Setup_Blocker()
    {
        var state = _resolver.Resolve(CreateInput(
            isRecording: true,
            captureTruth: HomeCaptureTruth.LiveOutput,
            hasModel: false));

        Assert.Equal("Recording in progress", state.Headline);
        Assert.Equal("Live capture output is active.", state.Reason);
        Assert.Null(state.ActionLabel);
        Assert.Equal(HomeCommandCenterTarget.None, state.Target);
    }

    [Fact]
    public void Resolve_Uses_Readiness_Blocker_Before_Recovery_Queue_Update_And_Provider()
    {
        var state = _resolver.Resolve(CreateInput(
            hasModel: false,
            recoveryRequested: true,
            setupRequired: true,
            queuedCount: 2,
            updateAvailable: true,
            providerNeedsAttention: true));

        Assert.Equal("Recording setup needs attention", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.SettingsSetup, state.Target);
        Assert.Equal("Open setup", state.ActionLabel);
    }

    [Fact]
    public void Resolve_Uses_Output_Failure_As_Recording_Blocker()
    {
        var state = _resolver.Resolve(CreateInput(outputReady: false));

        Assert.Equal("Choose a writable meeting output location.", state.Reason);
        Assert.Equal(HomeCommandCenterTarget.SettingsFilesAndUpdates, state.Target);
        Assert.Equal("Open output locations", state.ActionLabel);
    }

    [Fact]
    public void Resolve_Uses_Recovery_Before_Selected_Task_Setup()
    {
        var state = _resolver.Resolve(CreateInput(
            recoveryRequested: true,
            setupRequired: true,
            queuedCount: 1));

        Assert.Equal("Meeting recovery needs attention", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.Meetings, state.Target);
    }

    [Fact]
    public void Resolve_Uses_Selected_Task_Setup_Before_Queue_And_Notices()
    {
        var state = _resolver.Resolve(CreateInput(
            setupRequired: true,
            queuedCount: 1,
            updateAvailable: true,
            providerNeedsAttention: true));

        Assert.Equal("Finish setup for current task", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.SettingsSetup, state.Target);
    }

    [Fact]
    public void Resolve_Uses_Queue_Before_Update_And_Provider_Notice()
    {
        var state = _resolver.Resolve(CreateInput(
            queuedCount: 4,
            updateAvailable: true,
            providerNeedsAttention: true));

        Assert.Equal("Meeting work is queued", state.Headline);
        Assert.Equal("4 meeting work item(s) need review.", state.Reason);
        Assert.Equal(HomeCommandCenterTarget.Meetings, state.Target);
    }

    [Fact]
    public void Resolve_Uses_Update_Before_Provider_Notice()
    {
        var state = _resolver.Resolve(CreateInput(
            updateAvailable: true,
            providerNeedsAttention: true));

        Assert.Equal("Update available", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.SettingsUpdates, state.Target);
    }

    [Theory]
    [InlineData(HomeCaptureTruth.LiveOutput, "Live capture output is active.", HomeCommandCenterSeverity.Information)]
    [InlineData(HomeCaptureTruth.FallbackOutput, "Fallback capture output is active.", HomeCommandCenterSeverity.Warning)]
    [InlineData(HomeCaptureTruth.Degraded, "Recording is active, but capture state needs review.", HomeCommandCenterSeverity.Danger)]
    [InlineData(HomeCaptureTruth.Unavailable, "Recording is active, but no usable capture source is reported.", HomeCommandCenterSeverity.Danger)]
    public void Resolve_Reports_Each_Active_Capture_Truth(
        HomeCaptureTruth captureTruth,
        string expectedReason,
        HomeCommandCenterSeverity expectedSeverity)
    {
        var state = _resolver.Resolve(CreateInput(isRecording: true, captureTruth: captureTruth));

        Assert.Equal(captureTruth, state.CaptureTruth);
        Assert.Equal(expectedReason, state.Reason);
        Assert.Equal(expectedSeverity, state.Severity);
    }

    [Fact]
    public void Resolve_Treats_Stale_Active_Capture_As_Degraded()
    {
        var state = _resolver.Resolve(CreateInput(
            isRecording: true,
            captureTruth: HomeCaptureTruth.LiveOutput,
            captureObservedAtUtc: NowUtc - TimeSpan.FromMinutes(3)));

        Assert.Equal("Capture needs attention", state.Headline);
        Assert.Equal(HomeStateFreshness.Stale, state.Freshness);
        Assert.Equal(HomeCaptureTruth.LiveOutput, state.CaptureTruth);
    }

    [Fact]
    public void Resolve_Provider_Gap_Is_Last_Optional_Notice()
    {
        var state = _resolver.Resolve(CreateInput(providerNeedsAttention: true));

        Assert.Equal("Summary provider needs review", state.Headline);
        Assert.Equal(HomeCommandCenterTarget.SettingsSummaries, state.Target);
    }

    [Fact]
    public void Resolve_Ready_State_Has_No_Action_And_Does_Not_Expose_Diagnostics()
    {
        var state = _resolver.Resolve(CreateInput());

        Assert.Equal("Ready to record", state.Headline);
        Assert.Null(state.ActionLabel);
        Assert.Equal(HomeCommandCenterTarget.None, state.Target);
        Assert.DoesNotContain("path", state.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", state.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static HomeCommandCenterInput CreateInput(
        bool isRecording = false,
        HomeCaptureTruth captureTruth = HomeCaptureTruth.StaticReadiness,
        bool hasModel = true,
        bool outputReady = true,
        bool recoveryRequested = false,
        bool setupRequired = false,
        int queuedCount = 0,
        bool updateAvailable = false,
        bool providerNeedsAttention = false,
        DateTimeOffset? captureObservedAtUtc = null)
    {
        var readiness = new RecordingReadinessService().Project(new RecordingReadinessInput(
            HasLocalTranscription: hasModel,
            HasWritableOutput: outputReady,
            RecordingPermission: RecordingPermissionReadiness.Available,
            HasOptionalSpeakerLabeling: true,
            IsMicrophoneCaptureEnabled: false,
            IsAutoDetectionEnabled: true,
            TeamsCapabilityStatus.FallbackOnly,
            HasAdvancedProviderConfiguration: false));

        return new HomeCommandCenterInput(
            new HomeRecordingState(isRecording, NowUtc),
            new HomeReadinessState(readiness, NowUtc),
            new HomeCaptureState(captureTruth, captureObservedAtUtc ?? NowUtc),
            new HomeRecoveryState(recoveryRequested, HomeCommandCenterTarget.Meetings, NowUtc),
            new HomeCurrentTaskState(setupRequired, HomeCommandCenterTarget.SettingsSetup, NowUtc),
            new HomeQueueState(queuedCount, NowUtc),
            new HomeUpdateState(updateAvailable, NowUtc),
            new HomeProviderState(providerNeedsAttention, NowUtc),
            NowUtc);
    }
}
