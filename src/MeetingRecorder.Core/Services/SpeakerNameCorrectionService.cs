using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerNameCorrectionResult(
    SpeakerNameLearningResult LearningResult,
    string? LearningWarning);

public sealed record SpeakerNameRejectionResult(
    int RejectedCount,
    string? Warning);

public sealed record SpeakerNameRefreshResult(
    IReadOnlyList<SpeakerNamePrediction> Predictions,
    string? Warning);

public sealed record SpeakerNameUndoResult(
    int UpdatedSpeakerCount,
    int SuppressedMatchCount,
    string? Warning);

public sealed class SpeakerNameCorrectionService
{
    private readonly MeetingOutputCatalogService _outputCatalogService;
    private readonly SessionManifestStore _manifestStore;
    private readonly SpeakerNameLearningService _learningService;
    private readonly VoiceProfileMatcher _profileMatcher;
    private readonly VoiceProfileStore _profileStore;

    public SpeakerNameCorrectionService(
        MeetingOutputCatalogService outputCatalogService,
        SessionManifestStore manifestStore,
        SpeakerNameLearningService learningService,
        VoiceProfileMatcher profileMatcher,
        VoiceProfileStore profileStore)
    {
        _outputCatalogService = outputCatalogService;
        _manifestStore = manifestStore;
        _learningService = learningService;
        _profileMatcher = profileMatcher;
        _profileStore = profileStore;
    }

