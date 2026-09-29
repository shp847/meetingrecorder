namespace MeetingRecorder.Core.Services;

/// <summary>Coalesces current-work healing candidates. It performs no file I/O
/// or merge; callers must hand an eligible pair to the shared transaction.</summary>
public sealed class OngoingMeetingHealService
{
    private readonly OngoingMeetingHealEligibilityResolver _resolver;
    private readonly HashSet<string> _seenPairs = new(StringComparer.Ordinal);

    public OngoingMeetingHealService(OngoingMeetingHealEligibilityResolver? resolver = null)
    {
        _resolver = resolver ?? new OngoingMeetingHealEligibilityResolver();
    }

    public bool TryAdmit(OngoingMeetingHealCandidate candidate, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var pairKey = $"{candidate.Predecessor.SessionId}|{candidate.Successor.SessionId}";
        if (_seenPairs.Contains(pairKey))
        {
            return false;
        }

        if (_resolver.Evaluate(candidate, nowUtc) != OngoingMeetingHealEligibility.Eligible)
        {
            return false;
        }

        _seenPairs.Add(pairKey);
        return true;
    }
}
