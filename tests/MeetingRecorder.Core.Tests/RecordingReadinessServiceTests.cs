using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class RecordingReadinessServiceTests
{
    private readonly RecordingReadinessService _service = new();

    [Fact]
    public void Project_Requires_Only_Local_Transcription_Output_And_Recording_Permission()
    {
        var snapshot = _service.Project(new RecordingReadinessInput(
            HasLocalTranscription: true,
            HasWritableOutput: true,
            RecordingPermission: RecordingPermissionReadiness.Available,
            HasOptionalSpeakerLabeling: false,
            IsMicrophoneCaptureEnabled: false,
            IsAutoDetectionEnabled: false,
            TeamsCapabilityStatus.FallbackOnly,
            HasAdvancedProviderConfiguration: false));

        Assert.True(snapshot.IsReadyToRecord);
        Assert.Null(snapshot.PrimaryBlocker);
        Assert.False(snapshot.SpeakerLabeling.IsBlocking);
        Assert.False(snapshot.MicrophoneCapture.IsBlocking);
        Assert.False(snapshot.AutoDetection.IsBlocking);
        Assert.Contains(snapshot.Notices, item => item == snapshot.SpeakerLabeling);
        Assert.Contains("manual Start and Stop", snapshot.AutoDetection.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, true, RecordingPermissionReadiness.Available, RecordingReadinessRemediationTarget.SettingsSetup)]
    [InlineData(true, false, RecordingPermissionReadiness.Available, RecordingReadinessRemediationTarget.SettingsFilesAndUpdates)]
    [InlineData(true, true, RecordingPermissionReadiness.Blocked, RecordingReadinessRemediationTarget.WindowsRecordingPrivacy)]
    public void Project_Uses_One_Ordered_Primary_Blocker(
        bool transcriptionReady,
        bool outputReady,
        RecordingPermissionReadiness recordingPermission,
        RecordingReadinessRemediationTarget expectedTarget)
    {
        var snapshot = _service.Project(new RecordingReadinessInput(
            transcriptionReady,
            outputReady,
            recordingPermission,
            HasOptionalSpeakerLabeling: true,
            IsMicrophoneCaptureEnabled: false,
            IsAutoDetectionEnabled: false,
            TeamsCapabilityStatus.FallbackOnly,
            HasAdvancedProviderConfiguration: false));

        Assert.False(snapshot.IsReadyToRecord);
        Assert.NotNull(snapshot.PrimaryBlocker);
        Assert.Equal(expectedTarget, snapshot.PrimaryBlocker!.RemediationTarget);
    }

    [Theory]
    [InlineData(TeamsCapabilityStatus.FallbackOnly, "Local detector active.")]
    [InlineData(TeamsCapabilityStatus.CalendarAndOnlineMeeting, "Official integration available.")]
    [InlineData(TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly, "limited")]
    [InlineData(TeamsCapabilityStatus.BlockedByPolicyOrConsent, "blocked")]
    public void Project_Uses_Compact_Teams_Capability_Copy(TeamsCapabilityStatus status, string expectedCopy)
    {
        var snapshot = _service.Project(ReadyInput(teamsStatus: status));

        Assert.Contains(expectedCopy, snapshot.TeamsIntegration.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process", snapshot.TeamsIntegration.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", snapshot.TeamsIntegration.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_Does_Not_Expose_Paths_Secrets_Or_Provider_Tuning()
    {
        var snapshot = _service.Project(ReadyInput(advancedProviderConfigured: true));
        var copy = string.Join(" ", new[]
        {
            snapshot.LocalTranscription.Summary,
            snapshot.OutputLocation.Summary,
            snapshot.RecordingPermission.Summary,
            snapshot.AdvancedProvider.Summary,
        });

        Assert.DoesNotContain(":\\", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("key", copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("URL", copy, StringComparison.OrdinalIgnoreCase);
        Assert.False(snapshot.AdvancedProvider.IsBlocking);
    }

    [Fact]
    public void Project_Treats_Unknown_Windows_Recording_Access_As_A_Notice()
    {
        var snapshot = _service.Project(ReadyInput(recordingPermission: RecordingPermissionReadiness.Unknown));

        Assert.True(snapshot.IsReadyToRecord);
        Assert.False(snapshot.RecordingPermission.IsBlocking);
        Assert.Contains("checked when capture starts", snapshot.RecordingPermission.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static RecordingReadinessInput ReadyInput(
        TeamsCapabilityStatus teamsStatus = TeamsCapabilityStatus.FallbackOnly,
        bool advancedProviderConfigured = false,
        RecordingPermissionReadiness recordingPermission = RecordingPermissionReadiness.Available) =>
        new(
            HasLocalTranscription: true,
            HasWritableOutput: true,
            RecordingPermission: recordingPermission,
            HasOptionalSpeakerLabeling: true,
            IsMicrophoneCaptureEnabled: false,
            IsAutoDetectionEnabled: false,
            teamsStatus,
            advancedProviderConfigured);
}
