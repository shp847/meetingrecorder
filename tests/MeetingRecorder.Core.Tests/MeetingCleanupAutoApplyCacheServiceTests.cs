using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupAutoApplyCacheServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _cachePath;

    public MeetingCleanupAutoApplyCacheServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _cachePath = Path.Combine(_root, "cache", "meeting-cleanup-auto-apply-v1.json");
    }

    [Fact]
    public void RecordFailure_Suppresses_Automatic_Apply_Until_RecordSuccess_Clears_It()
    {
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);

        service.RecordFailure("archive:meeting-1", DateTimeOffset.Parse("2026-07-15T12:00:00Z"), "Access denied");

        Assert.True(service.ShouldSkipAutomaticApply("archive:meeting-1"));

        service.RecordSuccess("archive:meeting-1");

        Assert.False(service.ShouldSkipAutomaticApply("archive:meeting-1"));
    }

    [Fact]
    public void Different_Fingerprint_Does_Not_Inherit_A_Previous_Failure()
    {
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);

        service.RecordFailure("archive:meeting-1", DateTimeOffset.Parse("2026-07-15T12:00:00Z"), "Access denied");

        Assert.False(service.ShouldSkipAutomaticApply("archive:meeting-2"));
    }

    [Fact]
    public void RecordQueuedSuccess_Suppresses_Automatic_Apply_Across_Service_Restarts()
    {
        var queuedAtUtc = DateTimeOffset.Parse("2026-07-16T04:15:00Z");
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);

        service.RecordQueuedSuccess("speaker-labels:meeting-1", queuedAtUtc);

        var reloadedService = new MeetingCleanupAutoApplyCacheService(_cachePath);
        Assert.True(reloadedService.ShouldSkipAutomaticApply("speaker-labels:meeting-1"));
    }

    [Fact]
    public void RecordFailure_Replaces_A_Previous_Queued_Success()
    {
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);
        service.RecordQueuedSuccess(
            "speaker-labels:meeting-1",
            DateTimeOffset.Parse("2026-07-16T04:15:00Z"));

        service.RecordFailure(
            "speaker-labels:meeting-1",
            DateTimeOffset.Parse("2026-07-16T04:16:00Z"),
            "Worker rejected the request");

        var document = File.ReadAllText(_cachePath);
        Assert.Contains("Worker rejected the request", document, StringComparison.Ordinal);
        Assert.DoesNotContain("lastQueuedSuccessUtc\": \"2026-07-16T04:15:00", document, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordQueuedSuccesses_Persists_All_Distinct_Fingerprints()
    {
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);

        service.RecordQueuedSuccesses(
            ["speaker-labels:meeting-1", "speaker-labels:meeting-2", "speaker-labels:meeting-1"],
            DateTimeOffset.Parse("2026-07-16T04:15:00Z"));

        var reloadedService = new MeetingCleanupAutoApplyCacheService(_cachePath);
        Assert.True(reloadedService.ShouldSkipAutomaticApply("speaker-labels:meeting-1"));
        Assert.True(reloadedService.ShouldSkipAutomaticApply("speaker-labels:meeting-2"));
    }

    [Fact]
    public void RecordQueuedSuccesses_Does_Not_Overwrite_An_Existing_Failure()
    {
        var service = new MeetingCleanupAutoApplyCacheService(_cachePath);
        service.RecordFailure(
            "speaker-labels:meeting-1",
            DateTimeOffset.Parse("2026-07-16T04:15:00Z"),
            "Diarization failed");

        service.RecordQueuedSuccesses(
            ["speaker-labels:meeting-1"],
            DateTimeOffset.Parse("2026-07-16T04:16:00Z"));

        Assert.Contains("Diarization failed", File.ReadAllText(_cachePath), StringComparison.Ordinal);
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
}
