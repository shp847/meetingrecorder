using System.Reflection;

namespace MeetingRecorder.Core.Branding;

public static class AppBranding
{
    public const string ProductName = "Meeting Recorder";
    public static readonly string Version = ResolveVersion();
    public static readonly string DisplayNameWithVersion = ProductName + " v" + Version;
    public const string AuthorName = "Pranav Sharma";
    public const string AuthorEmail = "shp847@gmail.com";
    public const string GitHubRepositoryOwner = "shp847";
    public const string GitHubRepositoryName = "meetingrecorder";
    public const string DefaultUpdateFeedUrl = "https://api.github.com/repos/shp847/meetingrecorder/releases/latest";
    public const string DefaultReleasePageUrl = "https://github.com/shp847/meetingrecorder/releases";
    public const string RecordingLegalNotice = "You are responsible to comply with all applicable recording, privacy, employment, and consent laws and workplace policies in your location. Tell participants when they are being recorded and obtain consent where required. This app is not legal advice.";

    private static string ResolveVersion()
    {
        var informationalVersion = typeof(AppBranding).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? "0.0"
            : informationalVersion.Trim();
    }
}
