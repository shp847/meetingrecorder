using System.Text.Json;

namespace MeetingRecorder.Core.Services;

public sealed record OngoingMeetingHealReceipt(
    int SchemaVersion,
    string PredecessorSessionId,
    string SuccessorSessionId,
    DateTimeOffset HealedAtUtc,
    string ReasonCode,
    string ArchiveDirectory,
    string? SurvivingStem = null,
    string? PredecessorStem = null,
    string? SuccessorStem = null,
    DateTimeOffset? ReversedAtUtc = null)
{
    public const int CurrentSchemaVersion = 1;
}

public enum OngoingMeetingHealReversalStatus { Reversed, AlreadyReversed, Rejected }

public sealed record OngoingMeetingHealReversalRequest(string ReceiptPath, string AudioOutputDirectory, string TranscriptOutputDirectory);

public sealed class OngoingMeetingHealReversalService
{
    public async Task<OngoingMeetingHealReversalStatus> ReverseAsync(OngoingMeetingHealReversalRequest request, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var store = new OngoingMeetingHealReceiptStore(request.ReceiptPath);
        var receipt = await store.TryLoadAsync(cancellationToken);
        if (receipt is null || string.IsNullOrWhiteSpace(receipt.SurvivingStem) ||
            string.IsNullOrWhiteSpace(receipt.PredecessorStem) || string.IsNullOrWhiteSpace(receipt.SuccessorStem))
            return OngoingMeetingHealReversalStatus.Rejected;
        if (receipt.ReversedAtUtc is not null) return OngoingMeetingHealReversalStatus.AlreadyReversed;

        var sourceDirectories = new[] { receipt.PredecessorStem, receipt.SuccessorStem }
            .Select(stem => Path.Combine(receipt.ArchiveDirectory, "merge-split-pairs", stem)).ToArray();
        if (sourceDirectories.Any(directory => !Directory.Exists(directory))) return OngoingMeetingHealReversalStatus.Rejected;
        var files = sourceDirectories.SelectMany(Directory.EnumerateFiles).ToArray();
        if (files.Length == 0) return OngoingMeetingHealReversalStatus.Rejected;
        var moves = files.Select(file => new { Source = file, Destination = ResolveDestination(file, request) }).ToArray();
        var replaceableMergedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(request.AudioOutputDirectory, $"{receipt.SurvivingStem}.wav"),
            Path.Combine(request.TranscriptOutputDirectory, $"{receipt.SurvivingStem}.md"),
        };
        if (moves.GroupBy(move => move.Destination, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1) ||
            moves.Any(move => File.Exists(move.Destination) && !replaceableMergedPaths.Contains(move.Destination))) return OngoingMeetingHealReversalStatus.Rejected;

        DeleteMergedArtifact(Path.Combine(request.AudioOutputDirectory, $"{receipt.SurvivingStem}.wav"));
        DeleteMergedArtifact(Path.Combine(request.TranscriptOutputDirectory, $"{receipt.SurvivingStem}.md"));
        foreach (var move in moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
            File.Move(move.Source, move.Destination);
        }

        await store.SaveAsync(receipt with { ReversedAtUtc = nowUtc }, CancellationToken.None);
        return OngoingMeetingHealReversalStatus.Reversed;
    }

    private static string ResolveDestination(string source, OngoingMeetingHealReversalRequest request) =>
        Path.GetExtension(source).ToLowerInvariant() switch
        {
            ".wav" => Path.Combine(request.AudioOutputDirectory, Path.GetFileName(source)),
            ".md" => Path.Combine(request.TranscriptOutputDirectory, Path.GetFileName(source)),
            ".json" or ".ready" => Path.Combine(ArtifactPathBuilder.BuildTranscriptSidecarRoot(request.TranscriptOutputDirectory), Path.GetFileName(source)),
            _ => throw new InvalidOperationException("Unexpected archived heal artifact."),
        };

    private static void DeleteMergedArtifact(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class OngoingMeetingHealReceiptStore
{
    private readonly string _path;
    public OngoingMeetingHealReceiptStore(string path) => _path = Path.GetFullPath(path);

    public async Task SaveAsync(OngoingMeetingHealReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SchemaVersion != OngoingMeetingHealReceipt.CurrentSchemaVersion || string.IsNullOrWhiteSpace(receipt.ReasonCode))
            throw new ArgumentException("Invalid heal receipt.", nameof(receipt));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(receipt), cancellationToken);
        File.Move(temp, _path, true);
    }

    public async Task<OngoingMeetingHealReceipt?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<OngoingMeetingHealReceipt>(await File.ReadAllTextAsync(_path, cancellationToken));
            return receipt?.SchemaVersion == OngoingMeetingHealReceipt.CurrentSchemaVersion ? receipt : null;
        }
        catch (JsonException) { return null; }
    }
}
