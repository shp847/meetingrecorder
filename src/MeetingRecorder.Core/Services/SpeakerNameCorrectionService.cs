using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerNameCorrectionResult(
    SpeakerNameLearningResult LearningResult,
    string? LearningWarning,
    int RejectedCount = 0,
    bool RequiresReload = false);

/// <summary>
/// A user-confirmed Meeting Display Name edit. The expected values make the edit safe when
/// diarization is regenerated while a detail view remains open.
/// </summary>
public sealed record SpeakerNameCorrectionDraft(
    string SpeakerId,
    string ExpectedDisplayName,
    string UpdatedDisplayName,
    string? ExpectedProfileId,
    SpeakerNameSource ExpectedNameSource,
    bool RejectSuggestion = false,
    string? ExpectedSuggestedDisplayName = null);

public sealed record SpeakerNameReviewRequest(
    string ExpectedArtifactRevision,
    IReadOnlyList<SpeakerNameCorrectionDraft> Drafts);

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
        var manifest = await TryLoadManifestAsync(record.ManifestPath, cancellationToken);
        if (manifest?.ProcessingMetadata?.Speakers is not { Count: > 0 } speakers)
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count),
                "Meeting manifest is unavailable. Reload or reprocess this meeting before changing names.",
                RequiresReload: true);
        }

        var drafts = speakers
            .Where(speaker =>
                !string.IsNullOrWhiteSpace(speaker.Id) &&
                !string.IsNullOrWhiteSpace(speaker.DisplayName) &&
                speakerLabelMap.TryGetValue(speaker.DisplayName.Trim(), out var updatedDisplayName) &&
                !string.IsNullOrWhiteSpace(updatedDisplayName) &&
                !string.Equals(speaker.DisplayName.Trim(), updatedDisplayName.Trim(), StringComparison.Ordinal))
            .Select(speaker => new SpeakerNameCorrectionDraft(
                speaker.Id,
                speaker.DisplayName,
                speakerLabelMap[speaker.DisplayName.Trim()],
                speaker.ProfileId,
                speaker.NameSource))
            .ToArray();
        return await ApplyReviewAsync(
            record,
            new SpeakerNameReviewRequest(
                MeetingOutputCatalogService.GetSpeakerArtifactRevision(record),
                drafts),
            learningMode,
            now,
            cancellationToken);
    }

    /// <summary>
    /// Applies explicit name-review decisions only if their label IDs, expected prior values, and
    /// artifact revision still match. Conflicts preserve drafts and require a reload.
    /// </summary>
    public async Task<SpeakerNameCorrectionResult> ApplyReviewAsync(
        MeetingOutputRecord record,
        SpeakerNameReviewRequest request,
        SpeakerNameLearningMode learningMode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var drafts = request.Drafts ?? Array.Empty<SpeakerNameCorrectionDraft>();
        var requestedCount = drafts.Count;
        if (requestedCount == 0)
        {
            return new SpeakerNameCorrectionResult(new SpeakerNameLearningResult(0, 0, 0), null);
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedArtifactRevision))
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                "Meeting speaker data needs reload before changes can be applied.",
                RequiresReload: true);
        }

        if (!string.Equals(
                MeetingOutputCatalogService.GetSpeakerArtifactRevision(record),
                request.ExpectedArtifactRevision,
                StringComparison.Ordinal))
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                "Meeting speaker data changed. Reload before applying name changes.",
                RequiresReload: true);
        }

        var manifestBeforeChange = await TryLoadManifestAsync(record.ManifestPath, cancellationToken);
        if (manifestBeforeChange?.ProcessingMetadata?.Speakers is not { Count: > 0 } speakers)
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                "Meeting manifest is unavailable. Reload or reprocess this meeting before changing names.",
                RequiresReload: true);
        }

        var draftsBySpeakerId = drafts
            .Where(draft => !string.IsNullOrWhiteSpace(draft.SpeakerId))
            .GroupBy(draft => draft.SpeakerId.Trim(), StringComparer.Ordinal)
            .ToArray();
        if (draftsBySpeakerId.Length != requestedCount || draftsBySpeakerId.Any(group => group.Count() != 1))
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                "Speaker name review contains invalid or duplicate diarization labels. Reload before trying again.",
                RequiresReload: true);
        }

        var currentSpeakerGroups = speakers
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker.Id))
            .GroupBy(speaker => speaker.Id.Trim(), StringComparer.Ordinal)
            .ToArray();
        if (currentSpeakerGroups.Length != speakers.Count || currentSpeakerGroups.Any(group => group.Count() != 1))
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                "Meeting speaker metadata is invalid. Reload or reprocess this meeting before changing names.",
                RequiresReload: true);
        }

        var currentBySpeakerId = currentSpeakerGroups.ToDictionary(
            group => group.Key,
            group => group.Single(),
            StringComparer.Ordinal);
        var normalizedDrafts = new List<SpeakerNameCorrectionDraft>(draftsBySpeakerId.Length);
        foreach (var group in draftsBySpeakerId)
        {
            var draft = group.Single();
            if (!currentBySpeakerId.TryGetValue(group.Key, out var current) ||
                !MatchesExpectedSpeaker(current, draft))
            {
                return new SpeakerNameCorrectionResult(
                    new SpeakerNameLearningResult(0, 0, requestedCount),
                    "A Diarization Label changed while this review was open. Reload before applying name changes.",
                    RequiresReload: true);
            }

            normalizedDrafts.Add(draft with
            {
                SpeakerId = draft.SpeakerId.Trim(),
                ExpectedDisplayName = NormalizeDisplayName(draft.ExpectedDisplayName),
                UpdatedDisplayName = NormalizeDisplayName(draft.UpdatedDisplayName),
            });
        }

        var updatedBySpeakerId = new Dictionary<string, SpeakerIdentity>(StringComparer.Ordinal);
        var rejectedMatches = new List<SpeakerNameRejectedMatch>();
        var confirmedCorrections = new List<SpeakerNameCorrectionDraft>();
        foreach (var draft in normalizedDrafts)
        {
            var current = currentBySpeakerId[draft.SpeakerId];
            if (!string.IsNullOrWhiteSpace(draft.UpdatedDisplayName) &&
                !string.Equals(current.DisplayName, draft.UpdatedDisplayName, StringComparison.Ordinal))
            {
                updatedBySpeakerId[draft.SpeakerId] = current with
                {
                    DisplayName = draft.UpdatedDisplayName,
                    IsUserEdited = true,
                    ProfileId = null,
                    NameSource = SpeakerNameSource.UserEdited,
                    Confidence = null,
                    SuggestedDisplayName = null,
                    DecisionReason = null,
                };
                confirmedCorrections.Add(draft);
                continue;
            }

            if (draft.RejectSuggestion &&
                current.NameSource == SpeakerNameSource.SuggestedVoiceProfile &&
                !string.IsNullOrWhiteSpace(current.ProfileId))
            {
                updatedBySpeakerId[draft.SpeakerId] = current with
                {
                    ProfileId = null,
                    NameSource = SpeakerNameSource.None,
                    Confidence = null,
                    SuggestedDisplayName = null,
                    DecisionReason = null,
                };
                if (!string.IsNullOrWhiteSpace(manifestBeforeChange.SessionId))
                {
                    rejectedMatches.Add(new SpeakerNameRejectedMatch(
                        current.ProfileId,
                        manifestBeforeChange.SessionId,
                        current.Id));
                }
            }
        }

        if (updatedBySpeakerId.Count == 0)
        {
            return new SpeakerNameCorrectionResult(new SpeakerNameLearningResult(0, 0, requestedCount), null);
        }

        var transaction = await _outputCatalogService.UpdateSpeakerIdentitiesIfCurrentAsync(
            record,
            request.ExpectedArtifactRevision,
            updatedBySpeakerId.Values.ToArray(),
            cancellationToken);
        if (!transaction.IsApplied)
        {
            return new SpeakerNameCorrectionResult(
                new SpeakerNameLearningResult(0, 0, requestedCount),
                transaction.ConflictReason ?? "Meeting speaker data changed. Reload before applying name changes.",
                RequiresReload: true);
        }

        var warnings = new List<string>();
        SpeakerNameLearningResult learningResult;
        try
        {
            learningResult = await _learningService.LearnFromConfirmedCorrectionsAsync(
                manifestBeforeChange,
                confirmedCorrections,
                learningMode,
                now,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            learningResult = new SpeakerNameLearningResult(0, 0, confirmedCorrections.Count);
            warnings.Add("Local Voice Profile learning could not be saved.");
        }

        var rejectedCount = 0;
        if (rejectedMatches.Count > 0)
        {
            try
            {
                rejectedCount = await _learningService.RejectMatchesAsync(rejectedMatches, now, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add("Local Name Suggestion rejection could not be saved.");
            }
        }

        return new SpeakerNameCorrectionResult(
            learningResult,
            warnings.Count == 0 ? null : string.Join(" ", warnings),
            rejectedCount);
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

        var expectedArtifactRevision = MeetingOutputCatalogService.GetSpeakerArtifactRevision(record);
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
            var transaction = await _outputCatalogService.UpdateSpeakerIdentitiesIfCurrentAsync(
                record,
                expectedArtifactRevision,
                updatedSpeakers,
                cancellationToken);
            if (!transaction.IsApplied)
            {
                return new SpeakerNameRefreshResult(
                    Array.Empty<SpeakerNamePrediction>(),
                    transaction.ConflictReason ?? "Meeting speaker data changed. Reload before refreshing local suggestions.");
            }

            await _learningService.UpdateLastMatchedAsync(predictions, now, cancellationToken);
            return new SpeakerNameRefreshResult(predictions, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SpeakerNameRefreshResult(
                Array.Empty<SpeakerNamePrediction>(),
                "Local Name Suggestions could not be refreshed. Retry local profile storage or review Meeting Display Names only.");
        }
    }

    public async Task<SpeakerNameUndoResult> UndoProfileSpeakerNameRecognitionAsync(
        MeetingOutputRecord record,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expectedArtifactRevision = MeetingOutputCatalogService.GetSpeakerArtifactRevision(record);
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

        var transaction = await _outputCatalogService.UpdateSpeakerIdentitiesIfCurrentAsync(
            record,
            expectedArtifactRevision,
            updatedSpeakers,
            cancellationToken);
        if (!transaction.IsApplied)
        {
            return new SpeakerNameUndoResult(
                0,
                0,
                transaction.ConflictReason ?? "Meeting speaker data changed. Reload before undoing profile names.");
        }

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
                "Profile names were removed, but local suggestion suppression could not be saved.");
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

    private static bool MatchesExpectedSpeaker(SpeakerIdentity current, SpeakerNameCorrectionDraft draft)
    {
        return string.Equals(
                   NormalizeDisplayName(current.DisplayName),
                   NormalizeDisplayName(draft.ExpectedDisplayName),
                   StringComparison.Ordinal) &&
               string.Equals(
                   NormalizeOptional(current.ProfileId),
                   NormalizeOptional(draft.ExpectedProfileId),
                   StringComparison.Ordinal) &&
               current.NameSource == draft.ExpectedNameSource;
    }

    private static string NormalizeDisplayName(string value) => string.Join(
        " ",
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim();

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
