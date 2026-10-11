using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum SpeakerClusterMergeInverseDisposition
{
    Ready = 0,
    Invalid = 1,
    NotFound = 2,
    Superseded = 3,
}

public sealed record SpeakerClusterMergeInverseRequest(
    string OperationId,
    string CurrentArtifactRevision,
    IReadOnlyList<SpeakerClusterMergeMetadata>? Merges);

public sealed record SpeakerClusterMergeInverseDecision(
    SpeakerClusterMergeInverseDisposition Disposition,
    string Explanation,
    SpeakerClusterMergeMetadata? Merge,
    IReadOnlyList<SpeakerClusterMergeMetadata> RemainingMerges)
{
    public bool CanReverse => Disposition == SpeakerClusterMergeInverseDisposition.Ready;
}

/// <summary>
/// Validates an exact merge inverse before an undo writer runs. Only the newest
/// operation may be removed, so a later merge can never be silently reinterpreted.
/// </summary>
public static class SpeakerClusterMergeInverseResolver
{
    public static SpeakerClusterMergeInverseDecision Resolve(SpeakerClusterMergeInverseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = request.OperationId?.Trim() ?? string.Empty;
        var merges = request.Merges ?? Array.Empty<SpeakerClusterMergeMetadata>();
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(request.CurrentArtifactRevision))
        {
            return Result(SpeakerClusterMergeInverseDisposition.Invalid, "Meeting speaker data needs reload before a merge can be reversed.", null, merges);
        }

        var matches = merges
            .Select((merge, index) => (merge, index))
            .Where(item => string.Equals(item.merge.OperationId, operationId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return Result(SpeakerClusterMergeInverseDisposition.NotFound, "This speaker merge is no longer available to reverse.", null, merges);
        }

        if (matches.Length != 1)
        {
            return Result(SpeakerClusterMergeInverseDisposition.Invalid, "Speaker merge history is inconsistent. Reload before changing speakers.", null, merges);
        }

        var match = matches[0];
        if (match.index != merges.Count - 1)
        {
            return Result(SpeakerClusterMergeInverseDisposition.Superseded, "A later speaker merge depends on this one. Reverse the newest merge first.", match.merge, merges);
        }

        return Result(
            SpeakerClusterMergeInverseDisposition.Ready,
            "This is the newest speaker merge and can be reversed without changing unrelated name corrections.",
            match.merge,
            merges.Take(merges.Count - 1).ToArray());
    }

    private static SpeakerClusterMergeInverseDecision Result(
        SpeakerClusterMergeInverseDisposition disposition,
        string explanation,
        SpeakerClusterMergeMetadata? merge,
        IReadOnlyList<SpeakerClusterMergeMetadata> remaining) =>
        new(disposition, explanation, merge, remaining);
}
