namespace MeetingRecorder.Core.Services;

public sealed record WhisperModelStatusDisplayState(
    string StatusText,
    string DetailsText,
    bool IsHealthy,
    string? DashboardBannerText);

public static class WhisperModelStatusDisplayStateFactory
{
    public static WhisperModelStatusDisplayState Create(WhisperModelStatus status)
    {
        var sizeText = status.FileSizeBytes > 0
            ? $" File size: {FormatBytes(status.FileSizeBytes)}."
            : string.Empty;

        return status.Kind switch
        {
            WhisperModelStatusKind.Valid => new WhisperModelStatusDisplayState(
                "Model status: ready",
                $"{status.Message}{sizeText}",
                true,
                null),
            WhisperModelStatusKind.Missing => new WhisperModelStatusDisplayState(
                "Model status: missing",
                $"{status.Message}{sizeText}",
                false,
                "Transcription model missing. Open the Models tab to download or import a valid ggml Whisper model before generating transcripts."),
            WhisperModelStatusKind.Invalid => new WhisperModelStatusDisplayState(
                "Model status: invalid",
                $"{status.Message}{sizeText}",
                false,
                "Transcription model invalid. Open the Models tab to replace the configured Whisper model before generating transcripts."),
            _ => CreateError("Unknown Whisper model status."),
        };
    }

    public static WhisperModelStatusDisplayState CreateError(string message)
    {
        return new WhisperModelStatusDisplayState(
            "Model status: error",
            message,
            false,
            "Unable to inspect the configured Whisper model. Open the Models tab to review the model path and file.");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000)
        {
            return $"{bytes / 1_000_000_000d:0.##} GB";
        }

        if (bytes >= 1_000_000)
        {
            return $"{bytes / 1_000_000d:0.##} MB";
        }

        if (bytes >= 1_000)
        {
            return $"{bytes / 1_000d:0.##} KB";
        }

        return $"{bytes} bytes";
    }
}
