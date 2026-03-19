using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class MeetingOutputCatalogService
{
    private const int MaxJsonTitlePreviewBytes = 64 * 1024;

    private static readonly Regex StemPattern = new(
        "^(?<date>\\d{4}-\\d{2}-\\d{2})_(?<time>\\d{6})_(?<platform>[a-z]+)_(?<slug>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex JsonTitlePattern = new(
        "\"(?<name>title|Title)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownSpeakerLinePattern = new(
        "(?m)(?<prefix>^\\[\\d{2}:\\d{2}:\\d{2}\\s-\\s\\d{2}:\\d{2}:\\d{2}\\]\\s\\*\\*)(?<label>.+?)(?<suffix>:\\*\\*\\s)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ArtifactPathBuilder _pathBuilder;

    public MeetingOutputCatalogService(ArtifactPathBuilder pathBuilder)
    {
        _pathBuilder = pathBuilder;
    }

    public IReadOnlyList<MeetingOutputRecord> ListMeetings(string audioOutputDir, string transcriptOutputDir)
    {
        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir: null);
    }

    public IReadOnlyList<MeetingOutputRecord> ListMeetings(string audioOutputDir, string transcriptOutputDir, string? workDir)
    {
        var pathsByStem = new Dictionary<string, MeetingOutputMutableRecord>(StringComparer.OrdinalIgnoreCase);
        AddArtifacts(pathsByStem, audioOutputDir);
        AddArtifacts(pathsByStem, transcriptOutputDir);
        var manifestInfoByStem = LoadManifestInfoByStem(workDir, pathsByStem.Keys);

        return pathsByStem.Values
            .Select(record => BuildRecord(
                record,
                manifestInfoByStem.TryGetValue(record.Stem, out var manifestInfo)
                    ? manifestInfo
                    : null))
            .OrderByDescending(record => record.StartedAtUtc)
            .ThenBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<MeetingOutputRecord> RenameMeetingAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        return await RenameMeetingAsync(
            audioOutputDir,
            transcriptOutputDir,
            existingStem,
            newTitle,
            workDir: null,
            cancellationToken);
    }

    public async Task<MeetingOutputRecord> RenameMeetingAsync(
        string audioOutputDir,
        string transcriptOutputDir,
        string existingStem,
        string newTitle,
        string? workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(newTitle));
        }

        if (!TryParseStem(existingStem, out var stemInfo))
        {
            throw new InvalidOperationException($"The output stem '{existingStem}' does not match the expected format.");
        }

        var newStem = _pathBuilder.BuildFileStem(stemInfo.Platform, stemInfo.StartedAtUtc, newTitle);
        var meetings = ListMeetings(audioOutputDir, transcriptOutputDir, workDir);
        var existing = meetings.SingleOrDefault(record => string.Equals(record.Stem, existingStem, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Unable to locate published artifacts for stem '{existingStem}'.");

        var renamePairs = BuildRenamePairs(existing, newStem);
        foreach (var pair in renamePairs)
        {
            if (!string.Equals(pair.SourcePath, pair.DestinationPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(pair.DestinationPath))
            {
                throw new IOException($"Cannot rename '{pair.SourcePath}' because '{pair.DestinationPath}' already exists.");
            }
        }

        foreach (var pair in renamePairs)
        {
            if (string.Equals(pair.SourcePath, pair.DestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Move(pair.SourcePath, pair.DestinationPath, overwrite: false);
        }

        if (!string.IsNullOrWhiteSpace(existing.MarkdownPath))
        {
            await UpdateMarkdownTitleAsync(Path.Combine(transcriptOutputDir, $"{newStem}{Path.GetExtension(existing.MarkdownPath)}"), newTitle, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(existing.JsonPath))
        {
            await UpdateJsonTitleAsync(Path.Combine(transcriptOutputDir, $"{newStem}{Path.GetExtension(existing.JsonPath)}"), newTitle, cancellationToken);
        }

        await UpdateManifestTitleAsync(workDir, existingStem, newTitle, cancellationToken);

        return ListMeetings(audioOutputDir, transcriptOutputDir, workDir)
            .Single(record => string.Equals(record.Stem, newStem, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ListSpeakerLabels(MeetingOutputRecord record)
    {
        var labels = TryReadJsonSpeakerLabels(record.JsonPath);
        if (labels.Count > 0)
        {
            return labels;
        }

        return TryReadMarkdownSpeakerLabels(record.MarkdownPath);
    }

    public async Task RenameSpeakerLabelsAsync(
        MeetingOutputRecord record,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedMap = speakerLabelMap
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Key) &&
                !string.IsNullOrWhiteSpace(entry.Value) &&
                !string.Equals(entry.Key.Trim(), entry.Value.Trim(), StringComparison.Ordinal))
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => entry.Value.Trim(),
                StringComparer.Ordinal);

        if (normalizedMap.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.JsonPath) && File.Exists(record.JsonPath))
        {
            await UpdateJsonSpeakerLabelsAsync(record.JsonPath, normalizedMap, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(record.MarkdownPath) && File.Exists(record.MarkdownPath))
        {
            await UpdateMarkdownSpeakerLabelsAsync(record.MarkdownPath, normalizedMap, cancellationToken);
        }
    }

    public async Task<string> CreateSyntheticManifestForPublishedMeetingAsync(
        MeetingOutputRecord record,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(record.AudioPath) || !File.Exists(record.AudioPath))
        {
            throw new FileNotFoundException("A published audio file is required to synthesize a work manifest.", record.AudioPath);
        }

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        if (!string.IsNullOrWhiteSpace(record.ManifestPath) && File.Exists(record.ManifestPath))
        {
            return record.ManifestPath;
        }

        var now = DateTimeOffset.UtcNow;
        var manifestStore = new SessionManifestStore(_pathBuilder);
        var sessionId = $"imported-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, sessionId);

        Directory.CreateDirectory(sessionRoot);
        Directory.CreateDirectory(Path.Combine(sessionRoot, "raw"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "processing"));
        Directory.CreateDirectory(Path.Combine(sessionRoot, "logs"));

        var manifest = new MeetingSessionManifest
        {
            SessionId = sessionId,
            Platform = record.Platform,
            DetectedTitle = string.IsNullOrWhiteSpace(record.Title) ? record.Stem : record.Title,
            StartedAtUtc = record.StartedAtUtc == DateTimeOffset.MinValue ? now : record.StartedAtUtc,
            State = SessionState.Queued,
            DetectionEvidence = Array.Empty<DetectionSignal>(),
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
            MergedAudioPath = record.AudioPath,
            TranscriptionStatus = new ProcessingStageStatus("transcription", StageExecutionState.Queued, now, "Queued from an existing published audio file."),
            DiarizationStatus = new ProcessingStageStatus("diarization", StageExecutionState.NotStarted, now, null),
            PublishStatus = new ProcessingStageStatus("publish", StageExecutionState.NotStarted, now, null),
        };

        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        await manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);
        return manifestPath;
    }

    public async Task<MergedMeetingResult> MergeMeetingsAsync(
        IReadOnlyList<MeetingOutputRecord> records,
        string mergedTitle,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(mergedTitle))
        {
            throw new ArgumentException("A merged meeting title is required.", nameof(mergedTitle));
        }

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        var orderedRecords = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Stem))
            .GroupBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(record => record.StartedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.MaxValue : record.StartedAtUtc)
            .ThenBy(record => record.Stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedRecords.Length < 2)
        {
            throw new InvalidOperationException("Select at least two published meetings to merge them.");
        }

        var audioPaths = orderedRecords
            .Select(record => !string.IsNullOrWhiteSpace(record.AudioPath) && File.Exists(record.AudioPath)
                ? record.AudioPath
                : throw new FileNotFoundException($"Meeting '{record.Title}' is missing a published audio file.", record.AudioPath))
            .ToArray();

        var mergedPlatform = ResolveMergedPlatform(orderedRecords);
        var manifestStore = new SessionManifestStore(_pathBuilder);
        var mergedManifest = await manifestStore.CreateAsync(
            workDir,
            mergedPlatform,
            mergedTitle.Trim(),
            Array.Empty<DetectionSignal>(),
            cancellationToken);

        var startedAtUtc = orderedRecords
            .Select(record => record.StartedAtUtc)
            .Where(value => value != DateTimeOffset.MinValue)
            .DefaultIfEmpty(mergedManifest.StartedAtUtc)
            .Min();
        var sessionRoot = _pathBuilder.BuildSessionRoot(workDir, mergedManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var mergedAudioPath = Path.Combine(sessionRoot, "processing", "merged.wav");

        await new WaveChunkMerger().MergePublishedAudioFilesAsync(audioPaths, mergedAudioPath, cancellationToken);

        mergedManifest = mergedManifest with
        {
            StartedAtUtc = startedAtUtc,
            MergedAudioPath = mergedAudioPath,
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };
        await manifestStore.SaveAsync(mergedManifest, manifestPath, cancellationToken);

        return new MergedMeetingResult(
            manifestPath,
            _pathBuilder.BuildFileStem(mergedPlatform, startedAtUtc, mergedManifest.DetectedTitle),
            mergedManifest.DetectedTitle);
    }

    public async Task<SplitMeetingResult> SplitMeetingAsync(
        MeetingOutputRecord record,
        TimeSpan splitPoint,
        string workDir,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workDir))
        {
            throw new ArgumentException("A work directory is required.", nameof(workDir));
        }

        if (string.IsNullOrWhiteSpace(record.AudioPath) || !File.Exists(record.AudioPath))
        {
            throw new FileNotFoundException("A published audio file is required to split a meeting.", record.AudioPath);
        }

        using var durationReader = new AudioFileReader(record.AudioPath);
        if (splitPoint <= TimeSpan.Zero || splitPoint >= durationReader.TotalTime)
        {
            throw new InvalidOperationException("The split point must fall inside the published meeting duration.");
        }

        var baseTitle = string.IsNullOrWhiteSpace(record.Title) ? record.Stem : record.Title.Trim();
        var firstTitle = $"{baseTitle} part 1";
        var secondTitle = $"{baseTitle} part 2";
        var startedAtUtc = record.StartedAtUtc == DateTimeOffset.MinValue
            ? DateTimeOffset.UtcNow
            : record.StartedAtUtc;
        var secondStartedAtUtc = startedAtUtc + splitPoint;
        var platform = record.Platform;
        var manifestStore = new SessionManifestStore(_pathBuilder);

        var firstManifest = await manifestStore.CreateAsync(
            workDir,
            platform,
            firstTitle,
            Array.Empty<DetectionSignal>(),
            cancellationToken);
        var secondManifest = await manifestStore.CreateAsync(
            workDir,
            platform,
            secondTitle,
            Array.Empty<DetectionSignal>(),
            cancellationToken);

        var firstSessionRoot = _pathBuilder.BuildSessionRoot(workDir, firstManifest.SessionId);
        var secondSessionRoot = _pathBuilder.BuildSessionRoot(workDir, secondManifest.SessionId);
        var firstManifestPath = Path.Combine(firstSessionRoot, "manifest.json");
        var secondManifestPath = Path.Combine(secondSessionRoot, "manifest.json");
        var firstAudioPath = Path.Combine(firstSessionRoot, "processing", "merged.wav");
        var secondAudioPath = Path.Combine(secondSessionRoot, "processing", "merged.wav");

        await new WaveChunkMerger().SplitPublishedAudioFileAsync(
            record.AudioPath,
            splitPoint,
            firstAudioPath,
            secondAudioPath,
            cancellationToken);

        firstManifest = firstManifest with
        {
            DetectedTitle = firstTitle,
            StartedAtUtc = startedAtUtc,
            MergedAudioPath = firstAudioPath,
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };
        secondManifest = secondManifest with
        {
            DetectedTitle = secondTitle,
            StartedAtUtc = secondStartedAtUtc,
            MergedAudioPath = secondAudioPath,
            State = SessionState.Queued,
            EndedAtUtc = null,
            RawChunkPaths = Array.Empty<string>(),
            MicrophoneChunkPaths = Array.Empty<string>(),
        };

        await manifestStore.SaveAsync(firstManifest, firstManifestPath, cancellationToken);
        await manifestStore.SaveAsync(secondManifest, secondManifestPath, cancellationToken);

        return new SplitMeetingResult(
            firstManifestPath,
            _pathBuilder.BuildFileStem(platform, firstManifest.StartedAtUtc, firstManifest.DetectedTitle),
            firstManifest.DetectedTitle,
            secondManifestPath,
            _pathBuilder.BuildFileStem(platform, secondManifest.StartedAtUtc, secondManifest.DetectedTitle),
            secondManifest.DetectedTitle,
            splitPoint);
    }

    private static void AddArtifacts(IDictionary<string, MeetingOutputMutableRecord> pathsByStem, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(stem))
            {
                continue;
            }

            if (!pathsByStem.TryGetValue(stem, out var record))
            {
                record = new MeetingOutputMutableRecord(stem);
                pathsByStem.Add(stem, record);
            }

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                case ".mp3":
                case ".m4a":
                    record.AudioPath = path;
                    break;
                case ".md":
                    record.MarkdownPath = path;
                    break;
                case ".json":
                    record.JsonPath = path;
                    break;
                case ".ready":
                    record.ReadyMarkerPath = path;
                    break;
            }
        }
    }

    private static MeetingOutputRecord BuildRecord(MeetingOutputMutableRecord record, ManifestInfo? manifestInfo)
    {
        if (TryParseStem(record.Stem, out var stemInfo))
        {
            var storedTitle = manifestInfo?.Title ?? TryReadStoredTitle(record.JsonPath, record.MarkdownPath);
            return new MeetingOutputRecord(
                record.Stem,
                string.IsNullOrWhiteSpace(storedTitle) ? HumanizeSlug(stemInfo.TitleSlug) : storedTitle,
                stemInfo.StartedAtUtc,
                stemInfo.Platform,
                ResolveDuration(stemInfo.StartedAtUtc, record.AudioPath, manifestInfo),
                record.AudioPath,
                record.MarkdownPath,
                record.JsonPath,
                record.ReadyMarkerPath,
                manifestInfo?.ManifestPath,
                manifestInfo?.State);
        }

        var fallbackTimestamp = record.AudioPath is not null
            ? File.GetCreationTimeUtc(record.AudioPath)
            : DateTimeOffset.MinValue;
        return new MeetingOutputRecord(
            record.Stem,
            record.Stem,
            fallbackTimestamp,
            MeetingPlatform.Unknown,
            ResolveDuration(fallbackTimestamp, record.AudioPath, manifestInfo),
            record.AudioPath,
            record.MarkdownPath,
            record.JsonPath,
            record.ReadyMarkerPath,
            manifestInfo?.ManifestPath,
            manifestInfo?.State);
    }

    private IDictionary<string, ManifestInfo> LoadManifestInfoByStem(string? workDir, IEnumerable<string> discoveredStems)
    {
        var infoByStem = new Dictionary<string, ManifestInfo>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return infoByStem;
        }

        var stemSet = new HashSet<string>(discoveredStems, StringComparer.OrdinalIgnoreCase);
        if (stemSet.Count == 0)
        {
            return infoByStem;
        }

        var manifestStore = new SessionManifestStore(_pathBuilder);
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = manifestStore.LoadAsync(manifestPath).GetAwaiter().GetResult();
                var title = string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle;
                var stem = _pathBuilder.BuildFileStem(manifest.Platform, manifest.StartedAtUtc, title);
                if (!stemSet.Contains(stem))
                {
                    continue;
                }

                infoByStem[stem] = new ManifestInfo(title, manifestPath, manifest.State, manifest.StartedAtUtc, manifest.EndedAtUtc);
            }
            catch
            {
                // Ignore malformed or partially-written manifests when listing published outputs.
            }
        }

        return infoByStem;
    }

    private static bool TryParseStem(string stem, out StemInfo stemInfo)
    {
        var match = StemPattern.Match(stem);
        if (!match.Success)
        {
            stemInfo = default;
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
            stemInfo = default;
            return false;
        }

        var platform = match.Groups["platform"].Value switch
        {
            "teams" => MeetingPlatform.Teams,
            "gmeet" => MeetingPlatform.GoogleMeet,
            "manual" => MeetingPlatform.Manual,
            _ => MeetingPlatform.Unknown,
        };

        stemInfo = new StemInfo(startedAtUtc, platform, match.Groups["slug"].Value);
        return true;
    }

    private static string HumanizeSlug(string slug)
    {
        var words = slug
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
            .ToArray();
        return words.Length == 0 ? "Session" : string.Join(' ', words);
    }

    private static string? TryReadStoredTitle(string? jsonPath, string? markdownPath)
    {
        var titleFromJson = TryReadJsonTitle(jsonPath);
        if (!string.IsNullOrWhiteSpace(titleFromJson))
        {
            return titleFromJson;
        }

        return TryReadMarkdownTitle(markdownPath);
    }

    private static IReadOnlyList<string> TryReadJsonSpeakerLabels(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
            var segments = GetSegmentsArray(node);
            if (segments is null)
            {
                return Array.Empty<string>();
            }

            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var segmentNode in segments)
            {
                if (segmentNode is not JsonObject segmentObject)
                {
                    continue;
                }

                var label = GetJsonString(segmentObject, "SpeakerLabel", "speakerLabel");
                if (string.IsNullOrWhiteSpace(label) || !seen.Add(label))
                {
                    continue;
                }

                labels.Add(label);
            }

            return labels;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> TryReadMarkdownSpeakerLabels(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var content = File.ReadAllText(markdownPath);
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in MarkdownSpeakerLinePattern.Matches(content))
            {
                var label = match.Groups["label"].Value.Trim();
                if (string.IsNullOrWhiteSpace(label) || !seen.Add(label))
                {
                    continue;
                }

                labels.Add(label);
            }

            return labels;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static TimeSpan? ResolveDuration(
        DateTimeOffset startedAtUtc,
        string? audioPath,
        ManifestInfo? manifestInfo)
    {
        var durationStart = manifestInfo?.StartedAtUtc ?? startedAtUtc;
        if (manifestInfo?.EndedAtUtc is { } endedAtUtc &&
            durationStart != DateTimeOffset.MinValue &&
            endedAtUtc >= durationStart)
        {
            return endedAtUtc - durationStart;
        }

        return TryReadAudioDuration(audioPath);
    }

    private static TimeSpan? TryReadAudioDuration(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return null;
        }

        try
        {
            using var reader = new AudioFileReader(audioPath);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadJsonTitle(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(jsonPath);
            var previewLength = (int)Math.Min(stream.Length, MaxJsonTitlePreviewBytes);
            if (previewLength <= 0)
            {
                return null;
            }

            var previewBuffer = new byte[previewLength];
            var bytesRead = stream.Read(previewBuffer, 0, previewBuffer.Length);
            if (bytesRead <= 0)
            {
                return null;
            }

            var previewText = System.Text.Encoding.UTF8.GetString(previewBuffer, 0, bytesRead);
            var match = JsonTitlePattern.Match(previewText);
            if (!match.Success)
            {
                return null;
            }

            return Regex.Unescape(match.Groups["value"].Value);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadMarkdownTitle(string? markdownPath)
    {
        if (string.IsNullOrWhiteSpace(markdownPath) || !File.Exists(markdownPath))
        {
            return null;
        }

        try
        {
            var firstLine = File.ReadLines(markdownPath).FirstOrDefault();
            return firstLine is not null && firstLine.StartsWith("# ", StringComparison.Ordinal)
                ? firstLine[2..].Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<RenamePair> BuildRenamePairs(MeetingOutputRecord existing, string newStem)
    {
        var pairs = new List<RenamePair>();
        AddPairIfPresent(existing.AudioPath, newStem, pairs);
        AddPairIfPresent(existing.MarkdownPath, newStem, pairs);
        AddPairIfPresent(existing.JsonPath, newStem, pairs);
        AddPairIfPresent(existing.ReadyMarkerPath, newStem, pairs);
        return pairs;
    }

    private static void AddPairIfPresent(string? sourcePath, string newStem, ICollection<RenamePair> pairs)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Artifact path must include a directory.");
        pairs.Add(new RenamePair(sourcePath, Path.Combine(directory, $"{newStem}{Path.GetExtension(sourcePath)}")));
    }

    private static async Task UpdateMarkdownTitleAsync(string markdownPath, string newTitle, CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(markdownPath, cancellationToken);
        if (lines.Length == 0)
        {
            lines = new[] { $"# {newTitle}" };
        }
        else
        {
            lines[0] = $"# {newTitle}";
        }

        await File.WriteAllLinesAsync(markdownPath, lines, cancellationToken);
    }

    private static async Task UpdateJsonTitleAsync(string jsonPath, string newTitle, CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"Unable to parse transcript JSON '{jsonPath}'.");
        var propertyName = node.ContainsKey("title") ? "title" : "Title";
        node[propertyName] = newTitle;
        await File.WriteAllTextAsync(jsonPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        }), cancellationToken);
    }

    private static async Task UpdateJsonSpeakerLabelsAsync(
        string jsonPath,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(jsonPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"Unable to parse transcript JSON '{jsonPath}'.");
        var segments = GetSegmentsArray(node);
        if (segments is not null)
        {
            foreach (var segmentNode in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segmentNode is not JsonObject segmentObject)
                {
                    continue;
                }

                var propertyName = segmentObject.ContainsKey("speakerLabel") ? "speakerLabel" : "SpeakerLabel";
                var label = GetJsonString(segmentObject, "SpeakerLabel", "speakerLabel");
                if (string.IsNullOrWhiteSpace(label) || !speakerLabelMap.TryGetValue(label, out var updatedLabel))
                {
                    continue;
                }

                segmentObject[propertyName] = updatedLabel;
            }
        }

        await File.WriteAllTextAsync(jsonPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        }), cancellationToken);
    }

    private static async Task UpdateMarkdownSpeakerLabelsAsync(
        string markdownPath,
        IReadOnlyDictionary<string, string> speakerLabelMap,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(markdownPath))
        {
            return;
        }

        var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        var updated = MarkdownSpeakerLinePattern.Replace(
            markdown,
            match =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var label = match.Groups["label"].Value.Trim();
                if (!speakerLabelMap.TryGetValue(label, out var updatedLabel))
                {
                    return match.Value;
                }

                return $"{match.Groups["prefix"].Value}{updatedLabel}{match.Groups["suffix"].Value}";
            });

        await File.WriteAllTextAsync(markdownPath, updated, cancellationToken);
    }

    private static JsonArray? GetSegmentsArray(JsonObject? node)
    {
        if (node is null)
        {
            return null;
        }

        return node["Segments"] as JsonArray ?? node["segments"] as JsonArray;
    }

    private static string? GetJsonString(JsonObject node, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (node[propertyName] is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private async Task UpdateManifestTitleAsync(
        string? workDir,
        string existingStem,
        string newTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
        {
            return;
        }

        var manifestStore = new SessionManifestStore(_pathBuilder);
        foreach (var manifestPath in Directory.EnumerateFiles(workDir, "manifest.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MeetingSessionManifest manifest;
            try
            {
                manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken);
            }
            catch
            {
                continue;
            }

            var currentTitle = string.IsNullOrWhiteSpace(manifest.DetectedTitle) ? manifest.SessionId : manifest.DetectedTitle;
            var manifestStem = _pathBuilder.BuildFileStem(manifest.Platform, manifest.StartedAtUtc, currentTitle);
            if (!string.Equals(manifestStem, existingStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await manifestStore.SaveAsync(manifest with
            {
                DetectedTitle = newTitle,
            }, manifestPath, cancellationToken);
            return;
        }
    }

    private sealed class MeetingOutputMutableRecord
    {
        public MeetingOutputMutableRecord(string stem)
        {
            Stem = stem;
        }

        public string Stem { get; }

        public string? AudioPath { get; set; }

        public string? MarkdownPath { get; set; }

        public string? JsonPath { get; set; }

        public string? ReadyMarkerPath { get; set; }
    }

    private sealed record ManifestInfo(
        string Title,
        string ManifestPath,
        SessionState State,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc);

    private readonly record struct StemInfo(DateTimeOffset StartedAtUtc, MeetingPlatform Platform, string TitleSlug);

    private readonly record struct RenamePair(string SourcePath, string DestinationPath);

    private static MeetingPlatform ResolveMergedPlatform(IReadOnlyList<MeetingOutputRecord> records)
    {
        var distinctPlatforms = records
            .Select(record => record.Platform)
            .Where(platform => platform != MeetingPlatform.Unknown)
            .Distinct()
            .ToArray();

        return distinctPlatforms.Length == 1
            ? distinctPlatforms[0]
            : MeetingPlatform.Manual;
    }
}

public sealed record MeetingOutputRecord(
    string Stem,
    string Title,
    DateTimeOffset StartedAtUtc,
    MeetingPlatform Platform,
    TimeSpan? Duration,
    string? AudioPath,
    string? MarkdownPath,
    string? JsonPath,
    string? ReadyMarkerPath,
    string? ManifestPath,
    SessionState? ManifestState);

public sealed record MergedMeetingResult(
    string ManifestPath,
    string ExpectedStem,
    string Title);

public sealed record SplitMeetingResult(
    string FirstManifestPath,
    string FirstExpectedStem,
    string FirstTitle,
    string SecondManifestPath,
    string SecondExpectedStem,
    string SecondTitle,
    TimeSpan SplitPoint);
