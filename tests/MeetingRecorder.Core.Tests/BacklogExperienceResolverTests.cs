using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class BacklogExperienceResolverTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
    private readonly BacklogExperienceResolver _resolver = new();

    [Fact]
    public void Resolve_Uses_Fresh_Live_Processing_State_With_Measured_Eta_And_Metadata_Only_Copy()
    {
        var snapshot = Snapshot(
            ProcessingQueueRunState.Processing,
            remaining: 3,
            stageName: "transcription",
            stageState: StageExecutionState.Running,
            currentEta: TimeSpan.FromMinutes(8),
            overallEta: TimeSpan.FromMinutes(27),
            currentStageMessage: "C:\\private\\worker.log transcript words must not appear");

        var state = _resolver.Resolve(new BacklogExperienceInput(snapshot, null, null, NowUtc.AddSeconds(30)));

        Assert.Equal(BacklogExperienceKind.Processing, state.Kind);
        Assert.Equal("PROCESSING", state.Headline);
        Assert.Equal(3, state.RemainingCount);
        Assert.Equal("Creating transcript", state.StageText);
        Assert.Equal(BacklogEtaConfidence.Measured, state.EtaConfidence);
        Assert.Equal("ETA ~8m", state.CurrentEtaText);
        Assert.Equal("ETA ~27m", state.OverallEtaText);
        Assert.Contains("Current: Planning review.", state.Detail);
        Assert.DoesNotContain("private", state.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript words", state.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Stale_Live_Snapshot_Requires_Refresh_And_Never_Claims_Idle_Or_Eta()
    {
        var snapshot = Snapshot(
            ProcessingQueueRunState.Idle,
            remaining: 0,
            stageName: null,
            stageState: null,
            currentEta: TimeSpan.FromMinutes(1),
            overallEta: TimeSpan.FromMinutes(1),
            observedAtUtc: NowUtc - BacklogExperienceResolver.MaximumFreshStatusAge - TimeSpan.FromSeconds(1));

        var state = _resolver.Resolve(new BacklogExperienceInput(snapshot, null, null, NowUtc));

        Assert.Equal(BacklogExperienceKind.RefreshRequired, state.Kind);
        Assert.Equal("STATUS NEEDS REFRESH", state.Headline);
        Assert.Contains("unavailable", state.OverallEtaText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BacklogRecoveryAction.RefreshStatus, state.RecoveryAction);
    }

    [Fact]
    public void Resolve_Persisted_Backlog_Without_Live_Queue_Requires_Refresh_Not_Queued_Truth()
    {
        var snapshot = Snapshot(ProcessingQueueRunState.Idle, 0, null, null, null, null);

        var state = _resolver.Resolve(new BacklogExperienceInput(
            snapshot,
            new PersistedProcessingBacklogState(QueuedCount: 2, ProcessingCount: 1),
            null,
            NowUtc));

        Assert.Equal(BacklogExperienceKind.RefreshRequired, state.Kind);
        Assert.Equal(3, state.RemainingCount);
        Assert.DoesNotContain("QUEUED", state.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BacklogRecoveryAction.RefreshStatus, state.RecoveryAction);
    }

    [Fact]
    public void Resolve_Paused_By_Live_Recording_Protects_Capture_And_Leaves_Eta_Unavailable()
    {
        var snapshot = Snapshot(
            ProcessingQueueRunState.Paused,
            remaining: 2,
            stageName: null,
            stageState: null,
            currentEta: null,
            overallEta: null,
            pauseReason: ProcessingQueuePauseReason.LiveRecordingResponsiveMode);

        var state = _resolver.Resolve(new BacklogExperienceInput(snapshot, null, null, NowUtc));

        Assert.Equal(BacklogExperienceKind.Paused, state.Kind);
        Assert.Contains("live recording is protected", state.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Overall ETA unavailable while paused", state.OverallEtaText);
        Assert.False(state.HasAction);
    }

    [Theory]
    [InlineData(MeetingPrimaryRecommendationKind.RecoverTranscript, MeetingRecommendationActionTarget.MeetingDetails, "Failed", "RetryTranscript", "Review transcript recovery")]
    [InlineData(MeetingPrimaryRecommendationKind.ReviewMissingTranscript, MeetingRecommendationActionTarget.MeetingDetails, "Failed", "InspectSource", "Inspect source")]
    [InlineData(MeetingPrimaryRecommendationKind.Blocked, MeetingRecommendationActionTarget.SettingsSetup, "NeedsSetup", "OpenSetup", "Open setup")]
    [InlineData(MeetingPrimaryRecommendationKind.Blocked, MeetingRecommendationActionTarget.CheckAgain, "NeedsDecision", "ExplainUnrecoverableSource", "Review recovery")]
    [InlineData(MeetingPrimaryRecommendationKind.RepairSpeakerLabels, MeetingRecommendationActionTarget.MeetingDetails, "NeedsDecision", "RunSpeakerLabelsLater", "Review speaker labels")]
    [InlineData(MeetingPrimaryRecommendationKind.ReviewProcessing, MeetingRecommendationActionTarget.MeetingDetails, "NeedsDecision", "ProcessThisFirst", "Review processing priority")]
    public void Resolve_Failed_Recovery_Metadata_Maps_Each_Safe_Route(
        MeetingPrimaryRecommendationKind recommendationKind,
        MeetingRecommendationActionTarget recommendationTarget,
        string expectedKind,
        string expectedAction,
        string expectedLabel)
    {
        var recovery = new BacklogRecoveryMetadata(
            "2026-09-27_180000_teams_planning-review",
            "Planning review",
            true,
            recommendationKind,
            recommendationTarget,
            "Sanitized failure reason.",
            CanRetry: recommendationKind == MeetingPrimaryRecommendationKind.RecoverTranscript);

        var state = _resolver.Resolve(new BacklogExperienceInput(
            Snapshot(ProcessingQueueRunState.Idle, 0, null, null, null, null),
            null,
            recovery,
            NowUtc));

        Assert.Equal(expectedKind, state.Kind.ToString());
        Assert.Equal(expectedAction, state.RecoveryAction.ToString());
        Assert.Equal(expectedLabel, state.ActionLabel);
        Assert.True(state.HasAction);
        Assert.DoesNotContain("manifest", state.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("transcription", "RetryTranscript")]
    [InlineData("diarization", "RunSpeakerLabelsLater")]
    [InlineData("publish", "PublishTranscriptFirst")]
    public void Resolve_Failed_Live_Stage_Maps_A_Guided_Recovery_Action(string stageName, string expectedAction)
    {
        var snapshot = Snapshot(
            ProcessingQueueRunState.Processing,
            remaining: 1,
            stageName: stageName,
            stageState: StageExecutionState.Failed,
            currentEta: null,
            overallEta: null);

        var state = _resolver.Resolve(new BacklogExperienceInput(snapshot, null, null, NowUtc));

        Assert.Equal(BacklogExperienceKind.Failed, state.Kind);
        Assert.Equal(expectedAction, state.RecoveryAction.ToString());
        Assert.Equal(MeetingRecommendationActionTarget.MeetingDetails, state.ActionTarget);
        Assert.Contains("did not complete", state.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessingQueueStatusSnapshot Snapshot(
        ProcessingQueueRunState runState,
        int remaining,
        string? stageName,
        StageExecutionState? stageState,
        TimeSpan? currentEta,
        TimeSpan? overallEta,
        ProcessingQueuePauseReason pauseReason = ProcessingQueuePauseReason.None,
        DateTimeOffset? observedAtUtc = null,
        string? currentStageMessage = null) =>
        new(
            runState,
            pauseReason,
            QueuedCount: runState == ProcessingQueueRunState.Queued ? remaining : 0,
            TotalRemainingCount: remaining,
            CurrentManifestPath: "C:\\work\\planning\\manifest.json",
            CurrentTitle: "Planning review",
            CurrentPlatform: MeetingPlatform.Teams,
            CurrentStageName: stageName,
            CurrentStageState: stageState,
            CurrentStageUpdatedAtUtc: observedAtUtc ?? NowUtc,
            CurrentItemStartedAtUtc: NowUtc.AddMinutes(-3),
            CurrentItemEstimatedRemaining: currentEta,
            OverallEstimatedRemaining: overallEta,
            LastUpdatedAtUtc: observedAtUtc ?? NowUtc,
            CurrentStageMessage: currentStageMessage);
}
