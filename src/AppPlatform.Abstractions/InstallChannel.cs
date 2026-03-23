namespace AppPlatform.Abstractions;

public enum InstallChannel
{
    Unknown = 0,
    Msi = 1,
    CommandBootstrap = 2,
    ExecutableBootstrap = 3,
    PortableZip = 4,
    AutoUpdate = 5,
    DirectCli = 6,
}
