using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record SpeakerClusterMergeResult(
    IReadOnlyList<SpeakerTurn> SpeakerTurns,
    IReadOnlyDictionary<string, string> SpeakerIdMap,
    int OriginalSpeakerCount,
    int MergedSpeakerCount,
    string? DiagnosticMessage);

public sealed class SpeakerClusterMergeService
{
    public SpeakerClusterMergeResult MergeSimilarClusters(
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyList<SpeakerVoiceSample> voiceSamples)
    {
        var options = DiarizationCalibrationEnvironment.LoadSpeakerClusterMergeOptions();
        var speakerIds = speakerTurns
            .Select(static turn => turn.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static speakerId => speakerId, StringComparer.Ordinal)
            .ToArray();
        if (speakerIds.Length <= 1)
        {
            return BuildResult(speakerTurns, speakerIds.ToDictionary(static id => id, static id => id, StringComparer.Ordinal), speakerIds.Length);
        }

        var durations = BuildSpeakerDurations(speakerTurns);
        var totalDuration = TimeSpan.FromTicks(durations.Values.Sum(static duration => duration.Ticks));
        var parent = speakerIds.ToDictionary(static speakerId => speakerId, static speakerId => speakerId, StringComparer.Ordinal);
        var usableSamples = voiceSamples
            .Where(sample =>
                !string.IsNullOrWhiteSpace(sample.SpeakerId) &&
                sample.EmbeddingDimension > 0 &&
                sample.Embedding.Count == sample.EmbeddingDimension &&
                !string.IsNullOrWhiteSpace(sample.EmbeddingModelFileName))
            .GroupBy(static sample => sample.SpeakerId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(sample => sample.SpeechDuration).First(),
                StringComparer.Ordinal);

        MergeHighConfidenceSamplePairs(usableSamples.Values.ToArray(), parent, options);
        MergeSmallSampleClusters(usableSamples, durations, totalDuration, parent, options);
        MergeTinyUnsampledClusters(speakerTurns, usableSamples, durations, parent, options);

        var speakerIdMap = BuildCanonicalSpeakerIdMap(speakerIds, durations, parent);
        var mergedTurns = RemapSpeakerTurns(speakerTurns, speakerIdMap);
        return BuildResult(mergedTurns, speakerIdMap, speakerIds.Length);
    }

    private static void MergeHighConfidenceSamplePairs(
        IReadOnlyList<SpeakerVoiceSample> samples,
        Dictionary<string, string> parent,
        SpeakerClusterMergeOptions options)
    {
        for (var leftIndex = 0; leftIndex < samples.Count; leftIndex++)
        {
            var left = samples[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < samples.Count; rightIndex++)
            {
                var right = samples[rightIndex];
                if (!CanCompare(left, right))
                {
                    continue;
                }

                var similarity = VoiceProfileMatcher.CosineSimilarity(left.Embedding, right.Embedding);
                if (similarity >= options.HighConfidenceMergeThreshold)
                {
                    Union(parent, left.SpeakerId, right.SpeakerId);
                }
            }
        }
    }

    private static void MergeSmallSampleClusters(
        IReadOnlyDictionary<string, SpeakerVoiceSample> samplesBySpeakerId,
        IReadOnlyDictionary<string, TimeSpan> durations,
        TimeSpan totalDuration,
        Dictionary<string, string> parent,
        SpeakerClusterMergeOptions options)
    {
        var maximumSmallDuration = CalculateSmallClusterMaximumDuration(totalDuration, options);
        foreach (var sourceGroup in BuildGroups(parent))
        {
            var sourceDuration = GetGroupDuration(sourceGroup.Value, durations);
            if (sourceDuration > maximumSmallDuration)
            {
                continue;
            }

            var bestTarget = FindBestSampleTarget(sourceGroup.Key, samplesBySpeakerId, parent);
            if (bestTarget is not null && bestTarget.Value.Similarity >= options.SmallClusterMergeThreshold)
            {
                Union(parent, sourceGroup.Key, bestTarget.Value.TargetRoot);
            }
        }
    }