    public async Task<SpeakerNameCorrectionResult> ApplyCorrectionsAsync(
        MeetingOutputRecord record,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        SpeakerNameLearningMode learningMode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manifestBeforeRename = await TryLoadManifestAsync(record.ManifestPath, cancellationToken);
        await _outputCatalogService.RenameSpeakerLabelsAsync(record, speakerLabelMap, cancellationToken);

        if (manifestBeforeRename is null)
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count),
                "Speaker-name learning skipped because the original meeting manifest is unavailable.");
        }

        try
        {
            var learningResult = await _learningService.LearnFromCorrectionsAsync(
                manifestBeforeRename,
                speakerLabelMap,
                learningMode,
                now,
                cancellationToken);
            return new SpeakerNameCorrectionResult(learningResult, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count),
                $"Speaker-name learning skipped: {exception.Message}");
        }
    }

    public async Task<SpeakerNameRejectionResult> RejectMatchesAsync(
        MeetingOutputRecord record,
        IReadOnlyList<SpeakerNameRejectedMatch> rejectedMatches,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rejectedMatches.Count == 0)
        {
            return new SpeakerNameRejectionResult(0, null);
        }

        await _outputCatalogService.ClearSpeakerProfileSuggestionsAsync(record, rejectedMatches, cancellationToken);
        try
        {
            var rejectedCount = await _learningService.RejectMatchesAsync(rejectedMatches, now, cancellationToken);
            return new SpeakerNameRejectionResult(rejectedCount, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SpeakerNameRejectionResult(0, $"Speaker-name rejection memory skipped: {exception.Message}");
        }
    }

    public async Task<SpeakerNameRefreshResult> RefreshSpeakerNameAttributionAsync(
        MeetingOutputRecord record,
        SpeakerNameLearningMode learningMode,
        SpeakerNameRecognitionOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (learningMode == SpeakerNameLearningMode.Disabled)
        {
            return new SpeakerNameRefreshResult(
                Array.Empty<SpeakerNamePrediction>(),
                "Speaker-name refresh skipped because local speaker-name learning is disabled.");
        }

        var manifest = await TryLoadManifestAsync(record.ManifestPath, cancellationToken);
        if (manifest?.ProcessingMetadata?.Speakers is not { Count: > 0 } speakers ||
            manifest.ProcessingMetadata.SpeakerVoiceSamples is not { Count: > 0 } samples)
        {
            return new SpeakerNameRefreshResult(
                Array.Empty<SpeakerNamePrediction>(),
                "Speaker-name refresh skipped because this meeting does not have stored speaker voice samples.");
        }

        try
        {
            var profiles = (await _profileStore.LoadOrCreateAsync(cancellationToken)).Profiles;
            if (profiles.Count == 0)
            {
                return new SpeakerNameRefreshResult(
                    Array.Empty<SpeakerNamePrediction>(),
                    "Speaker-name refresh skipped because no local voice profiles are available yet.");
            }

            var predictions = _profileMatcher.Match(samples, profiles, options, manifest.SessionId);
            if (predictions.Count == 0)
            {
                return new SpeakerNameRefreshResult(Array.Empty<SpeakerNamePrediction>(), null);
            }

            var updatedSpeakers = _profileMatcher.ApplyPredictions(speakers, predictions);
            await _outputCatalogService.UpdateSpeakerIdentitiesAsync(record, updatedSpeakers, cancellationToken);
            await _learningService.UpdateLastMatchedAsync(predictions, now, cancellationToken);
            return new SpeakerNameRefreshResult(predictions, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SpeakerNameRefreshResult(
                Array.Empty<SpeakerNamePrediction>(),
                $"Speaker-name refresh skipped: {exception.Message}");
        }
    }

    public async Task<SpeakerNameUndoResult> UndoProfileSpeakerNameRecognitionAsync(
        MeetingOutputRecord record,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = await TryLoadManifestAsync(record.ManifestPath, cancellationToken);
        if (manifest?.ProcessingMetadata?.Speakers is not { Count: > 0 } speakers)
        {
            return new SpeakerNameUndoResult(
                0,
                0,
                "No undoable voice-profile speaker names were found because the meeting speaker metadata is unavailable.");
        }

        var updatedSpeakers = new List<SpeakerIdentity>(speakers.Count);
        var rejectedMatches = new List<SpeakerNameRejectedMatch>();
        var updatedSpeakerCount = 0;
        for (var index = 0; index < speakers.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var speaker = speakers[index];
            if (!IsUndoableProfileAttribution(speaker))
            {
                updatedSpeakers.Add(speaker);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(speaker.ProfileId))
            {
                rejectedMatches.Add(new SpeakerNameRejectedMatch(
                    speaker.ProfileId,
                    manifest.SessionId,
                    speaker.Id));
            }

            updatedSpeakers.Add(ClearProfileAttribution(speaker, index));
            updatedSpeakerCount++;
        }

        if (updatedSpeakerCount == 0)
        {
            return new SpeakerNameUndoResult(0, 0, null);
        }

        await _outputCatalogService.UpdateSpeakerIdentitiesAsync(record, updatedSpeakers, cancellationToken);

        try
        {
            var suppressedMatchCount = await _learningService.RejectMatchesAsync(
                rejectedMatches,
                now,
                cancellationToken);
            return new SpeakerNameUndoResult(updatedSpeakerCount, suppressedMatchCount, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SpeakerNameUndoResult(
                updatedSpeakerCount,
                0,
                $"Speaker-name undo completed, but profile feedback could not be saved: {exception.Message}");
        }
    }

    private async Task<MeetingSessionManifest?> TryLoadManifestAsync(
        string? manifestPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        return await _manifestStore.LoadAsync(manifestPath, cancellationToken);
    }

    private static bool IsUndoableProfileAttribution(SpeakerIdentity speaker)
    {
        return !speaker.IsUserEdited &&
            !string.IsNullOrWhiteSpace(speaker.ProfileId) &&
            speaker.NameSource is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile;
    }

    private static SpeakerIdentity ClearProfileAttribution(SpeakerIdentity speaker, int speakerIndex)
    {
        var displayName = speaker.NameSource == SpeakerNameSource.AutoAppliedVoiceProfile ||
                          string.IsNullOrWhiteSpace(speaker.DisplayName)
            ? $"Speaker {speakerIndex + 1}"
            : speaker.DisplayName;

        return speaker with
        {
            DisplayName = displayName,
            ProfileId = null,
            NameSource = SpeakerNameSource.None,
            Confidence = null,
            SuggestedDisplayName = null,
            DecisionReason = null,
        };
    }
}
