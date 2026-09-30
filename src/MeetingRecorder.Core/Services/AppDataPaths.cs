namespace MeetingRecorder.Core.Services;

public static class AppDataPaths
{
    private static readonly AsyncLocal<string?> TestAppRootOverride = new();

    /// <summary>
    /// Scopes app-data resolution to a disposable test root. This is internal so
    /// production startup has no alternate profile or command-line contract.
    /// </summary>
    internal static IDisposable PushTestAppRoot(string appRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);

        var previousRoot = TestAppRootOverride.Value;
        TestAppRootOverride.Value = Path.GetFullPath(appRoot);
        return new TestAppRootScope(previousRoot);
    }

    public static string GetManagedInstallRoot(string? userProfileRootOverride = null)
    {
        var userProfileRoot = string.IsNullOrWhiteSpace(userProfileRootOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfileRootOverride;

        return Path.Combine(userProfileRoot, "MeetingRecorder");
    }

    public static string GetManagedMeetingsRoot(string? documentsDirectoryOverride = null)
    {
        var documentsRoot = string.IsNullOrWhiteSpace(documentsDirectoryOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsDirectoryOverride;

        return Path.Combine(documentsRoot, "Meetings");
    }

    public static string GetManagedRecordingsRoot(string? documentsDirectoryOverride = null)
    {
        return Path.Combine(GetManagedMeetingsRoot(documentsDirectoryOverride), "Recordings");
    }

    public static string GetManagedTranscriptsRoot(string? documentsDirectoryOverride = null)
    {
        return Path.Combine(GetManagedMeetingsRoot(documentsDirectoryOverride), "Transcripts");
    }

    public static string GetManagedAppRoot(string? localApplicationDataRootOverride = null)
    {
        var localApplicationDataRoot = string.IsNullOrWhiteSpace(localApplicationDataRootOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataRootOverride;

        return Path.Combine(localApplicationDataRoot, "MeetingRecorder");
    }

    public static string GetAppRoot(string? applicationBaseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(applicationBaseDirectory) &&
            !string.IsNullOrWhiteSpace(TestAppRootOverride.Value))
        {
            return TestAppRootOverride.Value;
        }

        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;

        if (IsPortableMode(baseDirectory))
        {
            return Path.Combine(baseDirectory, "data");
        }

        return GetManagedAppRoot();
    }

    public static string GetManagedConfigPath(string? localApplicationDataRootOverride = null)
    {
        return Path.Combine(GetManagedAppRoot(localApplicationDataRootOverride), "config", "appsettings.json");
    }

    public static string GetConfigPath(string? applicationBaseDirectory = null)
    {
        return Path.Combine(GetAppRoot(applicationBaseDirectory), "config", "appsettings.json");
    }

    public static string GetGlobalLogPath(string? applicationBaseDirectory = null)
    {
        return Path.Combine(GetAppRoot(applicationBaseDirectory), "logs", "app.log");
    }

    public static string GetVoiceProfileStorePath(string? applicationBaseDirectory = null)
    {
        return Path.Combine(GetAppRoot(applicationBaseDirectory), "speaker-profiles", "voice-profiles.json");
    }

    public static string GetMeetingIdentityKeyPath(string? applicationBaseDirectory = null)
    {
        return Path.Combine(GetAppRoot(applicationBaseDirectory), "secrets", "meeting-identity-v1.key");
    }

    public static string GetSummaryProviderSecretStorePath(string? applicationBaseDirectory = null)
    {
        return Path.Combine(GetAppRoot(applicationBaseDirectory), "secrets", "summary-provider-secrets.json");
    }

    public static bool IsPortableMode(string? applicationBaseDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;
        return File.Exists(Path.Combine(baseDirectory, "portable.mode"));
    }

    private sealed class TestAppRootScope(string? previousRoot) : IDisposable
    {
        public void Dispose()
        {
            TestAppRootOverride.Value = previousRoot;
        }
    }
}
