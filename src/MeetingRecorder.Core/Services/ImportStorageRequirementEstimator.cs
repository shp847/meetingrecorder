namespace MeetingRecorder.Core.Services;

/// <summary>
/// Conservative local-only disk estimates for an import's app-owned copy and
/// normalized audio. This is an admission guard, not a quota reservation.
/// </summary>
public static class ImportStorageRequirementEstimator
{
    private const long FixedWorkOverheadBytes = 16L * 1024 * 1024;
    private const long FixedOutputOverheadBytes = 8L * 1024 * 1024;
    private const long NormalizedAudioBytesPerSecond = 384_000;

    public static ImportStorageRequirements Estimate(long sourceSizeBytes, TimeSpan? duration)
    {
        if (sourceSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSizeBytes));
        }

        var normalizedAudioBytes = duration is { } knownDuration && knownDuration > TimeSpan.Zero
            ? SaturatingMultiply((long)Math.Ceiling(knownDuration.TotalSeconds), NormalizedAudioBytesPerSecond)
            : SaturatingMultiply(sourceSizeBytes, 4);
        var workBytes = SaturatingAdd(
            SaturatingAdd(sourceSizeBytes, normalizedAudioBytes),
            FixedWorkOverheadBytes);
        var outputBytes = SaturatingAdd(
            Math.Max(sourceSizeBytes, normalizedAudioBytes),
            FixedOutputOverheadBytes);
        return new ImportStorageRequirements(workBytes, outputBytes);
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long SaturatingMultiply(long left, long right) =>
        left == 0 || right == 0
            ? 0
            : left > long.MaxValue / right
                ? long.MaxValue
                : left * right;
}

public sealed record ImportStorageRequirements(
    long RequiredWorkBytes,
    long RequiredOutputBytes);
