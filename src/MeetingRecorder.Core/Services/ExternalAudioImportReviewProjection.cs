using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Pure, display-safe admission state for the Meetings import review. The
/// projection intentionally omits local locators, exception details, and
/// audio content; callers retain those only inside their local queue request.
/// </summary>
public sealed record ExternalAudioImportReviewRowProjection(
    string Revision,
    string SourceDisplayName,
    string SourceMethodLabel,
    TimeSpan? Duration,
    ExternalAudioImportPreflightStatus PreflightStatus,
    string StatusText,
    string RetentionText,
    string RecoveryText,
    bool IsSetupBlocked,
    bool HasDraftValidationIssue,
    bool HasQueueFailure)
{
    public bool CanQueue =>
        PreflightStatus == ExternalAudioImportPreflightStatus.Ready &&
        !IsSetupBlocked &&
        !HasDraftValidationIssue &&
        !HasQueueFailure;

    public bool CanRetry => HasQueueFailure || ExternalAudioImportReviewProjection.IsRetryable(PreflightStatus);

    public bool CanSkipDuplicate => PreflightStatus == ExternalAudioImportPreflightStatus.Duplicate;

    public bool CanRemove => true;
}

public sealed record ExternalAudioImportReviewSummary(
    int TotalCount,
    int ReadyCount,
    int SetupBlockedCount,
    int DuplicateCount,
    int RetryableCount,
    int IssueCount,
    string StatusText);

public static class ExternalAudioImportReviewProjection
{
    public static ExternalAudioImportReviewRowProjection CreateRow(
        ExternalAudioImportCandidate candidate,
        bool isSetupBlocked,
        bool hasDraftValidationIssue,
        bool hasQueueFailure)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var preflightStatus = candidate.Preflight.Status;
        var statusText = isSetupBlocked && preflightStatus == ExternalAudioImportPreflightStatus.Ready
            ? "Setup required before queueing."
            : hasQueueFailure
                ? "Queue did not complete. Review and retry this row."
                : hasDraftValidationIssue
                    ? "Complete the row details before queueing."
                    : SafeStatusText(candidate);

