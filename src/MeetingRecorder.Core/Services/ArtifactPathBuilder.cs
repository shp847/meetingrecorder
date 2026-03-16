using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Text;

namespace MeetingRecorder.Core.Services;

public sealed class ArtifactPathBuilder
{
    public string BuildFileStem(MeetingPlatform platform, DateTimeOffset startedAtUtc, string sessionTitle)
    {
        var platformToken = platform switch
        {
            MeetingPlatform.Teams => "teams",
            MeetingPlatform.GoogleMeet => "gmeet",
            MeetingPlatform.Manual => "manual",
            _ => "unknown",
        };

        var safeTitle = Slugify(sessionTitle);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startedAtUtc:yyyy-MM-dd_HHmmss}_{platformToken}_{safeTitle}");
    }

    public string BuildSessionRoot(AppConfig config, string sessionId)
    {
        return BuildSessionRoot(config.WorkDir, sessionId);
    }

    public string BuildSessionRoot(string workDir, string sessionId)
    {
        return Path.Combine(workDir, sessionId);
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "session";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "session" : result;
    }
}
