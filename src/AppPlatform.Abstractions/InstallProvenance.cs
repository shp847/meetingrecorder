namespace AppPlatform.Abstractions;

public sealed record InstallProvenance(
    InstallChannel InitialChannel,
    InstallChannel LastUpdateChannel,
    string InitialVersion,
    string LastInstalledVersion,
    DateTimeOffset? LastInstalledAtUtc = null,
    DateTimeOffset? LastReleasePublishedAtUtc = null,
    long? LastReleaseAssetSizeBytes = null);
