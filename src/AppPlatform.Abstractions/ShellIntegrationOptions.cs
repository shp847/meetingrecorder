namespace AppPlatform.Abstractions;

public sealed record ShellIntegrationOptions(
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    bool LaunchOnLogin);
