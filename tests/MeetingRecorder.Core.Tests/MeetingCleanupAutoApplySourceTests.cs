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
        var migrationIndex = methodBlock.IndexOf("SeedMeetingCleanupAutoApplySuppressionFromPriorAttempts(visibleRecommendations, inspections);", StringComparison.Ordinal);
        var autoApplyIndex = methodBlock.IndexOf("await TryAutoApplyMeetingCleanupSafeFixesAsync(visibleRecommendations, refreshVersion, cancellationToken);", StringComparison.Ordinal);

        Assert.True(publishIndex >= 0, "Expected cleanup refresh to publish the visible recommendations.");
        Assert.True(migrationIndex >= 0 && migrationIndex < autoApplyIndex,
            "Prior queue attempts should be suppressed before automatic fixes are selected.");
        Assert.True(autoApplyIndex > publishIndex, "Automatic safe fixes should start only after recommendation rows are published.");
        Assert.Equal(1, CountOccurrences(methodBlock, "await TryAutoApplyMeetingCleanupSafeFixesAsync("));
    }

    [Fact]
    public void Automatic_Queue_Style_Successes_Are_Persisted_Instead_Of_Cleared()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task ExecuteAutomaticMeetingCleanupRecommendationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string BuildAutomaticMeetingCleanupApplyStatusText", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        var suppressionIndex = methodBlock.IndexOf("RecordQueuedSuccess(", StringComparison.Ordinal);
        var executionIndex = methodBlock.IndexOf("await ExecuteMeetingCleanupRecommendationAsync(", StringComparison.Ordinal);

        Assert.Contains("ShouldSuppressSuccessfulAutomaticApply(recommendation.Action)", methodBlock, StringComparison.Ordinal);
        Assert.Contains("RecordQueuedSuccess(", methodBlock, StringComparison.Ordinal);
        Assert.True(suppressionIndex >= 0 && suppressionIndex < executionIndex,
            "Queue-style automatic fixes should persist suppression before dispatch.");
        Assert.Contains("RecordSuccess(recommendation.Fingerprint)", methodBlock, StringComparison.Ordinal);
        Assert.Contains("RecordFailure(", methodBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_Execution_Offloads_The_Catalog_Scan_From_The_Ui_Thread()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var methodStart = source.IndexOf("private async Task ExecuteMeetingCleanupRecommendationAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string ResolveMeetingCleanupArchiveCategory", methodStart, StringComparison.Ordinal);
        var methodBlock = source[methodStart..methodEnd];

        Assert.Contains("var meetingsByStem = await Task.Run(", methodBlock, StringComparison.Ordinal);
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
