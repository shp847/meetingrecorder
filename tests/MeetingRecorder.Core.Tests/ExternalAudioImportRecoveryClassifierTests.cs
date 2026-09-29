using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportRecoveryClassifierTests
{
    [Theory]
    [InlineData(ExternalAudioImportJobState.Queued, true, ExternalAudioImportRecoveryDisposition.ResumeQueued)]
    [InlineData(ExternalAudioImportJobState.Queued, false, ExternalAudioImportRecoveryDisposition.ManualRecovery)]
    [InlineData(ExternalAudioImportJobState.Processing, true, ExternalAudioImportRecoveryDisposition.ResumeProcessing)]
    [InlineData(ExternalAudioImportJobState.Processing, false, ExternalAudioImportRecoveryDisposition.ManualRecovery)]
    [InlineData(ExternalAudioImportJobState.Failed, true, ExternalAudioImportRecoveryDisposition.RetryFromStagedWork)]
    [InlineData(ExternalAudioImportJobState.Failed, false, ExternalAudioImportRecoveryDisposition.ManualRecovery)]
    [InlineData(ExternalAudioImportJobState.BlockedBySetup, true, ExternalAudioImportRecoveryDisposition.BlockedBySetup)]
    [InlineData(ExternalAudioImportJobState.Removed, true, ExternalAudioImportRecoveryDisposition.Removed)]
    [InlineData(ExternalAudioImportJobState.PendingReview, true, ExternalAudioImportRecoveryDisposition.AwaitUser)]
    public void Classify_Maps_Durable_State_And_Staged_Truth(
        ExternalAudioImportJobState state,
        bool hasVerifiedStagedInput,
        ExternalAudioImportRecoveryDisposition expected)
    {
        var decision = ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
            Job(state),
            CompatibleSchema,
            SessionState.Queued,
            hasVerifiedStagedInput,
            HasCurrentPublishedArtifacts: false));

        Assert.Equal(expected, decision.Disposition);
        Assert.Equal(
            expected is ExternalAudioImportRecoveryDisposition.ResumeQueued or ExternalAudioImportRecoveryDisposition.ResumeProcessing,
            decision.MayEnqueue);
    }

    [Fact]
    public void Classify_Published_Manifest_And_Current_Artifacts_Win_A_Crash_After_Publish_Checkpoint()
    {
        var decision = ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
            Job(ExternalAudioImportJobState.Processing),
            CompatibleSchema,
            SessionState.Published,
            HasVerifiedStagedInput: true,
            HasCurrentPublishedArtifacts: true));

        Assert.Equal(ExternalAudioImportRecoveryDisposition.Published, decision.Disposition);
        Assert.False(decision.MayEnqueue);
        Assert.Contains("not backlog", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Published_Job_With_Missing_Artifacts_Requires_Repair()
    {
        var decision = ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
            Job(ExternalAudioImportJobState.Published),
            CompatibleSchema,
            SessionState.Published,
            HasVerifiedStagedInput: false,
            HasCurrentPublishedArtifacts: false));

        Assert.Equal(ExternalAudioImportRecoveryDisposition.ManualRecovery, decision.Disposition);
        Assert.Contains("need repair", decision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_Future_Or_ReadOnly_Record_Requires_Manual_Recovery()
    {
        var future = Job(ExternalAudioImportJobState.Queued) with
        {
            SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion + 1,
            IsReadOnly = true,
        };

        var decision = ExternalAudioImportRecoveryClassifier.Classify(new ExternalAudioImportRecoverySnapshot(
            future,
            new ExternalAudioImportJobSchemaCompatibility(CanRead: false, CanWrite: false, "Future schema."),
            SessionState.Queued,
            HasVerifiedStagedInput: true,
            HasCurrentPublishedArtifacts: false));

        Assert.Equal(ExternalAudioImportRecoveryDisposition.ManualRecovery, decision.Disposition);
        Assert.False(decision.MayEnqueue);
    }

    private static ExternalAudioImportJobSchemaCompatibility CompatibleSchema { get; } = new(
        CanRead: true,
        CanWrite: true,
        RecoveryText: string.Empty);

    private static ExternalAudioImportJob Job(ExternalAudioImportJobState state) => new()
    {
        SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion,
        JobId = Guid.NewGuid(),
        SessionId = "session-1",
        State = state,
        QueueIntent = ExternalAudioImportQueueIntent.UserRequested,
    };
}
