namespace MeetingRecorder.Core.Services;

public enum SpeakerQualitySeverity { Unknown, Normal, Review, RepairRecommended }
public enum SpeakerQualityRoute { Name, ParagraphOverride, Merge, Rematch, Repair, SetupOrWait }

public sealed record SpeakerQualityDiagnosisInput(
    bool HasCurrentMetadata, bool IsProcessing, bool HasLabels, int ClusterCount,
    double TinyTurnRatio, double RunChurnRatio, int UnsupportedSpeakerCount,
    int DuplicateEffectiveNameCount, bool HasGenericNames, bool HasProfileSuggestions,
    bool HasNormalSplitCandidate, bool IsRepairReady);

public sealed record SpeakerQualityDiagnosis(
    SpeakerQualitySeverity Severity, SpeakerQualityRoute Route,
    string PrimaryReason, IReadOnlyList<string> SecondarySignals);

public static class SpeakerQualityDiagnosisResolver
{
    public static SpeakerQualityDiagnosis Resolve(SpeakerQualityDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.HasCurrentMetadata) return new(SpeakerQualitySeverity.Unknown, SpeakerQualityRoute.SetupOrWait, "Speaker-label quality is unavailable until current meeting metadata is loaded.", []);
        if (input.IsProcessing || !input.HasLabels) return new(SpeakerQualitySeverity.Review, SpeakerQualityRoute.SetupOrWait, "Speaker labels are still unavailable or changing. Wait before reviewing speaker quality.", []);
        var fragmented = input.ClusterCount >= 9 && (input.TinyTurnRatio >= .5d || input.RunChurnRatio >= .6d || input.UnsupportedSpeakerCount >= 3);
        if (fragmented) return new(input.IsRepairReady ? SpeakerQualitySeverity.RepairRecommended : SpeakerQualitySeverity.Review, input.IsRepairReady ? SpeakerQualityRoute.Repair : SpeakerQualityRoute.SetupOrWait, "Speaker labels look structurally fragmented; repairing labels is different from renaming people.", Signals(input));
        if (input.HasNormalSplitCandidate) return new(SpeakerQualitySeverity.Review, SpeakerQualityRoute.Merge, "Two normal speaker clusters may be duplicates. Review a merge before reprocessing labels.", Signals(input));
        if (input.HasProfileSuggestions) return new(SpeakerQualitySeverity.Review, SpeakerQualityRoute.Rematch, "Local profile suggestions can be reviewed without repairing speaker labels.", Signals(input));
        if (input.HasGenericNames) return new(SpeakerQualitySeverity.Normal, SpeakerQualityRoute.Name, "Speaker clusters look usable. Name the people shown by the generic labels.", Signals(input));
        return new(SpeakerQualitySeverity.Normal, SpeakerQualityRoute.ParagraphOverride, "Speaker labels look structurally usable. Correct a specific paragraph only when its attribution is wrong.", Signals(input));
    }

    private static IReadOnlyList<string> Signals(SpeakerQualityDiagnosisInput input)
    {
        var signals = new List<string>();
        if (input.TinyTurnRatio >= .5d) signals.Add("many tiny speaker turns");
        if (input.RunChurnRatio >= .6d) signals.Add("rapid speaker switching");
        if (input.UnsupportedSpeakerCount >= 3) signals.Add("several unsupported clusters");
        if (input.DuplicateEffectiveNameCount > 0) signals.Add("duplicate display names");
        return signals;
    }
}
