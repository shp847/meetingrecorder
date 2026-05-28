using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerNameCorrectionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"MeetingRecorderSpeakerCorrections-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_Updates_Artifacts_And_Learns_Profile_From_PreRename_Manifest()
    {
        var context = await CreatePublishedMeetingAsync();
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        var service = CreateService(profileStore);

        var result = await service.ApplyCorrectionsAsync(
            context.Record,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Speaker 1"] = "Pranav Sharma",
            },
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);

        Assert.Equal(1, result.LearningResult.CreatedCount);
        Assert.Null(result.LearningWarning);

        var profile = Assert.Single((await profileStore.LoadOrCreateAsync()).Profiles);
        Assert.Equal("Pranav Sharma", profile.DisplayName);
        Assert.Contains("session-a", profile.ConfirmedMeetingIds);

        var updatedManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
        Assert.Equal("Pranav Sharma", Assert.Single(updatedManifest.ProcessingMetadata?.Speakers ?? []).DisplayName);

        var updatedMarkdown = await File.ReadAllTextAsync(context.MarkdownPath);
        Assert.Contains("**Pranav Sharma:** Hello there", updatedMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_Preserves_Artifact_Update_When_Learning_Store_Fails()
    {
        var context = await CreatePublishedMeetingAsync();
        var blockerPath = Path.Combine(_root, "blocked-store");
        await File.WriteAllTextAsync(blockerPath, "not a directory");
        var profileStore = new VoiceProfileStore(Path.Combine(blockerPath, "voice-profiles.json"));
        var service = CreateService(profileStore);

        var result = await service.ApplyCorrectionsAsync(
            context.Record,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Speaker 1"] = "Pranav Sharma",
            },
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);

        Assert.Equal(0, result.LearningResult.CreatedCount);
        Assert.NotNull(result.LearningWarning);

        var updatedMarkdown = await File.ReadAllTextAsync(context.MarkdownPath);
        Assert.Contains("**Pranav Sharma:** Hello there", updatedMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyCorrectionsAsync_Does_Not_Add_Duplicate_Profile_Sample_For_Unchanged_Save()
    {
        var context = await CreatePublishedMeetingAsync();
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        var service = CreateService(profileStore);
        var corrections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Speaker 1"] = "Pranav Sharma",
        };

        var first = await service.ApplyCorrectionsAsync(
            context.Record,
            corrections,
            SpeakerNameLearningMode.LocalAutoLearn,
            _now);
        var second = await service.ApplyCorrectionsAsync(
            context.Record,
            corrections,
            SpeakerNameLearningMode.LocalAutoLearn,
            _now.AddMinutes(1));

        Assert.Equal(1, first.LearningResult.CreatedCount);
        Assert.Equal(0, second.LearningResult.CreatedCount);
        Assert.Equal(0, second.LearningResult.UpdatedCount);
        var profile = Assert.Single((await profileStore.LoadOrCreateAsync()).Profiles);
        Assert.Equal(1, profile.SampleCount);
    }

    [Fact]
    public async Task RejectMatchesAsync_Clears_Suggestion_And_Stores_Profile_Rejection()
    {
        var context = await CreatePublishedMeetingAsync(
            speaker: new SpeakerIdentity(
                "speaker_00",
                "Speaker 1",
                false,
                "voice_pranav",
                SpeakerNameSource.SuggestedVoiceProfile,
                0.82d,
                "Pranav Sharma",
                SpeakerNameDecisionReason.SuggestedBelowAutoApplyThreshold));
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        await profileStore.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [Profile("voice_pranav", "Pranav Sharma")]));
        var service = CreateService(profileStore);

        var result = await service.RejectMatchesAsync(
            context.Record,
            [new SpeakerNameRejectedMatch("voice_pranav", "session-a", "speaker_00")],
            _now);

        Assert.Equal(1, result.RejectedCount);
        var profile = Assert.Single((await profileStore.LoadOrCreateAsync()).Profiles);
        var rejectedMatch = Assert.Single(profile.RejectedMatches ?? []);
        Assert.Equal("session-a", rejectedMatch.MeetingId);
        Assert.Equal("speaker_00", rejectedMatch.SpeakerId);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(context.JsonPath))?.AsObject()
            ?? throw new InvalidOperationException("Expected transcript JSON.");
        var speaker = document["speakers"]?[0]?.AsObject()
            ?? throw new InvalidOperationException("Expected speaker metadata.");
        Assert.Null(speaker["suggestedDisplayName"]);
        Assert.Equal("None", speaker["nameSource"]?.GetValue<string>());
    }

    [Fact]
    public async Task RejectMatchesAsync_Suppresses_Same_Profile_For_Same_Meeting_Speaker()
    {
        var context = await CreatePublishedMeetingAsync(
            speaker: new SpeakerIdentity(
                "speaker_00",
                "Speaker 1",
                false,
                "voice_pranav",
                SpeakerNameSource.SuggestedVoiceProfile,
                0.82d,
                "Pranav Sharma",
                SpeakerNameDecisionReason.SuggestedBelowAutoApplyThreshold));
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        await profileStore.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [Profile("voice_pranav", "Pranav Sharma")]));
        var service = CreateService(profileStore);

        await service.RejectMatchesAsync(
            context.Record,
            [new SpeakerNameRejectedMatch("voice_pranav", "session-a", "speaker_00")],
            _now);
        var refresh = await service.RefreshSpeakerNameAttributionAsync(
            context.Record,
            SpeakerNameLearningMode.LocalAutoLearn,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d, 2, TimeSpan.FromSeconds(8)),
            _now.AddMinutes(1));

        Assert.Empty(refresh.Predictions);
        Assert.Null(refresh.Warning);
    }

    [Fact]
    public async Task RefreshSpeakerNameAttributionAsync_Applies_Mature_HighConfidence_Profile_Without_Retranscription()
    {
        var context = await CreatePublishedMeetingAsync();
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        await profileStore.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [Profile("voice_pranav", "Pranav Sharma")]));
        var service = CreateService(profileStore);

        var result = await service.RefreshSpeakerNameAttributionAsync(
            context.Record,
            SpeakerNameLearningMode.LocalAutoLearn,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d, 2, TimeSpan.FromSeconds(8)),
            _now);

        var prediction = Assert.Single(result.Predictions);
        Assert.Equal(SpeakerNameSource.AutoAppliedVoiceProfile, prediction.Source);

        var updatedMarkdown = await File.ReadAllTextAsync(context.MarkdownPath);
        Assert.Contains("**Pranav Sharma:** Hello there", updatedMarkdown, StringComparison.Ordinal);

        var updatedManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
        var updatedSpeaker = Assert.Single(updatedManifest.ProcessingMetadata?.Speakers ?? []);
        Assert.Equal(SessionState.Published, updatedManifest.State);
        Assert.Equal("Pranav Sharma", updatedSpeaker.DisplayName);
        Assert.Equal(SpeakerNameSource.AutoAppliedVoiceProfile, updatedSpeaker.NameSource);
        Assert.Equal("audio", await File.ReadAllTextAsync(context.Record.AudioPath!));
    }

    [Fact]
    public async Task RefreshSpeakerNameAttributionAsync_Explains_When_Profiles_Are_Unavailable()
    {
        var context = await CreatePublishedMeetingAsync();
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        var service = CreateService(profileStore);

        var result = await service.RefreshSpeakerNameAttributionAsync(
            context.Record,
            SpeakerNameLearningMode.LocalAutoLearn,
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d, 2, TimeSpan.FromSeconds(8)),
            _now);

        Assert.Empty(result.Predictions);
        Assert.Equal("Speaker-name refresh skipped because no local voice profiles are available yet.", result.Warning);
    }

    [Fact]
    public async Task UndoProfileSpeakerNameRecognitionAsync_Clears_Profile_Sourced_Names_And_Preserves_User_Edits()
    {
        var context = await CreatePublishedMeetingAsync(
            speakers:
            [
                new SpeakerIdentity(
                    "speaker_00",
                    "Pranav Sharma",
                    false,
                    "voice_pranav",
                    SpeakerNameSource.AutoAppliedVoiceProfile,
                    0.93d,
                    null,
                    SpeakerNameDecisionReason.AutoAppliedHighConfidence),
                new SpeakerIdentity(
                    "speaker_01",
                    "Terry Smith",
                    true,
                    null,
                    SpeakerNameSource.UserEdited),
                new SpeakerIdentity(
                    "speaker_02",
                    "Speaker 3",
                    false,
                    "voice_alex",
                    SpeakerNameSource.SuggestedVoiceProfile,
                    0.81d,
                    "Alex Lee",
                    SpeakerNameDecisionReason.SuggestedBelowAutoApplyThreshold),
            ]);
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        await profileStore.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [
                Profile("voice_pranav", "Pranav Sharma"),
                Profile("voice_alex", "Alex Lee"),
            ]));
        var service = CreateService(profileStore);

        var result = await service.UndoProfileSpeakerNameRecognitionAsync(context.Record, _now);

        Assert.Equal(2, result.UpdatedSpeakerCount);
        Assert.Equal(2, result.SuppressedMatchCount);
        Assert.Null(result.Warning);

        var updatedManifest = await context.ManifestStore.LoadAsync(context.ManifestPath);
        var speakers = updatedManifest.ProcessingMetadata?.Speakers ?? [];
        Assert.Equal("Speaker 1", speakers[0].DisplayName);
        Assert.Equal(SpeakerNameSource.None, speakers[0].NameSource);
        Assert.Null(speakers[0].ProfileId);
        Assert.Equal("Terry Smith", speakers[1].DisplayName);
        Assert.Equal(SpeakerNameSource.UserEdited, speakers[1].NameSource);
        Assert.Equal("Speaker 3", speakers[2].DisplayName);
        Assert.Equal(SpeakerNameSource.None, speakers[2].NameSource);
        Assert.Null(speakers[2].SuggestedDisplayName);

        var markdown = await File.ReadAllTextAsync(context.MarkdownPath);
        Assert.Contains("**Speaker 1:** Hello there", markdown, StringComparison.Ordinal);
        Assert.Contains("**Terry Smith:** Thanks everyone", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("**Pranav Sharma:**", markdown, StringComparison.Ordinal);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(context.JsonPath))?.AsObject()
            ?? throw new InvalidOperationException("Expected transcript JSON.");
        var firstSpeaker = document["speakers"]?[0]?.AsObject()
            ?? throw new InvalidOperationException("Expected speaker metadata.");
        Assert.Equal("Speaker 1", firstSpeaker["displayName"]?.GetValue<string>());
        Assert.Null(firstSpeaker["profileId"]);
        Assert.Null(firstSpeaker["confidence"]);
        Assert.DoesNotContain("embedding", await File.ReadAllTextAsync(context.JsonPath), StringComparison.OrdinalIgnoreCase);

        var profiles = (await profileStore.LoadOrCreateAsync()).Profiles.ToDictionary(profile => profile.ProfileId);
        Assert.Contains(profiles["voice_pranav"].RejectedMatches ?? [], match =>
            match.MeetingId == "session-a" && match.SpeakerId == "speaker_00");
        Assert.Contains(profiles["voice_alex"].RejectedMatches ?? [], match =>
            match.MeetingId == "session-a" && match.SpeakerId == "speaker_02");
    }

    [Fact]
    public async Task UndoProfileSpeakerNameRecognitionAsync_Is_Idempotent()
    {
        var context = await CreatePublishedMeetingAsync(
            speaker: new SpeakerIdentity(
                "speaker_00",
                "Pranav Sharma",
                false,
                "voice_pranav",
                SpeakerNameSource.AutoAppliedVoiceProfile,
                0.93d,
                null,
                SpeakerNameDecisionReason.AutoAppliedHighConfidence));
        var profileStore = new VoiceProfileStore(Path.Combine(_root, "speaker-profiles", "voice-profiles.json"));
        await profileStore.SaveAsync(new VoiceProfileStoreDocument(
            1,
            _now,
            [Profile("voice_pranav", "Pranav Sharma")]));
        var service = CreateService(profileStore);

        var first = await service.UndoProfileSpeakerNameRecognitionAsync(context.Record, _now);
        var second = await service.UndoProfileSpeakerNameRecognitionAsync(context.Record, _now.AddMinutes(1));

        Assert.Equal(1, first.UpdatedSpeakerCount);
        Assert.Equal(0, second.UpdatedSpeakerCount);
        Assert.Equal(0, second.SuppressedMatchCount);
        Assert.Null(second.Warning);
        var profile = Assert.Single((await profileStore.LoadOrCreateAsync()).Profiles);
        Assert.Single(profile.RejectedMatches ?? []);
    }

    [Fact]
    public async Task UndoProfileSpeakerNameRecognitionAsync_Preserves_Artifact_Update_When_Feedback_Store_Fails()
    {
        var context = await CreatePublishedMeetingAsync(
            speaker: new SpeakerIdentity(
                "speaker_00",
                "Pranav Sharma",
                false,
                "voice_pranav",
                SpeakerNameSource.AutoAppliedVoiceProfile,
                0.93d,
                null,
                SpeakerNameDecisionReason.AutoAppliedHighConfidence));
        var blockerPath = Path.Combine(_root, "blocked-store");
        await File.WriteAllTextAsync(blockerPath, "not a directory");
        var profileStore = new VoiceProfileStore(Path.Combine(blockerPath, "voice-profiles.json"));
        var service = CreateService(profileStore);

        var result = await service.UndoProfileSpeakerNameRecognitionAsync(context.Record, _now);

        Assert.Equal(1, result.UpdatedSpeakerCount);
        Assert.Equal(0, result.SuppressedMatchCount);
        Assert.NotNull(result.Warning);
        var markdown = await File.ReadAllTextAsync(context.MarkdownPath);
        Assert.Contains("**Speaker 1:** Hello there", markdown, StringComparison.Ordinal);
    }

    private SpeakerNameCorrectionService CreateService(VoiceProfileStore profileStore)
    {
        var pathBuilder = new ArtifactPathBuilder();
        return new SpeakerNameCorrectionService(
            new MeetingOutputCatalogService(pathBuilder),
            new SessionManifestStore(pathBuilder),
            new SpeakerNameLearningService(profileStore),
            new VoiceProfileMatcher(),
            profileStore);
    }

    private async Task<TestMeetingContext> CreatePublishedMeetingAsync(
        SpeakerIdentity? speaker = null,
        IReadOnlyList<SpeakerIdentity>? speakers = null)
    {
        var audioDir = Path.Combine(_root, "audio");
        var transcriptDir = Path.Combine(_root, "transcripts");
        var jsonDir = Path.Combine(transcriptDir, "json");
        var workDir = Path.Combine(_root, "work", "session-a");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(jsonDir);
        Directory.CreateDirectory(workDir);

        var stem = "2026-04-30_120000_teams_test";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var jsonPath = Path.Combine(jsonDir, $"{stem}.json");
        var manifestPath = Path.Combine(workDir, "manifest.json");
        await File.WriteAllTextAsync(audioPath, "audio");
        var effectiveSpeakers = speakers?.ToArray() ?? [speaker ?? new SpeakerIdentity("speaker_00", "Speaker 1", false)];
        var transcriptLines = new List<string>
        {
            "# Test",
            string.Empty,
            "## Transcript",
            string.Empty,
        };
        for (var index = 0; index < effectiveSpeakers.Length; index++)
        {
            transcriptLines.Add($"[00:00:{index * 9:00} - 00:00:{(index + 1) * 9:00}] **{effectiveSpeakers[index].DisplayName}:** {SegmentText(index)}");
        }

        await File.WriteAllTextAsync(
            markdownPath,
            string.Join(Environment.NewLine, transcriptLines));
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(
                new
                {
                    title = "Test",
                    speakers = effectiveSpeakers.Select(effectiveSpeaker => new
                        {
                            id = effectiveSpeaker.Id,
                            displayName = effectiveSpeaker.DisplayName,
                            isUserEdited = effectiveSpeaker.IsUserEdited,
                            profileId = effectiveSpeaker.ProfileId,
                            nameSource = effectiveSpeaker.NameSource.ToString(),
                            confidence = effectiveSpeaker.Confidence,
                            suggestedDisplayName = effectiveSpeaker.SuggestedDisplayName,
                            decisionReason = effectiveSpeaker.DecisionReason?.ToString(),
                        }).ToArray(),
                    segments = effectiveSpeakers.Select((effectiveSpeaker, index) => new
                        {
                            start = TimeSpan.FromSeconds(index * 9).ToString(@"hh\:mm\:ss"),
                            end = TimeSpan.FromSeconds((index + 1) * 9).ToString(@"hh\:mm\:ss"),
                            speakerId = effectiveSpeaker.Id,
                            speakerLabel = effectiveSpeaker.DisplayName,
                            text = SegmentText(index),
                        }).ToArray(),
                },
                new JsonSerializerOptions { WriteIndented = true }));

        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        await manifestStore.SaveAsync(
            new MeetingSessionManifest
            {
                SessionId = "session-a",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Test",
                StartedAtUtc = _now,
                State = SessionState.Published,
                ProcessingMetadata = new MeetingProcessingMetadata(
                    "model.bin",
                    true,
                    effectiveSpeakers,
                    effectiveSpeakers.Select((effectiveSpeaker, index) => new SpeakerTurn(
                        effectiveSpeaker.Id,
                        TimeSpan.FromSeconds(index * 9),
                        TimeSpan.FromSeconds((index + 1) * 9))).ToArray(),
                    null,
                    effectiveSpeakers.Select((effectiveSpeaker, index) => Sample(effectiveSpeaker.Id, SampleVector(index))).ToArray()),
            },
            manifestPath);

        var record = new MeetingOutputRecord(
            stem,
            "Test",
            _now,
            MeetingPlatform.Teams,
            TimeSpan.FromSeconds(Math.Max(1, effectiveSpeakers.Length) * 9),
            audioPath,
            markdownPath,
            jsonPath,
            null,
            manifestPath,
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            true,
            "model.bin");

        return new TestMeetingContext(record, manifestStore, manifestPath, markdownPath, jsonPath);
    }

    private static string SegmentText(int index)
    {
        return index switch
        {
            0 => "Hello there",
            1 => "Thanks everyone",
            _ => "Follow up",
        };
    }

    private static float[] SampleVector(int index)
    {
        return index switch
        {
            1 => [0f, 1f, 0f],
            2 => [0f, 0f, 1f],
            _ => [1f, 0f, 0f],
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

    private VoiceProfile Profile(string profileId, string displayName)
    {
        return new VoiceProfile(
            profileId,
            displayName,
            "embedding.onnx",
            3,
            [1f, 0f, 0f],
            2,
            ["older-session"],
            null,
            VoiceProfileStatus.Active,
            []);
    }

    private sealed record TestMeetingContext(
        MeetingOutputRecord Record,
        SessionManifestStore ManifestStore,
        string ManifestPath,
        string MarkdownPath,
        string JsonPath);
}
