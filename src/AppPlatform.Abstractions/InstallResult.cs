namespace AppPlatform.Abstractions;

public sealed record InstallResult(
    string InstallRoot,
    string ExecutablePath,
    string? ReleaseVersion,
    bool DesktopShortcutCreated,
    bool StartMenuShortcutCreated);
