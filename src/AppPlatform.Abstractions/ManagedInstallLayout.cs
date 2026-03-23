namespace AppPlatform.Abstractions;

public sealed record ManagedInstallLayout(
    string InstallRoot,
    string DataRoot,
    string ConfigPath,
    IReadOnlyList<string> PreservedDataDirectories,
    IReadOnlyList<string> MergeWithoutOverwriteDirectories,
    IReadOnlyList<string> LegacyInstallRoots);
