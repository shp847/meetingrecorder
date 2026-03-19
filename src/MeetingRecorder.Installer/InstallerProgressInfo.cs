namespace MeetingRecorder.Installer;

internal sealed record InstallerProgressInfo(
    string Title,
    string Message,
    double? Percent = null,
    string? Detail = null);
