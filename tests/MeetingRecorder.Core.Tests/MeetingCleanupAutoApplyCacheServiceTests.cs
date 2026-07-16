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
