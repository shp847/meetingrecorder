using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.Text;

namespace MeetingRecorder.Installer;

internal static class ManualInstallGuideBuilder
{
    public static string Build(
        GitHubReleaseBootstrapInfo? releaseInfo,
        string installRoot)
    {
        var releasePageUrl = releaseInfo?.ReleasePageUrl ?? AppBranding.DefaultReleasePageUrl;
        var zipName = releaseInfo?.AppZipAsset.Name ?? "MeetingRecorder-win-x64.zip";

        var builder = new StringBuilder();
        builder.AppendLine("Manual install fallback");
        builder.AppendLine();
        builder.AppendLine("1. Open the GitHub release page:");
        builder.AppendLine(releasePageUrl);
        builder.AppendLine();
        builder.AppendLine($"2. Download `{zipName}`.");
        builder.AppendLine("3. Extract the ZIP anywhere you are allowed to run files from.");
        builder.AppendLine("4. Preferred fallback: run `Install-MeetingRecorder.cmd` from the extracted folder.");
        builder.AppendLine("5. If scripts are blocked, copy the extracted `MeetingRecorder` folder into:");
        builder.AppendLine(installRoot);
        builder.AppendLine("6. Open that folder and run `MeetingRecorder.App.exe` directly.");
        builder.AppendLine("7. In the app, open the Models tab and download or import a Whisper model if one is not ready yet.");
        return builder.ToString().TrimEnd();
    }
}
