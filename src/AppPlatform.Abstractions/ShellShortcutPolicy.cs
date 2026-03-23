namespace AppPlatform.Abstractions;

public sealed record ShellShortcutPolicy(
    string DisplayName,
    string DesktopShortcutFileName,
    string StartMenuShortcutFileName,
    string RunRegistryEntryName);
