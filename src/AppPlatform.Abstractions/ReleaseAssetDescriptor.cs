namespace AppPlatform.Abstractions;

public sealed record ReleaseAssetDescriptor(
    string Name,
    string DownloadUrl,
    long? SizeBytes,
    DateTimeOffset? UpdatedAtUtc);
