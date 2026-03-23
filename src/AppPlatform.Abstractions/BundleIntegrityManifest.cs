namespace AppPlatform.Abstractions;

public sealed record BundleIntegrityManifest(
    int FormatVersion,
    IReadOnlyList<BundleIntegrityEntry> RequiredFiles);
