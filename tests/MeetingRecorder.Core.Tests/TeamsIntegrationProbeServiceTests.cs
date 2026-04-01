using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class TeamsIntegrationProbeServiceTests
{
    [Fact]
    public void ResolveEffectiveMode_Prefers_Readable_ThirdParty_When_Auto_Mode_Is_Selected()
    {
        var config = new AppConfig
        {
            PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
            TeamsCapabilitySnapshot = new TeamsCapabilitySnapshot
            {
                Status = TeamsCapabilityStatus.ThirdPartyApiUsable,
                ThirdPartyApi = new TeamsThirdPartyApiCapability
                {
                    Status = TeamsThirdPartyApiStatus.ReadableStateAvailable,
                    SupportsReadableMeetingState = true,
                },
                ThirdPartyApiReadableStateSupported = true,
                Graph = new TeamsGraphCapability
                {
                    CalendarStatus = TeamsGraphCalendarStatus.Supported,
                    OnlineMeetingStatus = TeamsGraphOnlineMeetingStatus.Supported,
                    CalendarSupported = true,
                    OnlineMeetingSupported = true,
                },
            },
        };

        var mode = TeamsIntegrationSelectionPolicy.ResolveEffectiveMode(config);

        Assert.Equal(PreferredTeamsIntegrationMode.ThirdPartyApi, mode);
    }

    [Theory]
    [InlineData(PreferredTeamsIntegrationMode.GraphCalendar)]
    [InlineData(PreferredTeamsIntegrationMode.GraphCalendarAndOnlineMeeting)]
    public void ResolveEffectiveMode_Treats_Legacy_Graph_Modes_As_ThirdParty_Or_Fallback(PreferredTeamsIntegrationMode preferredMode)
    {
        var config = new AppConfig
        {
            PreferredTeamsIntegrationMode = preferredMode,
            TeamsCapabilitySnapshot = new TeamsCapabilitySnapshot
            {
                Status = TeamsCapabilityStatus.ThirdPartyApiUsable,
                ThirdPartyApi = new TeamsThirdPartyApiCapability
                {
                    Status = TeamsThirdPartyApiStatus.ReadableStateAvailable,
                    SupportsReadableMeetingState = true,
                },
            },
        };

        var mode = TeamsIntegrationSelectionPolicy.ResolveEffectiveMode(config);

        Assert.Equal(PreferredTeamsIntegrationMode.ThirdPartyApi, mode);
    }

    [Fact]
    public async Task RunAsync_Returns_ThirdParty_ControlOnly_When_Only_Control_Surface_Is_Available()
    {
        var service = new TeamsIntegrationProbeService(
            () => Task.FromResult<DetectionDecision?>(null),
            new StubTeamsThirdPartyApiAdapter(
                new TeamsThirdPartyApiProbeSnapshot(
                    TeamsThirdPartyApiStatus.ControlOnly,
                    ManageApiEnabled: true,
                    PairingState: TeamsPairingState.PairingAllowed,
                    SupportsReadableMeetingState: false,
                    Summary: "Third-party API is available for manual controls.",
                    Detail: "No readable meeting lifecycle data was exposed."),
                new TeamsOfficialMeetingLookupResult(TeamsOfficialMeetingLookupStatus.Unavailable, null, string.Empty)));

        var result = await service.RunAsync(new AppConfig(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly, result.CapabilitySnapshot.Status);
        Assert.Equal(TeamsThirdPartyApiStatus.ControlOnly, result.CapabilitySnapshot.ThirdPartyApi.Status);
        Assert.True(result.CapabilitySnapshot.ThirdPartyApi.ManageApiEnabled);
        Assert.Equal(TeamsPairingState.PairingAllowed, result.CapabilitySnapshot.ThirdPartyApi.PairingState);
        Assert.False(result.CapabilitySnapshot.ThirdPartyApi.SupportsReadableMeetingState);
        Assert.False(result.CapabilitySnapshot.GraphCalendarSupported);
        Assert.False(result.CapabilitySnapshot.GraphOnlineMeetingSupported);
    }

    [Fact]
    public async Task RunAsync_Returns_ThirdParty_Usable_When_Readable_State_Is_Available()
    {
        var service = new TeamsIntegrationProbeService(
            () => Task.FromResult<DetectionDecision?>(null),
            new StubTeamsThirdPartyApiAdapter(
                new TeamsThirdPartyApiProbeSnapshot(
                    TeamsThirdPartyApiStatus.ReadableStateAvailable,
                    ManageApiEnabled: true,
                    PairingState: TeamsPairingState.Paired,
                    SupportsReadableMeetingState: true,
                    Summary: "Third-party API usable.",
                    Detail: "Readable Teams meeting state is available from the paired client."),
                new TeamsOfficialMeetingLookupResult(TeamsOfficialMeetingLookupStatus.Unavailable, null, string.Empty)));

        var result = await service.RunAsync(new AppConfig(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(TeamsCapabilityStatus.ThirdPartyApiUsable, result.CapabilitySnapshot.Status);
        Assert.Equal(TeamsThirdPartyApiStatus.ReadableStateAvailable, result.CapabilitySnapshot.ThirdPartyApi.Status);
        Assert.True(result.CapabilitySnapshot.ThirdPartyApi.SupportsReadableMeetingState);
    }

    [Fact]
    public async Task RunAsync_Persists_ThirdParty_Blocked_By_Teams_Policy_Without_Promoting_It()
    {
        var service = new TeamsIntegrationProbeService(
            () => Task.FromResult<DetectionDecision?>(null),
            new StubTeamsThirdPartyApiAdapter(
                new TeamsThirdPartyApiProbeSnapshot(
                    TeamsThirdPartyApiStatus.BlockedByTeamsPolicy,
                    ManageApiEnabled: false,
                    PairingState: TeamsPairingState.Blocked,
                    SupportsReadableMeetingState: false,
                    Summary: "Third-party API blocked by Teams policy.",
                    Detail: "Teams allows third-party devices only when the org policy enables Manage API."),
                new TeamsOfficialMeetingLookupResult(TeamsOfficialMeetingLookupStatus.Unavailable, null, string.Empty)));

        var result = await service.RunAsync(new AppConfig(), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(TeamsCapabilityStatus.FallbackOnly, result.CapabilitySnapshot.Status);
        Assert.Equal(TeamsThirdPartyApiStatus.BlockedByTeamsPolicy, result.CapabilitySnapshot.ThirdPartyApi.Status);
        Assert.Equal(TeamsPairingState.Blocked, result.CapabilitySnapshot.ThirdPartyApi.PairingState);
    }

    [Fact]
    public async Task ApplyPreferredContextAsync_Uses_Validated_ThirdParty_Title_For_Teams_Decisions_And_Adds_Match_Signal()
    {
        var now = DateTimeOffset.UtcNow;
        var decision = new DetectionDecision(
            MeetingPlatform.Teams,
            true,
            true,
            1d,
            "Microsoft Teams",
            new[]
            {
                new DetectionSignal("window-title", "Microsoft Teams", 0.85d, now),
                new DetectionSignal("teams-host", "Microsoft Teams", 0.15d, now),
                new DetectionSignal("audio-activity", "peak=0.320", 0.2d, now),
            },
            "Detection confidence met the recording threshold and active system audio was present.");
        var thirdPartyContext = new TeamsOfficialMeetingContext(
            TeamsOfficialMeetingContextSource.ThirdPartyApi,
            "Client Sync",
            null,
            now.AddMinutes(-10),
            now.AddMinutes(20),
            "meeting-123");
        var arbitrator = new TeamsDetectionArbitrator(
            new StubTeamsThirdPartyApiAdapter(
                new TeamsThirdPartyApiProbeSnapshot(
                    TeamsThirdPartyApiStatus.ReadableStateAvailable,
                    true,
                    TeamsPairingState.Paired,
                    true,
                    "Third-party API usable.",
                    "Readable Teams meeting state is available."),
                new TeamsOfficialMeetingLookupResult(TeamsOfficialMeetingLookupStatus.Matched, thirdPartyContext, "Third-party pairing matched the current Teams meeting.")));

        var result = await arbitrator.ApplyPreferredContextAsync(
            decision,
            new AppConfig
            {
                PreferredTeamsIntegrationMode = PreferredTeamsIntegrationMode.Auto,
                TeamsCapabilitySnapshot = new TeamsCapabilitySnapshot
                {
                    Status = TeamsCapabilityStatus.ThirdPartyApiUsable,
                    ThirdPartyApi = new TeamsThirdPartyApiCapability
                    {
                        Status = TeamsThirdPartyApiStatus.ReadableStateAvailable,
                        SupportsReadableMeetingState = true,
                    },
                },
            },
            now,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Client Sync", result.SessionTitle);
        Assert.Contains(result.Signals, signal => signal.Source.Equals("official-teams-third-party", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Signals, signal => signal.Source.Equals("official-teams-match", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubTeamsThirdPartyApiAdapter : ITeamsThirdPartyApiAdapter
    {
        private readonly TeamsThirdPartyApiProbeSnapshot _snapshot;
        private readonly TeamsOfficialMeetingLookupResult _lookupResult;

        public StubTeamsThirdPartyApiAdapter(
            TeamsThirdPartyApiProbeSnapshot snapshot,
            TeamsOfficialMeetingLookupResult lookupResult)
        {
            _snapshot = snapshot;
            _lookupResult = lookupResult;
        }

        public Task<TeamsThirdPartyApiProbeSnapshot> ProbeAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }

        public Task<TeamsOfficialMeetingLookupResult> TryGetCurrentMeetingContextAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_lookupResult);
        }
    }
}
