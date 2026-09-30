using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingRecommendationResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 15, 0, 0, TimeSpan.Zero);
    private readonly MeetingRecommendationResolver _resolver = new();

    [Fact]
    public void Resolve_Prefers_Recoverable_Transcript_Failure_Over_All_Lower_Work()
    {
        var result = _resolver.Resolve(Input(
            sessionState: SessionState.Failed,
            transcript: MeetingRecommendationAvailability.Missing,
            cleanupRecommendations: [Cleanup()],
            summaryRetryAvailable: true,
            metadataPolishAvailable: true), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.RecoverTranscript, result.Kind);
        Assert.Equal(MeetingRecommendationActionTarget.MeetingDetails, result.ActionTarget);
        Assert.True(result.HasPrimaryAction);
    }

    [Theory]
    [InlineData(MeetingRecommendationAvailability.Missing, MeetingRecommendationActionTarget.SettingsSetup)]
    [InlineData(MeetingRecommendationAvailability.Disabled, MeetingRecommendationActionTarget.SettingsSetup)]
    [InlineData(MeetingRecommendationAvailability.Unknown, MeetingRecommendationActionTarget.CheckAgain)]
    [InlineData(MeetingRecommendationAvailability.Stale, MeetingRecommendationActionTarget.CheckAgain)]
    public void Resolve_Blocks_Recovery_When_Local_Setup_Is_Not_Confirmed(
        MeetingRecommendationAvailability setup,
        MeetingRecommendationActionTarget target)
    {
        var result = _resolver.Resolve(Input(
            sessionState: SessionState.Failed,
            transcript: MeetingRecommendationAvailability.Missing,
            localTranscriptionSetup: setup), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.Blocked, result.Kind);
        Assert.Equal(MeetingRecommendationSeverity.High, result.Severity);
        Assert.Equal(target, result.ActionTarget);
        Assert.NotNull(result.BlockReason);
        Assert.NotEmpty(result.BlockReason!);
    }

    [Fact]
    public void Resolve_Does_Not_Fall_Through_When_A_Failed_Transcript_Has_No_Source()
    {
        var result = _resolver.Resolve(Input(
            sessionState: SessionState.Failed,
            transcript: MeetingRecommendationAvailability.Missing,
            recoverableSource: MeetingRecommendationAvailability.Missing,
            cleanupRecommendations: [Cleanup()],
            summaryRetryAvailable: true), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.Blocked, result.Kind);
        Assert.Equal(MeetingRecommendationActionTarget.CheckAgain, result.ActionTarget);
        Assert.DoesNotContain("cleanup", result.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Prefers_Suspicious_Speaker_Labels_Over_Cleanup_And_Summary()
    {
        var result = _resolver.Resolve(Input(
            hasSuspiciousSpeakerLabels: true,
            cleanupRecommendations: [Cleanup()],
            summaryRetryAvailable: true), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.RepairSpeakerLabels, result.Kind);
        Assert.Equal(MeetingRecommendationActionTarget.MeetingDetails, result.ActionTarget);
    }

    [Fact]
    public void Resolve_Prefers_Missing_Transcript_Artifact_Over_Processing_And_Cleanup()
    {
        var result = _resolver.Resolve(Input(
            transcriptArtifact: MeetingRecommendationAvailability.Missing,
            processing: MeetingRecommendationProcessingState.Queued,
            cleanupRecommendations: [Cleanup()]), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.ReviewMissingTranscript, result.Kind);
        Assert.Equal(MeetingRecommendationSeverity.High, result.Severity);
    }

    [Fact]
    public void Resolve_Uses_One_Safe_Cleanup_Review_Route_Instead_Of_Applying_The_Cleanup()
    {
        var result = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup(MeetingCleanupAction.Archive)]), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.ReviewCleanup, result.Kind);
        Assert.Equal(MeetingRecommendationActionTarget.CleanupReview, result.ActionTarget);
        Assert.True(result.IsActionEligible);
        Assert.DoesNotContain("archive", result.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Reports_Evaluating_For_Stale_Or_Unknown_Metadata_Never_Complete()
    {
        var stale = _resolver.Resolve(Input(isSnapshotStale: true), Now);
        var unknown = _resolver.Resolve(Input(transcript: MeetingRecommendationAvailability.Unknown), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.Evaluating, stale.Kind);
        Assert.Equal(MeetingPrimaryRecommendationKind.Evaluating, unknown.Kind);
        Assert.NotEqual(MeetingPrimaryRecommendationKind.NoActionNeeded, stale.Kind);
        Assert.NotEqual(MeetingPrimaryRecommendationKind.NoActionNeeded, unknown.Kind);
    }

    [Fact]
    public void Resolve_Reports_Healthy_Meeting_As_Complete()
    {
        var result = _resolver.Resolve(Input(), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.NoActionNeeded, result.Kind);
        Assert.Equal("Complete — no action needed", result.Label);
        Assert.False(result.HasPrimaryAction);
    }

    [Fact]
    public void Resolve_Does_Not_Promote_Stale_State_Over_A_Failure_Or_Cleanup_Candidate()
    {
        var input = Input(
            sessionState: SessionState.Failed,
            transcript: MeetingRecommendationAvailability.Missing,
            cleanupRecommendations: [Cleanup()]) with
        {
            PresentationState = MeetingPresentationState.RefreshRequired,
        };

        var result = _resolver.Resolve(input, Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.Evaluating, result.Kind);
        Assert.False(result.HasPrimaryAction);
        Assert.Equal("stale-status", result.ReasonCode);
        Assert.Equal(MeetingRecommendationScope.None, result.Scope);
        Assert.Equal(MeetingRecommendationFreshness.RefreshRequired, result.Freshness);
    }

    [Fact]
    public void Resolve_Does_Not_Promote_An_Archived_Meeting()
    {
        var result = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]) with
        {
            PresentationState = MeetingPresentationState.Archived,
        }, Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.NoActionNeeded, result.Kind);
        Assert.Equal("archived", result.ReasonCode);
        Assert.False(result.HasPrimaryAction);
        Assert.Equal(MeetingRecommendationScope.None, result.Scope);
    }

    [Fact]
    public void Resolve_Ranks_Summary_Retry_Above_Cleanup_And_Uses_A_Single_Meeting_Scope()
    {
        var result = _resolver.Resolve(Input(
            summaryRetryAvailable: true,
            cleanupRecommendations: [Cleanup(MeetingCleanupAction.Archive)]), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.RetrySummary, result.Kind);
        Assert.Equal("summary-retry", result.ReasonCode);
        Assert.Equal(MeetingRecommendationScope.SingleMeeting, result.Scope);
    }

    [Fact]
    public void Resolve_Uses_Highest_Confidence_Cleanup_Then_Stable_Action_And_Fingerprint_Tie_Breakers()
    {
        var low = Cleanup(MeetingCleanupAction.Rename) with { Confidence = MeetingCleanupConfidence.Low, Fingerprint = "z" };
        var high = Cleanup(MeetingCleanupAction.Archive) with { Confidence = MeetingCleanupConfidence.High, Fingerprint = "a" };

        var first = _resolver.Resolve(Input(cleanupRecommendations: [low, high]), Now);
        var second = _resolver.Resolve(Input(cleanupRecommendations: [high, low]), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.ReviewCleanup, first.Kind);
        Assert.Equal(first.SnapshotFingerprint, second.SnapshotFingerprint);
        Assert.Equal(MeetingRecommendationScope.CleanupReview, first.Scope);
    }

    [Fact]
    public void Resolve_Fingerprint_Changes_When_The_Input_Revision_Changes()
    {
        var initial = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]), Now);
        var revised = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]) with { SnapshotVersion = 8 }, Now);

        Assert.NotEqual(initial.SnapshotFingerprint, revised.SnapshotFingerprint);
    }

    [Fact]
    public void Resolve_Dismisses_Only_The_Same_Low_Risk_Fingerprint_And_Reappears_On_Change_Or_Expiry()
    {
        var original = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]), Now);
        var dismissed = new MeetingRecommendationDismissal(
            original.SnapshotFingerprint,
            original.RecommendationVersion,
            Now.AddMinutes(-1));

        var hidden = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()], dismissals: [dismissed]), Now);
        var changed = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup(MeetingCleanupAction.Merge)], dismissals: [dismissed]), Now);
        var expired = _resolver.Resolve(Input(
            cleanupRecommendations: [Cleanup()],
            dismissals: [dismissed with { DismissedAtUtc = Now - MeetingRecommendationResolver.DismissalLifetime - TimeSpan.FromSeconds(1) }]), Now);

        Assert.True(hidden.IsDismissed);
        Assert.False(changed.IsDismissed);
        Assert.False(expired.IsDismissed);
    }

    [Fact]
    public void Resolve_Never_Allows_Dismissal_To_Hide_A_Failure()
    {
        var cleanup = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]), Now);
        var result = _resolver.Resolve(Input(
            sessionState: SessionState.Failed,
            transcript: MeetingRecommendationAvailability.Missing,
            dismissals: [new MeetingRecommendationDismissal(cleanup.SnapshotFingerprint, cleanup.RecommendationVersion, Now)]), Now);

        Assert.Equal(MeetingPrimaryRecommendationKind.RecoverTranscript, result.Kind);
        Assert.False(result.IsDismissed);
    }

    [Fact]
    public void Resolve_Produces_A_Stable_Fingerprint_Without_Meeting_Content_Or_Provider_Inputs()
    {
        var first = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]), Now);
        var second = _resolver.Resolve(Input(cleanupRecommendations: [Cleanup()]), Now.AddHours(1));
        var inputProperties = typeof(MeetingRecommendationInput).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(first.SnapshotFingerprint, second.SnapshotFingerprint);
        Assert.DoesNotContain(inputProperties, name => name.Contains("Text", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(inputProperties, name => name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    private static MeetingRecommendationInput Input(
        SessionState? sessionState = null,
        MeetingRecommendationAvailability recoverableSource = MeetingRecommendationAvailability.Available,
        MeetingRecommendationAvailability localTranscriptionSetup = MeetingRecommendationAvailability.Available,
        MeetingRecommendationAvailability transcript = MeetingRecommendationAvailability.Available,
        MeetingRecommendationAvailability transcriptArtifact = MeetingRecommendationAvailability.Available,
        MeetingRecommendationAvailability speakerRepair = MeetingRecommendationAvailability.Available,
        bool hasSuspiciousSpeakerLabels = false,
        MeetingRecommendationProcessingState processing = MeetingRecommendationProcessingState.Idle,
        bool summaryRetryAvailable = false,
        bool metadataPolishAvailable = false,
        IReadOnlyList<MeetingCleanupRecommendation>? cleanupRecommendations = null,
        IReadOnlyList<MeetingRecommendationDismissal>? dismissals = null,
        bool isSnapshotStale = false) =>
        new(
            "meeting-1",
            SnapshotVersion: 7,
            SnapshotObservedAtUtc: Now.AddMinutes(-1),
            isSnapshotStale,
            sessionState,
            recoverableSource,
            localTranscriptionSetup,
            transcript,
            transcriptArtifact,
            speakerRepair,
            hasSuspiciousSpeakerLabels,
            processing,
            summaryRetryAvailable,
            metadataPolishAvailable,
            cleanupRecommendations,
            dismissals);

    private static MeetingCleanupRecommendation Cleanup(MeetingCleanupAction action = MeetingCleanupAction.Rename) =>
        new(
            $"cleanup-{action}",
            action,
            MeetingCleanupConfidence.Medium,
            "Review title cleanup",
            "Metadata can be improved.",
            "meeting-1",
            ["meeting-1"],
            CanApplyAutomatically: false,
            SuggestedTitle: null,
            SuggestedSplitPoint: null,
            ReasonCode: "test-cleanup");
}
