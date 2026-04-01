using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupRecommendationEngineTests : IDisposable
{
    private readonly string _root;

    public MeetingCleanupRecommendationEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Analyze_Returns_HighConfidence_Archive_For_Short_Generic_Teams_False_Start()
    {
        var stem = "2026-03-20_130255_teams_microsoft-teams";
        var audioPath = Path.Combine(_root, $"{stem}.wav");
        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(31));

        var recommendation = AnalyzeSingle(
            CreateInspection(
                stem,
                "Microsoft Teams",
                DateTimeOffset.Parse("2026-03-20T13:02:55Z"),
                MeetingPlatform.Teams,
                TimeSpan.FromSeconds(31),
                audioPath: audioPath,
                markdownPath: Path.Combine(_root, $"{stem}.md")));

        Assert.Equal(MeetingCleanupAction.Archive, recommendation.Action);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.True(recommendation.CanApplyAutomatically);
    }

    [Fact]
    public void Analyze_Returns_HighConfidence_Archive_For_Editor_Window_False_Meeting()
    {
        var stem = "2026-03-20_014208_teams_2026-03-20-001940-teams-ducey-gallina-nick-md-meeting-recorder-visual-studio-code";

        var recommendation = AnalyzeSingle(
            CreateInspection(
                stem,
                "2026-03-20_001940_teams_ducey-gallina-nick.md - Meeting Recorder - Visual Studio Code",
                DateTimeOffset.Parse("2026-03-20T01:42:08Z"),
                MeetingPlatform.Teams,
                TimeSpan.FromSeconds(62),
                audioPath: Path.Combine(_root, $"{stem}.wav"),
                markdownPath: Path.Combine(_root, $"{stem}.md")));

        Assert.Equal(MeetingCleanupAction.Archive, recommendation.Action);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
    }

    [Fact]
    public void Analyze_Returns_HighConfidence_Archive_For_Transcript_Only_Orphan()
    {
        var recommendation = AnalyzeSingle(
            CreateInspection(
                "meeting",
                "meeting",
                DateTimeOffset.MinValue,
                MeetingPlatform.Unknown,
                duration: null,
                audioPath: null,
                markdownPath: Path.Combine(_root, "meeting.md")));

        Assert.Equal(MeetingCleanupAction.Archive, recommendation.Action);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
    }

    [Fact]
    public async Task Analyze_Returns_HighConfidence_Archive_For_SameStart_Duplicate_Publish()
    {
        var startedAtUtc = DateTimeOffset.Parse("2026-03-16T07:33:58Z");
        var genericStem = "2026-03-16_073358_manual_manual-session-2026-03-16-03-33";
        var betterStem = "2026-03-16_073358_manual_test-mic-only";
        var genericAudioPath = Path.Combine(_root, $"{genericStem}.wav");
        var betterAudioPath = Path.Combine(_root, $"{betterStem}.wav");
        await WriteSilentWaveFileAsync(genericAudioPath, TimeSpan.FromSeconds(81));
        File.Copy(genericAudioPath, betterAudioPath);

        var recommendations = MeetingCleanupRecommendationEngine.Analyze(
            new[]
            {
                CreateInspection(genericStem, "Manual Session 2026 03 16 03 33", startedAtUtc, MeetingPlatform.Manual, TimeSpan.FromSeconds(81), genericAudioPath, Path.Combine(_root, $"{genericStem}.md")),
                CreateInspection(betterStem, "Test-Mic Only", startedAtUtc, MeetingPlatform.Manual, TimeSpan.FromSeconds(81), betterAudioPath, Path.Combine(_root, $"{betterStem}.md")),
            });

        var recommendation = Assert.Single(recommendations, item => item.Action == MeetingCleanupAction.Archive);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.Contains(genericStem, recommendation.RelatedStems);
        Assert.Contains(betterStem, recommendation.RelatedStems);
        Assert.Equal(genericStem, recommendation.PrimaryStem);
    }

    [Fact]
    public async Task Analyze_Returns_HighConfidence_Merge_For_Punctuation_Only_Split_Pair()
    {
        var firstStem = "2026-03-20_132751_teams_wang-stein";
        var secondStem = "2026-03-20_133347_teams_wang-stein";
        var firstAudioPath = Path.Combine(_root, $"{firstStem}.wav");
        var secondAudioPath = Path.Combine(_root, $"{secondStem}.wav");
        await WriteSilentWaveFileAsync(firstAudioPath, TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(4));
        await WriteSilentWaveFileAsync(secondAudioPath, TimeSpan.FromSeconds(109));

        var recommendations = MeetingCleanupRecommendationEngine.Analyze(
            new[]
            {
                CreateInspection(firstStem, "Wang Stein", DateTimeOffset.Parse("2026-03-20T13:27:51Z"), MeetingPlatform.Teams, TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(4), firstAudioPath, Path.Combine(_root, $"{firstStem}.md")),
                CreateInspection(secondStem, "Wang, Stein", DateTimeOffset.Parse("2026-03-20T13:33:47Z"), MeetingPlatform.Teams, TimeSpan.FromSeconds(109), secondAudioPath, Path.Combine(_root, $"{secondStem}.md")),
            });

        var recommendation = Assert.Single(recommendations, item => item.Action == MeetingCleanupAction.Merge);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.True(recommendation.CanApplyAutomatically);
        Assert.Contains(firstStem, recommendation.RelatedStems);
        Assert.Contains(secondStem, recommendation.RelatedStems);
    }

    [Fact]
    public async Task Analyze_Returns_HighConfidence_Merge_For_SameTitle_Teams_Pair_With_Strong_Manifest_Continuity()
    {
        var firstStem = "2026-04-01_103307_teams_ionq-connect";
        var secondStem = "2026-04-01_105114_teams_ionq-connect";
        var firstAudioPath = Path.Combine(_root, $"{firstStem}.wav");
        var secondAudioPath = Path.Combine(_root, $"{secondStem}.wav");
        var firstDuration = TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(55);
        var secondDuration = TimeSpan.FromMinutes(22) + TimeSpan.FromSeconds(51);
        var firstStartedAtUtc = DateTimeOffset.Parse("2026-04-01T10:33:07Z");
        var secondStartedAtUtc = DateTimeOffset.Parse("2026-04-01T10:51:14Z");
        await WriteSilentWaveFileAsync(firstAudioPath, firstDuration);
        await WriteSilentWaveFileAsync(secondAudioPath, secondDuration);

        var recommendations = MeetingCleanupRecommendationEngine.Analyze(
            new[]
            {
                CreateInspection(
                    firstStem,
                    "IonQ connect",
                    firstStartedAtUtc,
                    MeetingPlatform.Teams,
                    firstDuration,
                    firstAudioPath,
                    Path.Combine(_root, $"{firstStem}.md"),
                    manifest: CreateTeamsManifest(
                        "first-session",
                        "IonQ connect",
                        firstStartedAtUtc,
                        firstStartedAtUtc + firstDuration,
                        "IonQ connect | Microsoft Teams")),
                CreateInspection(
                    secondStem,
                    "IonQ connect",
                    secondStartedAtUtc,
                    MeetingPlatform.Teams,
                    secondDuration,
                    secondAudioPath,
                    Path.Combine(_root, $"{secondStem}.md"),
                    manifest: CreateTeamsManifest(
                        "second-session",
                        "IonQ connect",
                        secondStartedAtUtc,
                        secondStartedAtUtc + secondDuration,
                        "IonQ connect | Microsoft Teams")),
            });

        var recommendation = Assert.Single(recommendations, item => item.Action == MeetingCleanupAction.Merge);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.True(recommendation.CanApplyAutomatically);
        Assert.Contains(firstStem, recommendation.RelatedStems);
        Assert.Contains(secondStem, recommendation.RelatedStems);
    }

    [Fact]
    public void Analyze_Returns_Rename_For_Generic_Title_With_Suggested_Title()
    {
        var recommendation = AnalyzeSingle(
            CreateInspection(
                "2026-03-19_173528_teams_microsoft-teams",
                "Microsoft Teams",
                DateTimeOffset.Parse("2026-03-19T17:35:28Z"),
                MeetingPlatform.Teams,
                TimeSpan.FromMinutes(2),
                audioPath: Path.Combine(_root, "rename.wav"),
                markdownPath: Path.Combine(_root, "rename.md"),
                suggestedTitle: "PE Software/SaaS Campaign",
                suggestedTitleSource: "Outlook calendar"));

        Assert.Equal(MeetingCleanupAction.Rename, recommendation.Action);
        Assert.False(recommendation.CanApplyAutomatically);
        Assert.Equal("PE Software/SaaS Campaign", recommendation.SuggestedTitle);
    }

    [Fact]
    public async Task Analyze_Returns_RegenerateTranscript_When_Audio_Exists_But_Transcript_Is_Missing()
    {
        var audioPath = Path.Combine(_root, "retry.wav");
        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(30));

        var recommendation = AnalyzeSingle(
            CreateInspection(
                "2026-03-19_214258_teams_microsoft-teams",
                "Microsoft Teams",
                DateTimeOffset.Parse("2026-03-19T21:42:58Z"),
                MeetingPlatform.Teams,
                TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(7),
                audioPath: audioPath,
                markdownPath: null));

        Assert.Equal(MeetingCleanupAction.RegenerateTranscript, recommendation.Action);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.True(recommendation.CanApplyAutomatically);
    }

    [Fact]
    public async Task Analyze_Returns_GenerateSpeakerLabels_When_Diarization_Is_Ready_And_Speaker_Labels_Are_Missing()
    {
        var stem = "2026-03-20_214258_teams_quarterly-review";
        var audioPath = Path.Combine(_root, $"{stem}.wav");
        var markdownPath = Path.Combine(_root, $"{stem}.md");
        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(7));
        await File.WriteAllTextAsync(markdownPath, "# Quarterly Review");

        var recommendation = AnalyzeSingle(
            CreateInspection(
                stem,
                "Quarterly Review",
                DateTimeOffset.Parse("2026-03-20T21:42:58Z"),
                MeetingPlatform.Teams,
                TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(7),
                audioPath: audioPath,
                markdownPath: markdownPath,
                diarizationReady: true));

        Assert.Equal(MeetingCleanupAction.GenerateSpeakerLabels, recommendation.Action);
        Assert.Equal(MeetingCleanupConfidence.High, recommendation.Confidence);
        Assert.True(recommendation.CanApplyAutomatically);
    }

    [Fact]
    public async Task Analyze_Does_Not_Return_GenerateSpeakerLabels_When_Diarization_Is_Unavailable()
    {
        var stem = "2026-03-20_214258_teams_quarterly-review";
        var audioPath = Path.Combine(_root, $"{stem}.wav");
        var markdownPath = Path.Combine(_root, $"{stem}.md");
        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(7));
        await File.WriteAllTextAsync(markdownPath, "# Quarterly Review");

        var recommendations = MeetingCleanupRecommendationEngine.Analyze(
            new[]
            {
                CreateInspection(
                    stem,
                    "Quarterly Review",
                    DateTimeOffset.Parse("2026-03-20T21:42:58Z"),
                    MeetingPlatform.Teams,
                    TimeSpan.FromMinutes(24) + TimeSpan.FromSeconds(7),
                    audioPath: audioPath,
                    markdownPath: markdownPath,
                    diarizationReady: false),
            });

        Assert.Empty(recommendations);
    }

    [Fact]
    public void Analyze_Returns_Split_Only_When_Strong_Manifest_Evidence_Exists()
    {
        var manifest = new MeetingSessionManifest
        {
            SessionId = "session",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Microsoft Teams",
            StartedAtUtc = DateTimeOffset.Parse("2026-03-20T18:00:00Z"),
            EndedAtUtc = DateTimeOffset.Parse("2026-03-20T18:30:00Z"),
            State = SessionState.Published,
            DetectionEvidence =
            [
                new DetectionSignal("window-title", "Alpha Review | Microsoft Teams", 1.0, DateTimeOffset.Parse("2026-03-20T18:01:00Z")),
                new DetectionSignal("window-title", "Alpha Review | Microsoft Teams", 1.0, DateTimeOffset.Parse("2026-03-20T18:09:00Z")),
                new DetectionSignal("window-title", "Beta Review | Microsoft Teams", 1.0, DateTimeOffset.Parse("2026-03-20T18:20:00Z")),
                new DetectionSignal("window-title", "Beta Review | Microsoft Teams", 1.0, DateTimeOffset.Parse("2026-03-20T18:27:00Z")),
            ],
        };

        var recommendations = MeetingCleanupRecommendationEngine.Analyze(
            new[]
            {
                CreateInspection(
                    "2026-03-20_180000_teams_microsoft-teams",
                    "Microsoft Teams",
                    DateTimeOffset.Parse("2026-03-20T18:00:00Z"),
                    MeetingPlatform.Teams,
                    TimeSpan.FromMinutes(30),
                    audioPath: Path.Combine(_root, "split.wav"),
                    markdownPath: Path.Combine(_root, "split.md"),
                    manifest: manifest),
            });

        var recommendation = Assert.Single(recommendations, item => item.Action == MeetingCleanupAction.Split);
        Assert.False(recommendation.CanApplyAutomatically);
        Assert.True(recommendation.SuggestedSplitPoint > TimeSpan.Zero);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private MeetingCleanupRecommendation AnalyzeSingle(MeetingInspectionRecord inspection)
    {
        return Assert.Single(MeetingCleanupRecommendationEngine.Analyze(new[] { inspection }));
    }

    private static MeetingInspectionRecord CreateInspection(
        string stem,
        string title,
        DateTimeOffset startedAtUtc,
        MeetingPlatform platform,
        TimeSpan? duration,
        string? audioPath,
        string? markdownPath,
        MeetingSessionManifest? manifest = null,
        string? suggestedTitle = null,
        string? suggestedTitleSource = null,
        bool diarizationReady = false,
        bool hasSpeakerLabels = false)
    {
        return new MeetingInspectionRecord(
            new MeetingOutputRecord(
                stem,
                title,
                startedAtUtc,
                platform,
                duration,
                audioPath,
                markdownPath,
                null,
                null,
                null,
                manifest?.State,
                Array.Empty<MeetingAttendee>(),
                hasSpeakerLabels,
                null),
            manifest,
            suggestedTitle,
            suggestedTitleSource,
            diarizationReady);
    }

    private static MeetingSessionManifest CreateTeamsManifest(
        string sessionId,
        string detectedTitle,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        string windowTitle)
    {
        return new MeetingSessionManifest
        {
            SessionId = sessionId,
            Platform = MeetingPlatform.Teams,
            DetectedTitle = detectedTitle,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            State = SessionState.Published,
            DetectionEvidence =
            [
                new DetectionSignal("window-title", windowTitle, 0.85, startedAtUtc),
            ],
            DetectedAudioSource = new DetectedAudioSource(
                "Microsoft Teams",
                windowTitle,
                null,
                AudioSourceMatchKind.Process,
                AudioSourceConfidence.Medium,
                startedAtUtc),
        };
    }

    private static Task WriteSilentWaveFileAsync(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[format.AverageBytesPerSecond];
        var remainingBytes = (int)Math.Round(duration.TotalSeconds * format.AverageBytesPerSecond);
        while (remainingBytes > 0)
        {
            var bytesToWrite = Math.Min(buffer.Length, remainingBytes);
            writer.Write(buffer, 0, bytesToWrite);
            remainingBytes -= bytesToWrite;
        }

        return Task.CompletedTask;
    }
}
