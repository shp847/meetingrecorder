using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static partial class MeetingTitleNormalizer
{
    [GeneratedRegex("[^\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericPattern();

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

        normalized = normalized.Trim('|', ' ');
        normalized = NonAlphaNumericPattern().Replace(normalized, " ");
        return CollapseWhitespace(normalized).ToLowerInvariant();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
