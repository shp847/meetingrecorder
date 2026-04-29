using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCleanupExecutionServiceTests : IDisposable
{
    private const FileAttributes WindowsFileAttributePinned = (FileAttributes)0x00080000;
    private const FileAttributes WindowsFileAttributeUnpinned = (FileAttributes)0x00100000;

    private readonly string _root;
    private readonly ArtifactPathBuilder _pathBuilder = new();
    private readonly MeetingOutputCatalogService _catalog;

    public MeetingCleanupExecutionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _catalog = new MeetingOutputCatalogService(_pathBuilder);
    }

    [Fact]
    public async Task ArchiveMeetingAsync_Moves_All_Related_Artifacts_Together()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var archiveRoot = CreateDirectory("archive");
        var stem = "2026-03-15_232832_teams_chat-muzzi-marcelo";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var jsonPath = Path.Combine(transcriptDir, "json", $"{stem}.json");
        var readyPath = Path.Combine(transcriptDir, "json", $"{stem}.ready");

        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(markdownPath, "# Chat Muzzi Marcelo");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, "{}");
        await File.WriteAllTextAsync(readyPath, string.Empty);

        var record = new MeetingOutputRecord(
            stem,
            "Chat Muzzi Marcelo",
            DateTimeOffset.Parse("2026-03-15T23:28:32Z"),
            MeetingPlatform.Teams,
            TimeSpan.Zero,
            audioPath,
            markdownPath,
            jsonPath,
            readyPath,
            null,
            null,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var service = new MeetingCleanupExecutionService(_pathBuilder, _catalog);

        await service.ArchiveMeetingAsync(record, archiveRoot, "archive-false-start", CancellationToken.None);

        var meetingArchiveDirectory = Path.Combine(archiveRoot, "archive-false-start", stem);
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, Path.GetFileName(audioPath))));
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, Path.GetFileName(markdownPath))));
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, Path.GetFileName(jsonPath))));
        Assert.True(File.Exists(Path.Combine(meetingArchiveDirectory, Path.GetFileName(readyPath))));
        Assert.False(File.Exists(audioPath));
        Assert.False(File.Exists(markdownPath));
    }

    [Fact]
    public async Task ArchiveMeetingAsync_Marks_Archived_Artifacts_Unpinned_For_Cloud_Storage()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var archiveRoot = CreateDirectory("archive");
        var stem = "2026-04-22_140050_teams_skywater-finance";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");

        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(markdownPath, "# Skywater Finance");

        var record = new MeetingOutputRecord(
            stem,
            "Skywater Finance",
            DateTimeOffset.Parse("2026-04-22T14:00:50Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(30),
            audioPath,
            markdownPath,
            null,
            null,
            null,
            null,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var service = new MeetingCleanupExecutionService(_pathBuilder, _catalog);

        await service.ArchiveMeetingAsync(record, archiveRoot, "archive-repair-backup", CancellationToken.None);

        var archivedAudioAttributes = File.GetAttributes(Path.Combine(archiveRoot, "archive-repair-backup", stem, Path.GetFileName(audioPath)));
        Assert.True(archivedAudioAttributes.HasFlag(WindowsFileAttributeUnpinned));
        Assert.False(archivedAudioAttributes.HasFlag(WindowsFileAttributePinned));
    }

    [Fact]
    public async Task MergeMeetingsAsync_Creates_One_Surviving_Published_Meeting_And_Archives_Originals()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var archiveRoot = CreateDirectory("archive");
        var firstStem = "2026-03-19_235528_teams_wang-stein";
        var secondStem = "2026-03-19_235531_teams_wang-stein";
        var firstAudioPath = Path.Combine(audioDir, $"{firstStem}.wav");
        var secondAudioPath = Path.Combine(audioDir, $"{secondStem}.wav");
        var firstMarkdownPath = Path.Combine(transcriptDir, $"{firstStem}.md");
        var secondMarkdownPath = Path.Combine(transcriptDir, $"{secondStem}.md");
        await WriteSilentWaveFileAsync(firstAudioPath, TimeSpan.FromSeconds(2));
        await WriteSilentWaveFileAsync(secondAudioPath, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(
            firstMarkdownPath,
            "# Wang Stein" + Environment.NewLine + Environment.NewLine + "## Transcript" + Environment.NewLine + Environment.NewLine + "[00:00:00 - 00:00:02] **Speaker:** First");
        await File.WriteAllTextAsync(
            secondMarkdownPath,
            "# Wang, Stein" + Environment.NewLine + Environment.NewLine + "## Transcript" + Environment.NewLine + Environment.NewLine + "[00:00:00 - 00:00:01] **Speaker:** Second");

        var first = new MeetingOutputRecord(firstStem, "Wang Stein", DateTimeOffset.Parse("2026-03-19T23:55:28Z"), MeetingPlatform.Teams, TimeSpan.FromSeconds(2), firstAudioPath, firstMarkdownPath, null, null, null, null, Array.Empty<MeetingAttendee>(), false, null);
        var second = new MeetingOutputRecord(secondStem, "Wang, Stein", DateTimeOffset.Parse("2026-03-19T23:55:31Z"), MeetingPlatform.Teams, TimeSpan.FromSeconds(1), secondAudioPath, secondMarkdownPath, null, null, null, null, Array.Empty<MeetingAttendee>(), false, null);
        var service = new MeetingCleanupExecutionService(_pathBuilder, _catalog);

        var merged = await service.MergeMeetingsAsync(first, second, "Wang, Stein", audioDir, transcriptDir, archiveRoot, CancellationToken.None);

        Assert.Equal("2026-03-19_235528_teams_wang-stein", merged.SurvivingStem);
        Assert.True(File.Exists(Path.Combine(audioDir, $"{merged.SurvivingStem}.wav")));
        Assert.True(File.Exists(Path.Combine(transcriptDir, $"{merged.SurvivingStem}.md")));
        Assert.False(File.Exists(secondAudioPath));
        Assert.False(File.Exists(secondMarkdownPath));
        Assert.True(Directory.Exists(Path.Combine(archiveRoot, "merge-split-pairs", firstStem)));
        Assert.True(Directory.Exists(Path.Combine(archiveRoot, "merge-split-pairs", secondStem)));
    }

    [Fact]
    public async Task DeleteMeetingPermanentlyAsync_Removes_All_Published_Artifacts_And_Linked_Session_Folder()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var workDir = CreateDirectory("work");
        var stem = "2026-03-20_160131_teams_intel-cio-discussion";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var jsonPath = Path.Combine(transcriptDir, "json", $"{stem}.json");
        var readyPath = Path.Combine(transcriptDir, "json", $"{stem}.ready");
        var sessionRoot = Path.Combine(workDir, "session-1");
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");

        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(markdownPath, "# Intel CIO Discussion");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, "{}");
        await File.WriteAllTextAsync(readyPath, string.Empty);
        Directory.CreateDirectory(sessionRoot);
        await File.WriteAllTextAsync(manifestPath, "{}");

        var record = new MeetingOutputRecord(
            stem,
            "Intel CIO Discussion",
            DateTimeOffset.Parse("2026-03-20T16:01:31Z"),
            MeetingPlatform.Teams,
            TimeSpan.FromMinutes(35),
            audioPath,
            markdownPath,
            jsonPath,
            readyPath,
            manifestPath,
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var service = new MeetingCleanupExecutionService(_pathBuilder, _catalog);

        await service.DeleteMeetingPermanentlyAsync(record, CancellationToken.None);

        Assert.False(File.Exists(audioPath));
        Assert.False(File.Exists(markdownPath));
        Assert.False(File.Exists(jsonPath));
        Assert.False(File.Exists(readyPath));
        Assert.False(Directory.Exists(sessionRoot));
    }

    [Fact]
    public async Task ArchiveMeetingAsync_Leaves_Linked_Session_Folder_Intact()
    {
        var audioDir = CreateDirectory("audio");
        var transcriptDir = CreateDirectory("transcripts");
        var archiveRoot = CreateDirectory("archive");
        var workDir = CreateDirectory("work");
        var stem = "2026-03-20_153324_manual_connect-on-globalfoundries";
        var audioPath = Path.Combine(audioDir, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDir, $"{stem}.md");
        var sessionRoot = Path.Combine(workDir, "session-2");
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");

        await WriteSilentWaveFileAsync(audioPath, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(markdownPath, "# Connect On Globalfoundries");
        Directory.CreateDirectory(sessionRoot);
        await File.WriteAllTextAsync(manifestPath, "{}");

        var record = new MeetingOutputRecord(
            stem,
            "Connect On Globalfoundries",
            DateTimeOffset.Parse("2026-03-20T15:33:24Z"),
            MeetingPlatform.Manual,
            TimeSpan.FromMinutes(23),
            audioPath,
            markdownPath,
            null,
            null,
            manifestPath,
            SessionState.Published,
            Array.Empty<MeetingAttendee>(),
            false,
            null);

        var service = new MeetingCleanupExecutionService(_pathBuilder, _catalog);

        await service.ArchiveMeetingAsync(record, archiveRoot, "archive-manual", CancellationToken.None);

        Assert.True(Directory.Exists(sessionRoot));
        Assert.True(File.Exists(manifestPath));
    }

    [Fact]
    public void ConsolidateLegacyArchiveRoots_Moves_Legacy_Folders_Under_Canonical_Archive_Root()
    {
        var meetingsRoot = CreateDirectory("meetings");
        var audioDir = Path.Combine(meetingsRoot, "Recordings");
        Directory.CreateDirectory(audioDir);

        var legacyFalseStarts = Path.Combine(meetingsRoot, "ArchivedFalseStarts");
        var legacyRepairs = Path.Combine(meetingsRoot, "ArchivedRepairs");
        Directory.CreateDirectory(legacyFalseStarts);
        Directory.CreateDirectory(legacyRepairs);
        File.WriteAllText(Path.Combine(legacyFalseStarts, "false-start.wav"), "audio");
        File.WriteAllText(Path.Combine(legacyRepairs, "repair.md"), "markdown");

        MeetingCleanupExecutionService.ConsolidateLegacyArchiveRoots(audioDir);

        var archiveRoot = MeetingCleanupExecutionService.GetArchiveRoot(audioDir);
        Assert.False(Directory.Exists(legacyFalseStarts));
        Assert.False(Directory.Exists(legacyRepairs));
        Assert.True(File.Exists(Path.Combine(archiveRoot, "legacy", "ArchivedFalseStarts", "false-start.wav")));
        Assert.True(File.Exists(Path.Combine(archiveRoot, "legacy", "ArchivedRepairs", "repair.md")));
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
}
