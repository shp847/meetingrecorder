using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerNameLearningResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount);

public sealed class SpeakerNameLearningService
{
    private const int MaximumMeetingIdsPerProfile = 128;
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
        }

        if (confirmedSamples.Count == 0)
        {
            return new SpeakerNameLearningResult(0, 0, speakerLabelMap.Count);
        }

        var createdCount = 0;
        var updatedCount = 0;
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
            VoiceProfileStatus.Active);
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

    private static string NormalizeDisplayName(string displayName)
    {
        return string.Join(
            " ",
            displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
