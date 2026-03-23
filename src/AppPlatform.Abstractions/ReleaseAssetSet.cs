namespace AppPlatform.Abstractions;

public sealed record ReleaseAssetSet(
    string Version,
    string? ReleasePageUrl,
    DateTimeOffset? PublishedAtUtc,
    ReleaseAssetDescriptor? InstallerExecutableAsset,
    ReleaseAssetDescriptor AppZipAsset,
    ReleaseAssetDescriptor? BackupCommandAsset,
    ReleaseAssetDescriptor? BackupPowerShellAsset);
