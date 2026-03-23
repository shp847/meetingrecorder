namespace AppPlatform.Abstractions;

public sealed record AppReleaseChannelPolicy(
    bool SupportsPerUserMsi,
    bool SupportsPortableZip,
    bool SupportsCommandBootstrap,
    bool SupportsExecutableBootstrap,
    bool SupportsAutoUpgrade);
