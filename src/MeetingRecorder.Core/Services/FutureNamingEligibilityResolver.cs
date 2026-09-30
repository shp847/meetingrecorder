namespace MeetingRecorder.Core.Services;

public enum FutureNamingEligibility
{
    Disabled, NoSamples, NoProfiles, IncompatibleModel, Immature, Short,
    BelowThreshold, Ambiguous, DuplicateCandidate, Suppressed, Suggested, AutoApplied,
}

public sealed record FutureNamingEligibilityInput(
    string MeetingRevision,
    string CanonicalSpeakerId,
    string PolicyVersion,
    bool IsEnabled,
    bool HasSample,
    bool HasCompatibleProfile,
    bool IsProfileMature,
    bool HasMinimumSpeech,
    bool MeetsSuggestionThreshold,
    bool MeetsAutoApplyThreshold,
    bool HasRequiredMargin,
    bool IsDuplicateProfileCandidate,
    bool IsSuppressed);

public sealed record FutureNamingEligibilityDecision(
    FutureNamingEligibility Eligibility,
    string Explanation,
    bool MaySuggest,
    bool MayAutoApply);

/// <summary>Classifies local post-processing attribution without accepting identity hints.</summary>
public static class FutureNamingEligibilityResolver
{
    public static FutureNamingEligibilityDecision Resolve(FutureNamingEligibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var eligibility = !input.IsEnabled ? FutureNamingEligibility.Disabled
            : !input.HasSample ? FutureNamingEligibility.NoSamples
            : !input.HasCompatibleProfile ? FutureNamingEligibility.NoProfiles
            : input.IsSuppressed ? FutureNamingEligibility.Suppressed
            : !input.MeetsSuggestionThreshold ? FutureNamingEligibility.BelowThreshold
            : !input.HasRequiredMargin ? FutureNamingEligibility.Ambiguous
            : input.IsDuplicateProfileCandidate ? FutureNamingEligibility.DuplicateCandidate
            : !input.IsProfileMature ? FutureNamingEligibility.Immature
            : !input.HasMinimumSpeech ? FutureNamingEligibility.Short
            : !input.MeetsAutoApplyThreshold ? FutureNamingEligibility.Suggested
            : FutureNamingEligibility.AutoApplied;

        return eligibility switch
        {
            FutureNamingEligibility.AutoApplied => new(eligibility, "A local voice profile met every conservative rule and may name this speaker after processing.", true, true),
            FutureNamingEligibility.Suggested => new(eligibility, "A local voice profile is a suggestion only and needs review before naming this speaker.", true, false),
            FutureNamingEligibility.Immature or FutureNamingEligibility.Short or FutureNamingEligibility.Ambiguous or FutureNamingEligibility.DuplicateCandidate => new(eligibility, "Local voice evidence is not strong enough to auto-name this speaker. Review a suggestion instead.", true, false),
            FutureNamingEligibility.Suppressed => new(eligibility, "This local profile was rejected for this meeting speaker and will not be suggested again.", false, false),
            FutureNamingEligibility.Disabled => new(eligibility, "Local voice-profile naming is disabled.", false, false),
            FutureNamingEligibility.NoSamples => new(eligibility, "This meeting has no usable local speaker evidence for recognition.", false, false),
            FutureNamingEligibility.NoProfiles => new(eligibility, "No compatible local voice profile is available for this speaker.", false, false),
            _ => new(eligibility, "Local voice evidence did not clear the suggestion threshold.", false, false),
        };
    }
}
