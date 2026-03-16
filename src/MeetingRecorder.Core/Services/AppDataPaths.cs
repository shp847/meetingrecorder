namespace MeetingRecorder.Core.Services;

public static class AppDataPaths
{
    public static string GetAppRoot(string? applicationBaseDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;

        if (IsPortableMode(baseDirectory))
        {
            return Path.Combine(baseDirectory, "data");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeetingRecorder");
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