        return new ExternalAudioImportReviewRowProjection(
            ExternalAudioImportIdentity.BuildObservationKey(
                candidate.SourcePath,
                candidate.SourceSizeBytes,
                candidate.SourceLastWriteUtc),
            SafeDisplayName(candidate.SourceDisplayName),
            GetMethodLabel(candidate.ImportMethod),
            candidate.Preflight.Duration,
            preflightStatus,
            statusText,
            "Original stays in place.",
            GetRecoveryText(preflightStatus, isSetupBlocked, hasDraftValidationIssue, hasQueueFailure),
            isSetupBlocked,
            hasDraftValidationIssue,
            hasQueueFailure);
    }

    public static ExternalAudioImportReviewSummary Summarize(
        IEnumerable<ExternalAudioImportReviewRowProjection> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var snapshot = rows.ToArray();
        var readyCount = snapshot.Count(row => row.CanQueue);
        var setupBlockedCount = snapshot.Count(row => row.IsSetupBlocked);
        var duplicateCount = snapshot.Count(row => row.CanSkipDuplicate);
        var retryableCount = snapshot.Count(row => row.CanRetry);
        var issueCount = snapshot.Length - readyCount;
        var statusText = snapshot.Length == 0
            ? "No import rows in review."
            : $"{snapshot.Length} import row(s): {readyCount} ready, {setupBlockedCount} setup blocked, {duplicateCount} duplicate, {retryableCount} retryable, {issueCount - setupBlockedCount} other issue(s).";
        return new ExternalAudioImportReviewSummary(
            snapshot.Length,
            readyCount,
            setupBlockedCount,
            duplicateCount,
            retryableCount,
            issueCount,
            statusText);
    }

    public static bool IsRetryable(ExternalAudioImportPreflightStatus status) => status is
        ExternalAudioImportPreflightStatus.StillCopying or
        ExternalAudioImportPreflightStatus.MissingFile or
        ExternalAudioImportPreflightStatus.OfflinePlaceholder or
        ExternalAudioImportPreflightStatus.BlockedStorage or
        ExternalAudioImportPreflightStatus.Changing;

    private static string SafeStatusText(ExternalAudioImportCandidate candidate)
    {
        // Preflight messages may originate with a codec, a storage provider, or
        // a decoder. Even a message without an obvious Windows path can expose
        // source-derived transcript or diagnostic content, so the review UI
        // only renders an allowlisted explanation from the durable status code.
        return GetFallbackStatusText(candidate.Preflight.Status);
    }

    private static string GetFallbackStatusText(ExternalAudioImportPreflightStatus status) => status switch
    {
        ExternalAudioImportPreflightStatus.Ready => "Ready to queue.",
        ExternalAudioImportPreflightStatus.Duplicate => "This file is already in review or has already been imported.",
        ExternalAudioImportPreflightStatus.MissingFile => "The source file is no longer available.",
        ExternalAudioImportPreflightStatus.OfflinePlaceholder => "This source is offline. Make it available on this PC, then retry.",
        ExternalAudioImportPreflightStatus.UnsupportedExtension => "This file type is not supported for import.",
        ExternalAudioImportPreflightStatus.StillCopying => "Waiting for the source file to finish copying.",
        ExternalAudioImportPreflightStatus.DecodeFailed => "Meeting Recorder could not read this file. Choose another supported file or repair it, then review it again.",
        ExternalAudioImportPreflightStatus.EmptyAudio => "This source does not contain usable audio.",
        ExternalAudioImportPreflightStatus.UnsupportedLocation => "Choose a local source stored on this PC before importing.",
        ExternalAudioImportPreflightStatus.BlockedStorage => "Managed storage needs attention before this file can be queued.",
        ExternalAudioImportPreflightStatus.UnsupportedCodec => "This file uses an audio format that Meeting Recorder cannot read.",
        ExternalAudioImportPreflightStatus.TooShort => "This source is too short to create a reliable transcript.",
        ExternalAudioImportPreflightStatus.Changing => "The source changed while it was being reviewed. Try again after copying finishes.",
        ExternalAudioImportPreflightStatus.ResourceLimit => "This source is too large or complex for safe local preflight.",
        _ => "This source needs review before queueing.",
    };

    private static string SafeDisplayName(string candidateName)
    {
        var fileName = Path.GetFileName(candidateName ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(fileName) ? "Unnamed audio source" : fileName;
    }

    private static string GetMethodLabel(ExternalAudioImportMethod method) => method switch
    {
        ExternalAudioImportMethod.FilePicker => "Add files",
        ExternalAudioImportMethod.DragDrop => "Drag and drop",
        ExternalAudioImportMethod.ImportInbox => "Import Inbox",
        _ => "Watched folder",
    };

    private static string GetRecoveryText(
        ExternalAudioImportPreflightStatus status,
        bool isSetupBlocked,
        bool hasDraftValidationIssue,
        bool hasQueueFailure)
    {
        if (isSetupBlocked)
        {
            return "Open Setup, then return to this row.";
        }

        if (hasDraftValidationIssue)
        {
            return "Correct the title or local start time.";
        }

        if (hasQueueFailure)
        {
            return "Retry this row. The original source was not changed.";
        }

        return status switch
        {
            ExternalAudioImportPreflightStatus.Ready => "Queue when ready.",
            ExternalAudioImportPreflightStatus.Duplicate => "Skip duplicate or review the existing import.",
            ExternalAudioImportPreflightStatus.StillCopying or
            ExternalAudioImportPreflightStatus.Changing => "Wait for copying to finish, then retry.",
            ExternalAudioImportPreflightStatus.MissingFile or
            ExternalAudioImportPreflightStatus.OfflinePlaceholder => "Restore the local source, then retry.",
            ExternalAudioImportPreflightStatus.BlockedStorage => "Open Settings and make managed storage available.",
            _ => "Choose another source or remove this row. The original was not changed.",
        };
    }
}
