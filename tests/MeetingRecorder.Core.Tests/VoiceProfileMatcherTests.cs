using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class VoiceProfileMatcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
    private static readonly SpeakerNameRecognitionOptions Options = new(0.86d, 0.78d, 0.05d);

    [Fact]
    public void Match_AutoApplies_When_Confidence_And_Margin_Are_High()
    {
        var matcher = new VoiceProfileMatcher();
        var predictions = matcher.Match(
            [Sample("speaker_00", [0.99f, 0.01f, 0f])],
            [
                Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f]),
                Profile("voice_alex", "Alex Lee", [0f, 1f, 0f]),
            ],
            Options);

        var prediction = Assert.Single(predictions);
        Assert.Equal("speaker_00", prediction.SpeakerId);
        Assert.Equal("voice_pranav", prediction.ProfileId);
        Assert.Equal("Pranav Sharma", prediction.DisplayName);
        Assert.Equal(SpeakerNameSource.AutoAppliedVoiceProfile, prediction.Source);
        Assert.True(prediction.Confidence >= 0.86d);
    }

    [Fact]
    public void Match_Suggests_When_Confidence_Is_Weak_For_AutoApply()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [Sample("speaker_00", [0.8f, 0.6f, 0f])],
            [Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f])],
            Options);

        var prediction = Assert.Single(predictions);
        Assert.Equal(SpeakerNameSource.SuggestedVoiceProfile, prediction.Source);
        Assert.Equal("Pranav Sharma", prediction.DisplayName);
        Assert.True(prediction.Confidence >= 0.78d);
        Assert.True(prediction.Confidence < 0.86d);
    }

    [Fact]
    public void Match_Does_Not_AutoApply_When_Profile_Margin_Is_Ambiguous()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            [
                Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f]),
                Profile("voice_alex", "Alex Lee", [0.96f, 0.28f, 0f]),
            ],
            Options);

        var prediction = Assert.Single(predictions);
        Assert.Equal(SpeakerNameSource.SuggestedVoiceProfile, prediction.Source);
    }

    [Fact]
    public void Match_Does_Not_Reuse_Same_Profile_For_Multiple_AutoApplied_Clusters()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [
                Sample("speaker_00", [0.99f, 0.01f, 0f]),
                Sample("speaker_01", [0.98f, 0.02f, 0f]),
            ],
            [Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f])],
            Options);

        Assert.Equal(SpeakerNameSource.AutoAppliedVoiceProfile, predictions.Single(item => item.SpeakerId == "speaker_00").Source);
        Assert.Equal(SpeakerNameSource.SuggestedVoiceProfile, predictions.Single(item => item.SpeakerId == "speaker_01").Source);
    }

    [Fact]
    public void Match_Ignores_Disabled_And_ModelMismatched_Profiles()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            [
                Profile("disabled", "Disabled", [1f, 0f, 0f]) with { Status = VoiceProfileStatus.Disabled },
                Profile("other-model", "Other Model", [1f, 0f, 0f]) with { EmbeddingModelFileName = "other.onnx" },
            ],
            Options);

        Assert.Empty(predictions);
    }

    private static SpeakerVoiceSample Sample(string speakerId, IReadOnlyList<float> embedding)
    {
        return new SpeakerVoiceSample(
            speakerId,
            "embedding.onnx",
            embedding.Count,
            embedding,
            TimeSpan.FromSeconds(9),
            Now);
    }

    private static VoiceProfile Profile(string profileId, string displayName, IReadOnlyList<float> centroid)
    {
        return new VoiceProfile(
            profileId,
            displayName,
            "embedding.onnx",
            centroid.Count,
            centroid,
            1,
            ["meeting-a"],
            null,
            VoiceProfileStatus.Active);
    }
}
