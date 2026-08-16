using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupAutoApplySourceTests
{
    [Fact]
    public void Full_Cleanup_Refresh_Triggers_Automatic_Safe_Fixes_Only_From_The_Background_Recommendation_Path()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task RunMeetingCleanupRecommendationRefreshAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void StartMeetingAttendeeBackfillRefresh", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        var publishIndex = methodBlock.IndexOf("ApplyMeetingRowsUpdate(records, _meetingCleanupRecommendations, preserveEditorDrafts: true);", StringComparison.Ordinal);
        var autoApplyIndex = methodBlock.IndexOf("await TryAutoApplyMeetingCleanupSafeFixesAsync(", StringComparison.Ordinal);

        Assert.True(publishIndex >= 0, "Expected cleanup refresh to publish the visible recommendations.");
        Assert.True(autoApplyIndex > publishIndex, "Automatic safe fixes should start only after recommendation rows are published.");
        Assert.Contains("Dispatcher.InvokeAsync", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("SeedMeetingCleanupAutoApplySuppressionFromPriorAttempts", methodBlock, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(methodBlock, "await TryAutoApplyMeetingCleanupSafeFixesAsync("));
    }

    [Fact]
    public void Automatic_Safe_Fix_Cache_Does_Not_Reuse_The_Legacy_Seeded_V1_File()
    {
        var sourcePath = GetPath(
            "src",
            "MeetingRecorder.App",
            "Services",
            "MeetingCleanupAutoApplyCacheService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("meeting-cleanup-auto-apply-v2.json", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Automatic_Queue_Style_Work_Is_Persisted_In_Ledger_Until_Worker_Completion()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task ExecuteAutomaticMeetingCleanupRecommendationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string BuildAutomaticMeetingCleanupApplyStatusText", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        var executionIndex = methodBlock.IndexOf("await ExecuteMeetingCleanupRecommendationAsync(", StringComparison.Ordinal);

        Assert.Contains("ShouldSuppressSuccessfulAutomaticApply(recommendation.Action)", methodBlock, StringComparison.Ordinal);
        Assert.True(executionIndex >= 0, "Queue-style automatic fixes should execute through the shared queue path.");
        Assert.Contains("_meetingCleanupWorkLedgerService.Record(", methodBlock, StringComparison.Ordinal);
        Assert.Contains("CleanupWorkState.Failed", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordQueuedSuccess(", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Scheduler_Uses_A_Persisted_Ledger_And_Priority_Queue()
    {
        var mainWindow = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));
        var queue = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "Services", "ProcessingQueueService.cs"));

        Assert.Contains("MeetingCleanupWorkLedgerService", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ProcessingQueue_OnWorkCompleted", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ProcessingWorkPriority.Cleanup", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SelectFairQueueIndexLocked", queue, StringComparison.Ordinal);
        Assert.Contains("IsOvernightDrainWindowActive", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void Automatic_Safe_Fix_Batch_Reuses_One_Catalog_Snapshot()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task TryAutoApplyMeetingCleanupSafeFixesAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task ExecuteAutomaticMeetingCleanupRecommendationAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("var meetingsByStem = records.ToDictionary(", methodBlock, StringComparison.Ordinal);
        Assert.Contains("meetingsByStem,", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("_meetingOutputCatalogService.ListMeetings(", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_Meeting_Refresh_Preserves_Previous_Cleanup_Recommendations_Until_Background_Scan_Completes()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task RefreshMeetingListAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void StartMeetingCleanupRecommendationRefresh", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];
        var successStart = methodBlock.IndexOf("var records = await Task.Run(", StringComparison.Ordinal);
        var successEnd = methodBlock.IndexOf("catch (OperationCanceledException)", successStart, StringComparison.Ordinal);
        var successBlock = methodBlock[successStart..successEnd];

        Assert.DoesNotContain(
            "_meetingCleanupRecommendations = Array.Empty<MeetingCleanupRecommendation>();",
            successBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "_allMeetingRows = BuildMeetingRows(records, _meetingCleanupRecommendations);",
            successBlock,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Review_Banner_Reserves_Layout_Space_While_Background_Scan_Loads()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var source = File.ReadAllText(sourcePath);
        var bannerStart = xaml.IndexOf("x:Name=\"MeetingCleanupReviewBannerBorder\"", StringComparison.Ordinal);
        var bannerEnd = xaml.IndexOf("</Border>", bannerStart, StringComparison.Ordinal);
        var bannerBlock = xaml[bannerStart..bannerEnd];
        var methodStart = source.IndexOf("private void UpdateMeetingCleanupReviewBanner", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task TryAutoApplyMeetingCleanupSafeFixesAsync", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("Visibility=\"Hidden\"", bannerBlock, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"88\"", bannerBlock, StringComparison.Ordinal);
        Assert.Contains("MeetingCleanupReviewBannerBorder.Visibility = Visibility.Hidden;", methodBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("MeetingCleanupReviewBannerBorder.Visibility = Visibility.Collapsed;", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Scheduler_Uses_A_Narrow_Blocker_And_Reports_Plan_Disabled_Work()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));

        Assert.Contains("private bool IsAutomaticCleanupSchedulerBlocked()", source, StringComparison.Ordinal);
        Assert.Contains("IsAutomaticCleanupSchedulerBlocked()", source, StringComparison.Ordinal);
        Assert.Contains("GetCleanupSchedulerStatus()", source, StringComparison.Ordinal);
        Assert.Contains("disabledLabels=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsMeetingActionInProgress() ||\r\n            outstandingCleanupCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Execution_Offloads_The_Catalog_Scan_From_The_Ui_Thread()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task ExecuteMeetingCleanupRecommendationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string ResolveMeetingCleanupArchiveCategory", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("meetingsByStem = await Task.Run(", methodBlock, StringComparison.Ordinal);
        Assert.Contains("cancellationToken);", methodBlock, StringComparison.Ordinal);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
