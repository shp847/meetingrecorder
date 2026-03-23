namespace AppPlatform.Abstractions;

public sealed record AppProductManifest(
    string ProductId,
    string ProductName,
    string DisplayName,
    string ExecutableName,
    string PortableLauncherFileName,
    string InstallerExecutableName,
    string InstallerMsiName,
    string PortableArchivePrefix,
    string UpdateFeedUrl,
    string ReleasePageUrl,
    string GitHubRepositoryOwner,
    string GitHubRepositoryName,
    ManagedInstallLayout ManagedInstallLayout,
    AppReleaseChannelPolicy ReleaseChannelPolicy,
    ShellShortcutPolicy ShortcutPolicy);
