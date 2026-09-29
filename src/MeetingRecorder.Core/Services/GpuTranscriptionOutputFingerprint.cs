using MeetingRecorder.Core.Processing;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Redacted structural evidence for an isolated CPU/GPU transcription spike.
/// It deliberately records shape only; transcript text and file locations stay
/// with the disposable fixture runner and are never part of a comparison report.
/// </summary>
public sealed record GpuTranscriptionSegmentShape(
    long StartMilliseconds,
    long EndMilliseconds,
    int CharacterCount);

public sealed record GpuTranscriptionOutputFingerprint(
    string Language,
    int SegmentCount,
    int CharacterCount,
    bool IsSchemaValid,
    IReadOnlyList<GpuTranscriptionSegmentShape> Segments)
{
    public static GpuTranscriptionOutputFingerprint Create(TranscriptionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var shapes = result.Segments
            .Select(segment => new GpuTranscriptionSegmentShape(
                (long)Math.Round(segment.Start.TotalMilliseconds),
                (long)Math.Round(segment.End.TotalMilliseconds),
                string.IsNullOrWhiteSpace(segment.Text) ? 0 : segment.Text.Length))
            .ToArray();
        var isSchemaValid = HasValidSchema(shapes);
        return new GpuTranscriptionOutputFingerprint(
            string.IsNullOrWhiteSpace(result.Language) ? "unknown" : result.Language.Trim().ToLowerInvariant(),
            shapes.Length,
            shapes.Sum(segment => segment.CharacterCount),
            isSchemaValid,
            shapes);
    }

    public static GpuTranscriptionOutputComparison Compare(
        GpuTranscriptionOutputFingerprint cpu,
        GpuTranscriptionOutputFingerprint candidate,
        TimeSpan timestampTolerance)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(candidate);
        if (timestampTolerance < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampTolerance));
        }

        var isSchemaCompatible = cpu.IsSchemaValid &&
                                 candidate.IsSchemaValid &&
                                 string.Equals(cpu.Language, candidate.Language, StringComparison.OrdinalIgnoreCase) &&
                                 cpu.SegmentCount == candidate.SegmentCount;
        var toleranceMilliseconds = (long)Math.Round(timestampTolerance.TotalMilliseconds);
        var isTimestampCompatible = isSchemaCompatible &&
                                    cpu.Segments.Zip(candidate.Segments).All(pair =>
                                        Math.Abs(pair.First.StartMilliseconds - pair.Second.StartMilliseconds) <= toleranceMilliseconds &&
                                        Math.Abs(pair.First.EndMilliseconds - pair.Second.EndMilliseconds) <= toleranceMilliseconds);
        return new GpuTranscriptionOutputComparison(
            isSchemaCompatible,
            isTimestampCompatible,
            RequiresManualQualityReview: true);
    }

    private static bool HasValidSchema(IReadOnlyList<GpuTranscriptionSegmentShape> segments)
    {
        long previousStartMilliseconds = -1;
        foreach (var segment in segments)
        {
            if (segment.StartMilliseconds < 0 ||
                segment.EndMilliseconds < segment.StartMilliseconds ||
                segment.CharacterCount < 0 ||
                segment.StartMilliseconds < previousStartMilliseconds)
            {
                return false;
            }

            previousStartMilliseconds = segment.StartMilliseconds;
        }

        return true;
    }
}

public sealed record GpuTranscriptionOutputComparison(
    bool IsSchemaCompatible,
    bool IsTimestampCompatible,
    bool RequiresManualQualityReview);
