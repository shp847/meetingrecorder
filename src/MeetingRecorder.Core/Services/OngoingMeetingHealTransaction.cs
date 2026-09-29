namespace MeetingRecorder.Core.Services;

public enum OngoingMeetingHealTransactionStatus
{
    Healed = 0,
    AlreadyHealed = 1,
    Rejected = 2,
    Busy = 3,
}

public sealed record OngoingMeetingHealTransactionRequest(
    OngoingMeetingHealCandidate Candidate,
    MeetingOutputRecord PredecessorOutput,
    MeetingOutputRecord SuccessorOutput,
    string AudioOutputDirectory,
    string TranscriptOutputDirectory,
    string ArchiveDirectory,
    string ReceiptPath,
    string ReasonCode = "same-strong-identity");

public sealed record OngoingMeetingHealTransactionResult(
    OngoingMeetingHealTransactionStatus Status,
    string? SurvivingStem = null,
    string? ArchiveDirectory = null);

/// <summary>
/// Executes one current-work heal under a local lease.  It deliberately has no
/// catalog scan or event subscription: callers supply the already-evaluated,
/// current pair so a stale or broad scan cannot rewrite history.
/// </summary>
internal sealed class OngoingMeetingHealTransaction
{
    private readonly OngoingMeetingHealService _admission;
    private readonly MeetingCleanupExecutionService _execution;

    public OngoingMeetingHealTransaction(
        MeetingCleanupExecutionService execution,
        OngoingMeetingHealService? admission = null)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _admission = admission ?? new OngoingMeetingHealService();
    }

    public async Task<OngoingMeetingHealTransactionResult> ExecuteAsync(
        OngoingMeetingHealTransactionRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);

        var receiptStore = new OngoingMeetingHealReceiptStore(request.ReceiptPath);
        if (OngoingMeetingHealService.IsCoveredBy(await receiptStore.TryLoadAsync(cancellationToken), request.Candidate))
        {
            return new(OngoingMeetingHealTransactionStatus.AlreadyHealed);
        }

        var leasePath = request.ReceiptPath + ".lease";
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        FileStream? lease = null;
        try
        {
            try
            {
                lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException)
            {
                return new(OngoingMeetingHealTransactionStatus.Busy);
            }

            if (OngoingMeetingHealService.IsCoveredBy(await receiptStore.TryLoadAsync(cancellationToken), request.Candidate))
            {
                return new(OngoingMeetingHealTransactionStatus.AlreadyHealed);
            }

            if (!HasCompletePublishedArtifacts(request.PredecessorOutput) ||
                !HasCompletePublishedArtifacts(request.SuccessorOutput) ||
                !_admission.TryAdmit(request.Candidate, nowUtc))
            {
                return new(OngoingMeetingHealTransactionStatus.Rejected);
            }

            // Do not accept cancellation after the preflight: the shared merge
            // archives the old artifacts before promoting its prepared outputs.
            var merge = await _execution.MergeMeetingsAsync(
                request.PredecessorOutput,
                request.SuccessorOutput,
                request.PredecessorOutput.Title,
                request.AudioOutputDirectory,
                request.TranscriptOutputDirectory,
                request.ArchiveDirectory,
                CancellationToken.None);

            var receipt = new OngoingMeetingHealReceipt(
                OngoingMeetingHealReceipt.CurrentSchemaVersion,
                request.Candidate.Predecessor.SessionId,
                request.Candidate.Successor.SessionId,
                nowUtc,
                request.ReasonCode,
                merge.ArchiveDirectory,
                merge.SurvivingStem,
                request.PredecessorOutput.Stem,
                request.SuccessorOutput.Stem);
            await receiptStore.SaveAsync(receipt, CancellationToken.None);
            try
            {
                await AppendVisibleHistoryAsync(
                    Path.Combine(request.TranscriptOutputDirectory, $"{merge.SurvivingStem}.md"),
                    request.Candidate,
                    request.ReasonCode,
                    nowUtc);
            }
            catch
            {
                await new OngoingMeetingHealReversalService().ReverseAsync(
                    new(request.ReceiptPath, request.AudioOutputDirectory, request.TranscriptOutputDirectory),
                    nowUtc,
                    CancellationToken.None);
                throw;
            }
            return new(OngoingMeetingHealTransactionStatus.Healed, merge.SurvivingStem, merge.ArchiveDirectory);
        }
        finally
        {
            lease?.Dispose();
            if (lease is not null && File.Exists(leasePath))
            {
                File.Delete(leasePath);
            }
        }
    }

    private static bool HasCompletePublishedArtifacts(MeetingOutputRecord output) =>
        !string.IsNullOrWhiteSpace(output.AudioPath) && File.Exists(output.AudioPath) &&
        !string.IsNullOrWhiteSpace(output.MarkdownPath) && File.Exists(output.MarkdownPath) &&
        !string.IsNullOrWhiteSpace(output.JsonPath) && File.Exists(output.JsonPath) &&
        !string.IsNullOrWhiteSpace(output.ReadyMarkerPath) && File.Exists(output.ReadyMarkerPath);

    private static async Task AppendVisibleHistoryAsync(
        string markdownPath,
        OngoingMeetingHealCandidate candidate,
        string reasonCode,
        DateTimeOffset healedAtUtc)
    {
        var existing = await File.ReadAllTextAsync(markdownPath, CancellationToken.None);
        var history = string.Join(
            Environment.NewLine,
            string.Empty,
            "## Continuity history",
            string.Empty,
            $"- Healed automatically: {healedAtUtc:O}",
            $"- Reason: {reasonCode}",
            $"- Source session IDs: {candidate.Predecessor.SessionId}, {candidate.Successor.SessionId}",
            "- Original published artifacts: retained in the local archive and can be reversed.",
            string.Empty);
        await File.WriteAllTextAsync(markdownPath, existing.TrimEnd() + Environment.NewLine + history, CancellationToken.None);
    }

    private static void ValidateRequest(OngoingMeetingHealTransactionRequest request)
    {
        if (string.Equals(request.Candidate.Predecessor.SessionId, request.Candidate.Successor.SessionId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.PredecessorOutput.ManifestPath) ||
            string.IsNullOrWhiteSpace(request.SuccessorOutput.ManifestPath))
        {
            throw new ArgumentException("Each distinct source meeting must retain its manifest reference.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ReceiptPath) || string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw new ArgumentException("A heal receipt path and reason are required.", nameof(request));
        }
    }
}
