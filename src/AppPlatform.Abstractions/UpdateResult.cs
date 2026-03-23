namespace AppPlatform.Abstractions;

public sealed record UpdateResult(
    string InstallRoot,
    string ExecutablePath,
    string? ReleaseVersion,
    bool Succeeded);
