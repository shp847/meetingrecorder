namespace AppPlatform.Abstractions;

public sealed record PendingUpdateState(
    string ZipPath,
    string Version,
    DateTimeOffset? PublishedAtUtc,
    long? AssetSizeBytes);
