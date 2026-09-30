using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum SpeakerCorrectionAction
{
    ConfirmName = 0,
    RejectSuggestion = 1,
    UndoRecognition = 2,
}

public enum SpeakerCorrectionDisposition
{
    Accepted = 0,
    Invalid = 1,
    Stale = 2,
    Duplicate = 3,
    NoOp = 4,
}

/// <summary>
/// Identity-keyed mutation request. Display text is a proposed value, never an identity key.
/// </summary>
public sealed record SpeakerCorrectionRequest(
    string OperationId,
    string MeetingStableIdentity,
    string ArtifactRevision,
    string SpeakerId,
    string? CanonicalSpeakerId,
    string PriorFingerprint,
    SpeakerCorrectionAction Action,
    string? UpdatedDisplayName,
    bool LearningEnabled);

public sealed record SpeakerCorrectionCurrentState(
    string ArtifactRevision,
    string SpeakerId,
    string? CanonicalSpeakerId,
    string Fingerprint,
    string DisplayName,
    SpeakerNameSource NameSource,
    string? ProfileId,
    bool HasEligibleEvidence);

public sealed record SpeakerCorrectionDecision(
    SpeakerCorrectionDisposition Disposition,
    string Explanation,
    string EffectiveSpeakerId,
    bool CanWriteArtifact,
    bool CanLearn);

public static class SpeakerCorrectionReceiptResolver
{
    public static SpeakerCorrectionDecision Resolve(
        SpeakerCorrectionRequest request,
        SpeakerCorrectionCurrentState current,
        IReadOnlySet<string>? completedOperationIds = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        var effectiveSpeakerId = Normalize(request.CanonicalSpeakerId) ?? Normalize(request.SpeakerId) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(request.OperationId) ||
            string.IsNullOrWhiteSpace(request.MeetingStableIdentity) ||
            string.IsNullOrWhiteSpace(request.ArtifactRevision) ||
            string.IsNullOrWhiteSpace(effectiveSpeakerId) ||
            string.IsNullOrWhiteSpace(request.PriorFingerprint))
        {
            return Result(SpeakerCorrectionDisposition.Invalid, "Speaker correction needs its meeting, revision, speaker, and prior state before it can be applied.", effectiveSpeakerId);
        }

        if (completedOperationIds?.Contains(request.OperationId) == true)
        {
            return Result(SpeakerCorrectionDisposition.Duplicate, "This speaker correction was already applied.", effectiveSpeakerId);
        }

        if (!string.Equals(request.ArtifactRevision, current.ArtifactRevision, StringComparison.Ordinal) ||
            !string.Equals(request.PriorFingerprint, current.Fingerprint, StringComparison.Ordinal) ||
            !string.Equals(effectiveSpeakerId, Normalize(current.CanonicalSpeakerId) ?? Normalize(current.SpeakerId), StringComparison.Ordinal))
        {
            return Result(SpeakerCorrectionDisposition.Stale, "Meeting speaker data changed. Reload before applying this correction.", effectiveSpeakerId);
        }

        return request.Action switch
        {
            SpeakerCorrectionAction.ConfirmName => ResolveConfirm(request, current, effectiveSpeakerId),
            SpeakerCorrectionAction.RejectSuggestion => ResolveReject(current, effectiveSpeakerId),
            SpeakerCorrectionAction.UndoRecognition => ResolveUndo(current, effectiveSpeakerId),
            _ => Result(SpeakerCorrectionDisposition.Invalid, "This speaker correction action is not recognized. Review the speaker before trying again.", effectiveSpeakerId),
        };
    }

    private static SpeakerCorrectionDecision ResolveConfirm(SpeakerCorrectionRequest request, SpeakerCorrectionCurrentState current, string effectiveSpeakerId)
    {
        var updatedName = Normalize(request.UpdatedDisplayName);
        if (updatedName is null)
        {
            return Result(SpeakerCorrectionDisposition.Invalid, "Enter a meeting display name before applying this correction.", effectiveSpeakerId);
        }

        if (string.Equals(updatedName, Normalize(current.DisplayName), StringComparison.Ordinal))
        {
            return Result(SpeakerCorrectionDisposition.NoOp, "This meeting display name is already current.", effectiveSpeakerId);
        }

        return new SpeakerCorrectionDecision(
            SpeakerCorrectionDisposition.Accepted,
            request.LearningEnabled && current.HasEligibleEvidence
                ? "The meeting display name will update first, then local learning may update its local profile."
                : "The meeting display name will update without changing local voice profiles.",
            effectiveSpeakerId,
            CanWriteArtifact: true,
            CanLearn: request.LearningEnabled && current.HasEligibleEvidence);
    }

    private static SpeakerCorrectionDecision ResolveReject(SpeakerCorrectionCurrentState current, string effectiveSpeakerId) =>
        current.NameSource == SpeakerNameSource.SuggestedVoiceProfile && !string.IsNullOrWhiteSpace(current.ProfileId)
            ? new SpeakerCorrectionDecision(SpeakerCorrectionDisposition.Accepted, "This local suggestion will be cleared and rejected only for this meeting speaker.", effectiveSpeakerId, true, false)
            : Result(SpeakerCorrectionDisposition.NoOp, "There is no local name suggestion to reject for this speaker.", effectiveSpeakerId);

    private static SpeakerCorrectionDecision ResolveUndo(SpeakerCorrectionCurrentState current, string effectiveSpeakerId) =>
        current.NameSource is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile
            ? new SpeakerCorrectionDecision(SpeakerCorrectionDisposition.Accepted, "Recognition-derived attribution will be removed without changing user-entered names.", effectiveSpeakerId, true, false)
            : Result(SpeakerCorrectionDisposition.NoOp, "There is no recognition-derived attribution to undo for this speaker.", effectiveSpeakerId);

    private static SpeakerCorrectionDecision Result(SpeakerCorrectionDisposition disposition, string explanation, string effectiveSpeakerId) =>
        new(disposition, explanation, effectiveSpeakerId, false, false);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
