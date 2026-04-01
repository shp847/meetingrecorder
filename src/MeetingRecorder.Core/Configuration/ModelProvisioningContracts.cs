namespace MeetingRecorder.Core.Configuration;

public enum TranscriptionModelProfilePreference
{
    StandardIncluded = 0,
    HighAccuracyDownloaded = 1,
    Custom = 2,
}

public enum SpeakerLabelingModelProfilePreference
{
    StandardIncluded = 0,
    HighAccuracyDownloaded = 1,
    Custom = 2,
}

public sealed record TranscriptionModelProvisioningStatus(
    TranscriptionModelProfilePreference RequestedProfile,
    TranscriptionModelProfilePreference ActiveProfile,
    bool RetryRecommended,
    string Summary,
    string Detail,
    string ActiveModelPath);

public sealed record SpeakerLabelingModelProvisioningStatus(
    SpeakerLabelingModelProfilePreference RequestedProfile,
    SpeakerLabelingModelProfilePreference ActiveProfile,
    bool RetryRecommended,
    string Summary,
    string Detail,
    string ActiveAssetPath);

public sealed record ModelProvisioningResult(
    DateTimeOffset RecordedAtUtc,
    TranscriptionModelProvisioningStatus Transcription,
    SpeakerLabelingModelProvisioningStatus SpeakerLabeling);
