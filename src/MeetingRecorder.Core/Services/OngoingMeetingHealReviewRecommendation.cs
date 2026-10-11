using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Local, expiring evidence that review-only mode found one heal candidate.
/// It deliberately contains only opaque session identifiers and eligibility;
/// it is not a transaction receipt and confers no mutation authority.
/// </summary>
public sealed record OngoingMeetingHealReviewRecommendation(
    int SchemaVersion,
    string PredecessorSessionId,
    string SuccessorSessionId,
    OngoingMeetingHealEligibility Eligibility,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public const int CurrentSchemaVersion = 1;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
}

internal sealed class OngoingMeetingHealReviewRecommendationStore
{
    private readonly string _directory;

    public OngoingMeetingHealReviewRecommendationStore(string workDirectory) =>
        _directory = Path.Combine(Path.GetFullPath(workDirectory), ".ongoing-heal", "review");

    public async Task SaveAsync(
        OngoingMeetingHealReviewRecommendation recommendation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        if (recommendation.SchemaVersion != OngoingMeetingHealReviewRecommendation.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(recommendation.PredecessorSessionId) ||
            string.IsNullOrWhiteSpace(recommendation.SuccessorSessionId) ||
            recommendation.ExpiresAtUtc <= recommendation.EvaluatedAtUtc)
        {
            throw new ArgumentException("Invalid review-only heal recommendation.", nameof(recommendation));
        }

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, GetFileName(recommendation.PredecessorSessionId, recommendation.SuccessorSessionId));
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(recommendation), cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    internal static string GetFileName(string predecessorSessionId, string successorSessionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{predecessorSessionId}\n{successorSessionId}"));
        return Convert.ToHexString(bytes).ToLowerInvariant() + ".json";
    }
}
