using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ImportInboxPathPolicyTests
{
    [Fact]
    public void DefaultInbox_Is_A_Sibling_Of_Managed_Recordings_And_Transcripts()
    {
        var documentsRoot = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

        var inboxPath = ImportInboxPathPolicy.GetDefaultInboxPath(documentsRoot);
        var result = ImportInboxPathPolicy.Validate(
            inboxPath,
            [
                AppDataPaths.GetManagedRecordingsRoot(documentsRoot),
                AppDataPaths.GetManagedTranscriptsRoot(documentsRoot),
                Path.Combine(documentsRoot, "MeetingRecorder-work"),
            ]);

        Assert.Equal(Path.Combine(documentsRoot, "Meetings", "Import Inbox"), inboxPath);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Recordings")]
    [InlineData("Recordings\\nested")]
    public void Validate_Rejects_An_Inbox_That_Overlaps_Protected_Storage(string inboxSuffix)
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var recordingsPath = Path.Combine(root, "Meetings", "Recordings");

        var result = ImportInboxPathPolicy.Validate(
            Path.Combine(root, "Meetings", inboxSuffix),
            [recordingsPath, Path.Combine(root, "work")]);

        Assert.Equal(ImportInboxPathValidationStatus.OverlapsProtectedStorage, result.Status);
        Assert.DoesNotContain(root, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Rejects_Protected_Storage_Nested_Inside_The_Inbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var inboxPath = Path.Combine(root, "Meetings", "Import Inbox");

        var result = ImportInboxPathPolicy.Validate(
            inboxPath,
            [Path.Combine(inboxPath, "archive")]);

        Assert.Equal(ImportInboxPathValidationStatus.OverlapsProtectedStorage, result.Status);
    }

    [Fact]
    public void Validate_Rejects_Remote_Inbox_Path_Without_Disclosing_It()
    {
        const string remotePath = "\\\\server\\share\\Meeting Recorder Import Inbox";

        var result = ImportInboxPathPolicy.Validate(remotePath, Array.Empty<string>());

        Assert.Equal(ImportInboxPathValidationStatus.UnsupportedLocation, result.Status);
        Assert.DoesNotContain("server", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckStorageHealth_Uses_Only_A_Temporary_AppOwned_Probe()
    {
        var inboxPath = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "inbox");

        var health = ImportInboxPathPolicy.CheckStorageHealth(inboxPath, requiredBytes: 1);

        Assert.True(health.IsReady);
        Assert.Equal(1, health.RequiredBytes);
        Assert.Empty(Directory.EnumerateFiles(inboxPath, ".meeting-recorder-inbox-probe-*.tmp"));
    }

    [Fact]
    public void CheckStorageHealth_Rejects_A_Negative_Requirement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImportInboxPathPolicy.CheckStorageHealth(Path.GetTempPath(), requiredBytes: -1));
    }

    [Fact]
    public void EstimateImportStorage_Accounts_For_The_Source_And_A_Conservative_Normalized_Copy()
    {
        var requirements = ImportStorageRequirementEstimator.Estimate(
            sourceSizeBytes: 1_024,
            duration: TimeSpan.FromSeconds(2));

        Assert.True(requirements.RequiredWorkBytes > 1_024);
        Assert.True(requirements.RequiredOutputBytes > 1_024);
        Assert.True(requirements.RequiredWorkBytes > requirements.RequiredOutputBytes);
    }

    [Fact]
    public void CheckImportStorageHealth_Reports_A_Storage_Role_Without_A_Path()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var unavailablePath = Path.Combine(root, "not-a-directory");
        File.WriteAllText(unavailablePath, "file");
        var config = new MeetingRecorder.Core.Configuration.AppConfig
        {
            WorkDir = Path.Combine(root, "work"),
            AudioOutputDir = unavailablePath,
            TranscriptOutputDir = Path.Combine(root, "transcripts"),
        };

        var health = ImportInboxPathPolicy.CheckImportStorageHealth(config, 1_024, TimeSpan.FromSeconds(1));

        Assert.Equal(ImportInboxStorageHealthStatus.Unavailable, health.Status);
        Assert.Equal("Meeting Recorder recordings storage is not ready for this import.", health.Message);
        Assert.DoesNotContain(root, health.Message, StringComparison.OrdinalIgnoreCase);
    }
}
