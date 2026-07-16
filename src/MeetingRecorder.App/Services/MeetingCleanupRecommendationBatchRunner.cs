using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal static class MeetingCleanupRecommendationBatchRunner
{
    public static async Task<MeetingCleanupRecommendationBatchResult> ExecuteAsync(
        IReadOnlyList<MeetingCleanupRecommendation> recommendations,
        Func<MeetingCleanupRecommendation, CancellationToken, Task> applyAsync,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        var results = new List<MeetingCleanupRecommendationBatchItemResult>(recommendations.Count);
        foreach (var recommendation in recommendations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await applyAsync(recommendation, cancellationToken);
                results.Add(new MeetingCleanupRecommendationBatchItemResult(recommendation, true, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new MeetingCleanupRecommendationBatchItemResult(
                    recommendation,
                    false,
                    string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message));
                if (!continueOnError)
                {
                    throw;
                }
            }
        }

        return new MeetingCleanupRecommendationBatchResult(results);
    }
}

internal sealed record MeetingCleanupRecommendationBatchItemResult(
    MeetingCleanupRecommendation Recommendation,
    bool Succeeded,
    string? ErrorMessage);

internal sealed record MeetingCleanupRecommendationBatchResult(
    IReadOnlyList<MeetingCleanupRecommendationBatchItemResult> Items)
{
    public int SucceededCount => Items.Count(item => item.Succeeded);

    public int FailedCount => Items.Count - SucceededCount;
}
