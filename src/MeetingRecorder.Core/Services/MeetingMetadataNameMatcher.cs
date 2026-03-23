using System.Text;

namespace MeetingRecorder.Core.Services;

public static class MeetingMetadataNameMatcher
{
    public static bool AreReasonableMatch(string? left, string? right)
    {
        var normalizedLeft = NormalizeDisplayName(left);
        var normalizedRight = NormalizeDisplayName(right);
        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return false;
        }

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftTokens = Tokenize(normalizedLeft);
        var rightTokens = Tokenize(normalizedRight);
        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return false;
        }

        IReadOnlyList<string> shorterTokens;
        IReadOnlySet<string> longerTokenSet;
        if (leftTokens.Length <= rightTokens.Length)
        {
            shorterTokens = leftTokens;
            longerTokenSet = new HashSet<string>(rightTokens, StringComparer.Ordinal);
        }
        else
        {
            shorterTokens = rightTokens;
            longerTokenSet = new HashSet<string>(leftTokens, StringComparer.Ordinal);
        }

        if (shorterTokens.Count == 1)
        {
            return shorterTokens[0].Length >= 4 && longerTokenSet.Contains(shorterTokens[0]);
        }

        return shorterTokens.All(longerTokenSet.Contains);
    }

    public static string ChoosePreferredDisplayName(string currentName, string candidateName)
    {
        var normalizedCurrent = NormalizeDisplayName(currentName);
        var normalizedCandidate = NormalizeDisplayName(candidateName);
        if (string.IsNullOrWhiteSpace(normalizedCurrent))
        {
            return normalizedCandidate;
        }

        if (string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return normalizedCurrent;
        }

        if (string.Equals(normalizedCurrent, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCurrent.Length >= normalizedCandidate.Length
                ? normalizedCurrent
                : normalizedCandidate;
        }

        var currentTokens = Tokenize(normalizedCurrent);
        var candidateTokens = Tokenize(normalizedCandidate);
        if (candidateTokens.Length > currentTokens.Length)
        {
            return normalizedCandidate;
        }

        if (currentTokens.Length > candidateTokens.Length)
        {
            return normalizedCurrent;
        }

        return normalizedCandidate.Length > normalizedCurrent.Length
            ? normalizedCandidate
            : normalizedCurrent;
    }

    public static IReadOnlyList<string> MergeNames(
        IReadOnlyList<string>? existingNames,
        IReadOnlyList<string>? candidateNames)
    {
        var merged = new List<string>();
        AddNames(existingNames, merged);
        AddNames(candidateNames, merged);
        return merged.ToArray();
    }

    public static string NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    private static void AddNames(IReadOnlyList<string>? names, IList<string> merged)
    {
        if (names is null || names.Count == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            var normalizedName = NormalizeDisplayName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var matchIndex = FindMatchIndex(merged, normalizedName);
            if (matchIndex < 0)
            {
                merged.Add(normalizedName);
                continue;
            }

            merged[matchIndex] = ChoosePreferredDisplayName(merged[matchIndex], normalizedName);
        }
    }

    private static int FindMatchIndex(IList<string> existingNames, string candidate)
    {
        for (var index = 0; index < existingNames.Count; index++)
        {
            if (AreReasonableMatch(existingNames[index], candidate))
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] Tokenize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
