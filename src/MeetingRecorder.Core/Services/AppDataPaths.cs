namespace MeetingRecorder.Core.Services;

public static class AppDataPaths
{
    public static string GetManagedInstallRoot(string? userProfileRootOverride = null)
    {
        var userProfileRoot = string.IsNullOrWhiteSpace(userProfileRootOverride)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfileRootOverride;

        return Path.Combine(userProfileRoot, "Documents", "MeetingRecorder");
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

    public static bool IsPortableMode(string? applicationBaseDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;
        return File.Exists(Path.Combine(baseDirectory, "portable.mode"));
    }
}
