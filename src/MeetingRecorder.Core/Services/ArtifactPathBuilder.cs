using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Text;

namespace MeetingRecorder.Core.Services;

public sealed class ArtifactPathBuilder
{
    public const string TranscriptSidecarDirectoryName = "json";

    public string BuildFileStem(MeetingPlatform platform, DateTimeOffset startedAtUtc, string sessionTitle)
    {
        var platformToken = platform switch
        {
            MeetingPlatform.Teams => "teams",
            MeetingPlatform.GoogleMeet => "gmeet",
            MeetingPlatform.Zoom => "zoom",
            MeetingPlatform.Manual => "manual",
            _ => "unknown",
        };

        var safeTitle = Slugify(sessionTitle);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{startedAtUtc:yyyy-MM-dd_HHmmss}_{platformToken}_{safeTitle}");
    }

    /// <summary>
    /// Imports retain the familiar time/platform/title stem while carrying a
    /// stable app-owned suffix. Two user-selected files with the same edited
    /// title and timestamp therefore cannot overwrite one another's output.
    /// </summary>
    public string BuildImportedFileStem(
        MeetingPlatform platform,
        DateTimeOffset startedAtUtc,
        string sessionTitle,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("An imported session id is required.", nameof(sessionId));
        }

        var identifier = new string(sessionId
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("The imported session id must contain letters or digits.", nameof(sessionId));
        }

        return $"{BuildFileStem(platform, startedAtUtc, sessionTitle)}-import-{identifier}";
    }

    public string BuildSessionRoot(AppConfig config, string sessionId)
    {
        return BuildSessionRoot(config.WorkDir, sessionId);
    }

    public string BuildSessionRoot(string workDir, string sessionId)
    {
        return Path.Combine(workDir, sessionId);
    }

    public static string BuildTranscriptSidecarRoot(string transcriptOutputDir)
    {
        return Path.Combine(transcriptOutputDir, TranscriptSidecarDirectoryName);
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
