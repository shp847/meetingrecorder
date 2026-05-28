using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerNameLearningResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount);

public sealed record SpeakerNameRejectedMatch(
    string ProfileId,
    string MeetingId,
    string SpeakerId);

public sealed class SpeakerNameLearningService
{
    private const int MaximumMeetingIdsPerProfile = 128;
    private const int MaximumRejectedMatchesPerProfile = 256;
    private readonly VoiceProfileStore _profileStore;

    public SpeakerNameLearningService(VoiceProfileStore profileStore)
    {
        _profileStore = profileStore;
    }

    public async Task<SpeakerNameLearningResult> LearnFromCorrectionsAsync(
        MeetingSessionManifest manifest,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        SpeakerNameLearningMode learningMode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (learningMode == SpeakerNameLearningMode.Disabled ||
            speakerLabelMap.Count == 0 ||
            manifest.ProcessingMetadata?.Speakers is not { Count: > 0 } speakers ||
            manifest.ProcessingMetadata.SpeakerVoiceSamples is not { Count: > 0 } samples)
        {
            return new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count);
        }

        var samplesBySpeakerId = samples
            .Where(sample =>
                !string.IsNullOrWhiteSpace(sample.SpeakerId) &&
                sample.EmbeddingDimension > 0 &&
                sample.Embedding.Count == sample.EmbeddingDimension)
            .GroupBy(sample => sample.SpeakerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var confirmedSamples = new List<(string DisplayName, SpeakerVoiceSample Sample)>();
        var rejectedMatches = new List<SpeakerNameRejectedMatch>();

        foreach (var speaker in speakers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(speaker.DisplayName) ||
                !speakerLabelMap.TryGetValue(speaker.DisplayName.Trim(), out var correctedName) ||
                string.IsNullOrWhiteSpace(correctedName) ||
                string.Equals(speaker.DisplayName.Trim(), correctedName.Trim(), StringComparison.Ordinal) ||
                !samplesBySpeakerId.TryGetValue(speaker.Id, out var sample))
            {
                continue;
            }

            confirmedSamples.Add((NormalizeDisplayName(correctedName), sample));
            if (!string.IsNullOrWhiteSpace(speaker.ProfileId) &&
                speaker.NameSource is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile)
            {
                rejectedMatches.Add(new SpeakerNameRejectedMatch(
                    speaker.ProfileId,
                    manifest.SessionId,
                    speaker.Id));
            }
        }

