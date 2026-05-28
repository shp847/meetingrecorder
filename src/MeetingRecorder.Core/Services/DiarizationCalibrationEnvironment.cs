using System.Globalization;

namespace MeetingRecorder.Core.Services;

public sealed record DiarizationThresholdOptions(
    float DefaultClusteringThreshold,
    IReadOnlyList<float> CollapsedSpeakerClusteringThresholds,
    IReadOnlyList<float> OverSegmentedSpeakerClusteringThresholds);

public sealed record DiarizationClusterSelectionOptions(
    TimeSpan AbsoluteMinimumSpeakerDuration,
    TimeSpan MaximumMinimumSpeakerDuration,
    double MinimumSpeakerDurationShare,
    int MaximumPreferredAutomaticSpeakerCount);

public sealed record SpeakerClusterMergeOptions(
    TimeSpan TinyClusterMaximumDuration,
    TimeSpan SmallClusterMaximumDuration,
    double SmallClusterDurationShare,
    double HighConfidenceMergeThreshold,
    double SmallClusterMergeThreshold);

public static class DiarizationCalibrationEnvironment
{
    public const string DefaultClusteringThresholdVariable = "MEETING_RECORDER_DIARIZATION_DEFAULT_THRESHOLD";
    public const string CollapsedClusteringThresholdsVariable = "MEETING_RECORDER_DIARIZATION_COLLAPSED_THRESHOLDS";
    public const string OverSegmentedClusteringThresholdsVariable = "MEETING_RECORDER_DIARIZATION_OVERSEGMENTED_THRESHOLDS";
    public const string MinimumSpeakerDurationSecondsVariable = "MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SECONDS";
    public const string MaximumMinimumSpeakerDurationSecondsVariable = "MEETING_RECORDER_DIARIZATION_MAX_MIN_SPEAKER_DURATION_SECONDS";
    public const string MinimumSpeakerDurationShareVariable = "MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SHARE";
    public const string MaximumPreferredSpeakerCountVariable = "MEETING_RECORDER_DIARIZATION_MAX_PREFERRED_SPEAKER_COUNT";
    public const string TinyClusterMaximumSecondsVariable = "MEETING_RECORDER_SPEAKER_CLUSTER_TINY_MAX_SECONDS";
    public const string SmallClusterMaximumSecondsVariable = "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_MAX_SECONDS";
    public const string SmallClusterDurationShareVariable = "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_SHARE";
    public const string HighConfidenceMergeThresholdVariable = "MEETING_RECORDER_SPEAKER_CLUSTER_HIGH_CONFIDENCE_THRESHOLD";
    public const string SmallClusterMergeThresholdVariable = "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_THRESHOLD";
    public const string SpeakerNameAutoApplyConfidenceThresholdVariable = "MEETING_RECORDER_SPEAKER_NAME_AUTO_APPLY_CONFIDENCE_THRESHOLD";
    public const string SpeakerNameSuggestionConfidenceThresholdVariable = "MEETING_RECORDER_SPEAKER_NAME_SUGGESTION_CONFIDENCE_THRESHOLD";
    public const string SpeakerNameMatchMarginThresholdVariable = "MEETING_RECORDER_SPEAKER_NAME_MATCH_MARGIN_THRESHOLD";
    public const string SpeakerNameMinimumAutoApplyProfileSamplesVariable = "MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_PROFILE_SAMPLES";
    public const string SpeakerNameMinimumAutoApplySpeechSecondsVariable = "MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_SPEECH_SECONDS";

    private static readonly float[] DefaultCollapsedSpeakerClusteringThresholds = [0.45f, 0.4f, 0.35f, 0.3f, 0.25f];
    private static readonly float[] DefaultOverSegmentedSpeakerClusteringThresholds = [0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.8f, 0.85f, 0.9f, 0.95f, 0.96f, 0.97f, 0.98f, 0.99f];

    public static DiarizationThresholdOptions LoadDiarizationThresholdOptions()
    {
        return LoadDiarizationThresholdOptions(Environment.GetEnvironmentVariable);
    }

    public static DiarizationClusterSelectionOptions LoadClusterSelectionOptions()
    {
        return LoadClusterSelectionOptions(Environment.GetEnvironmentVariable);
    }

    public static SpeakerClusterMergeOptions LoadSpeakerClusterMergeOptions()
    {
        return LoadSpeakerClusterMergeOptions(Environment.GetEnvironmentVariable);
    }

    public static SpeakerNameRecognitionOptions LoadSpeakerNameRecognitionOptions(
        SpeakerNameRecognitionOptions fallback)
    {
        return LoadSpeakerNameRecognitionOptions(fallback, Environment.GetEnvironmentVariable);
    }

