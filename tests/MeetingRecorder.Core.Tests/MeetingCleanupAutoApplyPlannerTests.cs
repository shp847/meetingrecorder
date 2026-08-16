using MeetingRecorder.App;
using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupAutoApplyPlannerTests : IDisposable
{
    private readonly string _root;
    private readonly MeetingCleanupAutoApplyCacheService _cacheService;

    public MeetingCleanupAutoApplyPlannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _cacheService = new MeetingCleanupAutoApplyCacheService(
            Path.Combine(_root, "cache", "meeting-cleanup-auto-apply-v1.json"));
    }

    [Fact]
    public void GetEligibleRecommendations_Filters_To_Safe_Fixes_And_Suppresses_Cached_Failures()
    {
        var archive = CreateRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        var regenerate = CreateRecommendation(
            "retry-1",
            MeetingCleanupAction.RegenerateTranscript,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        var rename = CreateRecommendation(
            "rename-1",
            MeetingCleanupAction.Rename,
            MeetingCleanupConfidence.Medium,
            canApplyAutomatically: false);
        _cacheService.RecordFailure(regenerate.Fingerprint, DateTimeOffset.Parse("2026-07-15T12:00:00Z"), "Locked");

        var result = MeetingCleanupAutoApplyPlanner.GetEligibleRecommendations(
            new[] { archive, regenerate, rename },
            _cacheService);

        Assert.Single(result);
        Assert.Equal(archive.Fingerprint, result[0].Fingerprint);
    }

    [Fact]
    public void ShouldStartAutomaticApply_Requires_A_Current_Full_Refresh_With_Eligible_Recommendations()
    {
        Assert.False(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Fast,
            isRefreshCurrent: true,
            isShutdownRequested: false,
            isMeetingActionInProgress: false,
            isAutoApplyInProgress: false,
            eligibleRecommendationCount: 1));
        Assert.False(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Full,
            isRefreshCurrent: false,
            isShutdownRequested: false,
            isMeetingActionInProgress: false,
            isAutoApplyInProgress: false,
            eligibleRecommendationCount: 1));
        Assert.False(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Full,
            isRefreshCurrent: true,
            isShutdownRequested: false,
            isMeetingActionInProgress: true,
            isAutoApplyInProgress: false,
            eligibleRecommendationCount: 1));
        Assert.False(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Full,
            isRefreshCurrent: true,
            isShutdownRequested: false,
            isMeetingActionInProgress: false,
            isAutoApplyInProgress: true,
            eligibleRecommendationCount: 1));
        Assert.False(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Full,
            isRefreshCurrent: true,
            isShutdownRequested: false,
            isMeetingActionInProgress: false,
            isAutoApplyInProgress: false,
            eligibleRecommendationCount: 0));
        Assert.True(MeetingCleanupAutoApplyPlanner.ShouldStartAutomaticApply(
            MeetingRefreshMode.Full,
            isRefreshCurrent: true,
            isShutdownRequested: false,
            isMeetingActionInProgress: false,
            isAutoApplyInProgress: false,
            eligibleRecommendationCount: 1));
    }

    [Fact]
    public void Manual_Safe_Fix_Selection_Bypasses_Automatic_Suppression()
    {
        var repair = CreateRecommendation(
            "repair-1",
            MeetingCleanupAction.RepairSpeakerLabels,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        _cacheService.RecordFailure(repair.Fingerprint, DateTimeOffset.Parse("2026-07-15T12:00:00Z"), "Locked");

        var automaticResult = MeetingCleanupAutoApplyPlanner.GetEligibleRecommendations([repair], _cacheService);
        var manualResult = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations([repair]);

        Assert.Empty(automaticResult);
        Assert.Single(manualResult);
        Assert.Equal(repair.Fingerprint, manualResult[0].Fingerprint);
    }

    [Theory]
    [InlineData(MeetingCleanupAction.RegenerateTranscript, true)]
    [InlineData(MeetingCleanupAction.GenerateSpeakerLabels, true)]
    [InlineData(MeetingCleanupAction.RepairSpeakerLabels, true)]
    [InlineData(MeetingCleanupAction.Archive, false)]
    [InlineData(MeetingCleanupAction.Merge, false)]
    public void ShouldSuppressSuccessfulAutomaticApply_Only_For_Queue_Style_Actions(
        MeetingCleanupAction action,
        bool expected)
    {
        Assert.Equal(expected, MeetingCleanupAutoApplyPlanner.ShouldSuppressSuccessfulAutomaticApply(action));
    }

    [Fact]
    public void GetEligibleRecommendations_Suppresses_A_Previously_Queued_Success()
    {
        var generateLabels = CreateRecommendation(
            "speaker-labels-1",
            MeetingCleanupAction.GenerateSpeakerLabels,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        _cacheService.RecordQueuedSuccess(
            generateLabels.Fingerprint,
            DateTimeOffset.Parse("2026-07-16T04:15:00Z"));

        var automaticResult = MeetingCleanupAutoApplyPlanner.GetEligibleRecommendations(
            [generateLabels],
            _cacheService);
        var manualResult = MainWindowInteractionLogic.GetAutoApplicableMeetingCleanupRecommendations(
            [generateLabels]);

        Assert.Empty(automaticResult);
        Assert.Single(manualResult);
    }

    [Fact]
    public void BuildSchedulerStatus_Separates_Disabled_SpeakerLabels_From_Eligible_Cleanup()
    {
        var ledger = new MeetingCleanupWorkLedgerService(
            Path.Combine(_root, "cache", "meeting-cleanup-work-ledger-v1.json"));
        var archive = CreateRecommendation(
            "archive-1",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        var speakerLabels = CreateRecommendation(
            "labels-1",
            MeetingCleanupAction.GenerateSpeakerLabels,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        var manualReview = CreateRecommendation(
            "archive-manual",
            MeetingCleanupAction.Archive,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: true);
        var summary = CreateRecommendation(
            "summary-1",
            MeetingCleanupAction.GenerateSummary,
            MeetingCleanupConfidence.High,
            canApplyAutomatically: false);
        ledger.Record(manualReview.Fingerprint, CleanupWorkState.ManualReview, detail: "Needs confirmation");

        var status = MeetingCleanupAutoApplyPlanner.BuildSchedulerStatus(
            [archive, speakerLabels, manualReview, summary],
            ledger,
            recommendation => recommendation.Action == MeetingCleanupAction.Archive,
            recommendation => recommendation.Action == MeetingCleanupAction.GenerateSummary);

        Assert.Equal(3, status.SafeFixCount);
        Assert.Equal(1, status.EligibleNowCount);
        Assert.Equal(1, status.DisabledSpeakerLabelCount);
        Assert.Equal(1, status.BlockedSummaryCount);
        Assert.Equal(1, status.ManualReviewCount);
        Assert.Contains("speaker-label", status.PrimaryBlocker, StringComparison.Ordinal);
    }

    [Fact]
    public void GetNextAutomaticBatch_Keeps_At_Most_Five_Attempts_Per_Batch()
    {
        var recommendations = Enumerable.Range(1, 10)
            .Select(index => CreateRecommendation(
                $"labels-{index}",
                MeetingCleanupAction.GenerateSpeakerLabels,
                MeetingCleanupConfidence.High,
                canApplyAutomatically: true))
            .ToArray();
        var result = MeetingCleanupAutoApplyPlanner.GetNextAutomaticBatch(
            recommendations,
            _cacheService,
            automaticAttemptCount: 3);

        Assert.Equal(2, result.Count);
        Assert.Equal(["labels-1", "labels-2"], result.Select(item => item.Fingerprint));

        Assert.Empty(MeetingCleanupAutoApplyPlanner.GetNextAutomaticBatch(
            recommendations,
            _cacheService,
            automaticAttemptCount: 5));
    }

    [Fact]
    public void IsAutomaticBatchRefillDue_Requires_A_Previous_Batch_Cooldown_And_Idle_Queue()
    {
        var lastBatchStartedUtc = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var afterCooldown = lastBatchStartedUtc + MeetingCleanupAutoApplyPlanner.AutomaticBatchCooldown;

        Assert.False(MeetingCleanupAutoApplyPlanner.IsAutomaticBatchRefillDue(
            automaticAttemptCount: 0,
            lastBatchStartedUtc,
            afterCooldown,
            isProcessingQueueIdle: true));
        Assert.False(MeetingCleanupAutoApplyPlanner.IsAutomaticBatchRefillDue(
            automaticAttemptCount: 5,
            lastBatchStartedUtc,
            afterCooldown.AddSeconds(-1),
            isProcessingQueueIdle: true));
        Assert.False(MeetingCleanupAutoApplyPlanner.IsAutomaticBatchRefillDue(
            automaticAttemptCount: 5,
            lastBatchStartedUtc,
            afterCooldown,
            isProcessingQueueIdle: false));
        Assert.True(MeetingCleanupAutoApplyPlanner.IsAutomaticBatchRefillDue(
            automaticAttemptCount: 1,
            lastBatchStartedUtc,
            afterCooldown,
            isProcessingQueueIdle: true));
        Assert.True(MeetingCleanupAutoApplyPlanner.IsAutomaticBatchRefillDue(
            automaticAttemptCount: MeetingCleanupAutoApplyPlanner.MaxAutomaticFixesPerBatch,
            lastBatchStartedUtc,
            afterCooldown,
            isProcessingQueueIdle: true));
    }

    [Fact]
    public async Task BatchRunner_Continues_After_A_Failure_And_Reports_Per_Item_Results()
    {
        var appliedFingerprints = new List<string>();
        var first = CreateRecommendation("archive-1", MeetingCleanupAction.Archive, MeetingCleanupConfidence.High, true);
        var second = CreateRecommendation("archive-2", MeetingCleanupAction.Archive, MeetingCleanupConfidence.High, true);
        var third = CreateRecommendation("archive-3", MeetingCleanupAction.Archive, MeetingCleanupConfidence.High, true);

        var result = await MeetingCleanupRecommendationBatchRunner.ExecuteAsync(
            new[] { first, second, third },
            (recommendation, _) =>
            {
                appliedFingerprints.Add(recommendation.Fingerprint);
                if (string.Equals(recommendation.Fingerprint, second.Fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Simulated failure");
                }

                return Task.CompletedTask;
            },
            continueOnError: true,
            CancellationToken.None);

        Assert.Equal(new[] { first.Fingerprint, second.Fingerprint, third.Fingerprint }, appliedFingerprints);
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal("Simulated failure", Assert.Single(result.Items.Where(item => !item.Succeeded)).ErrorMessage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }
    }

    private static MeetingCleanupRecommendation CreateRecommendation(
        string fingerprint,
        MeetingCleanupAction action,
        MeetingCleanupConfidence confidence,
        bool canApplyAutomatically)
    {
        return new MeetingCleanupRecommendation(
            fingerprint,
            action,
            confidence,
            "Safe fix",
            "Description",
            "meeting-1",
            new[] { "meeting-1" },
            canApplyAutomatically,
            SuggestedTitle: null,
            SuggestedSplitPoint: null);
    }
}
