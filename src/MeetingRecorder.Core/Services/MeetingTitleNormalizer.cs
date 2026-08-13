using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static partial class MeetingTitleNormalizer
{
    [GeneratedRegex("[^\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericPattern();

    [GeneratedRegex("\\b([a-z]{3}-[a-z]{4}-[a-z]{3})\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleMeetCodePattern();

    [GeneratedRegex("\\s+and\\s+\\d+\\s+more\\s+pages?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserTabCountPattern();

    public static string NormalizeForComparison(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = title.Trim();
        normalized = CollapseWhitespace(normalized);

        if (normalized.StartsWith("Meeting compact view |", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Meeting compact view |".Length..].Trim();
        }

        if (normalized.EndsWith("| Pinned window", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"| Pinned window".Length].Trim();
        }

        if (normalized.StartsWith("Meet -", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Google Meet", StringComparison.OrdinalIgnoreCase))
        {
            normalized = RemoveGoogleMeetBrowserDecoration(normalized);
        }

        var meetCodeMatch = GoogleMeetCodePattern().Match(normalized);
        if (meetCodeMatch.Success)
        {
            normalized = meetCodeMatch.Groups[1].Value;
        }

        normalized = normalized.Trim('|', ' ');
        normalized = NonAlphaNumericPattern().Replace(normalized, " ");
        return CollapseWhitespace(normalized).ToLowerInvariant();
    }

    private static string RemoveGoogleMeetBrowserDecoration(string value)
    {
        var profileMarkerIndex = value.LastIndexOf(" - Work - ", StringComparison.OrdinalIgnoreCase);
        if (profileMarkerIndex < 0)
        {
            profileMarkerIndex = value.LastIndexOf(" - Personal - ", StringComparison.OrdinalIgnoreCase);
        }

        var title = profileMarkerIndex > "Meet -".Length
            ? value[..profileMarkerIndex]
            : value;
        return BrowserTabCountPattern().Replace(title, string.Empty).Trim();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
