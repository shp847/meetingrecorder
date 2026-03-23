namespace AppPlatform.Abstractions;

public sealed record UpdateRequest(
    string ZipPath,
    string InstallRoot,
    int SourceProcessId,
    string? ReleaseVersion,
    DateTimeOffset? ReleasePublishedAtUtc,
    long? ReleaseAssetSizeBytes,
    InstallChannel Channel = InstallChannel.Unknown);