    internal static DiarizationThresholdOptions LoadDiarizationThresholdOptions(Func<string, string?> getValue)
    {
        return new DiarizationThresholdOptions(
            ReadFloat(getValue, DefaultClusteringThresholdVariable, 0.5f, minimum: 0.01f, maximum: 1f),
            ReadFloatArray(getValue, CollapsedClusteringThresholdsVariable, DefaultCollapsedSpeakerClusteringThresholds),
            ReadFloatArray(getValue, OverSegmentedClusteringThresholdsVariable, DefaultOverSegmentedSpeakerClusteringThresholds));
    }

    internal static DiarizationClusterSelectionOptions LoadClusterSelectionOptions(Func<string, string?> getValue)
    {
        return new DiarizationClusterSelectionOptions(
            ReadSeconds(getValue, MinimumSpeakerDurationSecondsVariable, TimeSpan.FromSeconds(2), minimumSeconds: 0.1d, maximumSeconds: 60d),
            ReadSeconds(getValue, MaximumMinimumSpeakerDurationSecondsVariable, TimeSpan.FromSeconds(15), minimumSeconds: 0.1d, maximumSeconds: 120d),
            ReadDouble(getValue, MinimumSpeakerDurationShareVariable, 0.005d, minimum: 0d, maximum: 1d),
            ReadInt(getValue, MaximumPreferredSpeakerCountVariable, 8, minimum: 2, maximum: 16));
    }

    internal static SpeakerClusterMergeOptions LoadSpeakerClusterMergeOptions(Func<string, string?> getValue)
    {
        return new SpeakerClusterMergeOptions(
            ReadSeconds(getValue, TinyClusterMaximumSecondsVariable, TimeSpan.FromSeconds(15), minimumSeconds: 0d, maximumSeconds: 300d),
            ReadSeconds(getValue, SmallClusterMaximumSecondsVariable, TimeSpan.FromSeconds(30), minimumSeconds: 0d, maximumSeconds: 600d),
            ReadDouble(getValue, SmallClusterDurationShareVariable, 0.03d, minimum: 0d, maximum: 1d),
            ReadDouble(getValue, HighConfidenceMergeThresholdVariable, 0.88d, minimum: 0.01d, maximum: 1d),
            ReadDouble(getValue, SmallClusterMergeThresholdVariable, 0.60d, minimum: 0.01d, maximum: 1d));
    }

    internal static SpeakerNameRecognitionOptions LoadSpeakerNameRecognitionOptions(
        SpeakerNameRecognitionOptions fallback,
        Func<string, string?> getValue)
    {
        return new SpeakerNameRecognitionOptions(
            ReadDouble(getValue, SpeakerNameAutoApplyConfidenceThresholdVariable, fallback.AutoApplyConfidenceThreshold, minimum: 0.01d, maximum: 1d),
            ReadDouble(getValue, SpeakerNameSuggestionConfidenceThresholdVariable, fallback.SuggestionConfidenceThreshold, minimum: 0.01d, maximum: 1d),
            ReadDouble(getValue, SpeakerNameMatchMarginThresholdVariable, fallback.MatchMarginThreshold, minimum: 0d, maximum: 1d),
            ReadInt(getValue, SpeakerNameMinimumAutoApplyProfileSamplesVariable, fallback.MinimumAutoApplyProfileSampleCount, minimum: 1, maximum: 100),
            ReadSeconds(getValue, SpeakerNameMinimumAutoApplySpeechSecondsVariable, fallback.MinimumAutoApplySpeechDuration, minimumSeconds: 0d, maximumSeconds: 600d));
    }

    private static float[] ReadFloatArray(
        Func<string, string?> getValue,
        string variableName,
        IReadOnlyList<float> fallback)
    {
        var rawValue = getValue(variableName);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback.ToArray();
        }

        var values = rawValue
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => TryParseFloat(item, out var value) && value is >= 0.01f and <= 1f ? value : (float?)null)
            .ToArray();
        return values.Length == 0 || values.Any(value => value is null)
            ? fallback.ToArray()
            : values.Select(value => value!.Value).ToArray();
    }

    private static float ReadFloat(
        Func<string, string?> getValue,
        string variableName,
        float fallback,
        float minimum,
        float maximum)
    {
        return TryParseFloat(getValue(variableName), out var value) && value >= minimum && value <= maximum
            ? value
            : fallback;
    }

    private static double ReadDouble(
        Func<string, string?> getValue,
        string variableName,
        double fallback,
        double minimum,
        double maximum)
    {
        return double.TryParse(getValue(variableName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            value >= minimum &&
            value <= maximum
            ? value
            : fallback;
    }

    private static int ReadInt(
        Func<string, string?> getValue,
        string variableName,
        int fallback,
        int minimum,
        int maximum)
    {
        return int.TryParse(getValue(variableName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value >= minimum &&
            value <= maximum
            ? value
            : fallback;
    }

    private static TimeSpan ReadSeconds(
        Func<string, string?> getValue,
        string variableName,
        TimeSpan fallback,
        double minimumSeconds,
        double maximumSeconds)
    {
        return double.TryParse(getValue(variableName), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds >= minimumSeconds &&
            seconds <= maximumSeconds
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }

    private static bool TryParseFloat(string? value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
