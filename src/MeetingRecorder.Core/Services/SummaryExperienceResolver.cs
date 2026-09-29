using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

internal sealed record SummaryExperienceInput(
    MeetingSummaryGenerationMode GenerationMode,
    MeetingSummaryProviderPreference ProviderPreference,
    bool HasOpenAiKey,
    int HostedConsentVersion);

internal sealed record SummaryExperienceState(
    SummaryExperienceMode Mode,
    bool CanGenerate,
    bool RequiresHostedConsent,
    string StatusText)
{
    public bool CanUseHostedRoute => !RequiresHostedConsent;
}

/// <summary>
/// Keeps hosted transcript routing behind an explicit, versioned consent gate.
/// A loopback route remains a transport choice, not a locality promise.
/// </summary>
internal static class SummaryExperienceResolver
{
    public const int HostedRouteConsentPolicyVersion = 1;

    public static SummaryExperienceState Resolve(SummaryExperienceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.GenerationMode != MeetingSummaryGenerationMode.Enabled)
        {
            return new SummaryExperienceState(SummaryExperienceMode.Off, false, false, "Summaries are off.");
        }

        var hostedConsentValid = input.HostedConsentVersion >= HostedRouteConsentPolicyVersion;
        return input.ProviderPreference switch
        {
            MeetingSummaryProviderPreference.LocalOnly =>
                new SummaryExperienceState(SummaryExperienceMode.LocalOnly, true, false, "Local summary route selected."),
            MeetingSummaryProviderPreference.OpenAiOnly when !input.HasOpenAiKey =>
                new SummaryExperienceState(SummaryExperienceMode.HostedOnly, false, false, "Hosted summaries need a saved key."),
            MeetingSummaryProviderPreference.OpenAiOnly when !hostedConsentValid =>
                new SummaryExperienceState(SummaryExperienceMode.HostedOnly, false, true, "Hosted summaries need your saved consent."),
            MeetingSummaryProviderPreference.OpenAiOnly =>
                new SummaryExperienceState(SummaryExperienceMode.HostedOnly, true, false, "Hosted summary route selected."),
            MeetingSummaryProviderPreference.LocalThenOpenAi when !input.HasOpenAiKey =>
                new SummaryExperienceState(SummaryExperienceMode.LocalOnly, true, false, "Local summary route selected; hosted fallback is unavailable."),
            MeetingSummaryProviderPreference.LocalThenOpenAi when !hostedConsentValid =>
                new SummaryExperienceState(SummaryExperienceMode.LocalOnly, true, true, "Local summary route selected; hosted fallback needs your saved consent."),
            _ => new SummaryExperienceState(SummaryExperienceMode.LocalWithHostedFallback, true, false, "Local summary route with hosted fallback selected."),
        };
    }
}
