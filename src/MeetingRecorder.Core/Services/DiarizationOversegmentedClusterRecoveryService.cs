using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record DiarizationOversegmentedClusterRecoveryResult(
    bool Recovered,
    DiarizationClusterSelection Selection,
    IReadOnlyList<SpeakerVoiceSample> VoiceSamples,
    SpeakerClusterMergeResult? MergeResult,
    string? DiagnosticMessage);

public sealed class DiarizationOversegmentedClusterRecoveryService
{
    private readonly DiarizationClusterSelectionService _clusterSelectionService;
    private readonly SpeakerClusterMergeService _speakerClusterMergeService;

    public DiarizationOversegmentedClusterRecoveryService()
        : this(new DiarizationClusterSelectionService(), new SpeakerClusterMergeService())
    {
    }

    public DiarizationOversegmentedClusterRecoveryService(
        DiarizationClusterSelectionService clusterSelectionService,
        SpeakerClusterMergeService speakerClusterMergeService)
    {
        _clusterSelectionService = clusterSelectionService;
        _speakerClusterMergeService = speakerClusterMergeService;
    }

    public DiarizationOversegmentedClusterRecoveryResult TryRecover(
        DiarizationClusterSelection selection,
        IReadOnlyList<SpeakerVoiceSample> voiceSamples)
    {
        if (!ShouldAttemptRecovery(selection))
        {
            return new DiarizationOversegmentedClusterRecoveryResult(
                false,
                selection,
                voiceSamples,
                null,
                null);
        }

        if (voiceSamples.Count == 0)
        {
            return new DiarizationOversegmentedClusterRecoveryResult(
                false,
                selection,
                voiceSamples,
                null,
                "Over-segmented speaker clustering could not be merged because no speaker voice samples were available.");
        }

        var mergeResult = _speakerClusterMergeService.MergeSimilarClusters(
            selection.Candidate.SpeakerTurns,
            voiceSamples);
        var mergedSelection = _clusterSelectionService.SelectBestCandidate(
            [new DiarizationClusterCandidate(selection.Candidate.Threshold, mergeResult.SpeakerTurns)]);
        var remappedVoiceSamples = RemapVoiceSamples(
            voiceSamples,
            mergeResult.SpeakerIdMap,
            mergedSelection.Candidate.SpeakerTurns);

        if (!mergedSelection.IsAutomaticSpeakerCountSupported)
        {
            return new DiarizationOversegmentedClusterRecoveryResult(
                false,
                selection,
                remappedVoiceSamples,
                mergeResult,
                $"Over-segmented speaker clustering merge left {mergedSelection.SupportedSpeakerCount} supported speakers.");
        }

        return new DiarizationOversegmentedClusterRecoveryResult(
            true,
            mergedSelection,
            remappedVoiceSamples,
            mergeResult,
            CombineDiagnosticMessages(
                mergeResult.DiagnosticMessage,
                mergedSelection.DiagnosticMessage));
    }

    public static bool ShouldAttemptRecovery(DiarizationClusterSelection selection)
    {
        return !selection.IsAutomaticSpeakerCountSupported &&
            selection.SupportedSpeakerCount > DiarizationClusterSelectionService.MaximumAutomaticSpeakerCount;
    }

    private static IReadOnlyList<SpeakerVoiceSample> RemapVoiceSamples(
        IReadOnlyList<SpeakerVoiceSample> voiceSamples,
        IReadOnlyDictionary<string, string> speakerIdMap,
        IReadOnlyList<SpeakerTurn> selectedSpeakerTurns)
    {
        if (voiceSamples.Count == 0)
        {
            return voiceSamples;
        }

        var selectedSpeakerIds = selectedSpeakerTurns
            .Select(static turn => turn.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .ToHashSet(StringComparer.Ordinal);
        return voiceSamples
            .Select(sample => speakerIdMap.TryGetValue(sample.SpeakerId, out var speakerId)
                ? sample with { SpeakerId = speakerId }
                : sample)
            .Where(sample => selectedSpeakerIds.Count == 0 || selectedSpeakerIds.Contains(sample.SpeakerId))
            .GroupBy(static sample => sample.SpeakerId, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static sample => sample.SpeechDuration)
                .First())
            .OrderBy(static sample => sample.SpeakerId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? CombineDiagnosticMessages(params string?[] messages)
    {
        var combinedMessages = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => message!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return combinedMessages.Length == 0
            ? null
            : string.Join(" ", combinedMessages);
    }
}
