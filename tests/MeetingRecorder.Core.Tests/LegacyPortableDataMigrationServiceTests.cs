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
            desktopDirectory: CreateDirectory("desktop"),
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);
        Assert.True(File.Exists(Path.Combine(managedAppRoot, "config", "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(documentsDirectory, "Meetings", "Transcripts", "meeting.md")));
    }

    [Fact]
    public void TryMigrateFromLegacyPortableInstall_Copies_InstallLocal_Data_Into_Managed_Root()
    {
        var appBaseDirectory = CreateDirectory("app");
        var legacyAudioPath = Path.Combine(appBaseDirectory, "data", "audio", "meeting.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyAudioPath)!);
        File.WriteAllText(legacyAudioPath, "audio");

        var managedAppRoot = CreateDirectory("managed");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: CreateDirectory("documents"),
            desktopDirectory: CreateDirectory("desktop"),
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);
        Assert.True(File.Exists(Path.Combine(_root, "documents", "Meetings", "Recordings", "meeting.wav")));
    }

    [Fact]
    public void TryMigrateFromLegacyPortableInstall_Merges_Desktop_Data_Even_When_Managed_Config_Exists()
    {
        var appBaseDirectory = CreateDirectory("app");
        var managedAppRoot = CreateDirectory(Path.Combine("managed", "config"));
        File.WriteAllText(Path.Combine(managedAppRoot, "appsettings.json"), "{ \"current\": true }");

        var desktopDirectory = CreateDirectory("desktop");
        var legacyTranscriptPath = Path.Combine(desktopDirectory, "MeetingRecorder", "data", "transcripts", "meeting.ready");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyTranscriptPath)!);
        File.WriteAllText(legacyTranscriptPath, "ready");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: CreateDirectory("documents"),
            desktopDirectory: desktopDirectory,
            managedAppRootOverride: Path.GetDirectoryName(managedAppRoot)!);

        Assert.True(migrated);
        Assert.True(File.Exists(Path.Combine(_root, "documents", "Meetings", "Transcripts", "meeting.ready")));
        Assert.Equal(
            "{ \"current\": true }",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(managedAppRoot)!, "config", "appsettings.json")));
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
            desktopDirectory: CreateDirectory("desktop"),
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
            desktopDirectory: CreateDirectory("desktop"),
            managedAppRootOverride: Path.GetDirectoryName(managedAppRoot)!);

        Assert.False(migrated);
        Assert.Equal(
            "{ \"current\": true }",
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(managedAppRoot)!, "config", "appsettings.json")));
    }

    [Fact]
    public async Task TryMigrateFromLegacyPortableInstall_Preserves_And_Remap_Model_Settings_Into_Managed_Root()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var managedAppRoot = CreateDirectory("managed");
        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));

        var legacyModelPath = Path.Combine(legacyDataRoot, "models", "asr", "ggml-small.en-q8_0.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyModelPath)!);
        File.WriteAllText(legacyModelPath, "model");

        var legacyDiarizationPath = Path.Combine(legacyDataRoot, "models", "diarization");
        Directory.CreateDirectory(legacyDiarizationPath);

        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(
            legacyConfigPath,
            $$"""
            {
              "modelCacheDir": "{{Path.Combine(legacyDataRoot, "models").Replace("\\", "\\\\")}}",
              "transcriptionModelPath": "{{legacyModelPath.Replace("\\", "\\\\")}}",
              "diarizationAssetPath": "{{legacyDiarizationPath.Replace("\\", "\\\\")}}"
            }
            """);

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            desktopDirectory: CreateDirectory("desktop"),
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);

        var migratedConfigPath = Path.Combine(managedAppRoot, "config", "appsettings.json");
        var migratedConfig = await new AppConfigStore(migratedConfigPath, documentsDirectory).LoadOrCreateAsync();

        Assert.Equal(Path.Combine(managedAppRoot, "models"), migratedConfig.ModelCacheDir);
        Assert.Equal(
            Path.Combine(managedAppRoot, "models", "asr", "ggml-small.en-q8_0.bin"),
            migratedConfig.TranscriptionModelPath);
        Assert.Equal(Path.Combine(managedAppRoot, "models", "diarization"), migratedConfig.DiarizationAssetPath);
        Assert.True(File.Exists(Path.Combine(managedAppRoot, "models", "asr", "ggml-small.en-q8_0.bin")));
    }

    [Fact]
    public async Task TryMigrateFromLegacyPortableInstall_Keeps_Microphone_Capture_On_By_Default()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var managedAppRoot = CreateDirectory("managed");
        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));

        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(legacyConfigPath, "{ \"micCaptureEnabled\": false }");

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            desktopDirectory: CreateDirectory("desktop"),
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);

        var migratedConfigPath = Path.Combine(managedAppRoot, "config", "appsettings.json");
        var migratedConfig = await new AppConfigStore(migratedConfigPath, documentsDirectory).LoadOrCreateAsync();

        Assert.True(migratedConfig.MicCaptureEnabled);
    }

    [Fact]
    public async Task TryMigrateFromLegacyPortableInstall_Does_Not_Preserve_Stale_Installed_Release_Metadata()
    {
        var appBaseDirectory = CreateDirectory("app");
        var documentsDirectory = CreateDirectory("documents");
        var managedAppRoot = CreateDirectory("managed");
        var legacyDataRoot = CreateDirectory(Path.Combine("documents", "MeetingRecorder", "data"));

        var legacyConfigPath = Path.Combine(legacyDataRoot, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        File.WriteAllText(
            legacyConfigPath,
            """
            {
              "installedReleaseVersion": "0.1",
              "installedReleasePublishedAtUtc": "2026-03-17T19:28:22Z",
              "installedReleaseAssetSizeBytes": 74220015,
              "pendingUpdateZipPath": "C:\\temp\\old.zip",
              "pendingUpdateVersion": "0.2",
              "pendingUpdatePublishedAtUtc": "2026-03-18T02:30:00Z",
              "pendingUpdateAssetSizeBytes": 74230000
            }
            """);

        var migrated = LegacyPortableDataMigrationService.TryMigrateFromLegacyPortableInstall(
            applicationBaseDirectory: appBaseDirectory,
            documentsDirectory: documentsDirectory,
            managedAppRootOverride: managedAppRoot);

        Assert.True(migrated);

        var migratedConfigPath = Path.Combine(managedAppRoot, "config", "appsettings.json");
        var migratedConfig = await new AppConfigStore(migratedConfigPath, documentsDirectory).LoadOrCreateAsync();

        Assert.Equal(MeetingRecorder.Core.Branding.AppBranding.Version, migratedConfig.InstalledReleaseVersion);
        Assert.Null(migratedConfig.InstalledReleasePublishedAtUtc);
        Assert.Null(migratedConfig.InstalledReleaseAssetSizeBytes);
        Assert.Equal(string.Empty, migratedConfig.PendingUpdateZipPath);
        Assert.Equal(string.Empty, migratedConfig.PendingUpdateVersion);
        Assert.Null(migratedConfig.PendingUpdatePublishedAtUtc);
        Assert.Null(migratedConfig.PendingUpdateAssetSizeBytes);
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
