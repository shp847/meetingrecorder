using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed class TeamsDetectionArbitrator
{
    private readonly ITeamsThirdPartyApiAdapter _thirdPartyApiAdapter;

    public TeamsDetectionArbitrator(ITeamsThirdPartyApiAdapter thirdPartyApiAdapter)
    {
        _thirdPartyApiAdapter = thirdPartyApiAdapter;
    }

    public async Task<DetectionDecision?> ApplyPreferredContextAsync(
        DetectionDecision? decision,
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (decision is null || decision.Platform != MeetingPlatform.Teams)
        {
            return decision;
        }

        var effectiveMode = TeamsIntegrationSelectionPolicy.ResolveEffectiveMode(config);
        if (effectiveMode == PreferredTeamsIntegrationMode.FallbackOnly)
        {
            return decision;
        }

        var lookup = await _thirdPartyApiAdapter.TryGetCurrentMeetingContextAsync(nowUtc, cancellationToken);

        if (lookup.Status == TeamsOfficialMeetingLookupStatus.Unavailable)
        {
            return decision;
        }

        if (lookup.Status == TeamsOfficialMeetingLookupStatus.NoCurrentMatch ||
            lookup.CurrentMeetingContext is null ||
            lookup.CurrentMeetingContext.EndedAtUtc < nowUtc)
        {
            return AppendLifecycleSignal(
                decision,
                "official-teams-no-current-match",
                string.IsNullOrWhiteSpace(lookup.Detail)
                    ? "No current official Teams meeting is active."
                    : lookup.Detail,
                nowUtc,
                "Official Teams metadata did not find a current matching meeting window.");
        }

        var context = lookup.CurrentMeetingContext;
        if (string.IsNullOrWhiteSpace(context.Title))
        {
            return AppendLifecycleSignal(
                decision,
                "official-teams-match",
                "Current official Teams meeting matched local evidence.",
                nowUtc,
                "Official Teams metadata matched the current meeting window.");
        }

        var sourceName = context.Source switch
        {
            TeamsOfficialMeetingContextSource.ThirdPartyApi => "official-teams-third-party",
            TeamsOfficialMeetingContextSource.GraphOnlineMeeting => "official-teams-online-meeting",
            _ => "official-teams-calendar",
        };
        var signalValue = string.IsNullOrWhiteSpace(context.JoinUrl)
            ? context.Title
            : $"{context.Title}; joinUrl={context.JoinUrl}";
        var updatedSignals = decision.Signals
            .Concat(
            [
                new DetectionSignal(sourceName, signalValue, 0.25d, nowUtc),
                new DetectionSignal("official-teams-match", context.Title, 0.25d, nowUtc),
            ])
            .ToArray();
        var updatedReason = string.IsNullOrWhiteSpace(decision.Reason)
            ? "Official Teams metadata matched the current meeting window."
            : $"{decision.Reason} Official Teams metadata matched the current meeting window.";

        return decision with
        {
            SessionTitle = context.Title,
            Signals = updatedSignals,
            Reason = updatedReason,
        };
    }

    private static DetectionDecision AppendLifecycleSignal(
        DetectionDecision decision,
        string source,
        string value,
        DateTimeOffset observedAtUtc,
        string reasonSuffix)
    {
        var updatedSignals = decision.Signals
            .Concat([new DetectionSignal(source, value, 0.25d, observedAtUtc)])
            .ToArray();
        var updatedReason = string.IsNullOrWhiteSpace(decision.Reason)
            ? reasonSuffix
            : $"{decision.Reason} {reasonSuffix}";

        return decision with
        {
            Signals = updatedSignals,
            Reason = updatedReason,
        };
    }
}
