using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerNameLearningServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"MeetingRecorderSpeakerLearning-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task LearnFromCorrectionsAsync_Creates_Profile_From_User_Rename()
    {
        var store = new VoiceProfileStore(Path.Combine(_root, "voice-profiles.json"));
        var service = new SpeakerNameLearningService(store);
        var manifest = BuildManifest(
            [new SpeakerIdentity("speaker_00", "Speaker 1", false)],
            [Sample("speaker_00", [1f, 0f, 0f])]);

        var result = await service.LearnFromCorrectionsAsync(
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Speaker 1"] = "Pranav Sharma",
            },
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);

        var document = await store.LoadOrCreateAsync();
        var profile = Assert.Single(document.Profiles);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal("Pranav Sharma", profile.DisplayName);
        Assert.Equal("embedding.onnx", profile.EmbeddingModelFileName);
        Assert.Equal([1f, 0f, 0f], profile.Centroid);
        Assert.Contains("session-a", profile.ConfirmedMeetingIds);
    }

    [Fact]
    public async Task LearnFromCorrectionsAsync_Does_Not_Add_Duplicate_Sample_For_Same_Meeting()
    {
        var store = new VoiceProfileStore(Path.Combine(_root, "voice-profiles.json"));
        var service = new SpeakerNameLearningService(store);
        var manifest = BuildManifest(
            [new SpeakerIdentity("speaker_00", "Speaker 1", false)],
            [Sample("speaker_00", [1f, 0f, 0f])]);
        var corrections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Speaker 1"] = "Pranav Sharma",
        };

        var first = await service.LearnFromCorrectionsAsync(
            manifest,
            corrections,
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);
        var second = await service.LearnFromCorrectionsAsync(
            manifest,
            corrections,
            SpeakerNameLearningMode.LocalAutoLearn,
            _now.AddMinutes(1));

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(0, second.UpdatedCount);
        Assert.Equal(1, second.SkippedCount);
        var profile = Assert.Single((await store.LoadOrCreateAsync()).Profiles);
        Assert.Equal(1, profile.SampleCount);
        Assert.Equal(["session-a"], profile.ConfirmedMeetingIds);
    }

    [Fact]
    public async Task LearnFromCorrectionsAsync_Updates_Corrected_Profile_Not_Rejected_Profile()
    {
        var store = new VoiceProfileStore(Path.Combine(_root, "voice-profiles.json"));
        await store.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [
                Profile("voice_wrong", "Alex Lee", [0f, 1f, 0f]),
                Profile("voice_correct", "Pranav Sharma", [1f, 0f, 0f]),
            ]));
        var service = new SpeakerNameLearningService(store);
        var manifest = BuildManifest(
            [
                new SpeakerIdentity(
                    "speaker_00",
                    "Alex Lee",
                    false,
                    "voice_wrong",
                    SpeakerNameSource.AutoAppliedVoiceProfile,
                    0.91d),
            ],
            [Sample("speaker_00", [1f, 0f, 0f])]);

        var result = await service.LearnFromCorrectionsAsync(
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Alex Lee"] = "Pranav Sharma",
            },
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);

        var document = await store.LoadOrCreateAsync();
        var wrong = document.Profiles.Single(profile => profile.ProfileId == "voice_wrong");
        var correct = document.Profiles.Single(profile => profile.ProfileId == "voice_correct");
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal([0f, 1f, 0f], wrong.Centroid);
        Assert.True(correct.SampleCount > 1);
        Assert.Contains("session-a", correct.ConfirmedMeetingIds);
    }

    [Fact]
    public async Task LearnFromCorrectionsAsync_Does_Nothing_When_Disabled()
    {
        var store = new VoiceProfileStore(Path.Combine(_root, "voice-profiles.json"));
        var service = new SpeakerNameLearningService(store);
        var manifest = BuildManifest(
            [new SpeakerIdentity("speaker_00", "Speaker 1", false)],
            [Sample("speaker_00", [1f, 0f, 0f])]);

        var result = await service.LearnFromCorrectionsAsync(
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Speaker 1"] = "Pranav Sharma",
            },
            SpeakerNameLearningMode.Disabled,
            _now);

        var document = await store.LoadOrCreateAsync();
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(document.Profiles);
    }

    [Fact]
    public async Task RejectMatchesAsync_Suppresses_Same_Profile_Only_For_That_Meeting_Speaker()
    {
        var store = new VoiceProfileStore(Path.Combine(_root, "voice-profiles.json"));
        await store.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [Profile("voice_pranav", "Pranav Sharma", [1f, 0f, 0f])]));
        var service = new SpeakerNameLearningService(store);
        var matcher = new VoiceProfileMatcher();

        var rejectedCount = await service.RejectMatchesAsync(
            [new SpeakerNameRejectedMatch("voice_pranav", "session-a", "speaker_00")],
            _now);
        var profiles = (await store.LoadOrCreateAsync()).Profiles;

        Assert.Equal(1, rejectedCount);
        Assert.Empty(matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            profiles,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d),
            "session-a"));
        Assert.NotEmpty(matcher.Match(
            [Sample("speaker_01", [1f, 0f, 0f])],
            profiles,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d),
            "session-a"));
        Assert.NotEmpty(matcher.Match(
            [Sample("speaker_00", [1f, 0f, 0f])],
            profiles,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d),
            "session-b"));
    }

    private MeetingSessionManifest BuildManifest(
        IReadOnlyList<SpeakerIdentity> speakers,
        IReadOnlyList<SpeakerVoiceSample> speakerVoiceSamples)
    {
        return new MeetingSessionManifest
        {
            SessionId = "session-a",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Test Meeting",
            StartedAtUtc = _now,
            ProcessingMetadata = new MeetingProcessingMetadata(
                "model.bin",
                true,
                speakers,
                [new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(9))],
                null,
                speakerVoiceSamples),
        };
    }

    private SpeakerVoiceSample Sample(string speakerId, IReadOnlyList<float> embedding)
    {
        return new SpeakerVoiceSample(
            speakerId,
            "embedding.onnx",
            embedding.Count,
            embedding,
            TimeSpan.FromSeconds(9),
            _now);
    }

    private VoiceProfile Profile(string profileId, string displayName, IReadOnlyList<float> centroid)
    {
        return new VoiceProfile(
            profileId,
            displayName,
            "embedding.onnx",
            centroid.Count,
            centroid,
            1,
            ["older-session"],
            null,
            VoiceProfileStatus.Active);
    }
}
