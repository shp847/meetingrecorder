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

    [GeneratedRegex("\\s+and\\s+\\d+\\s+more\\s+pages?(?:\\s+-\\s+(?:Work|Personal))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserTabCountWithProfilePattern();

    public static string NormalizeGoogleMeetWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = CollapseWhitespace(title.Trim());
        const string edgeSuffix = " - Microsoft Edge";
        var hadEdgeSuffix = normalized.EndsWith(edgeSuffix, StringComparison.OrdinalIgnoreCase);
        if (hadEdgeSuffix)
        {
            normalized = normalized[..^edgeSuffix.Length].TrimEnd();
        }

        // A Google Meet caption is authoritative even when a browser omits its
        // final product suffix. Remove only the known tab-count/profile tail;
        // do not trim an ordinary meeting name that merely ends in "- Work".
        if (normalized.StartsWith("Meet - ", StringComparison.OrdinalIgnoreCase))
        {
            var titleBeforeTabTailRemoval = normalized;
            normalized = BrowserTabCountWithProfilePattern().Replace(normalized, string.Empty).Trim();
            var hadBrowserTabTail = !string.Equals(
                titleBeforeTabTailRemoval,
                normalized,
                StringComparison.Ordinal);
            if (hadEdgeSuffix)
            {
                normalized = RemoveBrowserProfileSuffix(normalized);
            }

            if (!hadEdgeSuffix && !hadBrowserTabTail)
            {
                return normalized;
            }

            return normalized["Meet - ".Length..].Trim();
        }

        if (!hadEdgeSuffix)
        {
            return normalized;
        }

        normalized = RemoveBrowserProfileSuffix(normalized);
        normalized = BrowserTabCountPattern().Replace(normalized, string.Empty).Trim();
        return normalized;
    }

    private static string RemoveBrowserProfileSuffix(string value)
    {
        foreach (var profileSuffix in new[] { " - Work", " - Personal" })
        {
            if (value.EndsWith(profileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return value[..^profileSuffix.Length].TrimEnd();
            }
        }

        return value;
    }

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
