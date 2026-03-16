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
        var pathsByStem = new Dictionary<string, MeetingOutputMutableRecord>(StringComparer.OrdinalIgnoreCase);

        AddArtifacts(pathsByStem, audioOutputDir);
        AddArtifacts(pathsByStem, transcriptOutputDir);

        return pathsByStem.Values
            .Select(BuildRecord)
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
        var meetings = ListMeetings(audioOutputDir, transcriptOutputDir);
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

        return ListMeetings(audioOutputDir, transcriptOutputDir)
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

    private static MeetingOutputRecord BuildRecord(MeetingOutputMutableRecord record)
    {
        if (TryParseStem(record.Stem, out var stemInfo))
        {
            return new MeetingOutputRecord(
                record.Stem,
                HumanizeSlug(stemInfo.TitleSlug),
                stemInfo.StartedAtUtc,
                stemInfo.Platform,
                record.AudioPath,
                record.MarkdownPath,
                record.JsonPath,
                record.ReadyMarkerPath);
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
            record.ReadyMarkerPath);
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
    string? ReadyMarkerPath);
