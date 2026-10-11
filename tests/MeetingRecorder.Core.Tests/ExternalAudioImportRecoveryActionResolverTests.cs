using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportRecoveryActionResolverTests
{
    [Fact]
    public void Resolve_FailedImportWithVerifiedStage_PrefersRetryFromWorkAndNeverDeletesSource()
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.Failed,
            hasVerifiedStagedInput: true,
            hasSafeOriginalSource: true));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.RetryFromStagedWork, plan.PrimaryAction.Kind);
        Assert.True(plan.PrimaryAction.IsEnabled);
        Assert.Contains("staged", plan.PrimaryAction.Consequence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ExternalAudioImportRecoveryActionKind.RemoveDraft,
            Assert.Single(plan.Actions.Where(action => action.Kind == ExternalAudioImportRecoveryActionKind.RemoveDraft)).Kind);
        Assert.Contains("original file stays", plan.PrimaryAction.Consequence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deletes the original", plan.PrimaryAction.Consequence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_FailedImportWithoutStageButCurrentSource_OffersReobserveOriginal()
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.Failed,
            hasVerifiedStagedInput: false,
            hasSafeOriginalSource: true));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.RetryFromOriginal, plan.PrimaryAction.Kind);
        Assert.True(plan.PrimaryAction.IsEnabled);
        Assert.Contains("recheck", plan.PrimaryAction.Consequence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Actions, action => action.Kind == ExternalAudioImportRecoveryActionKind.OpenSourceLocation && action.IsEnabled);
    }

    [Fact]
    public void Resolve_MissingStageAndSource_ExplainsReplacementIsTheOnlyRecovery()
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.Failed,
            hasVerifiedStagedInput: false,
            hasSafeOriginalSource: false));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.ReplaceSource, plan.PrimaryAction.Kind);
        Assert.True(plan.PrimaryAction.IsEnabled);
        Assert.Contains("replacement", plan.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Actions, action =>
            action.Kind == ExternalAudioImportRecoveryActionKind.RetryFromOriginal &&
            !action.IsEnabled &&
            action.DisabledReason!.Contains("original", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SetupBlock_OnlyRoutesToSetupAndDoesNotPromiseQueueing()
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.BlockedBySetup,
            hasVerifiedStagedInput: true,
            hasSafeOriginalSource: true));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.OpenSetup, plan.PrimaryAction.Kind);
        Assert.True(plan.PrimaryAction.IsEnabled);
        Assert.DoesNotContain(plan.Actions, action => action.Kind == ExternalAudioImportRecoveryActionKind.RetryFromStagedWork && action.IsEnabled);
    }

    [Theory]
    [InlineData(ExternalAudioImportJobState.Changing)]
    [InlineData(ExternalAudioImportJobState.SourceMissing)]
    public void Resolve_ChangedOrMissingSource_OffersReplacementInsteadOfAStaleRetry(ExternalAudioImportJobState state)
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            state,
            hasVerifiedStagedInput: false,
            hasSafeOriginalSource: false));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.ReplaceSource, plan.PrimaryAction.Kind);
        Assert.True(plan.PrimaryAction.IsEnabled);
    }

    [Fact]
    public void Resolve_ReadOnlyOrPublishedImport_ExposesNoMutatingImportAction()
    {
        var readOnly = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.Failed,
            hasVerifiedStagedInput: true,
            hasSafeOriginalSource: true,
            canWrite: false));
        var published = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.Published,
            hasVerifiedStagedInput: false,
            hasSafeOriginalSource: true,
            hasCurrentPublishedArtifacts: true));

        Assert.Equal(ExternalAudioImportRecoveryActionKind.None, readOnly.PrimaryAction.Kind);
        Assert.All(readOnly.Actions, action => Assert.False(action.IsEnabled));
        Assert.Equal(ExternalAudioImportRecoveryActionKind.None, published.PrimaryAction.Kind);
        Assert.Empty(published.Actions);
    }

    [Fact]
    public void Resolve_RemoveDraftHasNamedConsequenceAndRequiresConfirmation()
    {
        var plan = ExternalAudioImportRecoveryActionResolver.Resolve(Snapshot(
            ExternalAudioImportJobState.PendingReview,
            hasVerifiedStagedInput: false,
            hasSafeOriginalSource: true));

        var remove = Assert.Single(plan.Actions.Where(action => action.Kind == ExternalAudioImportRecoveryActionKind.RemoveDraft));
        Assert.True(remove.IsEnabled);
        Assert.True(remove.RequiresConfirmation);
        Assert.Contains("staged copy", remove.Consequence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original file stays", remove.Consequence, StringComparison.OrdinalIgnoreCase);
    }

    private static ExternalAudioImportRecoveryActionSnapshot Snapshot(
        ExternalAudioImportJobState state,
        bool hasVerifiedStagedInput,
        bool hasSafeOriginalSource,
        bool canWrite = true,
        bool hasCurrentPublishedArtifacts = false) => new(
            new ExternalAudioImportJob
            {
                SchemaVersion = ExternalAudioImportJob.CurrentSchemaVersion,
                JobId = Guid.NewGuid(),
                SessionId = "session-1",
                State = state,
                QueueIntent = ExternalAudioImportQueueIntent.UserRequested,
            },
            new ExternalAudioImportJobSchemaCompatibility(
                CanRead: true,
                CanWrite: canWrite,
                RecoveryText: canWrite ? string.Empty : "Update Meeting Recorder before changing this import record."),
            hasVerifiedStagedInput,
            hasSafeOriginalSource,
            hasCurrentPublishedArtifacts);
}
