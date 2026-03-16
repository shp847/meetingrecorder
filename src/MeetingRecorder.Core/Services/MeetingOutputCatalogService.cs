using MeetingRecorder.Core.Domain;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

public sealed class MeetingOutputCatalogService
{
    private static readonly Regex StemPattern = new(
        "^(?<date>\\d{4}-\\d{2}-\\d{2})_(?<time>\\d{6})_(?<platform>[a-z]+)_(?<slug>.+)$",
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
        var manifestInfoByStem = LoadManifestInfoByStem(workDir);

        AddArtifacts(pathsByStem, audioOutputDir);
        AddArtifacts(pathsByStem, transcriptOutputDir);

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
            record.AudioPath,
            record.MarkdownPath,
            record.JsonPath,
            record.ReadyMarkerPath,
            manifestInfo?.ManifestPath,
            manifestInfo?.State);
    }

    private IDictionary<string, ManifestInfo> LoadManifestInfoByStem(string? workDir)
    {
        var infoByStem = new Dictionary<string, ManifestInfo>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
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
                infoByStem[stem] = new ManifestInfo(title, manifestPath, manifest.State);
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

    private static string? TryReadJsonTitle(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(jsonPath))?.AsObject();
            if (node is null)
            {
                return null;
            }

            return node["title"]?.GetValue<string>() ?? node["Title"]?.GetValue<string>();
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

    private sealed record ManifestInfo(string Title, string ManifestPath, SessionState State);

    private readonly record struct StemInfo(DateTimeOffset StartedAtUtc, MeetingPlatform Platform, string TitleSlug);

    private readonly record struct RenamePair(string SourcePath, string DestinationPath);
}

public sealed record MeetingOutputRecord(
    string Stem,
    string Title,
    DateTimeOffset StartedAtUtc,
    MeetingPlatform Platform,
    string? AudioPath,
    string? MarkdownPath,
    string? JsonPath,
    string? ReadyMarkerPath,
    string? ManifestPath,
    SessionState? ManifestState);
