using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.App.Services;

internal sealed class UnavailableTeamsThirdPartyApiAdapter : ITeamsThirdPartyApiAdapter
{
    public Task<TeamsThirdPartyApiProbeSnapshot> ProbeAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TeamsThirdPartyApiProbeSnapshot(
            Status: TeamsThirdPartyApiStatus.Unavailable,
            ManageApiEnabled: false,
            PairingState: TeamsPairingState.Unknown,
            SupportsReadableMeetingState: false,
            Summary: "Fallback only.",
            Detail: "No compatible Teams third-party integration bridge is configured in this build."));
    }

    public Task<TeamsOfficialMeetingLookupResult> TryGetCurrentMeetingContextAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new TeamsOfficialMeetingLookupResult(
            TeamsOfficialMeetingLookupStatus.Unavailable,
            null,
            "No readable third-party Teams meeting bridge is configured."));
    }
}
