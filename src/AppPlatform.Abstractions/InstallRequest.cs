namespace AppPlatform.Abstractions;

public sealed record InstallRequest(
    string BundleRoot,
    string? InstallRoot,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    bool LaunchAfterInstall,
    string? ReleaseVersion,
    DateTimeOffset? ReleasePublishedAtUtc,
    long? ReleaseAssetSizeBytes,
    InstallChannel Channel = InstallChannel.Unknown);
