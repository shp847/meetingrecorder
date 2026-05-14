using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerNameRecognitionOptions(
    double AutoApplyConfidenceThreshold,
    double SuggestionConfidenceThreshold,
    double MatchMarginThreshold);

public sealed record SpeakerNamePrediction(
    string SpeakerId,
    string ProfileId,
    string DisplayName,
    double Confidence,
    SpeakerNameSource Source);

public sealed class VoiceProfileMatcher
{
    public IReadOnlyList<SpeakerNamePrediction> Match(
        IReadOnlyList<SpeakerVoiceSample> samples,
        IReadOnlyList<VoiceProfile> profiles,
        SpeakerNameRecognitionOptions options)
    {
        if (samples.Count == 0 || profiles.Count == 0)
        {
            return Array.Empty<SpeakerNamePrediction>();
        }

        var normalizedOptions = NormalizeOptions(options);
        var candidates = samples
            .Select(sample => BuildCandidate(sample, profiles, normalizedOptions))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        if (candidates.Length == 0)
        {
            return Array.Empty<SpeakerNamePrediction>();
        }

        var winningAutoApplyByProfileId = candidates
            .Where(candidate => candidate.Source == SpeakerNameSource.AutoAppliedVoiceProfile)
            .GroupBy(candidate => candidate.ProfileId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(candidate => candidate.Confidence)
                    .ThenBy(candidate => candidate.SpeakerId, StringComparer.Ordinal)
                    .First().SpeakerId,
                StringComparer.Ordinal);

        return candidates
            .Select(candidate =>
                candidate.Source == SpeakerNameSource.AutoAppliedVoiceProfile &&
                winningAutoApplyByProfileId.TryGetValue(candidate.ProfileId, out var winningSpeakerId) &&
                !string.Equals(candidate.SpeakerId, winningSpeakerId, StringComparison.Ordinal)
                    ? candidate with { Source = SpeakerNameSource.SuggestedVoiceProfile }
                    : candidate)
            .OrderBy(candidate => candidate.SpeakerId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<SpeakerIdentity> ApplyPredictions(
        IReadOnlyList<SpeakerIdentity> speakers,
        IReadOnlyList<SpeakerNamePrediction> predictions)
    {
        if (speakers.Count == 0 || predictions.Count == 0)
        {
            return speakers;
        }

        var predictionsBySpeakerId = predictions
            .GroupBy(prediction => prediction.SpeakerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return speakers
            .Select(speaker =>
            {
                if (!predictionsBySpeakerId.TryGetValue(speaker.Id, out var prediction))
                {
                    return speaker;
                }

                return prediction.Source == SpeakerNameSource.AutoAppliedVoiceProfile
                    ? speaker with
                    {
                        DisplayName = prediction.DisplayName,
                        ProfileId = prediction.ProfileId,
                        NameSource = SpeakerNameSource.AutoAppliedVoiceProfile,
                        Confidence = prediction.Confidence,
                        SuggestedDisplayName = null,
                    }
                    : speaker with
                    {
                        ProfileId = prediction.ProfileId,
                        NameSource = SpeakerNameSource.SuggestedVoiceProfile,
                        Confidence = prediction.Confidence,
                        SuggestedDisplayName = prediction.DisplayName,
                    };
            })
            .ToArray();
    }

    private static SpeakerNamePrediction? BuildCandidate(
        SpeakerVoiceSample sample,
        IReadOnlyList<VoiceProfile> profiles,
        SpeakerNameRecognitionOptions options)
    {
        if (sample.EmbeddingDimension <= 0 ||
            sample.Embedding.Count != sample.EmbeddingDimension ||
            string.IsNullOrWhiteSpace(sample.EmbeddingModelFileName))
        {
            return null;
        }

        var rankedMatches = profiles
            .Where(profile =>
                profile.Status == VoiceProfileStatus.Active &&
                profile.EmbeddingDimension == sample.EmbeddingDimension &&
                string.Equals(profile.EmbeddingModelFileName, sample.EmbeddingModelFileName, StringComparison.OrdinalIgnoreCase) &&
                profile.Centroid.Count == sample.EmbeddingDimension)
            .Select(profile => new
            {
                Profile = profile,
                Confidence = CosineSimilarity(sample.Embedding, profile.Centroid),
            })
            .Where(match => match.Confidence >= options.SuggestionConfidenceThreshold)
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Profile.ProfileId, StringComparer.Ordinal)
            .ToArray();

        if (rankedMatches.Length == 0)
        {
            return null;
        }

        var best = rankedMatches[0];
        var secondBestConfidence = rankedMatches.Length > 1 ? rankedMatches[1].Confidence : 0d;
        var source =
            best.Confidence >= options.AutoApplyConfidenceThreshold &&
            best.Confidence - secondBestConfidence >= options.MatchMarginThreshold
                ? SpeakerNameSource.AutoAppliedVoiceProfile
                : SpeakerNameSource.SuggestedVoiceProfile;

        return new SpeakerNamePrediction(
            sample.SpeakerId,
            best.Profile.ProfileId,
            best.Profile.DisplayName,
            best.Confidence,
            source);
    }

    private static SpeakerNameRecognitionOptions NormalizeOptions(SpeakerNameRecognitionOptions options)
    {
        var autoApply = NormalizeThreshold(options.AutoApplyConfidenceThreshold, 0.86d);
        var suggestion = NormalizeThreshold(options.SuggestionConfidenceThreshold, 0.78d);
        var margin = NormalizeThreshold(options.MatchMarginThreshold, 0.05d);
        if (suggestion > autoApply)
        {
            suggestion = 0.78d;
        }

        return new SpeakerNameRecognitionOptions(autoApply, suggestion, margin);
    }

    private static double NormalizeThreshold(double value, double fallback)
    {
        return value is > 0d and <= 1d ? value : fallback;
    }

    internal static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || left.Count != right.Count)
        {
            return 0d;
        }

        double dot = 0d;
        double leftSquares = 0d;
        double rightSquares = 0d;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftSquares += left[index] * left[index];
            rightSquares += right[index] * right[index];
        }

        if (leftSquares <= 0d || rightSquares <= 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftSquares) * Math.Sqrt(rightSquares));
    }
}
