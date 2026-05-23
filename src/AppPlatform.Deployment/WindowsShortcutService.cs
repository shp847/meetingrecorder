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

    public string GetPinnedTaskbarShortcutRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "TaskBar");
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

    public ShortcutRepairResult RepairExistingShortcuts(
        ShellShortcutPolicy shortcutPolicy,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string? desktopRoot = null,
        string? programsRoot = null,
        bool repairDesktopShortcut = true,
        bool repairStartMenuShortcut = true)
    {
        var resolvedDesktopRoot = desktopRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var resolvedProgramsRoot = programsRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var removedLegacyPaths = RemoveLegacyShortcuts(
            shortcutPolicy,
            resolvedDesktopRoot,
            resolvedProgramsRoot,
            repairDesktopShortcut,
            repairStartMenuShortcut);
        var repairedShortcutPaths = new List<string>();

        if (repairDesktopShortcut && ShouldRepairShortcut(removedLegacyPaths, resolvedDesktopRoot))
        {
            var desktopShortcutPath = Path.Combine(resolvedDesktopRoot, shortcutPolicy.DesktopShortcutFileName);
            if (TryCreateShortcut(desktopShortcutPath, targetPath, workingDirectory, iconPath).Success)
            {
                repairedShortcutPaths.Add(desktopShortcutPath);
            }
        }

        if (repairStartMenuShortcut && ShouldRepairShortcut(removedLegacyPaths, resolvedProgramsRoot))
        {
            var startMenuShortcutPath = Path.Combine(resolvedProgramsRoot, shortcutPolicy.StartMenuShortcutFileName);
            if (TryCreateShortcut(startMenuShortcutPath, targetPath, workingDirectory, iconPath).Success)
            {
                repairedShortcutPaths.Add(startMenuShortcutPath);
            }
        }

        return new ShortcutRepairResult(removedLegacyPaths, repairedShortcutPaths);
    }

    public IReadOnlyList<string> RepairPinnedTaskbarShortcuts(
        ShellShortcutPolicy shortcutPolicy,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string? taskbarRoot = null)
    {
        if (taskbarRoot is null && IsPathUnderDirectory(targetPath, Path.GetTempPath()))
        {
            return [];
        }

        var resolvedTaskbarRoot = taskbarRoot ?? GetPinnedTaskbarShortcutRoot();
        if (string.IsNullOrWhiteSpace(resolvedTaskbarRoot) ||
            !Directory.Exists(resolvedTaskbarRoot))
        {
            return [];
        }

        var repairedShortcutPaths = new List<string>();
        foreach (var shortcutPath in GetPinnedTaskbarShortcutPaths(resolvedTaskbarRoot, shortcutPolicy))
        {
            if (!File.Exists(shortcutPath))
            {
                continue;
            }

            if (!ShouldRepairPinnedTaskbarShortcut(shortcutPath, targetPath, workingDirectory, iconPath))
            {
                continue;
            }

            if (TryCreateShortcut(shortcutPath, targetPath, workingDirectory, iconPath).Success)
            {
                repairedShortcutPaths.Add(shortcutPath);
            }
        }

        return repairedShortcutPaths;
    }

    private static bool ShouldRepairPinnedTaskbarShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        var shortcut = TryReadShortcut(shortcutPath);
        if (shortcut is null)
        {
            return false;
        }

        var existingTargetPath = shortcut.TargetPath;
        var existingWorkingDirectory = shortcut.WorkingDirectory;
        var existingIconPath = ExtractIconPath(shortcut.IconLocation);

        var targetMatches = PathsEqual(existingTargetPath, targetPath);
        var workingDirectoryMatches = PathsEqual(existingWorkingDirectory, workingDirectory);
        var iconMatches = PathsEqual(existingIconPath, iconPath);
        if (targetMatches && workingDirectoryMatches && iconMatches)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existingTargetPath) || !File.Exists(existingTargetPath))
        {
            return true;
        }

        return targetMatches ||
            workingDirectoryMatches ||
            IsPathUnderDirectory(existingTargetPath, workingDirectory) ||
            IsPathUnderDirectory(existingWorkingDirectory, workingDirectory);
    }

    private static ShortcutSnapshot? TryReadShortcut(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            try
            {
                dynamic dynamicShell = shell;
                var shortcut = dynamicShell.CreateShortcut(shortcutPath);
                if (shortcut is null)
                {
                    return null;
                }

                try
                {
                    dynamic dynamicShortcut = shortcut;
                    return new ShortcutSnapshot(
                        dynamicShortcut.TargetPath,
                        dynamicShortcut.WorkingDirectory,
                        dynamicShortcut.IconLocation);
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
        catch
        {
            return null;
        }
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

    private static IEnumerable<string> GetPinnedTaskbarShortcutPaths(
        string taskbarRoot,
        ShellShortcutPolicy shortcutPolicy)
    {
        var candidateNames = new[]
        {
            shortcutPolicy.DisplayName + ".lnk",
            shortcutPolicy.DesktopShortcutFileName,
            shortcutPolicy.StartMenuShortcutFileName,
        };
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidateName in candidateNames)
        {
            if (string.IsNullOrWhiteSpace(candidateName))
            {
                continue;
            }

            var shortcutPath = Path.Combine(taskbarRoot, Path.ChangeExtension(candidateName, ".lnk"));
            if (seenPaths.Add(shortcutPath))
            {
                yield return shortcutPath;
            }
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

    private static bool ShouldRepairShortcut(IEnumerable<string> removedLegacyPaths, string shellRoot)
    {
        foreach (var removedPath in removedLegacyPaths)
        {
            var parentPath = Path.GetDirectoryName(removedPath);
            if (string.Equals(parentPath, shellRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(removedPath, shellRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractIconPath(string iconLocation)
    {
        var trimmed = iconLocation.Trim().Trim('"');
        var separatorIndex = trimmed.LastIndexOf(',');
        if (separatorIndex > 0 &&
            int.TryParse(trimmed[(separatorIndex + 1)..], out _))
        {
            trimmed = trimmed[..separatorIndex].Trim().Trim('"');
        }

        return trimmed;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        var normalizedDirectory = NormalizePath(directory);
        return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(
                normalizedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(trimmed))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record ShortcutSnapshot(
        string TargetPath,
        string WorkingDirectory,
        string IconLocation);
}

public sealed record ShortcutCreationResult(bool Success, string? ErrorMessage);

public sealed record ShortcutRepairResult(
    IReadOnlyList<string> RemovedLegacyPaths,
    IReadOnlyList<string> RepairedShortcutPaths);
