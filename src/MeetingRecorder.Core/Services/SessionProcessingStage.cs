namespace MeetingRecorder.Core.Services;

/// <summary>
/// The bounded worker operation requested by a staged backlog item. FullPass
/// preserves the existing worker contract; the other values are opt-in only.
/// </summary>
public enum SessionProcessingStage
{
    FullPass = 0,
    Transcript = 1,
    Diarization = 2,
    Summary = 3,
}

/// <summary>
/// Immutable queue identity supplied with an opt-in staged worker invocation.
/// It is opaque to the processor: the app queue remains the authority that
/// leases and completes work, while the worker simply echoes the exact
/// identity in its completion receipt.
/// </summary>
public sealed record SessionProcessingWorkLease(
    Guid WorkId,
    string WorkRevision,
    string LeaseToken);

/// <summary>
/// A compact stdout receipt for a staged worker pass. The receipt contains no
/// meeting title, manifest path, audio path, transcript content, or provider
/// detail, so the queue can validate ownership without logging user data.
/// </summary>
public sealed record SessionProcessingWorkReceipt(
    int SchemaVersion,
    Guid WorkId,
    string WorkRevision,
    string LeaseToken,
    SessionProcessingStage Stage)
{
    public const int CurrentSchemaVersion = 1;
}

public static class SessionProcessingStageParser
{
    public static bool TryParse(string? value, out SessionProcessingStage stage)
    {
        stage = SessionProcessingStage.FullPass;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "full":
            case "full-pass":
                stage = SessionProcessingStage.FullPass;
                return true;
            case "transcript":
                stage = SessionProcessingStage.Transcript;
                return true;
            case "diarization":
                stage = SessionProcessingStage.Diarization;
                return true;
            case "summary":
                stage = SessionProcessingStage.Summary;
                return true;
            default:
                return false;
        }
    }
}
