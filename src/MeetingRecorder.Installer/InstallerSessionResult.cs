using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Installer;

internal sealed record InstallerSessionResult(
    string InstallRoot,
    string LaunchExecutablePath,
    GitHubReleaseBootstrapInfo? ReleaseInfo,
    string ManualSteps,
    string? ReleasePageUrl);
