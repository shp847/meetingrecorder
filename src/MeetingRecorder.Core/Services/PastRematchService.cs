using System.Security.Cryptography;
using System.Text;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record PastRematchResult(
    PastRematchPlanItem PlanItem,
    PastRematchOutcome Outcome,
    string? Warning,
    IReadOnlyList<SpeakerNamePrediction> Predictions);

/// <summary>Runs one explicitly requested, metadata-only historic name rematch.</summary>
public sealed class PastRematchService
{
    private readonly SessionManifestStore _manifestStore;
    private readonly VoiceProfileStore _profileStore;
    private readonly SpeakerNameCorrectionService _correctionService;
    private readonly PastRematchReceiptStore _receiptStore;

    public PastRematchService(
        SessionManifestStore manifestStore,
        VoiceProfileStore profileStore,
        SpeakerNameCorrectionService correctionService,
        PastRematchReceiptStore? receiptStore = null)
    {
        _manifestStore = manifestStore;
        _profileStore = profileStore;
        _correctionService = correctionService;
        _receiptStore = receiptStore ?? new PastRematchReceiptStore();
    }

    public async Task<PastRematchResult> RematchOneAsync(
        MeetingOutputRecord record,
        SpeakerNameLearningMode learningMode,
        SpeakerNameRecognitionOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await RematchOneAsync(record, learningMode, options, now, operationId: null, cancellationToken);
    }

    public async Task<PastRematchResult> RematchOneAsync(
        MeetingOutputRecord record,
        SpeakerNameLearningMode learningMode,
        SpeakerNameRecognitionOptions options,
        DateTimeOffset now,
        string? operationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = await BuildPlanAsync(record, isBusy: false, cancellationToken);
        var planItem = AssertSinglePlanItem(plan, record);
        operationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        var existing = await _receiptStore.FindAsync(operationId, planItem.MeetingStableIdentity, cancellationToken);
        if (existing is not null)
        {
            return new PastRematchResult(planItem, existing.Outcome, existing.Reason, Array.Empty<SpeakerNamePrediction>());
        }
        if (learningMode == SpeakerNameLearningMode.Disabled)
        {
            return await RecordAsync(new PastRematchResult(
                planItem with { Disposition = PastRematchDisposition.Skip, Reason = "Local speaker-name learning is disabled." },
                PastRematchOutcome.Skipped,
                "Local speaker-name learning is disabled.",
                Array.Empty<SpeakerNamePrediction>()), operationId, now, cancellationToken);
        }
        if (planItem.Disposition != PastRematchDisposition.Eligible)
        {
            return await RecordAsync(new PastRematchResult(planItem, ToOutcome(planItem.Disposition), planItem.Reason, Array.Empty<SpeakerNamePrediction>()), operationId, now, cancellationToken);
        }

        var attributionOperationId = "past-rematch-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            operationId + "\n" + planItem.MeetingStableIdentity + "\n" + planItem.ArtifactRevision)))[..24];
        var refresh = await _correctionService.RefreshSpeakerNameAttributionAsync(
            record,
            learningMode,
            options,
            now,
            attributionOperationId,
            cancellationToken);
        var outcome = refresh.Predictions.Any(prediction => prediction.Source == SpeakerNameSource.AutoAppliedVoiceProfile)
            ? PastRematchOutcome.AutoApplied
            : refresh.Predictions.Any(prediction => prediction.Source == SpeakerNameSource.SuggestedVoiceProfile)
                ? PastRematchOutcome.Suggested
                : PastRematchOutcome.Unchanged;
        return await RecordAsync(new PastRematchResult(planItem, outcome, refresh.Warning, refresh.Predictions), operationId, now, cancellationToken);
    }

