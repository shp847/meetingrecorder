using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum OngoingMeetingHealEligibility
{
    Eligible = 0,
    UnknownIdentity = 1,
    DifferentMeeting = 2,
    TooFarApart = 3,
    ActiveOrIncomplete = 4,
    UserMetadataConflict = 5,
    MissingArtifacts = 6,
}

public sealed record OngoingMeetingHealCandidate(
    MeetingSessionManifest Predecessor,
    MeetingSessionManifest Successor,
    MeetingIdentitySnapshot? PredecessorIdentity,
    MeetingIdentitySnapshot? SuccessorIdentity,
    bool HasActiveLease,
    bool HasUserMetadataConflict);

public sealed class OngoingMeetingHealEligibilityResolver
{
    public static readonly TimeSpan MaximumAdjacency = TimeSpan.FromMinutes(5);
    private readonly MeetingContinuityMatcher _matcher;

    public OngoingMeetingHealEligibilityResolver(MeetingContinuityMatcher? matcher = null) =>
        _matcher = matcher ?? new MeetingContinuityMatcher();

    public OngoingMeetingHealEligibility Evaluate(OngoingMeetingHealCandidate candidate, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.HasActiveLease ||
            candidate.Predecessor.State is SessionState.Recording or SessionState.Processing or SessionState.Finalizing ||
            candidate.Successor.State is SessionState.Recording or SessionState.Processing or SessionState.Finalizing)
        {
            return OngoingMeetingHealEligibility.ActiveOrIncomplete;
        }

        if (candidate.HasUserMetadataConflict)
        {
            return OngoingMeetingHealEligibility.UserMetadataConflict;
        }

        if (candidate.Predecessor.EndedAtUtc is not { } endedAt ||
            candidate.Successor.StartedAtUtc < endedAt ||
            candidate.Successor.StartedAtUtc - endedAt > MaximumAdjacency)
        {
            return OngoingMeetingHealEligibility.TooFarApart;
        }

        if (string.IsNullOrWhiteSpace(candidate.Predecessor.MergedAudioPath) ||
            string.IsNullOrWhiteSpace(candidate.Successor.MergedAudioPath))
        {
            return OngoingMeetingHealEligibility.MissingArtifacts;
        }

        return _matcher.Compare(candidate.PredecessorIdentity, candidate.SuccessorIdentity, MeetingIdentityComparisonMode.ManifestToManifest, nowUtc).Verdict switch
        {
            MeetingIdentityVerdict.SameMeeting => OngoingMeetingHealEligibility.Eligible,
            MeetingIdentityVerdict.DifferentMeeting => OngoingMeetingHealEligibility.DifferentMeeting,
            _ => OngoingMeetingHealEligibility.UnknownIdentity,
        };
    }
}
