namespace AppPlatform.Abstractions;

public sealed record InstallProvenance(
    InstallChannel InitialChannel,
    InstallChannel LastUpdateChannel,
    string InitialVersion,
    string LastInstalledVersion);