    public async Task<IReadOnlyList<PastRematchResult>> RematchBatchAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        SpeakerNameLearningMode learningMode,
        SpeakerNameRecognitionOptions options,
        DateTimeOffset now,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOperationId = string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId.Trim();
        var results = new List<PastRematchResult>();
        var pending = new List<(MeetingOutputRecord Record, PastRematchPlanItem PlanItem)>();
        foreach (var record in records)
        {
            pending.Add((record, AssertSinglePlanItem(
                await BuildPlanAsync(record, isBusy: false, CancellationToken.None),
                record)));
        }
        for (var index = 0; index < pending.Count; index++)
        {
            var target = pending[index];
            if (cancellationToken.IsCancellationRequested)
            {
                results.AddRange(await RecordCancelledAsync(pending.Skip(index), effectiveOperationId, now));
                break;
            }

            try
            {
                results.Add(await RematchOneAsync(target.Record, learningMode, options, now, effectiveOperationId, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                results.AddRange(await RecordCancelledAsync(pending.Skip(index), effectiveOperationId, now));
                break;
            }
        }
        return results;
    }

    public async Task<PastRematchPlan> BuildPlanAsync(
        MeetingOutputRecord record,
        bool isBusy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var revision = MeetingOutputCatalogService.GetSpeakerArtifactRevision(record);
        var manifest = string.IsNullOrWhiteSpace(record.ManifestPath) || !File.Exists(record.ManifestPath)
            ? null
            : await _manifestStore.LoadAsync(record.ManifestPath, cancellationToken);
        var speakers = manifest?.ProcessingMetadata?.Speakers ?? Array.Empty<SpeakerIdentity>();
        var samples = manifest?.ProcessingMetadata?.SpeakerVoiceSamples ?? Array.Empty<SpeakerVoiceSample>();
        var profiles = await _profileStore.LoadOrCreateAsync(cancellationToken);
        var target = new PastRematchTarget(
            manifest?.SessionId ?? record.Stem,
            revision,
            CreateMetadataFingerprint(speakers, samples),
            record.ManifestState == SessionState.Published && !string.IsNullOrWhiteSpace(record.JsonPath) && File.Exists(record.JsonPath),
            samples.Any(sample => profiles.Profiles.Any(profile =>
                profile.Status == VoiceProfileStatus.Active &&
                profile.EmbeddingDimension == sample.EmbeddingDimension &&
                string.Equals(profile.EmbeddingModelFileName, sample.EmbeddingModelFileName, StringComparison.OrdinalIgnoreCase))),
            profiles.Profiles.Any(profile => profile.Status == VoiceProfileStatus.Active),
            speakers.Any(speaker => speaker.NameSource is SpeakerNameSource.None or SpeakerNameSource.SuggestedVoiceProfile),
            speakers.Any(speaker => speaker.IsUserEdited || speaker.NameSource == SpeakerNameSource.UserEdited),
            HasSegmentOverride: false,
            samples.Any(sample => profiles.Profiles.Any(profile => profile.RejectedMatches?.Any(match =>
                string.Equals(match.MeetingId, manifest?.SessionId, StringComparison.Ordinal) &&
                string.Equals(match.SpeakerId, sample.SpeakerId, StringComparison.Ordinal)) == true)),
            isBusy,
            HasCurrentArtifactFingerprint: !string.IsNullOrWhiteSpace(revision));
        return PastRematchPlanner.Build([target]);
    }

    private static PastRematchPlanItem AssertSinglePlanItem(PastRematchPlan plan, MeetingOutputRecord record) =>
        plan.Items.SingleOrDefault() ?? new PastRematchPlanItem(
            record.Stem,
            string.Empty,
            string.Empty,
            PastRematchDisposition.Blocked,
            "Meeting metadata is unavailable. Reload before rematching names.");

    private static PastRematchOutcome ToOutcome(PastRematchDisposition disposition) =>
        disposition == PastRematchDisposition.Blocked ? PastRematchOutcome.Failed : PastRematchOutcome.Skipped;

    private static string CreateMetadataFingerprint(
        IReadOnlyList<SpeakerIdentity> speakers,
        IReadOnlyList<SpeakerVoiceSample> samples)
    {
        var metadata = speakers.Select(speaker => $"{speaker.Id}|{speaker.NameSource}|{speaker.ProfileId}|{speaker.IsUserEdited}")
            .Concat(samples.Select(sample => $"{sample.SpeakerId}|{sample.EmbeddingModelFileName}|{sample.EmbeddingDimension}|{sample.SpeechDuration}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", metadata))));
    }

    private async Task<PastRematchResult> RecordAsync(
        PastRematchResult result,
        string operationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            await _receiptStore.AppendOrReplaceAsync(new PastRematchReceipt(
                PastRematchReceipt.CurrentSchemaVersion,
                operationId,
                result.PlanItem.MeetingStableIdentity,
                result.PlanItem.ArtifactRevision,
                result.Outcome,
                result.Warning ?? result.PlanItem.Reason,
                now), cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return result with { Warning = string.Join(" ", new[] { result.Warning, "Local rematch outcome receipt could not be saved." }.Where(value => !string.IsNullOrWhiteSpace(value))) };
        }
    }

    private async Task<IReadOnlyList<PastRematchResult>> RecordCancelledAsync(
        IEnumerable<(MeetingOutputRecord Record, PastRematchPlanItem PlanItem)> pending,
        string operationId,
        DateTimeOffset now)
    {
        var results = new List<PastRematchResult>();
        foreach (var target in pending)
        {
            results.Add(await RecordAsync(new PastRematchResult(
                target.PlanItem,
                PastRematchOutcome.Cancelled,
                "Rematch cancelled before this meeting was dispatched; no work was rolled back.",
                Array.Empty<SpeakerNamePrediction>()), operationId, now, CancellationToken.None));
        }

        return results;
    }
}