        if (confirmedSamples.Count == 0 && rejectedMatches.Count == 0)
        {
            return new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count);
        }

        var createdCount = 0;
        var updatedCount = 0;
        var normalizedRejectedMatches = NormalizeRejectedMatches(rejectedMatches, now);
        await _profileStore.UpdateAsync(
            document =>
            {
                var profiles = document.Profiles.ToList();
                foreach (var confirmedSample in confirmedSamples)
                {
                    var existingIndex = profiles.FindIndex(profile =>
                        profile.Status == VoiceProfileStatus.Active &&
                        profile.EmbeddingDimension == confirmedSample.Sample.EmbeddingDimension &&
                        string.Equals(profile.DisplayName, confirmedSample.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(profile.EmbeddingModelFileName, confirmedSample.Sample.EmbeddingModelFileName, StringComparison.OrdinalIgnoreCase));

                    if (existingIndex >= 0)
                    {
                        if (HasAlreadyLearnedFromMeeting(profiles[existingIndex], manifest.SessionId))
                        {
                            continue;
                        }

                        profiles[existingIndex] = UpdateProfile(
                            profiles[existingIndex],
                            manifest.SessionId,
                            confirmedSample.Sample,
                            now);
                        updatedCount++;
                    }
                    else
                    {
                        profiles.Add(CreateProfile(
                            confirmedSample.DisplayName,
                            manifest.SessionId,
                            confirmedSample.Sample,
                            now));
                        createdCount++;
                    }
                }

                ApplyRejectedMatches(profiles, normalizedRejectedMatches);

                return document with
                {
                    UpdatedAtUtc = now,
                    Profiles = profiles,
                };
            },
            cancellationToken);

        return new SpeakerNameLearningResult(
            createdCount,
            updatedCount,
            Math.Max(0, speakerLabelMap.Count - createdCount - updatedCount));
    }

    private static bool HasAlreadyLearnedFromMeeting(VoiceProfile profile, string sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId) &&
            profile.ConfirmedMeetingIds.Any(meetingId =>
                string.Equals(meetingId, sessionId, StringComparison.Ordinal));
    }

    public async Task UpdateLastMatchedAsync(
        IReadOnlyList<SpeakerNamePrediction> predictions,
        DateTimeOffset matchedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var matchedProfileIds = predictions
            .Where(prediction => prediction.Source is SpeakerNameSource.AutoAppliedVoiceProfile or SpeakerNameSource.SuggestedVoiceProfile)
            .Select(prediction => prediction.ProfileId)
            .Where(profileId => !string.IsNullOrWhiteSpace(profileId))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (matchedProfileIds.Count == 0)
        {
            return;
        }

        await _profileStore.UpdateAsync(
            document => document with
            {
                UpdatedAtUtc = matchedAtUtc,
                Profiles = document.Profiles
                    .Select(profile => matchedProfileIds.Contains(profile.ProfileId)
                        ? profile with { LastMatchedAtUtc = matchedAtUtc }
                        : profile)
                    .ToArray(),
            },
            cancellationToken);
    }

    public async Task<int> RejectMatchesAsync(
        IReadOnlyList<SpeakerNameRejectedMatch> rejectedMatches,
        DateTimeOffset rejectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedRejectedMatches = NormalizeRejectedMatches(rejectedMatches, rejectedAtUtc);
        if (normalizedRejectedMatches.Count == 0)
        {
            return 0;
        }

        var rejectedCount = 0;
        await _profileStore.UpdateAsync(
            document =>
            {
                var profiles = document.Profiles.ToList();
                ApplyRejectedMatches(profiles, normalizedRejectedMatches, () => rejectedCount++);
                return document with
                {
                    UpdatedAtUtc = rejectedAtUtc,
                    Profiles = profiles,
                };
            },
            cancellationToken);

        return rejectedCount;
    }

    private static VoiceProfile CreateProfile(
        string displayName,
        string sessionId,
        SpeakerVoiceSample sample,
        DateTimeOffset now)
    {
        return new VoiceProfile(
            $"voice_{Guid.NewGuid():N}",
            displayName,
            sample.EmbeddingModelFileName,
            sample.EmbeddingDimension,
            VoiceProfileStore.NormalizeVector(sample.Embedding),
            1,
            [sessionId],
            now,
            VoiceProfileStatus.Active,
            []);
    }

    private static VoiceProfile UpdateProfile(
        VoiceProfile profile,
        string sessionId,
        SpeakerVoiceSample sample,
        DateTimeOffset now)
    {
        var normalizedSample = VoiceProfileStore.NormalizeVector(sample.Embedding);
        var previousWeight = Math.Max(1, profile.SampleCount);
        var nextSampleCount = previousWeight + 1;
        var updatedCentroid = profile.Centroid
            .Select((value, index) => ((value * previousWeight) + normalizedSample[index]) / nextSampleCount)
            .ToArray();
        var meetingIds = profile.ConfirmedMeetingIds
            .Concat([sessionId])
            .Where(meetingId => !string.IsNullOrWhiteSpace(meetingId))
            .Distinct(StringComparer.Ordinal)
            .TakeLast(MaximumMeetingIdsPerProfile)
            .ToArray();

        return profile with
        {
            Centroid = VoiceProfileStore.NormalizeVector(updatedCentroid),
            SampleCount = nextSampleCount,
            ConfirmedMeetingIds = meetingIds,
            LastMatchedAtUtc = now,
            Status = VoiceProfileStatus.Active,
        };
    }

    private static IReadOnlyList<(string ProfileId, VoiceProfileRejectedMatch Match)> NormalizeRejectedMatches(
        IReadOnlyList<SpeakerNameRejectedMatch> rejectedMatches,
        DateTimeOffset rejectedAtUtc)
    {
        return rejectedMatches
            .Where(match =>
                !string.IsNullOrWhiteSpace(match.ProfileId) &&
                !string.IsNullOrWhiteSpace(match.MeetingId) &&
                !string.IsNullOrWhiteSpace(match.SpeakerId))
            .GroupBy(match => $"{match.ProfileId.Trim()}\n{match.MeetingId.Trim()}\n{match.SpeakerId.Trim()}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(match => (
                ProfileId: match.ProfileId.Trim(),
                Match: new VoiceProfileRejectedMatch(
                    match.MeetingId.Trim(),
                    match.SpeakerId.Trim(),
                    rejectedAtUtc)))
            .ToArray();
    }

    private static void ApplyRejectedMatches(
        List<VoiceProfile> profiles,
        IReadOnlyList<(string ProfileId, VoiceProfileRejectedMatch Match)> rejectedMatches,
        Action? onRejected = null)
    {
        if (rejectedMatches.Count == 0)
        {
            return;
        }

        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var matchesForProfile = rejectedMatches
                .Where(match => string.Equals(match.ProfileId, profile.ProfileId, StringComparison.Ordinal))
                .Select(match => match.Match)
                .ToArray();
            if (matchesForProfile.Length == 0)
            {
                continue;
            }

            var existing = profile.RejectedMatches ?? Array.Empty<VoiceProfileRejectedMatch>();
            var beforeKeys = existing
                .Select(match => $"{match.MeetingId}\n{match.SpeakerId}")
                .ToHashSet(StringComparer.Ordinal);
            var mergedMatches = existing
                .Concat(matchesForProfile)
                .GroupBy(match => $"{match.MeetingId}\n{match.SpeakerId}", StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(match => match.RejectedAtUtc).First())
                .OrderByDescending(match => match.RejectedAtUtc)
                .Take(MaximumRejectedMatchesPerProfile)
                .OrderBy(match => match.MeetingId, StringComparer.Ordinal)
                .ThenBy(match => match.SpeakerId, StringComparer.Ordinal)
                .ToArray();
            var afterKeys = mergedMatches
                .Select(match => $"{match.MeetingId}\n{match.SpeakerId}")
                .ToHashSet(StringComparer.Ordinal);
            if (afterKeys.Except(beforeKeys, StringComparer.Ordinal).Any())
            {
                onRejected?.Invoke();
            }

            profiles[index] = profile with { RejectedMatches = mergedMatches };
        }
    }

    private static string NormalizeDisplayName(string displayName)
    {
        return string.Join(
            " ",
            displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
