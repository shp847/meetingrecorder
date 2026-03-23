namespace AppPlatform.Abstractions;

public sealed record BundleIntegrityEntry(
    string RelativePath,
    long LengthBytes,
    string Sha256);
