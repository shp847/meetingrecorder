using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalAudioImportReviewProjectionTests
{
    [Fact]
    public void CreateRow_Projects_A_Ready_Candidate_Without_A_Local_Path()
    {
        var candidate = Candidate(
            "C:\\private\\intake\\memo.wav",
            ExternalAudioImportPreflightStatus.Ready,
            "Ready to queue.");

        var row = ExternalAudioImportReviewProjection.CreateRow(
            candidate,
            isSetupBlocked: false,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);

        Assert.True(row.CanQueue);
        Assert.Equal("memo.wav", row.SourceDisplayName);
        Assert.Equal("Add files", row.SourceMethodLabel);
        Assert.Equal("Original stays in place.", row.RetentionText);
        Assert.Equal(64, row.Revision.Length);
        Assert.DoesNotContain(candidate.SourcePath, row.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(candidate.SourcePath, row.RecoveryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateRow_Maps_Setup_Duplicate_And_Retryable_States_To_Separate_Actions()
    {
        var ready = ExternalAudioImportReviewProjection.CreateRow(
            Candidate("C:\\intake\\ready.wav", ExternalAudioImportPreflightStatus.Ready, "Ready to queue."),
            isSetupBlocked: true,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);
        var duplicate = ExternalAudioImportReviewProjection.CreateRow(
            Candidate("C:\\intake\\duplicate.wav", ExternalAudioImportPreflightStatus.Duplicate, "Already imported."),
            isSetupBlocked: false,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);
        var changing = ExternalAudioImportReviewProjection.CreateRow(
            Candidate("C:\\intake\\changing.wav", ExternalAudioImportPreflightStatus.Changing, "The source changed while it was being reviewed."),
            isSetupBlocked: false,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);

        Assert.False(ready.CanQueue);
        Assert.Contains("Open Setup", ready.RecoveryText, StringComparison.Ordinal);
        Assert.True(duplicate.CanSkipDuplicate);
        Assert.False(duplicate.CanRetry);
        Assert.Contains("Skip duplicate", duplicate.RecoveryText, StringComparison.Ordinal);
        Assert.True(changing.CanRetry);
        Assert.Contains("then retry", changing.RecoveryText, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRow_Redacts_Arbitrary_Local_Locators_And_Diagnostics_From_Display_Status()
    {
        var candidate = Candidate(
            "C:\\private\\intake\\memo.wav",
            ExternalAudioImportPreflightStatus.DecodeFailed,
            "Decoder report at C:\\private\\diagnostics\\secret.log included private transcript text.");

        var row = ExternalAudioImportReviewProjection.CreateRow(
            candidate,
            isSetupBlocked: false,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);

        Assert.Equal("Meeting Recorder could not read this file. Choose another supported file or repair it, then review it again.", row.StatusText);
        Assert.DoesNotContain("C:\\private", row.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private transcript text", row.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateRow_Uses_The_Status_Allowlist_Even_When_A_Diagnostic_Has_No_Path()
    {
        var candidate = Candidate(
            "C:\\private\\intake\\memo.wav",
            ExternalAudioImportPreflightStatus.DecodeFailed,
            "Decoder payload included spoken meeting content without a locator.");

        var row = ExternalAudioImportReviewProjection.CreateRow(
            candidate,
            isSetupBlocked: false,
            hasDraftValidationIssue: false,
            hasQueueFailure: false);

        Assert.Equal("Meeting Recorder could not read this file. Choose another supported file or repair it, then review it again.", row.StatusText);
        Assert.DoesNotContain("spoken meeting content", row.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summarize_Reports_Stable_Counts_For_Bulk_Queue_Decisions()
    {
        var rows = new[]
        {
            ExternalAudioImportReviewProjection.CreateRow(
                Candidate("C:\\intake\\ready.wav", ExternalAudioImportPreflightStatus.Ready, "Ready to queue."),
                false, false, false),
            ExternalAudioImportReviewProjection.CreateRow(
                Candidate("C:\\intake\\setup.wav", ExternalAudioImportPreflightStatus.Ready, "Ready to queue."),
                true, false, false),
            ExternalAudioImportReviewProjection.CreateRow(
                Candidate("C:\\intake\\duplicate.wav", ExternalAudioImportPreflightStatus.Duplicate, "Already imported."),
                false, false, false),
            ExternalAudioImportReviewProjection.CreateRow(
                Candidate("C:\\intake\\locked.wav", ExternalAudioImportPreflightStatus.StillCopying, "Waiting for the source file."),
                false, false, false),
        };

        var summary = ExternalAudioImportReviewProjection.Summarize(rows);

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(1, summary.ReadyCount);
        Assert.Equal(1, summary.SetupBlockedCount);
        Assert.Equal(1, summary.DuplicateCount);
        Assert.Equal(1, summary.RetryableCount);
        Assert.Equal(3, summary.IssueCount);
        Assert.Equal("4 import row(s): 1 ready, 1 setup blocked, 1 duplicate, 1 retryable, 2 other issue(s).", summary.StatusText);
    }

    private static ExternalAudioImportCandidate Candidate(
        string sourcePath,
        ExternalAudioImportPreflightStatus status,
        string message) => new(
        sourcePath,
        Path.GetFileName(sourcePath),
        ExternalAudioImportMethod.FilePicker,
        "Imported meeting",
        DateTimeOffset.Parse("2026-09-27T18:00:00Z"),
        2_048,
        DateTimeOffset.Parse("2026-09-27T17:55:00Z"),
        new ExternalAudioImportPreflightResult(status, message, TimeSpan.FromSeconds(120)));
}
