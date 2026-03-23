namespace MeetingRecorder.Installer;

internal sealed class WindowsShortcutService
{
    private readonly AppPlatform.Deployment.WindowsShortcutService _inner = new();

    public string GetDesktopShortcutPath()
    {
        return _inner.GetDesktopShortcutPath();
    }

    public string GetStartMenuShortcutPath()
    {
        return _inner.GetStartMenuShortcutPath();
    }

    public ShortcutCreationResult TryCreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath)
    {
        var result = _inner.TryCreateShortcut(shortcutPath, targetPath, workingDirectory, iconPath);
        return new ShortcutCreationResult(result.Success, result.ErrorMessage);
    }
}

internal sealed record ShortcutCreationResult(bool Success, string? ErrorMessage);
