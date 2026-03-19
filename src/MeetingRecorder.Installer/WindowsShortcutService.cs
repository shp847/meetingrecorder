using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MeetingRecorder.Installer;

internal sealed class WindowsShortcutService
{
    private const string ShortcutFileName = "Meeting Recorder.lnk";

    public string GetDesktopShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutFileName);
    }

    public string GetStartMenuShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ShortcutFileName);
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

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(shortcutPath)
            ?? throw new InvalidOperationException("Shortcut path must include a directory."));

        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("Windows shortcut creation is not available on this system.");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Unable to create the Windows shortcut shell object.");

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath],
                culture: CultureInfo.InvariantCulture);

            if (shortcut is null)
            {
                throw new InvalidOperationException("Unable to create the Windows shortcut object.");
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [targetPath],
                culture: CultureInfo.InvariantCulture);
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [workingDirectory],
                culture: CultureInfo.InvariantCulture);
            shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: ["Meeting Recorder"],
                culture: CultureInfo.InvariantCulture);

            if (File.Exists(iconPath))
            {
                shortcutType.InvokeMember(
                    "IconLocation",
                    BindingFlags.SetProperty,
                    binder: null,
                    target: shortcut,
                    args: [iconPath],
                    culture: CultureInfo.InvariantCulture);
            }

            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null,
                culture: CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.ReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}

internal sealed record ShortcutCreationResult(bool Success, string? ErrorMessage);
