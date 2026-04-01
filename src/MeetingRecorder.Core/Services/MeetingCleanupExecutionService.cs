using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using System.Text;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed record MeetingCleanupMergeResult(string SurvivingStem, string ArchiveDirectory);

internal sealed partial class MeetingCleanupExecutionService
{
    [GeneratedRegex("^\\[(?<start>\\d{2}:\\d{2}:\\d{2})\\s-\\s(?<end>\\d{2}:\\d{2}:\\d{2})\\](?<suffix>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TranscriptTimestampPattern();

    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly MeetingOutputCatalogService _catalog;

    public MeetingCleanupExecutionService(ArtifactPathBuilder pathBuilder, MeetingOutputCatalogService catalog)
    {
        _pathBuilder = pathBuilder;
        _catalog = catalog;
    }

    public static string GetArchiveRoot(string audioOutputDir)
    {
        return Path.Combine(
            Directory.GetParent(audioOutputDir)?.FullName ?? audioOutputDir,
            "Archive");
    }

    public static string GetArchiveCategory(MeetingCleanupRecommendation recommendation)
    {
        return recommendation.ReasonCode switch
        {
            "archive-editor-false-meeting" => "editor-window-false-meetings",
            "archive-short-generic-teams" => "short-generic-teams-false-starts",
            "archive-tiny-audio" => "tiny-or-empty-audio",
            "archive-orphan-transcript" => "orphan-transcript-only",
            "archive-duplicate-publish" => "duplicate-publishes",
            _ => "meeting-cleanup",
        };
    }

    public static string CreateExecutionArchiveDirectory(string archiveRoot, string label)
    {
        var safeLabel = string.Join(
            "-",
            label.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeLabel))
        {
            safeLabel = "cleanup";
        }

        var executionDirectory = Path.Combine(
            archiveRoot,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeLabel}");
        Directory.CreateDirectory(executionDirectory);
        return executionDirectory;
    }

    public static void ConsolidateLegacyArchiveRoots(string audioOutputDir)
    {
        var archiveRoot = GetArchiveRoot(audioOutputDir);
        Directory.CreateDirectory(archiveRoot);

        foreach (var legacyRoot in GetLegacyArchiveRoots(audioOutputDir))
        {
            if (!Directory.Exists(legacyRoot))
            {
                continue;
            }

            var destinationRoot = Path.Combine(archiveRoot, "legacy", Path.GetFileName(legacyRoot));
            MoveDirectoryContents(legacyRoot, destinationRoot);

            if (!Directory.EnumerateFileSystemEntries(legacyRoot).Any())
            {
                Directory.Delete(legacyRoot);
            }
        }
    }

    public Task ArchiveMeetingAsync(
        MeetingOutputRecord meeting,
        string archiveDirectory,
        string category,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destinationDirectory = Path.Combine(archiveDirectory, category, meeting.Stem);
        Directory.CreateDirectory(destinationDirectory);
        MoveIfPresent(meeting.AudioPath, destinationDirectory);
        MoveIfPresent(meeting.MarkdownPath, destinationDirectory);
        MoveIfPresent(meeting.JsonPath, destinationDirectory);
        MoveIfPresent(meeting.ReadyMarkerPath, destinationDirectory);
        return Task.CompletedTask;
    }

    public Task DeleteMeetingPermanentlyAsync(
        MeetingOutputRecord meeting,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DeleteFileIfPresent(meeting.AudioPath);
        DeleteFileIfPresent(meeting.MarkdownPath);
        DeleteFileIfPresent(meeting.JsonPath);
        DeleteFileIfPresent(meeting.ReadyMarkerPath);

        if (!string.IsNullOrWhiteSpace(meeting.ManifestPath))
        {
            var sessionRoot = Path.GetDirectoryName(meeting.ManifestPath);
            if (!string.IsNullOrWhiteSpace(sessionRoot) && Directory.Exists(sessionRoot))
            {
                Directory.Delete(sessionRoot, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    public async Task<MeetingCleanupMergeResult> MergeMeetingsAsync(
        MeetingOutputRecord first,
        MeetingOutputRecord second,
        string preferredTitle,
        string audioOutputDir,
        string transcriptOutputDir,
        string archiveDirectory,
        CancellationToken cancellationToken = default)
    {
        return await MergeMeetingsAsync(
            [first, second],
            preferredTitle,
            audioOutputDir,
            transcriptOutputDir,
            archiveDirectory,
            cancellationToken);
    }

    public async Task<MeetingCleanupMergeResult> MergeMeetingsAsync(
        IReadOnlyList<MeetingOutputRecord> meetings,
        string preferredTitle,
        string audioOutputDir,
        string transcriptOutputDir,
        string archiveDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var orderedMeetings = meetings
            .Where(meeting => !string.IsNullOrWhiteSpace(meeting.Stem))
            .OrderBy(meeting => meeting.StartedAtUtc)
            .ThenBy(meeting => meeting.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedMeetings.Length < 2)
        {
            throw new InvalidOperationException("At least two published meetings are required to merge a split chain.");
        }

        var first = orderedMeetings[0];
        var mergedStem = _pathBuilder.BuildFileStem(first.Platform, first.StartedAtUtc, preferredTitle);
        var tempDirectory = Path.Combine(archiveDirectory, "_temp");
        Directory.CreateDirectory(tempDirectory);
        var temporaryAudioPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.wav");
        var temporaryMarkdownPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.md");

        await new WaveChunkMerger().MergePublishedAudioFilesAsync(
            orderedMeetings.Select(meeting => meeting.AudioPath!).ToArray(),
            temporaryAudioPath,
            cancellationToken);

        var mergedMarkdown = await BuildMergedMarkdownAsync(orderedMeetings, preferredTitle, cancellationToken);
        await File.WriteAllTextAsync(temporaryMarkdownPath, mergedMarkdown, cancellationToken);

        foreach (var meeting in orderedMeetings)
        {
            await ArchiveMeetingAsync(meeting, archiveDirectory, "merge-split-pairs", cancellationToken);
        }

        var finalAudioPath = Path.Combine(audioOutputDir, $"{mergedStem}{Path.GetExtension(first.AudioPath!)}");
        var finalMarkdownPath = Path.Combine(transcriptOutputDir, $"{mergedStem}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(finalAudioPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finalMarkdownPath)!);

        if (File.Exists(finalAudioPath))
        {
            File.Delete(finalAudioPath);
        }

        if (File.Exists(finalMarkdownPath))
        {
            File.Delete(finalMarkdownPath);
        }

        File.Move(temporaryAudioPath, finalAudioPath);
        File.Move(temporaryMarkdownPath, finalMarkdownPath);

        return new MeetingCleanupMergeResult(mergedStem, archiveDirectory);
    }

    public Task<MeetingOutputRecord> RenameMeetingAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string workDir,
        MeetingOutputRecord record,
        string suggestedTitle,
        CancellationToken cancellationToken = default)
    {
        return _catalog.RenameMeetingAsync(
            audioOutputDir,
            transcriptOutputDir,
            record.Stem,
            suggestedTitle,
            workDir,
            cancellationToken);
    }

    private static async Task<string> BuildMergedMarkdownAsync(
        MeetingOutputRecord first,
        MeetingOutputRecord second,
        string preferredTitle,
        CancellationToken cancellationToken)
    {
        return await BuildMergedMarkdownAsync([first, second], preferredTitle, cancellationToken);
    }

    private static async Task<string> BuildMergedMarkdownAsync(
        IReadOnlyList<MeetingOutputRecord> meetings,
        string preferredTitle,
        CancellationToken cancellationToken)
    {
        var orderedMeetings = meetings
            .OrderBy(meeting => meeting.StartedAtUtc)
            .ThenBy(meeting => meeting.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (orderedMeetings.Length == 0)
        {
            throw new InvalidOperationException("At least one meeting is required to build merged markdown.");
        }

        var first = orderedMeetings[0];
        var builder = new StringBuilder();
        builder.AppendLine($"# {preferredTitle}");
        builder.AppendLine();
        builder.AppendLine($"- Session ID: cleanup-{first.StartedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        builder.AppendLine($"- Platform: {first.Platform}");
        builder.AppendLine($"- Started (UTC): {first.StartedAtUtc:O}");
        builder.AppendLine();
        builder.AppendLine("## Transcript");
        builder.AppendLine();
        var currentOffset = TimeSpan.Zero;
        for (var index = 0; index < orderedMeetings.Length; index++)
        {
            var meeting = orderedMeetings[index];
            var body = await ReadTranscriptBodyAsync(meeting.MarkdownPath!, currentOffset, cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                if (!builder.ToString().EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal))
                {
                    builder.AppendLine();
                }

                builder.Append(body);
            }

            currentOffset += meeting.Duration ?? TimeSpan.Zero;
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static async Task<string> ReadTranscriptBodyAsync(string markdownPath, TimeSpan offset, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        var transcriptHeadingIndex = content.IndexOf("## Transcript", StringComparison.Ordinal);
        if (transcriptHeadingIndex < 0)
        {
            return string.Empty;
        }

        var body = content[(transcriptHeadingIndex + "## Transcript".Length)..].TrimStart('\r', '\n');
        if (offset == TimeSpan.Zero)
        {
            return body.TrimEnd();
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var match = TranscriptTimestampPattern().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var shiftedStart = TimeSpan.Parse(match.Groups["start"].Value) + offset;
            var shiftedEnd = TimeSpan.Parse(match.Groups["end"].Value) + offset;
            lines[index] = $"[{shiftedStart:hh\\:mm\\:ss} - {shiftedEnd:hh\\:mm\\:ss}]{match.Groups["suffix"].Value}";
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static void MoveIfPresent(string? sourcePath, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
        if (File.Exists(destinationPath))
        {
            File.Delete(sourcePath);
            return;
        }

        File.Move(sourcePath, destinationPath);
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }

    private static IEnumerable<string> GetLegacyArchiveRoots(string audioOutputDir)
    {
        var meetingsRoot = Directory.GetParent(audioOutputDir)?.FullName ?? audioOutputDir;
        yield return Path.Combine(meetingsRoot, "ArchivedFalseStarts");
        yield return Path.Combine(meetingsRoot, "ArchivedGenericCleanup");
        yield return Path.Combine(meetingsRoot, "ArchivedRepairs");
    }

    private static void MoveDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            MoveFileSystemEntry(entry, destinationPath);
        }
    }

    private static void MoveFileSystemEntry(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            MoveFileWithUniqueDestination(sourcePath, destinationPath);
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        if (!Directory.Exists(destinationPath) && !File.Exists(destinationPath))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        if (File.Exists(destinationPath))
        {
            destinationPath = BuildUniqueDestinationPath(destinationPath);
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        foreach (var childEntry in Directory.EnumerateFileSystemEntries(sourcePath))
        {
            var childDestinationPath = Path.Combine(destinationPath, Path.GetFileName(childEntry));
            MoveFileSystemEntry(childEntry, childDestinationPath);
        }

        if (!Directory.EnumerateFileSystemEntries(sourcePath).Any())
        {
            Directory.Delete(sourcePath);
        }
    }

    private static void MoveFileWithUniqueDestination(string sourcePath, string destinationPath)
    {
        var finalDestinationPath = destinationPath;
        if (File.Exists(finalDestinationPath) || Directory.Exists(finalDestinationPath))
        {
            finalDestinationPath = BuildUniqueDestinationPath(finalDestinationPath);
        }

        File.Move(sourcePath, finalDestinationPath);
    }

    private static string BuildUniqueDestinationPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{fileNameWithoutExtension}-migrated-{suffix}{extension}");
    }
}