    private static (string TargetRoot, double Similarity)? FindBestSampleTarget(
        string sourceRoot,
        IReadOnlyDictionary<string, SpeakerVoiceSample> samplesBySpeakerId,
        Dictionary<string, string> parent)
    {
        var sourceSamples = samplesBySpeakerId.Values
            .Where(sample => string.Equals(Find(parent, sample.SpeakerId), sourceRoot, StringComparison.Ordinal))
            .ToArray();
        if (sourceSamples.Length == 0)
        {
            return null;
        }

        (string TargetRoot, double Similarity)? best = null;
        foreach (var sourceSample in sourceSamples)
        {
            foreach (var targetSample in samplesBySpeakerId.Values)
            {
                var targetRoot = Find(parent, targetSample.SpeakerId);
                if (string.Equals(sourceRoot, targetRoot, StringComparison.Ordinal) ||
                    !CanCompare(sourceSample, targetSample))
                {
                    continue;
                }

                var similarity = VoiceProfileMatcher.CosineSimilarity(sourceSample.Embedding, targetSample.Embedding);
                if (best is null || similarity > best.Value.Similarity)
                {
                    best = (targetRoot, similarity);
                }
            }
        }

        return best;
    }

    private static void MergeTinyUnsampledClusters(
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyDictionary<string, SpeakerVoiceSample> samplesBySpeakerId,
        IReadOnlyDictionary<string, TimeSpan> durations,
        Dictionary<string, string> parent,
        SpeakerClusterMergeOptions options)
    {
        if (samplesBySpeakerId.Count == 0)
        {
            return;
        }

        foreach (var group in BuildGroups(parent))
        {
            if (group.Value.Any(samplesBySpeakerId.ContainsKey) ||
                GetGroupDuration(group.Value, durations) > options.TinyClusterMaximumDuration)
            {
                continue;
            }

            var nearestRoot = FindNearestNeighborRoot(speakerTurns, group.Key, samplesBySpeakerId, parent);
            if (!string.IsNullOrWhiteSpace(nearestRoot))
            {
                Union(parent, group.Key, nearestRoot);
            }
        }
    }

    private static string? FindNearestNeighborRoot(
        IReadOnlyList<SpeakerTurn> speakerTurns,
        string sourceRoot,
        IReadOnlyDictionary<string, SpeakerVoiceSample> samplesBySpeakerId,
        Dictionary<string, string> parent)
    {
        var orderedTurns = speakerTurns
            .Where(static turn => !string.IsNullOrWhiteSpace(turn.SpeakerId))
            .OrderBy(static turn => turn.Start)
            .ThenBy(static turn => turn.End)
            .ToArray();
        var bestGap = TimeSpan.MaxValue;
        string? bestRoot = null;
        for (var index = 0; index < orderedTurns.Length; index++)
        {
            var turn = orderedTurns[index];
            if (!string.Equals(Find(parent, turn.SpeakerId), sourceRoot, StringComparison.Ordinal))
            {
                continue;
            }

            ConsiderNeighbor(index - 1, turn.Start, isPrevious: true);
            ConsiderNeighbor(index + 1, turn.End, isPrevious: false);

            void ConsiderNeighbor(int neighborIndex, TimeSpan boundary, bool isPrevious)
            {
                if (neighborIndex < 0 || neighborIndex >= orderedTurns.Length)
                {
                    return;
                }

                var neighbor = orderedTurns[neighborIndex];
                var neighborRoot = Find(parent, neighbor.SpeakerId);
                if (string.Equals(neighborRoot, sourceRoot, StringComparison.Ordinal))
                {
                    return;
                }

                if (!RootHasSample(neighborRoot, samplesBySpeakerId, parent))
                {
                    return;
                }

                var gap = isPrevious
                    ? boundary - neighbor.End
                    : neighbor.Start - boundary;
                if (gap < TimeSpan.Zero)
                {
                    gap = TimeSpan.Zero;
                }

                if (gap < bestGap)
                {
                    bestGap = gap;
                    bestRoot = neighborRoot;
                }
            }
        }

        return bestRoot;
    }

    private static bool RootHasSample(
        string root,
        IReadOnlyDictionary<string, SpeakerVoiceSample> samplesBySpeakerId,
        Dictionary<string, string> parent)
    {
        return samplesBySpeakerId.Keys.Any(speakerId =>
            string.Equals(Find(parent, speakerId), root, StringComparison.Ordinal));
    }

