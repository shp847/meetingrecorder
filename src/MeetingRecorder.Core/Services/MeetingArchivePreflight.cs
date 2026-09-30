namespace MeetingRecorder.Core.Services;

public enum MeetingArchiveOperation { Archive = 0, Recover = 1, PermanentDelete = 2 }
public enum MeetingArchivePreflightStatus { Eligible = 0, MissingArtifact = 1, Busy = 2, Collision = 3, ReceiptUnavailable = 4, ReceiptInvalid = 5 }
public sealed record MeetingArchiveArtifact(string Class, bool Exists, long SizeBytes);
public sealed record MeetingArchivePreflightInput(string MeetingId, MeetingArchiveOperation Operation, IReadOnlyList<MeetingArchiveArtifact> Artifacts, bool IsBusy, bool HasReceipt, bool IsReceiptValid, bool HasDestinationCollision, bool LinkedSessionFolderExists);
public sealed record MeetingArchivePreflightResult(MeetingArchivePreflightStatus Status, string Summary, IReadOnlyList<string> ArtifactClasses, bool IncludesLinkedSessionFolder, bool RequiresTypedDeleteConfirmation, bool CanProceed);

/// <summary>Pure trust boundary for archive, recovery, and delete previews; no file mutations.</summary>
public static class MeetingArchivePreflight
{
    public static MeetingArchivePreflightResult Evaluate(MeetingArchivePreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var classes = input.Artifacts.Where(item => item.Exists).Select(item => item.Class).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (input.IsBusy) return Result(MeetingArchivePreflightStatus.Busy, "Meeting maintenance is busy; refresh before changing artifacts.", classes, input, false);
        if (input.Operation == MeetingArchiveOperation.Recover && !input.HasReceipt) return Result(MeetingArchivePreflightStatus.ReceiptUnavailable, "Archive recovery needs its receipt.", classes, input, false);
        if (input.Operation == MeetingArchiveOperation.Recover && !input.IsReceiptValid) return Result(MeetingArchivePreflightStatus.ReceiptInvalid, "Archive receipt cannot be trusted for recovery.", classes, input, false);
        if (input.Operation == MeetingArchiveOperation.Recover && input.HasDestinationCollision) return Result(MeetingArchivePreflightStatus.Collision, "Recovery destination already contains meeting artifacts.", classes, input, false);
        if (classes.Length == 0) return Result(MeetingArchivePreflightStatus.MissingArtifact, "No published meeting artifacts are available for this operation.", classes, input, false);
        var summary = input.Operation switch { MeetingArchiveOperation.Archive => "Move published artifacts to the archive; the linked work session stays in place.", MeetingArchiveOperation.Recover => "Restore the receipt-backed published artifacts; the work session was never archived.", _ => "Permanently erase the listed published artifacts and linked work session when present." };
        return Result(MeetingArchivePreflightStatus.Eligible, summary, classes, input, true);
    }
    private static MeetingArchivePreflightResult Result(MeetingArchivePreflightStatus status, string summary, IReadOnlyList<string> classes, MeetingArchivePreflightInput input, bool canProceed) => new(status, summary, classes, input.Operation == MeetingArchiveOperation.PermanentDelete && input.LinkedSessionFolderExists, input.Operation == MeetingArchiveOperation.PermanentDelete, canProceed);
}
