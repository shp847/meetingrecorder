using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public static class TeamsIntegrationSelectionPolicy
{
    public static PreferredTeamsIntegrationMode ResolveEffectiveMode(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var snapshot = config.TeamsCapabilitySnapshot ?? new TeamsCapabilitySnapshot();
        var hasThirdParty = snapshot.ThirdPartyApi.Status == TeamsThirdPartyApiStatus.ReadableStateAvailable ||
            snapshot.ThirdPartyApiReadableStateSupported;

        return config.PreferredTeamsIntegrationMode switch
        {
            PreferredTeamsIntegrationMode.FallbackOnly => PreferredTeamsIntegrationMode.FallbackOnly,
            PreferredTeamsIntegrationMode.ThirdPartyApi => hasThirdParty
                ? PreferredTeamsIntegrationMode.ThirdPartyApi
                : ResolveAutoMode(hasThirdParty),
            PreferredTeamsIntegrationMode.GraphCalendar => ResolveAutoMode(hasThirdParty),
            PreferredTeamsIntegrationMode.GraphCalendarAndOnlineMeeting => ResolveAutoMode(hasThirdParty),
            _ => ResolveAutoMode(hasThirdParty),
        };
    }

    private static PreferredTeamsIntegrationMode ResolveAutoMode(bool hasThirdParty)
    {
        if (hasThirdParty)
        {
            return PreferredTeamsIntegrationMode.ThirdPartyApi;
        }

        return PreferredTeamsIntegrationMode.FallbackOnly;
    }
}
