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
        var autoApplyIndex = methodBlock.IndexOf("await TryAutoApplyMeetingCleanupSafeFixesAsync(visibleRecommendations, refreshVersion, cancellationToken);", StringComparison.Ordinal);

        Assert.True(publishIndex >= 0, "Expected cleanup refresh to publish the visible recommendations.");
        Assert.True(autoApplyIndex > publishIndex, "Automatic safe fixes should start only after recommendation rows are published.");
        Assert.Equal(1, CountOccurrences(methodBlock, "await TryAutoApplyMeetingCleanupSafeFixesAsync("));
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
