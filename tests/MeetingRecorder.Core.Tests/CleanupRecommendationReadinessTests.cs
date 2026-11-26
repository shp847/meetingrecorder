using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class CleanupRecommendationReadinessTests
{
    [Fact]
    public async Task Merge_Requires_Recovery_Artifacts_For_Every_Meeting()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            async Task<MeetingOutputRecord> CreateMeeting(string stem, bool includeReady)
            {
                var audio = Path.Combine(root, stem + ".wav");
                var markdown = Path.Combine(root, stem + ".md");
                var json = Path.Combine(root, stem + ".json");
                var ready = Path.Combine(root, stem + ".ready");
                foreach (var path in new[] { audio, markdown, json }) await File.WriteAllTextAsync(path, "data");
                if (includeReady) await File.WriteAllTextAsync(ready, "ready");
                return new MeetingOutputRecord(stem, stem, DateTimeOffset.UtcNow, MeetingPlatform.Teams,
                    TimeSpan.FromMinutes(1), audio, markdown, json, ready, null, SessionState.Published,
                    Array.Empty<MeetingAttendee>(), false, null);
            }

            var first = await CreateMeeting("first", true);
            var second = await CreateMeeting("second", false);
            var records = new Dictionary<string, MeetingOutputRecord> { ["first"] = first, ["second"] = second };
            var merge = new MeetingCleanupRecommendation("merge", MeetingCleanupAction.Merge, MeetingCleanupConfidence.High,
                "Merge", "description", "first", ["first", "second"], true, null, null);
            Assert.False(CleanupRecommendationReadiness.Project(merge, records).CanApplyAutomatically);

            await File.WriteAllTextAsync(second.ReadyMarkerPath!, "ready");
            Assert.True(CleanupRecommendationReadiness.Project(merge, records).CanApplyAutomatically);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
