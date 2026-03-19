using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class LegacyPortableDataMigrationServiceTests : IDisposable
{
    private readonly string _root;

    public LegacyPortableDataMigrationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryMigrateFromLegacyPortableInstall_Copies_Legacy_Data_When_Managed_Config_Is_Missing()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var managedAppRoot = CreateDirectory("managed");
        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));

        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(legacyConfigPath, "{ \"micCaptureEnabled\": true }");

        var legacyTranscriptPath = Path.Combine(legacyDataRoot, "transcripts", "meeting.md");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyTranscriptPath)!);
        File.WriteAllText(legacyTranscriptPath, "# transcript");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);
        Assert.True(File.Exists(Path.Combine(managedAppRoot, "config", "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(managedAppRoot, "transcripts", "meeting.md")));
    }

    [Fact]
    public void TryMigrateFromLegacyPortableInstall_Does_Not_Run_In_Portable_Mode()
    {
        var appBaseDirectory = CreateDirectory("portable-app");
        File.WriteAllText(Path.Combine(appBaseDirectory, "portable.mode"), string.Empty);
        var documentsDirectory = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data", "config"));
        File.WriteAllText(Path.Combine(documentsDirectory, "appsettings.json"), "{ }");
        var managedAppRoot = CreateDirectory("managed");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: CreateDirectory("documents"),
            managedAppRootOverride: managedAppRoot);

        Assert.False(migrated);
        Assert.False(File.Exists(Path.Combine(managedAppRoot, "config", "appsettings.json")));
    }

    [Fact]
    public void TryMigrateFromLegacyPortableInstall_Does_Not_Overwrite_Existing_Managed_Config()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var managedAppRoot = CreateDirectory(Path.Combine("managed", "config"));
        File.WriteAllText(Path.Combine(managedAppRoot, "appsettings.json"), "{ \"current\": true }");

        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data", "config"));
        File.WriteAllText(Path.Combine(legacyDataRoot, "appsettings.json"), "{ \"legacy\": true }");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            managedAppRootOverride: Path.GetDirectoryName(managedAppRoot)!);

        Assert.False(migrated);
        Assert.Equal(
            "{ \"current\": true }",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(managedAppRoot)!, "config", "appsettings.json")));
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
            // Best-effort cleanup only.
        }
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
