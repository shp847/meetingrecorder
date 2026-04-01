using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.App.Services;

internal enum TeamsOfficialMeetingContextSource
{
    None = 0,
    ThirdPartyApi = 1,
    GraphCalendar = 2,
    GraphOnlineMeeting = 3,
}

internal sealed record TeamsOfficialMeetingContext(
    TeamsOfficialMeetingContextSource Source,
    string Title,
    string? JoinUrl,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string? ExternalMeetingId);

internal enum TeamsOfficialMeetingLookupStatus
{
    Unavailable = 0,
    NoCurrentMatch = 1,
    Matched = 2,
}

internal sealed record TeamsOfficialMeetingLookupResult(
    TeamsOfficialMeetingLookupStatus Status,
    TeamsOfficialMeetingContext? CurrentMeetingContext,
    string Detail);

internal sealed record TeamsThirdPartyApiProbeSnapshot(
    TeamsThirdPartyApiStatus Status,
    bool ManageApiEnabled,
    TeamsPairingState PairingState,
    bool SupportsReadableMeetingState,
    string Summary,
    string Detail);

internal sealed record TeamsProbeResult(
    TeamsCapabilitySnapshot CapabilitySnapshot,
    string HeuristicBaselineSummary,
    DetectionDecision? HeuristicBaselineDecision);

internal interface ITeamsThirdPartyApiAdapter
{
    Task<TeamsThirdPartyApiProbeSnapshot> ProbeAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<TeamsOfficialMeetingLookupResult> TryGetCurrentMeetingContextAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class TeamsIntegrationProbeService
{
    private readonly Func<Task<DetectionDecision?>> _detectHeuristicBaselineAsync;
    private readonly ITeamsThirdPartyApiAdapter _thirdPartyApiAdapter;

    public TeamsIntegrationProbeService(
        Func<Task<DetectionDecision?>> detectHeuristicBaselineAsync,
        ITeamsThirdPartyApiAdapter thirdPartyApiAdapter)
    {
        _detectHeuristicBaselineAsync = detectHeuristicBaselineAsync;
        _thirdPartyApiAdapter = thirdPartyApiAdapter;
    }

    public async Task<TeamsProbeResult> RunAsync(
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var baselineDecision = await _detectHeuristicBaselineAsync();
        var heuristicBaselineSummary = BuildHeuristicBaselineSummary(baselineDecision);
        var thirdPartySnapshot = await _thirdPartyApiAdapter.ProbeAsync(nowUtc, cancellationToken);

        var capabilitySnapshot = BuildCapabilitySnapshot(
            nowUtc,
            thirdPartySnapshot);

        return new TeamsProbeResult(
            capabilitySnapshot,
            heuristicBaselineSummary,
            baselineDecision);
    }

    private static TeamsCapabilitySnapshot BuildCapabilitySnapshot(
        DateTimeOffset nowUtc,
        TeamsThirdPartyApiProbeSnapshot thirdPartySnapshot)
    {
        var status = ResolveStatus(thirdPartySnapshot);
        var summary = ResolveSummary(status, thirdPartySnapshot);
        var detail = string.Join(
            Environment.NewLine,
            new[]
            {
                thirdPartySnapshot.Detail,
            }.Where(text => !string.IsNullOrWhiteSpace(text)));

        return new TeamsCapabilitySnapshot
        {
            Status = status,
            LastProbeUtc = nowUtc,
            Summary = summary,
            Detail = detail,
            HeuristicBaselineReady = true,
            ThirdPartyApi = new TeamsThirdPartyApiCapability
            {
                Status = thirdPartySnapshot.Status,
                ManageApiEnabled = thirdPartySnapshot.ManageApiEnabled,
                PairingState = thirdPartySnapshot.PairingState,
                SupportsReadableMeetingState = thirdPartySnapshot.SupportsReadableMeetingState,
                Summary = thirdPartySnapshot.Summary,
                Detail = thirdPartySnapshot.Detail,
            },
            ThirdPartyApiAvailable = thirdPartySnapshot.Status is not TeamsThirdPartyApiStatus.Unavailable,
            ThirdPartyApiReadableStateSupported = thirdPartySnapshot.SupportsReadableMeetingState,
        };
    }

    private static TeamsCapabilityStatus ResolveStatus(
        TeamsThirdPartyApiProbeSnapshot thirdPartySnapshot)
    {
        if (thirdPartySnapshot.Status == TeamsThirdPartyApiStatus.ReadableStateAvailable)
        {
            return TeamsCapabilityStatus.ThirdPartyApiUsable;
        }

        if (thirdPartySnapshot.Status == TeamsThirdPartyApiStatus.ControlOnly)
        {
            return TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly;
        }

        return TeamsCapabilityStatus.FallbackOnly;
    }

    private static string ResolveSummary(
        TeamsCapabilityStatus status,
        TeamsThirdPartyApiProbeSnapshot thirdPartySnapshot)
    {
        return status switch
        {
            TeamsCapabilityStatus.ThirdPartyApiUsable => string.IsNullOrWhiteSpace(thirdPartySnapshot.Summary)
                ? "Third-party API usable"
                : thirdPartySnapshot.Summary,
            TeamsCapabilityStatus.ThirdPartyApiAvailableButControlOnly => string.IsNullOrWhiteSpace(thirdPartySnapshot.Summary)
                ? "Third-party API available but control-only"
                : thirdPartySnapshot.Summary,
            _ => "Fallback only.",
        };
    }

    private static string BuildHeuristicBaselineSummary(DetectionDecision? decision)
    {
        if (decision is null)
        {
            return "Heuristic baseline: no supported meeting candidate is active right now.";
        }

        var verb = decision.ShouldStart
            ? "would auto-start"
            : decision.ShouldKeepRecording
                ? "would keep recording"
                : "would not start";
        return $"Heuristic baseline: {decision.Platform} '{decision.SessionTitle}' {verb}.";
    }
}
