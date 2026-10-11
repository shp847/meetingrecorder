using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public enum RecordingReadinessRemediationTarget
{
    None = 0,
    SettingsSetup = 1,
    SettingsFilesAndUpdates = 2,
    WindowsRecordingPrivacy = 3,
    SettingsRecording = 4,
    SettingsAdvanced = 5,
}

public enum RecordingPermissionReadiness
{
    Unknown = 0,
    Available = 1,
    Blocked = 2,
}

public sealed record RecordingReadinessInput(
    bool HasLocalTranscription,
    bool HasWritableOutput,
    RecordingPermissionReadiness RecordingPermission,
    bool HasOptionalSpeakerLabeling,
    bool IsMicrophoneCaptureEnabled,
    bool IsAutoDetectionEnabled,
    TeamsCapabilityStatus TeamsCapabilityStatus,
    bool HasAdvancedProviderConfiguration);

public sealed record RecordingReadinessItem(
    bool IsReady,
    bool IsBlocking,
    string Summary,
    RecordingReadinessRemediationTarget RemediationTarget);

/// <summary>
/// Read-only, local-ready projection for the recording journey. It does not
/// validate paths, initiate downloads, request permissions, or change config.
/// Callers supply already-known capability facts and render this result.
/// </summary>
public sealed record RecordingReadinessSnapshot(
    bool IsReadyToRecord,
    RecordingReadinessItem LocalTranscription,
    RecordingReadinessItem OutputLocation,
    RecordingReadinessItem RecordingPermission,
    RecordingReadinessItem SpeakerLabeling,
    RecordingReadinessItem MicrophoneCapture,
    RecordingReadinessItem AutoDetection,
    RecordingReadinessItem TeamsIntegration,
    RecordingReadinessItem AdvancedProvider,
    RecordingReadinessItem? PrimaryBlocker,
    IReadOnlyList<RecordingReadinessItem> Notices);

public sealed class RecordingReadinessService
{
    public RecordingReadinessSnapshot Project(RecordingReadinessInput input)
    {
        var transcription = input.HasLocalTranscription
            ? Ready("Local transcription ready.")
            : Blocking("Local transcription needs setup.", RecordingReadinessRemediationTarget.SettingsSetup);
        var output = input.HasWritableOutput
            ? Ready("Meeting output location ready.")
            : Blocking("Choose a writable meeting output location.", RecordingReadinessRemediationTarget.SettingsFilesAndUpdates);
        var permission = input.RecordingPermission switch
        {
            RecordingPermissionReadiness.Available => Ready("Windows recording permission ready."),
            RecordingPermissionReadiness.Blocked => Blocking(
                "Windows recording permission is needed.",
                RecordingReadinessRemediationTarget.WindowsRecordingPrivacy),
            _ => Notice(
                "Windows recording access is checked when capture starts.",
                RecordingReadinessRemediationTarget.WindowsRecordingPrivacy),
        };
        var speakerLabeling = input.HasOptionalSpeakerLabeling
            ? Ready("Optional speaker labeling ready.")
            : Notice("Speaker labeling is optional. Transcripts still publish normally.", RecordingReadinessRemediationTarget.SettingsSetup);
        var microphone = input.IsMicrophoneCaptureEnabled
            ? Ready("Microphone capture is on for future recordings.")
            : Notice("Microphone capture is off. Meeting audio still records.", RecordingReadinessRemediationTarget.SettingsRecording);
        var detection = input.IsAutoDetectionEnabled
            ? Ready("Automatic meeting detection is on.")
            : Notice("Automatic detection is off. Manual Start and Stop remain available.", RecordingReadinessRemediationTarget.SettingsRecording);
        var teams = ProjectTeamsIntegration(input.TeamsCapabilityStatus);
        var advancedProvider = input.HasAdvancedProviderConfiguration
            ? Notice("An advanced provider configuration is active.", RecordingReadinessRemediationTarget.SettingsAdvanced)
            : Ready("Built-in local processing is active.");

        var primaryBlocker = new[] { transcription, output, permission }.FirstOrDefault(item => item.IsBlocking);
        var notices = new[] { speakerLabeling, microphone, detection, teams, advancedProvider }
            .Where(item => !item.IsReady)
            .ToArray();

        return new RecordingReadinessSnapshot(
            primaryBlocker is null,
            transcription,
            output,
            permission,
            speakerLabeling,
            microphone,
            detection,
            teams,
            advancedProvider,
            primaryBlocker,
            notices);
    }

    public static RecordingReadinessItem ProjectTeamsIntegration(TeamsCapabilityStatus status)
    {
        return status switch
        {
            TeamsCapabilityStatus.FallbackOnly => Ready("Local detector active."),
            TeamsCapabilityStatus.ThirdPartyApiUsable or
                TeamsCapabilityStatus.CalendarBacked or
                TeamsCapabilityStatus.CalendarAndOnlineMeeting => Ready("Official integration available."),
            TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly =>
                Notice("Limited. The local detector remains available.", RecordingReadinessRemediationTarget.SettingsSetup),
            TeamsCapabilityStatus.BlockedByPolicyOrConsent or
                TeamsCapabilityStatus.SignedOut =>
                Notice("Blocked. The local detector remains available.", RecordingReadinessRemediationTarget.SettingsSetup),
            _ => Notice("Teams integration status is unavailable. The local detector remains available.", RecordingReadinessRemediationTarget.SettingsSetup),
        };
    }

    private static RecordingReadinessItem Ready(string summary) =>
        new(true, false, summary, RecordingReadinessRemediationTarget.None);

    private static RecordingReadinessItem Notice(string summary, RecordingReadinessRemediationTarget target) =>
        new(false, false, summary, target);

    private static RecordingReadinessItem Blocking(string summary, RecordingReadinessRemediationTarget target) =>
        new(false, true, summary, target);
}
