using MeetingRecorder.App;
using MeetingRecorder.App.Services;
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
