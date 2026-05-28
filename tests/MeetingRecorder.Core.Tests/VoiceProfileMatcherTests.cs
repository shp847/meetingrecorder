using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class VoiceProfileMatcherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
    private static readonly SpeakerNameRecognitionOptions Options = new(
        0.86d,
        0.78d,
        0.05d,
        2,
        TimeSpan.FromSeconds(8));

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
        Assert.Equal(SpeakerNameDecisionReason.AutoAppliedHighConfidence, prediction.DecisionReason);
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
        Assert.Equal(SpeakerNameDecisionReason.SuggestedBelowAutoApplyThreshold, prediction.DecisionReason);
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
        Assert.Equal(SpeakerNameDecisionReason.SuggestedAmbiguousProfileMargin, prediction.DecisionReason);
    }

    [Fact]
    public void Match_Suggests_When_Profile_Is_Not_Mature_Enough_For_AutoApply()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [Sample("speaker_00", [0.99f, 0.01f, 0f])],
            [Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f], sampleCount: 1)],
            Options,
            meetingId: "meeting-a");

        var prediction = Assert.Single(predictions);
        Assert.Equal(SpeakerNameSource.SuggestedVoiceProfile, prediction.Source);
        Assert.Equal(SpeakerNameDecisionReason.SuggestedProfileNeedsMoreSamples, prediction.DecisionReason);
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
        Assert.Equal(
            SpeakerNameDecisionReason.SuggestedDuplicateProfileCandidate,
            predictions.Single(item => item.SpeakerId == "speaker_01").DecisionReason);
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

    [Fact]
    public void Match_Ignores_Profile_Rejected_For_This_Meeting_Speaker()
    {
        var matcher = new VoiceProfileMatcher();

        var predictions = matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            [
                Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f]) with
                {
                    RejectedMatches =
                    [
                        new VoiceProfileRejectedMatch("meeting-a", "speaker_00", Now),
                    ],
                },
            ],
            Options,
            meetingId: "meeting-a");

        Assert.Empty(predictions);
    }

    [Fact]
    public void Match_Rejection_Is_Scoped_To_Meeting_And_Speaker()
    {
        var matcher = new VoiceProfileMatcher();
        var profile = Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f]) with
        {
            RejectedMatches =
            [
                new VoiceProfileRejectedMatch("meeting-a", "speaker_00", Now),
            ],
        };

        var sameMeetingDifferentSpeaker = matcher.Match(
            [Sample("speaker_01", [1f, 0f, 0f])],
            [profile],
            Options,
            meetingId: "meeting-a");
        var differentMeetingSameSpeaker = matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            [profile],
            Options,
            meetingId: "meeting-b");

        Assert.Single(sameMeetingDifferentSpeaker);
        Assert.Single(differentMeetingSameSpeaker);
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

    private static VoiceProfile Profile(
        string profileId,
        string displayName,
        IReadOnlyList<float> centroid,
        int sampleCount = 2)
    {
        return new VoiceProfile(
            profileId,
            displayName,
            "embedding.onnx",
            centroid.Count,
            centroid,
            sampleCount,
            ["meeting-a"],
            null,
            VoiceProfileStatus.Active,
            []);
    }
}