    private static Dictionary<string, string[]> BuildGroups(Dictionary<string, string> parent)
    {
        return parent.Keys
            .GroupBy(speakerId => Find(parent, speakerId), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static speakerId => speakerId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static Dictionary<string, TimeSpan> BuildSpeakerDurations(IReadOnlyList<SpeakerTurn> speakerTurns)
    {
        return speakerTurns
            .Where(static turn => turn.End > turn.Start && !string.IsNullOrWhiteSpace(turn.SpeakerId))
            .GroupBy(static turn => turn.SpeakerId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => TimeSpan.FromTicks(group.Sum(static turn => (turn.End - turn.Start).Ticks)),
                StringComparer.Ordinal);
    }

    private static TimeSpan CalculateSmallClusterMaximumDuration(
        TimeSpan totalDuration,
        SpeakerClusterMergeOptions options)
    {
        var proportionalDuration = TimeSpan.FromTicks((long)(totalDuration.Ticks * options.SmallClusterDurationShare));
        return proportionalDuration > options.SmallClusterMaximumDuration
            ? options.SmallClusterMaximumDuration
            : proportionalDuration;
    }

    private static TimeSpan GetGroupDuration(
        IReadOnlyList<string> speakerIds,
        IReadOnlyDictionary<string, TimeSpan> durations)
    {
        return TimeSpan.FromTicks(speakerIds.Sum(speakerId =>
            durations.TryGetValue(speakerId, out var duration) ? duration.Ticks : 0L));
    }

    private static IReadOnlyDictionary<string, string> BuildCanonicalSpeakerIdMap(
        IReadOnlyList<string> speakerIds,
        IReadOnlyDictionary<string, TimeSpan> durations,
        Dictionary<string, string> parent)
    {
        var canonicalByRoot = speakerIds
            .GroupBy(speakerId => Find(parent, speakerId), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group
                    .OrderByDescending(speakerId => durations.TryGetValue(speakerId, out var duration) ? duration : TimeSpan.Zero)
                    .ThenBy(static speakerId => speakerId, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);

        return speakerIds.ToDictionary(
            speakerId => speakerId,
            speakerId => canonicalByRoot[Find(parent, speakerId)],
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<SpeakerTurn> RemapSpeakerTurns(
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyDictionary<string, string> speakerIdMap)
    {
        var remappedTurns = speakerTurns
            .Where(static turn => !string.IsNullOrWhiteSpace(turn.SpeakerId))
            .Select(turn => speakerIdMap.TryGetValue(turn.SpeakerId, out var speakerId)
                ? turn with { SpeakerId = speakerId }
                : turn)
            .OrderBy(static turn => turn.Start)
            .ThenBy(static turn => turn.End)
            .ToArray();

        var mergedTurns = new List<SpeakerTurn>(remappedTurns.Length);
        foreach (var turn in remappedTurns)
        {
            if (mergedTurns.Count == 0)
            {
                mergedTurns.Add(turn);
                continue;
            }

            var previous = mergedTurns[^1];
            if (string.Equals(previous.SpeakerId, turn.SpeakerId, StringComparison.Ordinal) &&
                turn.Start <= previous.End)
            {
                mergedTurns[^1] = previous with
                {
                    End = turn.End > previous.End ? turn.End : previous.End,
                };
                continue;
            }

            mergedTurns.Add(turn);
        }

        return mergedTurns;
    }

    private static SpeakerClusterMergeResult BuildResult(
        IReadOnlyList<SpeakerTurn> speakerTurns,
        IReadOnlyDictionary<string, string> speakerIdMap,
        int originalSpeakerCount)
    {
        var mergedSpeakerCount = speakerTurns
            .Select(static turn => turn.SpeakerId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var diagnosticMessage = mergedSpeakerCount < originalSpeakerCount
            ? $"Merged similar speaker clusters from {originalSpeakerCount} to {mergedSpeakerCount} speakers."
            : null;
        return new SpeakerClusterMergeResult(
            speakerTurns,
            speakerIdMap,
            originalSpeakerCount,
            mergedSpeakerCount,
            diagnosticMessage);
    }

    private static bool CanCompare(SpeakerVoiceSample left, SpeakerVoiceSample right)
    {
        return left.EmbeddingDimension == right.EmbeddingDimension &&
            string.Equals(left.EmbeddingModelFileName, right.EmbeddingModelFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Find(Dictionary<string, string> parent, string speakerId)
    {
        var current = speakerId;
        while (!string.Equals(parent[current], current, StringComparison.Ordinal))
        {
            current = parent[current];
        }

        var root = current;
        current = speakerId;
        while (!string.Equals(parent[current], current, StringComparison.Ordinal))
        {
            var next = parent[current];
            parent[current] = root;
            current = next;
        }

        return root;
    }

    private static void Union(Dictionary<string, string> parent, string leftSpeakerId, string rightSpeakerId)
    {
        var leftRoot = Find(parent, leftSpeakerId);
        var rightRoot = Find(parent, rightSpeakerId);
        if (!string.Equals(leftRoot, rightRoot, StringComparison.Ordinal))
        {
            parent[rightRoot] = leftRoot;
        }
    }
}
