using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SummaryExperienceResolverTests
{
    [Theory]
    [InlineData(MeetingSummaryGenerationMode.Disabled, MeetingSummaryProviderPreference.LocalOnly, false, 0, SummaryExperienceMode.Off, false)]
    [InlineData(MeetingSummaryGenerationMode.Enabled, MeetingSummaryProviderPreference.LocalOnly, false, 0, SummaryExperienceMode.LocalOnly, true)]
    [InlineData(MeetingSummaryGenerationMode.Enabled, MeetingSummaryProviderPreference.LocalThenOpenAi, true, 0, SummaryExperienceMode.LocalOnly, true)]
    [InlineData(MeetingSummaryGenerationMode.Enabled, MeetingSummaryProviderPreference.LocalThenOpenAi, true, 1, SummaryExperienceMode.LocalWithHostedFallback, true)]
    [InlineData(MeetingSummaryGenerationMode.Enabled, MeetingSummaryProviderPreference.OpenAiOnly, true, 0, SummaryExperienceMode.HostedOnly, false)]
    [InlineData(MeetingSummaryGenerationMode.Enabled, MeetingSummaryProviderPreference.OpenAiOnly, true, 1, SummaryExperienceMode.HostedOnly, true)]
    public void Resolve_Projects_Only_ConsentAuthorized_Hosted_Modes(
        MeetingSummaryGenerationMode generationMode,
        MeetingSummaryProviderPreference preference,
        bool hasOpenAiKey,
        int consentVersion,
        SummaryExperienceMode expectedMode,
        bool canGenerate)
    {
        var state = SummaryExperienceResolver.Resolve(new SummaryExperienceInput(
            generationMode,
            preference,
            hasOpenAiKey,
            consentVersion));

        Assert.Equal(expectedMode, state.Mode);
        Assert.Equal(canGenerate, state.CanGenerate);
    }

    [Fact]
    public void Resolve_Requires_Current_Policy_Consent_For_Hosted_Routing()
    {
        var state = SummaryExperienceResolver.Resolve(new SummaryExperienceInput(
            MeetingSummaryGenerationMode.Enabled,
            MeetingSummaryProviderPreference.OpenAiOnly,
            HasOpenAiKey: true,
            HostedConsentVersion: SummaryExperienceResolver.HostedRouteConsentPolicyVersion - 1));

        Assert.True(state.RequiresHostedConsent);
        Assert.False(state.CanUseHostedRoute);
        Assert.Contains("consent", state.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
