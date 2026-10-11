using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// The durable lifecycle of one explicit ASAP request. It is derived from a
/// manifest snapshot and is never persisted separately from that manifest.
/// </summary>
internal enum AsapLifecycleState
{
    TranscriptPending = 0,
    Publishing = 1,
    SpeakerLabelsPending = 2,
    Complete = 3,
    Ineligible = 4,
    TerminalFailure = 5,
}

internal sealed record AsapLifecycleInput(
    SessionState? SessionState,
    StageExecutionState TranscriptionState,
    StageExecutionState DiarizationState,
    StageExecutionState PublishState,
    bool SkipSpeakerLabeling,
    bool ForceSpeakerLabeling,
    bool HasRecoverableSource,
    bool IsSpeakerLabelingAvailable,
    bool IsFailureRecoverable);

internal sealed record AsapLifecycleEvaluation(
    AsapLifecycleState State,
    string StatusText,
    string Reason,
    bool RetainRequest,
    bool ShouldClearRequest,
    bool SpeakerLabelsRemain,
    bool RetryEligible);

/// <summary>
/// Maps already-known stage metadata to the only states allowed to retain an
/// ASAP request. Unknown or unavailable label setup cannot be called success.
/// </summary>
internal sealed class AsapLifecycleResolver
{
    public AsapLifecycleEvaluation Resolve(AsapLifecycleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.SessionState is null)
        {
            return Ineligible("The meeting record is no longer available.");
        }

        if (input.SessionState == SessionState.Failed)
        {
            return input.IsFailureRecoverable && input.HasRecoverableSource
                ? new AsapLifecycleEvaluation(
                    AsapLifecycleState.TranscriptPending,
                    "ASAP: transcript recovery pending",
                    "The meeting can be reviewed for transcript recovery before priority continues.",
                    RetainRequest: true,
                    ShouldClearRequest: false,
                    SpeakerLabelsRemain: false,
                    RetryEligible: true)
                : new AsapLifecycleEvaluation(
                    AsapLifecycleState.TerminalFailure,
                    "ASAP ended: terminal failure",
                    "The meeting cannot continue automatically from its current terminal failure.",
                    RetainRequest: false,
                    ShouldClearRequest: true,
                    SpeakerLabelsRemain: false,
                    RetryEligible: false);
        }

        if (input.TranscriptionState is not (StageExecutionState.Succeeded or StageExecutionState.Skipped))
        {
            return input.HasRecoverableSource || input.TranscriptionState != StageExecutionState.Failed
                ? new AsapLifecycleEvaluation(
                    AsapLifecycleState.TranscriptPending,
                    "ASAP: transcript",
                    "Transcript work still needs to reach a terminal result.",
                    RetainRequest: true,
                    ShouldClearRequest: false,
                    SpeakerLabelsRemain: false,
                    RetryEligible: input.TranscriptionState == StageExecutionState.Failed)
                : Ineligible("No usable source remains for transcript processing.");
        }

        if (input.PublishState is not (StageExecutionState.Succeeded or StageExecutionState.Skipped))
        {
            return new AsapLifecycleEvaluation(
                AsapLifecycleState.Publishing,
                "ASAP: publishing",
                "The transcript is ready, but publication has not reached a terminal result.",
                RetainRequest: true,
                ShouldClearRequest: false,
                SpeakerLabelsRemain: false,
                RetryEligible: input.PublishState == StageExecutionState.Failed);
        }

        var labelsRequired = input.ForceSpeakerLabeling || !input.SkipSpeakerLabeling;
        if (!labelsRequired)
        {
            return Complete("Speaker labeling was intentionally deferred for this meeting.");
        }

        if (input.DiarizationState == StageExecutionState.Succeeded)
        {
            return Complete("Transcript publication and eligible speaker labeling reached terminal results.");
        }

        if (input.DiarizationState == StageExecutionState.Skipped)
        {
            return input.ForceSpeakerLabeling
                ? Ineligible("The requested speaker-labeling pass could not run for this meeting.")
                : Complete("Speaker labeling reached an intentional terminal skip for this meeting.");
        }

        if (!input.IsSpeakerLabelingAvailable)
        {
            return Ineligible("Speaker labeling is unavailable for this meeting, so priority cannot continue to labels.");
        }

        if (input.DiarizationState is StageExecutionState.NotStarted or StageExecutionState.Queued or StageExecutionState.Running)
        {
            return new AsapLifecycleEvaluation(
                AsapLifecycleState.SpeakerLabelsPending,
                "ASAP: speaker labels remain",
                "Transcript publication is complete; eligible speaker labeling still needs a terminal result.",
                RetainRequest: true,
                ShouldClearRequest: false,
                SpeakerLabelsRemain: true,
                RetryEligible: false);
        }

        if (input.DiarizationState == StageExecutionState.Failed)
        {
            return new AsapLifecycleEvaluation(
                AsapLifecycleState.TerminalFailure,
                "ASAP ended: speaker labels failed",
                "Speaker labeling reached a terminal failure and needs explicit review before another attempt.",
                RetainRequest: false,
                ShouldClearRequest: true,
                SpeakerLabelsRemain: false,
                RetryEligible: false);
        }

        return Complete("Transcript publication and eligible speaker labeling reached terminal results.");
    }

    public AsapLifecycleEvaluation Resolve(MeetingSessionManifest? manifest, bool isSpeakerLabelingAvailable, bool isFailureRecoverable)
    {
        if (manifest is null)
        {
            return Resolve(new AsapLifecycleInput(
                null,
                StageExecutionState.NotStarted,
                StageExecutionState.NotStarted,
                StageExecutionState.NotStarted,
                SkipSpeakerLabeling: false,
                ForceSpeakerLabeling: false,
                HasRecoverableSource: false,
                isSpeakerLabelingAvailable,
                isFailureRecoverable));
        }

        var overrides = manifest.ProcessingOverrides;
        return Resolve(new AsapLifecycleInput(
            manifest.State,
            manifest.TranscriptionStatus.State,
            manifest.DiarizationStatus.State,
            manifest.PublishStatus.State,
            overrides?.SkipSpeakerLabeling == true,
            overrides?.ForceSpeakerLabeling == true,
            HasRecoverableSource(manifest),
            isSpeakerLabelingAvailable,
            isFailureRecoverable));
    }

    private static AsapLifecycleEvaluation Complete(string reason) =>
        new(
            AsapLifecycleState.Complete,
            "ASAP complete",
            reason,
            RetainRequest: false,
            ShouldClearRequest: true,
            SpeakerLabelsRemain: false,
            RetryEligible: false);

    private static AsapLifecycleEvaluation Ineligible(string reason) =>
        new(
            AsapLifecycleState.Ineligible,
            "ASAP unavailable",
            reason,
            RetainRequest: false,
            ShouldClearRequest: true,
            SpeakerLabelsRemain: false,
            RetryEligible: false);

    private static bool HasRecoverableSource(MeetingSessionManifest manifest) =>
        !string.IsNullOrWhiteSpace(manifest.MergedAudioPath) ||
        manifest.RawChunkPaths.Count > 0 ||
        manifest.LoopbackCaptureSegments.Any(segment => segment.ChunkPaths.Count > 0) ||
        manifest.MicrophoneCaptureSegments.Any(segment => segment.ChunkPaths.Count > 0) ||
        !string.IsNullOrWhiteSpace(manifest.ImportedSourceAudio?.OriginalPath);
}
