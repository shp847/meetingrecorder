using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Installer;

internal sealed record InstallerSessionResult(
    string InstallRoot,
    string BootstrapCommandPath,
    string DiagnosticLogPath,
    GitHubReleaseBootstrapInfo? ReleaseInfo,
    string ManualSteps,
    string? ReleasePageUrl);
