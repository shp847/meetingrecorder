namespace MeetingRecorder.Core.Services;

public sealed record SpeakerRepairPreflightInput(
    string MeetingId,
    string ArtifactRevision,
    bool HasCurrentStructuredArtifact,
    bool IsBusy,
    bool IsRepairReady,
    int UserEnteredNameCount,
    int ProfileAttributionCount);

public sealed record SpeakerRepairPreflight(
    bool CanQueue,
    string Summary,
    string Consequence);

/// <summary>Explains the boundary before a user explicitly queues speaker-label repair.</summary>
public static class SpeakerRepairPreflightResolver
{
    public static SpeakerRepairPreflight Resolve(SpeakerRepairPreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.ArtifactRevision) || !input.HasCurrentStructuredArtifact)
        {
            return new(false, "Meeting metadata changed or is unavailable. Reload before repairing speaker labels.", "No repair was queued.");
        }

        if (input.IsBusy)
        {
            return new(false, "This meeting is changing now. Wait for processing to finish before repairing speaker labels.", "No repair was queued.");
        }

        if (!input.IsRepairReady)
        {
            return new(false, "Speaker-label repair is unavailable until local labeling setup and source artifacts are ready.", "No repair was queued.");
        }

        var names = input.UserEnteredNameCount == 0
            ? "No user-entered meeting display names were found."
            : $"{input.UserEnteredNameCount} user-entered meeting display name(s) will not be copied onto newly generated speaker clusters.";
        var profiles = input.ProfileAttributionCount == 0
            ? "No profile-attributed names were found."
            : $"{input.ProfileAttributionCount} profile-attributed name(s) will need review after repair.";
        return new(true,
            "Repair re-runs local speaker labeling from the existing transcript snapshot; it is not a name refresh.",
            $"{names} {profiles} Audio and transcript words are not retranscribed.");
    }
}
