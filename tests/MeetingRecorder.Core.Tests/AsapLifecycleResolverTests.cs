using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AsapLifecycleResolverTests
{
    private readonly AsapLifecycleResolver _resolver = new();

    [Fact]
    public void Resolve_Transcript_Work_Retains_Asap_Priority()
    {
        var result = _resolver.Resolve(Input(
            transcription: StageExecutionState.Running,
            diarization: StageExecutionState.NotStarted,
            publish: StageExecutionState.NotStarted));

        Assert.Equal(AsapLifecycleState.TranscriptPending, result.State);
        Assert.Equal("ASAP: transcript", result.StatusText);
        Assert.True(result.RetainRequest);
        Assert.False(result.ShouldClearRequest);
    }

    [Fact]
    public void Resolve_Publish_After_Transcript_Retains_Asap_Priority()
    {
        var result = _resolver.Resolve(Input(
            transcription: StageExecutionState.Succeeded,
            diarization: StageExecutionState.NotStarted,
            publish: StageExecutionState.Running));

        Assert.Equal(AsapLifecycleState.Publishing, result.State);
        Assert.Equal("ASAP: publishing", result.StatusText);
        Assert.True(result.RetainRequest);
    }

    [Fact]
    public void Resolve_Published_Transcript_With_Eligible_Queued_Labels_Retains_Asap_Priority()
    {
        var result = _resolver.Resolve(Input(
            transcription: StageExecutionState.Succeeded,
            diarization: StageExecutionState.Queued,
            publish: StageExecutionState.Succeeded));

        Assert.Equal(AsapLifecycleState.SpeakerLabelsPending, result.State);
        Assert.Equal("ASAP: speaker labels remain", result.StatusText);
        Assert.True(result.RetainRequest);
        Assert.True(result.SpeakerLabelsRemain);
    }

    [Fact]
    public void Resolve_Intentionally_Deferred_Labels_Completes_Without_Claiming_Labels_Ran()
    {
        var result = _resolver.Resolve(Input(
            transcription: StageExecutionState.Succeeded,
            diarization: StageExecutionState.Skipped,
            publish: StageExecutionState.Succeeded,
            skipSpeakerLabeling: true));

        Assert.Equal(AsapLifecycleState.Complete, result.State);
        Assert.True(result.ShouldClearRequest);
        Assert.Contains("intentionally deferred", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Forced_Labels_With_Unavailable_Setup_Is_Ineligible_Not_Complete()
    {
        var result = _resolver.Resolve(Input(
            transcription: StageExecutionState.Succeeded,
            diarization: StageExecutionState.NotStarted,
            publish: StageExecutionState.Succeeded,
            forceSpeakerLabeling: true,
            speakerLabelingAvailable: false));

        Assert.Equal(AsapLifecycleState.Ineligible, result.State);
        Assert.True(result.ShouldClearRequest);
        Assert.DoesNotContain("complete", result.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Recoverable_Failure_Retains_Request_But_Terminal_Failure_Clears_It()
    {
        var recoverable = _resolver.Resolve(Input(
            state: SessionState.Failed,
            transcription: StageExecutionState.Failed,
            diarization: StageExecutionState.Skipped,
            publish: StageExecutionState.Skipped,
            failureRecoverable: true));
        var terminal = _resolver.Resolve(Input(
            state: SessionState.Failed,
            transcription: StageExecutionState.Failed,
            diarization: StageExecutionState.Skipped,
            publish: StageExecutionState.Skipped,
            failureRecoverable: false));

        Assert.Equal(AsapLifecycleState.TranscriptPending, recoverable.State);
        Assert.True(recoverable.RetainRequest);
        Assert.True(recoverable.RetryEligible);
        Assert.Equal(AsapLifecycleState.TerminalFailure, terminal.State);
        Assert.True(terminal.ShouldClearRequest);
    }

    [Fact]
    public void Resolve_Missing_Meeting_Is_Ineligible_And_Clears_Request()
    {
        var result = _resolver.Resolve(Input(state: null));

        Assert.Equal(AsapLifecycleState.Ineligible, result.State);
        Assert.True(result.ShouldClearRequest);
        Assert.Contains("no longer available", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static AsapLifecycleInput Input(
        SessionState? state = SessionState.Processing,
        StageExecutionState transcription = StageExecutionState.Succeeded,
        StageExecutionState diarization = StageExecutionState.Succeeded,
        StageExecutionState publish = StageExecutionState.Succeeded,
        bool skipSpeakerLabeling = false,
        bool forceSpeakerLabeling = false,
        bool speakerLabelingAvailable = true,
        bool failureRecoverable = false) =>
        new(
            state,
            transcription,
            diarization,
            publish,
            skipSpeakerLabeling,
            forceSpeakerLabeling,
            true,
            speakerLabelingAvailable,
            failureRecoverable);
}
