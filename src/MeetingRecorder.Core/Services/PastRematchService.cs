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

    public PastRematchService(
        SessionManifestStore manifestStore,
        VoiceProfileStore profileStore,
        SpeakerNameCorrectionService correctionService)
    {
        _manifestStore = manifestStore;
        _profileStore = profileStore;
        _correctionService = correctionService;
    }

    public async Task<PastRematchResult> RematchOneAsync(
        MeetingOutputRecord record,
        SpeakerNameLearningMode learningMode,
        SpeakerNameRecognitionOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = await BuildPlanAsync(record, isBusy: false, cancellationToken);
        var planItem = AssertSinglePlanItem(plan, record);
        if (learningMode == SpeakerNameLearningMode.Disabled)
        {
            return new PastRematchResult(
                planItem with { Disposition = PastRematchDisposition.Skip, Reason = "Local speaker-name learning is disabled." },
                PastRematchOutcome.Skipped,
                "Local speaker-name learning is disabled.",
                Array.Empty<SpeakerNamePrediction>());
        }
        if (planItem.Disposition != PastRematchDisposition.Eligible)
        {
            return new PastRematchResult(planItem, ToOutcome(planItem.Disposition), planItem.Reason, Array.Empty<SpeakerNamePrediction>());
        }

        var operationId = "past-rematch-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            planItem.MeetingStableIdentity + "\n" + planItem.ArtifactRevision)))[..24];
        var refresh = await _correctionService.RefreshSpeakerNameAttributionAsync(
            record,
            learningMode,
            options,
            now,
            operationId,
            cancellationToken);
        var outcome = refresh.Predictions.Any(prediction => prediction.Source == SpeakerNameSource.AutoAppliedVoiceProfile)
            ? PastRematchOutcome.AutoApplied
            : refresh.Predictions.Any(prediction => prediction.Source == SpeakerNameSource.SuggestedVoiceProfile)
                ? PastRematchOutcome.Suggested
                : PastRematchOutcome.Unchanged;
        return new PastRematchResult(planItem, outcome, refresh.Warning, refresh.Predictions);
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
}
