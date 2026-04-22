using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class PublishedMeetingRepairServiceTests : IDisposable
{
    private readonly string _root;

    public PublishedMeetingRepairServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Merges_Punctuation_Only_Title_Splits_And_Archives_Originals()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");

        var firstStem = "2026-03-19_235528_teams_wang-stein";
        var secondStem = "2026-03-19_235531_teams_wang-stein";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{firstStem}.wav"), TimeSpan.FromSeconds(2));
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{secondStem}.wav"), TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{firstStem}.md"),
            string.Join(
                Environment.NewLine,
                "# Wang Stein",
                string.Empty,
                "- Session ID: first",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-19T23:55:28.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:02] **Speaker:** First segment"));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{secondStem}.md"),
            string.Join(
                Environment.NewLine,
                "# Wang, Stein",
                string.Empty,
                "- Session ID: second",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-19T23:55:31.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:01] **Speaker:** Second segment"));

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.Equal(1, result.MergedSplitPairCount);
        Assert.Equal(2, result.ArchivedArtifactCount);
        var mergedStem = "2026-03-19_235528_teams_wang-stein";
        var mergedMarkdownPath = Path.Combine(transcriptDir, $"{mergedStem}.md");
        Assert.True(File.Exists(Path.Combine(audioDir, $"{mergedStem}.wav")));
        Assert.True(File.Exists(mergedMarkdownPath));
        var markdown = await File.ReadAllTextAsync(mergedMarkdownPath);
        Assert.Contains("# Wang, Stein", markdown, StringComparison.Ordinal);
        Assert.Contains("[00:00:02 - 00:00:03] **Speaker:** Second segment", markdown, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(audioDir, $"{secondStem}.wav")));
        Assert.False(File.Exists(Path.Combine(transcriptDir, $"{secondStem}.md")));
        Assert.True(Directory.Exists(result.ArchiveDirectory));
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Merges_Repeated_Split_Meeting_Chains_Into_One_Publish()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");

        var stems = new[]
        {
            "2026-03-24_170250_teams_int-globalfoundries-daily",
            "2026-03-24_170437_teams_int-globalfoundries-daily",
            "2026-03-24_170650_teams_int-globalfoundries-daily",
            "2026-03-24_171019_teams_int-globalfoundries-daily",
        };
        var durations = new[]
        {
            TimeSpan.FromSeconds(96),
            TimeSpan.FromSeconds(53),
            TimeSpan.FromSeconds(34),
            TimeSpan.FromSeconds(32),
        };
        var transcriptBodies = new[]
        {
            "[00:00:00 - 00:01:36] **Speaker:** First segment",
            "[00:00:00 - 00:00:53] **Speaker:** Second segment",
            "[00:00:00 - 00:00:34] **Speaker:** Third segment",
            "[00:00:00 - 00:00:32] **Speaker:** Fourth segment",
        };

        for (var index = 0; index < stems.Length; index++)
        {
            await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{stems[index]}.wav"), durations[index]);
            await File.WriteAllTextAsync(
                Path.Combine(transcriptDir, $"{stems[index]}.md"),
                string.Join(
                    Environment.NewLine,
                    "# [INT] GlobalFoundries daily",
                    string.Empty,
                    $"- Session ID: segment-{index + 1}",
                    "- Platform: Teams",
                    $"- Started (UTC): 2026-03-24T17:{new[] { "02:50", "04:37", "06:50", "10:19" }[index]}.0000000+00:00",
                    string.Empty,
                    "## Transcript",
                    string.Empty,
                    transcriptBodies[index]));
        }

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.Equal(3, result.MergedSplitPairCount);
        var mergedStem = stems[0];
        var mergedMarkdownPath = Path.Combine(transcriptDir, $"{mergedStem}.md");
        Assert.True(File.Exists(Path.Combine(audioDir, $"{mergedStem}.wav")));
        Assert.True(File.Exists(mergedMarkdownPath));
        var markdown = await File.ReadAllTextAsync(mergedMarkdownPath);
        Assert.Contains("# [INT] GlobalFoundries daily", markdown, StringComparison.Ordinal);
        Assert.Contains("[00:01:36 - 00:02:29] **Speaker:** Second segment", markdown, StringComparison.Ordinal);
        Assert.Contains("[00:02:29 - 00:03:03] **Speaker:** Third segment", markdown, StringComparison.Ordinal);
        Assert.Contains("[00:03:03 - 00:03:35] **Speaker:** Fourth segment", markdown, StringComparison.Ordinal);

        for (var index = 1; index < stems.Length; index++)
        {
            Assert.False(File.Exists(Path.Combine(audioDir, $"{stems[index]}.wav")));
            Assert.False(File.Exists(Path.Combine(transcriptDir, $"{stems[index]}.md")));
        }

        Assert.True(Directory.Exists(result.ArchiveDirectory));
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Archives_Editor_Window_Bogus_Published_Meetings()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");

        var bogusStem = "2026-03-20_014208_teams_2026-03-20-001940-teams-ducey-gallina-nick-md-meeting-recorder-visual-studio-code";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{bogusStem}.wav"), TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{bogusStem}.md"),
            string.Join(
                Environment.NewLine,
                "# 2026-03-20_001940_teams_ducey-gallina-nick.md - Meeting Recorder - Visual Studio Code",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:01] **Speaker:** bogus"));

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.Equal(1, result.ArchivedEditorArtifactCount);
        Assert.False(File.Exists(Path.Combine(audioDir, $"{bogusStem}.wav")));
        Assert.False(File.Exists(Path.Combine(transcriptDir, $"{bogusStem}.md")));
        Assert.True(Directory.Exists(result.ArchiveDirectory));
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Reruns_Current_Repair_When_A_Legacy_V1_Marker_Already_Exists()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");
        var repairsDir = Path.Combine(appRoot, "repairs");
        Directory.CreateDirectory(repairsDir);
        await File.WriteAllTextAsync(Path.Combine(repairsDir, "published-meeting-repair-v1.done"), "legacy");

        var firstStem = "2026-03-20_132751_teams_wang-stein";
        var secondStem = "2026-03-20_133347_teams_wang-stein";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{firstStem}.wav"), TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(4));
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{secondStem}.wav"), TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{firstStem}.md"),
            string.Join(
                Environment.NewLine,
                "# Wang Stein",
                string.Empty,
                "- Session ID: first",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-20T13:27:51.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:05:04] **Speaker:** First segment"));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{secondStem}.md"),
            string.Join(
                Environment.NewLine,
                "# Wang, Stein",
                string.Empty,
                "- Session ID: second",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-20T13:33:47.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:01] **Speaker:** Second segment"));

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.False(result.AlreadyApplied);
        Assert.Equal(1, result.MergedSplitPairCount);
        Assert.True(File.Exists(Path.Combine(repairsDir, "published-meeting-repair-v1.done")));
        Assert.True(File.Exists(result.MarkerPath));
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Reruns_Current_Repair_When_A_Legacy_V4_Marker_Already_Exists_And_Merges_ManifestBacked_SameTitle_Splits()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");
        var workDir = Path.Combine(appRoot, "work");
        Directory.CreateDirectory(workDir);
        var repairsDir = Path.Combine(appRoot, "repairs");
        Directory.CreateDirectory(repairsDir);
        await File.WriteAllTextAsync(Path.Combine(repairsDir, "published-meeting-repair-v4.done"), "legacy");

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var firstStem = "2026-04-01_103307_teams_ionq-connect";
        var secondStem = "2026-04-01_105114_teams_ionq-connect";
        var firstDuration = TimeSpan.FromMinutes(16) + TimeSpan.FromSeconds(55);
        var secondDuration = TimeSpan.FromMinutes(22) + TimeSpan.FromSeconds(51);
        var firstStartedAtUtc = DateTimeOffset.Parse("2026-04-01T10:33:07Z");
        var secondStartedAtUtc = DateTimeOffset.Parse("2026-04-01T10:51:14Z");

        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{firstStem}.wav"), firstDuration);
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{secondStem}.wav"), secondDuration);
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{firstStem}.md"),
            string.Join(
                Environment.NewLine,
                "# IonQ connect",
                string.Empty,
                "- Session ID: first",
                "- Platform: Teams",
                "- Started (UTC): 2026-04-01T10:33:07.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:16:55] **Speaker:** First segment"));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{secondStem}.md"),
            string.Join(
                Environment.NewLine,
                "# IonQ connect",
                string.Empty,
                "- Session ID: second",
                "- Platform: Teams",
                "- Started (UTC): 2026-04-01T10:51:14.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:22:51] **Speaker:** Second segment"));

        await SaveManifestAsync(
            manifestStore,
            workDir,
            CreateTeamsManifest(
                "20260401103307-first",
                "IonQ connect",
                firstStartedAtUtc,
                firstStartedAtUtc + firstDuration,
                "IonQ connect | Microsoft Teams"));
        await SaveManifestAsync(
            manifestStore,
            workDir,
            CreateTeamsManifest(
                "20260401105114-second",
                "IonQ connect",
                secondStartedAtUtc,
                secondStartedAtUtc + secondDuration,
                "IonQ connect | Microsoft Teams"));

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.False(result.AlreadyApplied);
        Assert.Equal(1, result.MergedSplitPairCount);
        Assert.True(File.Exists(Path.Combine(repairsDir, "published-meeting-repair-v4.done")));
        Assert.True(File.Exists(result.MarkerPath));
        var mergedMarkdownPath = Path.Combine(transcriptDir, $"{firstStem}.md");
        Assert.True(File.Exists(mergedMarkdownPath));
        Assert.False(File.Exists(Path.Combine(audioDir, $"{secondStem}.wav")));
        Assert.False(File.Exists(Path.Combine(transcriptDir, $"{secondStem}.md")));
        var markdown = await File.ReadAllTextAsync(mergedMarkdownPath);
        Assert.Contains("[00:16:55 - 00:39:46] **Speaker:** Second segment", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Archives_Short_Generic_Teams_False_Start()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");

        var stem = "2026-03-20_130255_teams_microsoft-teams";
        await WriteSilentWaveFileAsync(Path.Combine(audioDir, $"{stem}.wav"), TimeSpan.FromSeconds(31));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{stem}.md"),
            string.Join(
                Environment.NewLine,
                "# Microsoft Teams",
                string.Empty,
                "- Session ID: fake",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-20T13:02:55.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:31] **Speaker:** bogus"));

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.Equal(1, result.ArchivedShortGenericTeamsMeetingCount);
        Assert.False(File.Exists(Path.Combine(audioDir, $"{stem}.wav")));
        Assert.False(File.Exists(Path.Combine(transcriptDir, $"{stem}.md")));
        Assert.True(Directory.Exists(result.ArchiveDirectory));
        Assert.Contains(
            $"{Path.DirectorySeparatorChar}Archive{Path.DirectorySeparatorChar}",
            result.ArchiveDirectory,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ArchivedRepairs", result.ArchiveDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Reruns_Echo_Repair_For_Previously_Repaired_Published_Microphone_Sessions()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");
        var workDir = Path.Combine(appRoot, "work");
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var startedAtUtc = DateTimeOffset.Parse("2026-03-16T08:10:31Z");
        var sessionRoot = Path.Combine(workDir, "20260316081031-echo");
        var rawDir = Path.Combine(sessionRoot, "raw");
        var processingDir = Path.Combine(sessionRoot, "processing");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(processingDir);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, startedAtUtc, "Echo");
        var loopbackChunkPath = Path.Combine(rawDir, "loopback-chunk-0001.wav");
        var microphoneChunkPath = Path.Combine(rawDir, "microphone-chunk-0001.wav");
        var publishedAudioPath = Path.Combine(audioDir, $"{stem}.wav");
        var processingAudioPath = Path.Combine(processingDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");

        await WriteWaveFileAsync(loopbackChunkPath, amplitude: 5_000, duration: TimeSpan.FromMilliseconds(600));
        await WriteWaveFileAsync(microphoneChunkPath, amplitude: 3_000, duration: TimeSpan.FromMilliseconds(600));
        await WriteSilentWaveFileAsync(publishedAudioPath, TimeSpan.FromMilliseconds(600));
        await WriteSilentWaveFileAsync(processingAudioPath, TimeSpan.FromMilliseconds(600));
        await File.WriteAllTextAsync(
            markdownPath,
            string.Join(
                Environment.NewLine,
                "# Echo",
                string.Empty,
                "- Session ID: 20260316081031-echo",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-16T08:10:31.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:01] **Speaker:** echo"));

        var manifest = new MeetingSessionManifest
        {
            SessionId = "20260316081031-echo",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Echo",
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = startedAtUtc.AddMilliseconds(600),
            State = SessionState.Published,
            RawChunkPaths = [loopbackChunkPath],
            LoopbackCaptureSegments =
            [
                new LoopbackCaptureSegment(
                    startedAtUtc,
                    startedAtUtc.AddMilliseconds(600),
                    [loopbackChunkPath],
                    string.Empty,
                    "Test speakers",
                    "Multimedia"),
            ],
            MicrophoneChunkPaths = [microphoneChunkPath],
            MicrophoneCaptureSegments =
            [
                new MicrophoneCaptureSegment(
                    startedAtUtc,
                    startedAtUtc.AddMilliseconds(600),
                    [microphoneChunkPath]),
            ],
            MergedAudioPath = processingAudioPath,
            PublishStatus = new ProcessingStageStatus(
                "publish",
                StageExecutionState.Succeeded,
                startedAtUtc.AddDays(1),
                "Published audio was republished after March 23, 2026 echo repair."),
        };

        await SaveManifestAsync(manifestStore, workDir, manifest);

        var originalPublishedBytes = await File.ReadAllBytesAsync(publishedAudioPath);
        var originalProcessingBytes = await File.ReadAllBytesAsync(processingAudioPath);

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.False(result.AlreadyApplied);
        Assert.True(File.Exists(result.MarkerPath));
        Assert.Equal("published-meeting-repair-v6.done", Path.GetFileName(result.MarkerPath));
        Assert.True(Directory.Exists(result.ArchiveDirectory));

        var updatedManifest = await manifestStore.LoadAsync(Path.Combine(sessionRoot, "manifest.json"));
        Assert.Equal(processingAudioPath, updatedManifest.MergedAudioPath);
        Assert.Contains("echo repair v6", updatedManifest.PublishStatus.Message, StringComparison.OrdinalIgnoreCase);

        var reportPath = Path.Combine(result.ArchiveDirectory, "echo-repair-report.txt");
        Assert.True(File.Exists(reportPath));
        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Repaired: 1", report, StringComparison.Ordinal);
        Assert.Contains(stem, report, StringComparison.Ordinal);

        var meetingArchiveDirectory = Path.Combine(result.ArchiveDirectory, stem);
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, $"{stem}.wav")));
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, $"processing-{stem}.wav")));
        Assert.Equal(originalPublishedBytes, await File.ReadAllBytesAsync(Path.Combine(meetingArchiveDirectory, $"{stem}.wav")));
        Assert.Equal(originalProcessingBytes, await File.ReadAllBytesAsync(Path.Combine(meetingArchiveDirectory, $"processing-{stem}.wav")));
        Assert.NotEqual(originalPublishedBytes, await File.ReadAllBytesAsync(publishedAudioPath));
    }

    [Fact]
    public async Task RepairKnownIssuesAsync_Skips_Unrepairable_Echo_Sessions_And_Writes_The_Reason_To_The_Report()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var appRoot = CreateDirectory("app");
        var workDir = Path.Combine(appRoot, "work");
        Directory.CreateDirectory(workDir);

        var pathBuilder = new ArtifactPathBuilder();
        var manifestStore = new SessionManifestStore(pathBuilder);
        var startedAtUtc = DateTimeOffset.Parse("2026-03-23T16:11:43Z");
        var sessionRoot = Path.Combine(workDir, "20260323161143-missing-published");
        var rawDir = Path.Combine(sessionRoot, "raw");
        var processingDir = Path.Combine(sessionRoot, "processing");
        Directory.CreateDirectory(rawDir);
        Directory.CreateDirectory(processingDir);

        var stem = pathBuilder.BuildFileStem(MeetingPlatform.Teams, startedAtUtc, "Missing Publish");
        var loopbackChunkPath = Path.Combine(rawDir, "loopback-chunk-0001.wav");
        var microphoneChunkPath = Path.Combine(rawDir, "microphone-chunk-0001.wav");
        var processingAudioPath = Path.Combine(processingDir, $"{stem}.wav");

        await WriteWaveFileAsync(loopbackChunkPath, amplitude: 4_000, duration: TimeSpan.FromMilliseconds(400));
        await WriteWaveFileAsync(microphoneChunkPath, amplitude: 2_000, duration: TimeSpan.FromMilliseconds(400));
        await WriteSilentWaveFileAsync(processingAudioPath, TimeSpan.FromMilliseconds(400));
        await File.WriteAllTextAsync(
            Path.Combine(transcriptDir, $"{stem}.md"),
            string.Join(
                Environment.NewLine,
                "# Missing Publish",
                string.Empty,
                "- Session ID: 20260323161143-missing-published",
                "- Platform: Teams",
                "- Started (UTC): 2026-03-23T16:11:43.0000000+00:00",
                string.Empty,
                "## Transcript",
                string.Empty,
                "[00:00:00 - 00:00:01] **Speaker:** missing"));

        await SaveManifestAsync(
            manifestStore,
            workDir,
            new MeetingSessionManifest
            {
                SessionId = "20260323161143-missing-published",
                Platform = MeetingPlatform.Teams,
                DetectedTitle = "Missing Publish",
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = startedAtUtc.AddMilliseconds(400),
                State = SessionState.Published,
                RawChunkPaths = [loopbackChunkPath],
                LoopbackCaptureSegments =
                [
                    new LoopbackCaptureSegment(
                        startedAtUtc,
                        startedAtUtc.AddMilliseconds(400),
                        [loopbackChunkPath],
                        string.Empty,
                        "Test speakers",
                        "Multimedia"),
                ],
                MicrophoneChunkPaths = [microphoneChunkPath],
                MicrophoneCaptureSegments =
                [
                    new MicrophoneCaptureSegment(
                        startedAtUtc,
                        startedAtUtc.AddMilliseconds(400),
                        [microphoneChunkPath]),
                ],
                MergedAudioPath = processingAudioPath,
                PublishStatus = new ProcessingStageStatus(
                    "publish",
                    StageExecutionState.Succeeded,
                    startedAtUtc.AddDays(1),
                    "Published audio was republished after March 23, 2026 echo repair."),
            });

        var result = await PublishedMeetingRepairService.RepairKnownIssuesAsync(audioDir, transcriptDir, appRoot);

        Assert.False(result.AlreadyApplied);
        var reportPath = Path.Combine(result.ArchiveDirectory, "echo-repair-report.txt");
        Assert.True(File.Exists(reportPath));
        var report = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Skipped: 1", report, StringComparison.Ordinal);
        Assert.Contains($"{stem}: missing published audio", report, StringComparison.Ordinal);
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

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(path);
        return path;
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

    private static Task WriteWaveFileAsync(string path, short amplitude, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16_000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        var remainingFrames = (int)Math.Round(duration.TotalSeconds * format.SampleRate);
        var buffer = new byte[format.AverageBytesPerSecond];
        var frameOffset = 0;
        while (frameOffset < remainingFrames)
        {
            var framesToWrite = Math.Min((buffer.Length / format.BlockAlign), remainingFrames - frameOffset);
            for (var frameIndex = 0; frameIndex < framesToWrite; frameIndex++)
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(frameIndex * format.BlockAlign, sizeof(short)), amplitude);
            }

            writer.Write(buffer, 0, framesToWrite * format.BlockAlign);
            frameOffset += framesToWrite;
        }

        return Task.CompletedTask;
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

    private static Task SaveManifestAsync(
        SessionManifestStore manifestStore,
        string workDir,
        MeetingSessionManifest manifest)
    {
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        return manifestStore.SaveAsync(manifest, manifestPath);
    }
}
