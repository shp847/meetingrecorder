using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

/// <summary>
/// Finds at most one adjacent, current published pair for the opt-in healer.
/// It never examines archive contents or performs historical-chain repair.
/// </summary>
internal static class OngoingMeetingHealPass
{
    private static readonly TimeSpan MaximumCurrentWorkAge = TimeSpan.FromHours(24);

    public static async Task<OngoingMeetingHealTransactionResult?> RunOnceAsync(
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        string workDirectory,
        string audioOutputDirectory,
        string transcriptOutputDirectory,
        DateTimeOffset nowUtc,
        bool reviewOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory) || !Directory.Exists(workDirectory))
        {
            return null;
        }

        var manifests = new List<(string Path, MeetingSessionManifest Manifest)>();
        foreach (var path in Directory.EnumerateFiles(workDirectory, "manifest.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var manifest = await manifestStore.LoadAsync(path, cancellationToken);
                if (manifest.State == SessionState.Published &&
                    manifest.EndedAtUtc is { } endedAt &&
                    endedAt >= nowUtc - MaximumCurrentWorkAge &&
                    endedAt <= nowUtc + OngoingMeetingHealEligibilityResolver.MaximumAdjacency)
                {
                    manifests.Add((path, manifest));
                }
            }
            catch (IOException) { }
            catch (System.Text.Json.JsonException) { }
        }

        if (manifests.Count < 2)
        {
            return null;
        }

        var catalog = new MeetingOutputCatalogService(pathBuilder);
        var outputByManifest = catalog.ListMeetings(audioOutputDirectory, transcriptOutputDirectory, workDirectory)
            .Where(output => !string.IsNullOrWhiteSpace(output.ManifestPath))
            .ToDictionary(output => Path.GetFullPath(output.ManifestPath!), StringComparer.OrdinalIgnoreCase);
        var ordered = manifests.OrderBy(entry => entry.Manifest.StartedAtUtc)
            .ThenBy(entry => entry.Manifest.SessionId, StringComparer.Ordinal)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            var predecessor = ordered[index - 1];
            var successor = ordered[index];
            if (!outputByManifest.TryGetValue(Path.GetFullPath(predecessor.Path), out var predecessorOutput) ||
                !outputByManifest.TryGetValue(Path.GetFullPath(successor.Path), out var successorOutput))
            {
                continue;
            }

            var candidate = new OngoingMeetingHealCandidate(
                predecessor.Manifest,
                successor.Manifest,
                predecessor.Manifest.IdentitySnapshot,
                successor.Manifest.IdentitySnapshot,
                HasActiveLease: false,
                HasUserMetadataConflict: HasMetadataConflict(predecessor.Manifest, successor.Manifest),
                HasCompletePublishedArtifacts: HasCompletePublishedArtifacts(predecessorOutput) && HasCompletePublishedArtifacts(successorOutput),
                ArtifactOrderIsMonotonic: predecessorOutput.StartedAtUtc <= successorOutput.StartedAtUtc,
                HasLineageCycle: false);
            if (reviewOnly && new OngoingMeetingHealEligibilityResolver().Evaluate(candidate, nowUtc) == OngoingMeetingHealEligibility.Eligible)
            {
                return new OngoingMeetingHealTransactionResult(
                    OngoingMeetingHealTransactionStatus.ReviewOnly,
                    predecessorOutput.Stem);
            }
            var receiptPath = Path.Combine(workDirectory, ".ongoing-heal", $"{predecessor.Manifest.SessionId}-{successor.Manifest.SessionId}.json");
            var archiveDirectory = Path.Combine(
                MeetingCleanupExecutionService.GetArchiveRoot(audioOutputDirectory),
                "ongoing-heal",
                $"{predecessor.Manifest.SessionId}-{successor.Manifest.SessionId}");
            var transaction = new OngoingMeetingHealTransaction(
                new MeetingCleanupExecutionService(pathBuilder, catalog));
            var result = await transaction.ExecuteAsync(
                new(candidate, predecessorOutput, successorOutput, audioOutputDirectory, transcriptOutputDirectory, archiveDirectory, receiptPath),
                nowUtc,
                cancellationToken);
            if (result.Status is OngoingMeetingHealTransactionStatus.Healed or OngoingMeetingHealTransactionStatus.AlreadyHealed or OngoingMeetingHealTransactionStatus.Busy)
            {
                return result;
            }
        }

        return null;
    }

    private static bool HasCompletePublishedArtifacts(MeetingOutputRecord output) =>
        !string.IsNullOrWhiteSpace(output.AudioPath) && File.Exists(output.AudioPath) &&
        !string.IsNullOrWhiteSpace(output.MarkdownPath) && File.Exists(output.MarkdownPath) &&
        !string.IsNullOrWhiteSpace(output.JsonPath) && File.Exists(output.JsonPath) &&
        !string.IsNullOrWhiteSpace(output.ReadyMarkerPath) && File.Exists(output.ReadyMarkerPath);

    private static bool HasMetadataConflict(MeetingSessionManifest predecessor, MeetingSessionManifest successor) =>
        predecessor.Summary is not null ||
        successor.Summary is not null ||
        predecessor.ProcessingMetadata?.HasSpeakerLabels == true ||
        successor.ProcessingMetadata?.HasSpeakerLabels == true ||
        !TextMatches(predecessor.ProjectName, successor.ProjectName) ||
        !ValuesMatch(predecessor.KeyAttendees, successor.KeyAttendees) ||
        !ValuesMatch(predecessor.Attendees.Select(attendee => attendee.Name), successor.Attendees.Select(attendee => attendee.Name));

    private static bool TextMatches(string? first, string? second) =>
        string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ValuesMatch(IEnumerable<string> first, IEnumerable<string> second) =>
        first.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(second.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
}
