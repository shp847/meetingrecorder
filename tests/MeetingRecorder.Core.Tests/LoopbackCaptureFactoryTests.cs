using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Domain;
using NAudio.CoreAudioApi;

namespace MeetingRecorder.Core.Tests;

public sealed class LoopbackCaptureFactoryTests
{
    [Fact]
    public void Evaluate_Returns_Selection_Metadata_For_The_Preferred_Endpoint()
    {
        var factory = new SystemLoopbackCaptureFactory(
            (role, _) => role == Role.Multimedia
                ? new LoopbackCaptureProbeSnapshot(
                    Role.Multimedia,
                    "render-multimedia",
                    "Laptop speakers",
                    0d,
                    false,
                    Array.Empty<AudioSourceSessionSnapshot>())
                : new LoopbackCaptureProbeSnapshot(
                    Role.Communications,
                    "render-communications",
                    "USB headset",
                    0.42d,
                    true,
                    [
                        new AudioSourceSessionSnapshot(
                            4242,
                            "ms-teams",
                            0.42d,
                            true,
                            false,
                            false,
                            "Microsoft Teams",
                            "teams-session"),
                    ]),
            static _ => throw new NotSupportedException(),
            static _ => throw new NotSupportedException());

        var result = factory.Evaluate(
            MeetingPlatform.Teams,
            detectedAudioSource: null,
            activityThreshold: 0.05d);

        Assert.Equal(Role.Communications, result.PreferredSelection.Role);
        Assert.Equal("render-communications", result.PreferredSelection.DeviceId);
        Assert.Equal("USB headset", result.PreferredSelection.FriendlyName);
        Assert.False(result.PreferredSelection.IsFallbackCapture);
        Assert.Equal(1, result.PreferredSelection.MeetingSessionMatches);
        Assert.Contains("communications", result.PreferredSelection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectPreferredEndpointRole_Prefers_Communications_When_GoogleMeet_Metadata_Is_Only_There()
    {
        var multimedia = new LoopbackCaptureProbeSnapshot(
            Role.Multimedia,
            "render-multimedia",
            "Laptop speakers",
            0.30d,
            true,
            [
                new AudioSourceSessionSnapshot(
                    9001,
                    "spotify",
                    0.30d,
                    true,
                    false,
                    false,
                    "Spotify",
                    "spotify-session"),
            ]);
        var communications = new LoopbackCaptureProbeSnapshot(
            Role.Communications,
            "render-communications",
            "Bluetooth headset",
            0.12d,
            true,
            [
                new AudioSourceSessionSnapshot(
                    5001,
                    "msedge",
                    0.12d,
                    true,
                    false,
                    false,
                    "Meet - abc-defg-hij",
                    "https://meet.google.com/abc-defg-hij"),
            ]);

        var result = SystemLoopbackCaptureFactory.SelectPreferredEndpointRole(
            MeetingPlatform.GoogleMeet,
            detectedAudioSource: new DetectedAudioSource(
                "Google Meet",
                "Meet - abc-defg-hij - Work - Microsoft Edge",
                "Meet - abc-defg-hij",
                AudioSourceMatchKind.BrowserTab,
                AudioSourceConfidence.High,
                DateTimeOffset.UtcNow),
            multimedia,
            communications);

        Assert.Equal(Role.Communications, result);
    }

    [Fact]
    public void SelectPreferredEndpointRole_Keeps_Multimedia_When_Communications_Is_The_Same_Device()
    {
        var multimedia = new LoopbackCaptureProbeSnapshot(
            Role.Multimedia,
            "shared-device",
            "USB headset",
            0.18d,
            true,
            Array.Empty<AudioSourceSessionSnapshot>());
        var communications = new LoopbackCaptureProbeSnapshot(
            Role.Communications,
            "shared-device",
            "USB headset",
            0.18d,
            true,
            Array.Empty<AudioSourceSessionSnapshot>());

        var result = SystemLoopbackCaptureFactory.SelectPreferredEndpointRole(
            MeetingPlatform.Teams,
            detectedAudioSource: null,
            multimedia,
            communications);

        Assert.Equal(Role.Multimedia, result);
    }

    [Fact]
    public void Evaluate_Falls_Back_To_The_Default_Loopback_Device_When_No_Endpoint_Can_Be_Resolved()
    {
        var factory = new SystemLoopbackCaptureFactory(
            static (_, _) => null,
            static _ => throw new NotSupportedException(),
            static _ => throw new NotSupportedException());

        var result = factory.Evaluate(
            MeetingPlatform.Manual,
            detectedAudioSource: null,
            activityThreshold: 0.05d);

        Assert.True(result.PreferredSelection.IsFallbackCapture);
        Assert.Equal("Windows default render device", result.PreferredSelection.FriendlyName);
        Assert.Contains("default", result.PreferredSelection.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
