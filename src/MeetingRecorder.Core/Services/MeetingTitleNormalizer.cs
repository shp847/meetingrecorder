using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static partial class MeetingTitleNormalizer
{
    [GeneratedRegex("[^\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericPattern();

    [GeneratedRegex("\\b([a-z]{3}-[a-z]{4}-[a-z]{3})\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoogleMeetCodePattern();

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

        var meetCodeMatch = GoogleMeetCodePattern().Match(normalized);
        if (meetCodeMatch.Success)
        {
            normalized = meetCodeMatch.Groups[1].Value;
        }

        normalized = normalized.Trim('|', ' ');
        normalized = NonAlphaNumericPattern().Replace(normalized, " ");
        return CollapseWhitespace(normalized).ToLowerInvariant();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
