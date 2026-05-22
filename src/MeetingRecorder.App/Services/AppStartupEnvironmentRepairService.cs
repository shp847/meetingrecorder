namespace MeetingRecorder.App.Services;

internal static class AppStartupEnvironmentRepairService
{
    private const string WinDirEnvironmentVariableName = "windir";
    private const string SystemRootEnvironmentVariableName = "SystemRoot";

    public static bool EnsureWpfFontEnvironment()
    {
        return EnsureWpfFontEnvironment(
            Environment.GetEnvironmentVariable,
            (name, value) => Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process),
            ResolveWindowsDirectoryFromSystemFolder);
    }

    internal static bool EnsureWpfFontEnvironment(
        Func<string, string?> getEnvironmentVariable,
        Action<string, string> setEnvironmentVariable,
        Func<string> getWindowsDirectory)
    {
        var currentWinDir = getEnvironmentVariable(WinDirEnvironmentVariableName);
        if (IsUsableWindowsDirectory(currentWinDir))
        {
            return false;
        }

        var repairedWinDir = SelectUsableWindowsDirectory(
            getEnvironmentVariable(SystemRootEnvironmentVariableName),
            getWindowsDirectory());
        if (repairedWinDir is null)
        {
            return false;
        }

        setEnvironmentVariable(WinDirEnvironmentVariableName, repairedWinDir);
        return true;
    }

    private static string? SelectUsableWindowsDirectory(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (IsUsableWindowsDirectory(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsUsableWindowsDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var fontsPath = Path.Combine(path, "Fonts") + Path.DirectorySeparatorChar;
            return Uri.TryCreate(fontsPath, UriKind.Absolute, out var fontsUri) && fontsUri.IsFile;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string ResolveWindowsDirectoryFromSystemFolder()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            return string.Empty;
        }

        return Directory.GetParent(systemDirectory)?.FullName ?? string.Empty;
    }
}
