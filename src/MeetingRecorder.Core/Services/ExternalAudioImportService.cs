using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class ExternalAudioImportService
{
    private static readonly Regex PublishedStemPattern = new(
        "^(?<date>\\d{4}-\\d{2}-\\d{2})_(?<time>\\d{6})_(?<platform>[a-z]+)_(?<slug>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".mp4",
    };

    private static readonly TimeSpan MinimumFileQuietPeriod = TimeSpan.FromSeconds(15);

    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly SessionManifestStore _manifestStore;

    public ExternalAudioImportService(ArtifactPathBuilder pathBuilder)
    {
        _pathBuilder = pathBuilder;
        _manifestStore = new SessionManifestStore(pathBuilder);
    }

    public async Task<IReadOnlyList<ImportedExternalAudioResult>> ImportPendingAudioFilesAsync(
        AppConfig config,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(config.AudioOutputDir) || !Directory.Exists(config.AudioOutputDir))
        {
            return Array.Empty<ImportedExternalAudioResult>();
        }

        if (string.IsNullOrWhiteSpace(config.WorkDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(config));
        }

        Directory.CreateDirectory(config.WorkDir);
        var knownImports = await LoadKnownImportsAsync(config.WorkDir, cancellationToken);
        var imported = new List<ImportedExternalAudioResult>();

        foreach (var sourcePath in Directory.EnumerateFiles(config.AudioOutputDir).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSupportedSourceAudioPath(sourcePath))
            {
                continue;
            }

            var sourceFile = new FileInfo(sourcePath);
            if (!sourceFile.Exists)
            {
                continue;
            }

            if (!HasSettled(sourceFile, nowUtc))
            {
                continue;
            }

            if (HasTranscriptArtifactForSource(config.TranscriptOutputDir, sourcePath))
            {
                continue;
            }

            var sourceMetadata = new ImportedSourceAudioInfo(
                NormalizePath(sourcePath),
                sourceFile.Length,
                CreateUtcTimestamp(sourceFile.LastWriteTimeUtc));

            if (knownImports.Any(existing => SourceMatches(existing, sourceMetadata)))
            {
                continue;
            }

            var result = await ImportSingleAudioFileAsync(config.WorkDir, sourceMetadata, nowUtc, cancellationToken);
            knownImports.Add(sourceMetadata);
            imported.Add(result);
        }

        return imported;
    }

    private async Task<ImportedExternalAudioResult> ImportSingleAudioFileAsync(
        string workDir,
        ImportedSourceAudioInfo sourceMetadata,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var importedMeetingInfo = ResolveImportedMeetingInfo(sourceMetadata.OriginalPath, sourceMetadata.SourceLastWriteUtc);
        var detectionEvidence = new[]
        {
            new DetectionSignal(
                "external-audio-import",
                Path.GetFileName(sourceMetadata.OriginalPath),
                1d,
                nowUtc),
        };

        var manifest = await _manifestStore.CreateAsync(
            workDir,
            importedMeetingInfo.Platform,
            importedMeetingInfo.Title,
            detectionEvidence,
            cancellationToken);

        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, manifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var copiedAudioPath = Path.Combine(
            sessionRoot,
            "processing",
            $"imported-source{Path.GetExtension(sourceMetadata.OriginalPath)}");

        try
        {
            File.Copy(sourceMetadata.OriginalPath, copiedAudioPath, overwrite: false);

            var updatedManifest = manifest with
            {
                Platform = importedMeetingInfo.Platform,
                StartedAtUtc = importedMeetingInfo.StartedAtUtc,
                MergedAudioPath = copiedAudioPath,
                ImportedSourceAudio = sourceMetadata,
                TranscriptionStatus = new ProcessingStageStatus(
                    "transcription",
                    StageExecutionState.NotStarted,
                    nowUtc,
                    "Queued from an externally dropped audio file."),
            };

            await _manifestStore.SaveAsync(updatedManifest, manifestPath, cancellationToken);
            TryDeleteSourceFile(sourceMetadata.OriginalPath);

            return new ImportedExternalAudioResult(
                manifestPath,
                sourceMetadata.OriginalPath,
                updatedManifest.DetectedTitle);
        }
        catch
        {
            TryDeleteFile(copiedAudioPath);
            TryDeleteDirectory(sessionRoot);
            throw;
        }
    }

    private async Task<List<ImportedSourceAudioInfo>> LoadKnownImportsAsync(string workDir, CancellationToken cancellationToken)
    {
        var knownImports = new List<ImportedSourceAudioInfo>();
        if (!Directory.Exists(workDir))
        {
            return knownImports;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var manifest = await _manifestStore.LoadAsync(manifestPath, cancellationToken);
                if (manifest.ImportedSourceAudio is null)
                {
                    continue;
                }

                knownImports.Add(manifest.ImportedSourceAudio with
                {
                    OriginalPath = NormalizePath(manifest.ImportedSourceAudio.OriginalPath),
                });
            }
            catch
            {
                // Ignore malformed or partially-written manifests while scanning for imports.
            }
        }

        return knownImports;
    }

    private static bool HasSettled(FileInfo sourceFile, DateTimeOffset nowUtc)
    {
        var lastWriteUtc = CreateUtcTimestamp(sourceFile.LastWriteTimeUtc);
        return nowUtc - lastWriteUtc >= MinimumFileQuietPeriod;
    }

    private static bool HasTranscriptArtifactForSource(string transcriptOutputDir, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(transcriptOutputDir) || !Directory.Exists(transcriptOutputDir))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        return File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.md")) ||
            File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.json")) ||
            File.Exists(Path.Combine(transcriptOutputDir, $"{stem}.ready"));
    }

    private static bool IsSupportedSourceAudioPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        return !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SourceMatches(ImportedSourceAudioInfo existing, ImportedSourceAudioInfo current)
    {
        return string.Equals(existing.OriginalPath, current.OriginalPath, StringComparison.OrdinalIgnoreCase) &&
            existing.SourceSizeBytes == current.SourceSizeBytes &&
            existing.SourceLastWriteUtc.UtcDateTime == current.SourceLastWriteUtc.UtcDateTime;
    }

    private static string BuildImportedTitle(string sourcePath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath)?.Trim();
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return "Imported Audio";
        }

        return fileNameWithoutExtension
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
    }

    private static ImportedMeetingInfo ResolveImportedMeetingInfo(string sourcePath, DateTimeOffset fallbackStartedAtUtc)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (TryParsePublishedStem(stem, out var parsedInfo))
        {
            return parsedInfo;
        }

        return new ImportedMeetingInfo(
            MeetingPlatform.Manual,
            BuildImportedTitle(sourcePath),
            fallbackStartedAtUtc);
    }

    private static bool TryParsePublishedStem(string? stem, out ImportedMeetingInfo meetingInfo)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            meetingInfo = default;
            return false;
        }

        var match = PublishedStemPattern.Match(stem);
        if (!match.Success)
        {
            meetingInfo = default;
            return false;
        }

        var timestampText = $"{match.Groups["date"].Value}_{match.Groups["time"].Value}";
        if (!DateTimeOffset.TryParseExact(
                timestampText,
                "yyyy-MM-dd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedAtUtc))
        {
            meetingInfo = default;
            return false;
        }

        var platform = match.Groups["platform"].Value switch
        {
            "teams" => MeetingPlatform.Teams,
            "gmeet" => MeetingPlatform.GoogleMeet,
            "manual" => MeetingPlatform.Manual,
            _ => MeetingPlatform.Unknown,
        };

        meetingInfo = new ImportedMeetingInfo(
            platform,
            HumanizeSlug(match.Groups["slug"].Value),
            startedAtUtc);
        return true;
    }

    private static string HumanizeSlug(string slug)
    {
        var words = slug
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
            .ToArray();
        return words.Length == 0 ? "Imported Audio" : string.Join(' ', words);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static DateTimeOffset CreateUtcTimestamp(DateTime utcDateTime)
    {
        return new(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
    }

    private static void TryDeleteSourceFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only. The persisted source metadata prevents duplicate re-import attempts.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort rollback only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort rollback only.
        }
    }

    private readonly record struct ImportedMeetingInfo(
        MeetingPlatform Platform,
        string Title,
        DateTimeOffset StartedAtUtc);
}

public sealed record ImportedExternalAudioResult(
    string ManifestPath,
    string OriginalSourcePath,
    string Title);
