using AppPlatform.Abstractions;

namespace AppPlatform.Deployment;

public sealed class WindowsShortcutService
{
    private const string DefaultShortcutFileName = "Meeting Recorder.lnk";

    public string GetDesktopShortcutPath()
    {
        return GetDesktopShortcutPath(new ShellShortcutPolicy(
            "Meeting Recorder",
            DefaultShortcutFileName,
            DefaultShortcutFileName,
            "MeetingRecorder"));
    }

    public string GetDesktopShortcutPath(ShellShortcutPolicy shortcutPolicy)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            shortcutPolicy.DesktopShortcutFileName);
    }

    public string GetStartMenuShortcutPath()
    {
        return GetStartMenuShortcutPath(new ShellShortcutPolicy(
            "Meeting Recorder",
            DefaultShortcutFileName,
            DefaultShortcutFileName,
            "MeetingRecorder"));
    }

    public string GetStartMenuShortcutPath(ShellShortcutPolicy shortcutPolicy)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            shortcutPolicy.StartMenuShortcutFileName);
    }

    public ShortcutCreationResult TryCreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        try
        {
            CreateShortcut(shortcutPath, targetPath, workingDirectory, iconPath);
            return new ShortcutCreationResult(true, null);
        }
        catch (Exception exception)
        {
            return new ShortcutCreationResult(false, exception.Message);
        }
    }

    public IReadOnlyList<string> RemoveLegacyShortcuts(
        ShellShortcutPolicy shortcutPolicy,
        string? desktopRoot = null,
        string? programsRoot = null,
        bool removeDesktopShortcut = true,
        bool removeStartMenuShortcut = true)
    {
        var removedPaths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedDesktopRoot = desktopRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var resolvedProgramsRoot = programsRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        if (removeDesktopShortcut)
        {
            foreach (var legacyShortcutPath in GetLegacyShortcutPaths(
                resolvedDesktopRoot,
                shortcutPolicy.DisplayName,
                shortcutPolicy.DesktopShortcutFileName))
            {
                TryDeleteFile(legacyShortcutPath, removedPaths, seenPaths);
            }
        }

        if (removeStartMenuShortcut)
        {
            foreach (var legacyShortcutPath in GetLegacyShortcutPaths(
                resolvedProgramsRoot,
                shortcutPolicy.DisplayName,
                shortcutPolicy.StartMenuShortcutFileName))
            {
                TryDeleteFile(legacyShortcutPath, removedPaths, seenPaths);
            }

            var legacyFolderPath = Path.Combine(resolvedProgramsRoot, shortcutPolicy.DisplayName);
            if (Directory.Exists(legacyFolderPath))
            {
                Directory.Delete(legacyFolderPath, recursive: true);
                if (seenPaths.Add(legacyFolderPath))
                {
                    removedPaths.Add(legacyFolderPath);
                }
            }
        }

        return removedPaths;
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(shortcutPath)
            ?? throw new InvalidOperationException("Shortcut path must include a directory."));

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable on this machine.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Unable to create the Windows shortcut shell.");

        try
        {
            dynamic dynamicShell = shell;
            var shortcut = dynamicShell.CreateShortcut(shortcutPath);
            if (shortcut is null)
            {
                throw new InvalidOperationException("Unable to create the Windows shortcut.");
            }

            try
            {
                dynamic dynamicShortcut = shortcut;
                dynamicShortcut.TargetPath = targetPath;
                dynamicShortcut.WorkingDirectory = workingDirectory;
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    dynamicShortcut.IconLocation = iconPath;
                }

                dynamicShortcut.Save();
            }
            finally
            {
                TryReleaseComObject(shortcut);
            }
        }
        finally
        {
            TryReleaseComObject(shell);
        }
    }

    private static IEnumerable<string> GetLegacyShortcutPaths(
        string rootDirectory,
        string displayName,
        string shortcutFileName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            yield break;
        }

        yield return Path.Combine(rootDirectory, displayName + ".lnk");
        yield return Path.Combine(rootDirectory, displayName + ".cmd");

        var shortcutPath = Path.Combine(rootDirectory, shortcutFileName);
        yield return shortcutPath;

        var lnkShortcutPath = Path.ChangeExtension(shortcutPath, ".lnk");
        if (!string.Equals(lnkShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return lnkShortcutPath;
        }

        var cmdShortcutPath = Path.ChangeExtension(shortcutPath, ".cmd");
        if (!string.Equals(cmdShortcutPath, shortcutPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return cmdShortcutPath;
        }
    }

    private static void TryReleaseComObject(object instance)
    {
        try
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteFile(
        string path,
        ICollection<string> removedPaths,
        ISet<string> seenPaths)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
        if (seenPaths.Add(path))
        {
            removedPaths.Add(path);
        }
    }
}

public sealed record ShortcutCreationResult(bool Success, string? ErrorMessage);
