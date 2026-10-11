namespace MeetingRecorder.Core.Services;

public enum MeetingActionId
{
    OpenDetails = 0,
    OpenTranscript = 1,
    OpenAudio = 2,
    OpenContainingFolder = 3,
    CopyTranscriptPath = 4,
    CopyAudioPath = 5,
    ReviewRecommendation = 6,
    Rename = 7,
    SuggestTitle = 8,
    RetryTranscript = 9,
    ReTranscribeWithDifferentModel = 10,
    AddSpeakerLabels = 11,
    ProcessAsap = 12,
    ClearAsap = 13,
    Split = 14,
    EditProject = 15,
    Archive = 16,
    DeletePermanently = 17,
    ApplyRecommendations = 18,
    MergeSelected = 19,
    ReTranscribeSelectedWithModel = 20,
    AddSpeakerLabelsToSelected = 21,
    ArchiveSelected = 22,
    DeleteSelectedPermanently = 23,
    RushBacklog = 24,
}

public enum MeetingActionFamily
{
    Open = 0,
    Fix = 1,
    Organize = 2,
    Processing = 3,
    Danger = 4,
}

public enum MeetingActionSelectionCardinality
{
    None = 0,
    ExactlyOne = 1,
    OneOrMore = 2,
    AtLeastTwo = 3,
}

public enum MeetingActionConfirmationPolicy
{
    None = 0,
    ExistingExplicitReview = 1,
    TypedPermanentDelete = 2,
}

public enum MeetingActionOutcomeTarget
{
    ExistingCommand = 0,
    MeetingDetail = 1,
    CleanupReview = 2,
    QueueDecision = 3,
    Archive = 4,
    PermanentDelete = 5,
}

public sealed record MeetingActionCatalogInput(
    int SelectedCount,
    bool IsBusy,
    bool HasFocusedMeeting,
    bool CanOpenAudio,
    bool CanOpenTranscript,
    bool HasPrimaryRecommendation,
    bool CanRegenerateTranscript,
    bool CanSplit,
    bool CanAddSpeakerLabels,
    bool CanChangeRushProcessing,
    bool IsMarkedAsap,
    bool HasCleanupRecommendations,
    bool CanMergeSelected,
    bool CanReTranscribeSelected,
    bool CanAddSpeakerLabelsToSelected,
    bool CanArchiveSelected,
    bool CanDeleteSelectedPermanently,
    bool HasProcessingBacklog,
    IReadOnlyDictionary<MeetingActionId, MeetingActionSelectionAvailability>? SelectionAvailability = null);

public sealed record MeetingActionSelectionAvailability(
    int EligibleCount,
    int BlockedCount,
    string? FirstBlockedReason);

public sealed record MeetingActionCatalogEntry(
    MeetingActionId Id,
    MeetingActionFamily Family,
    MeetingActionSelectionCardinality SelectionCardinality,
    MeetingActionConfirmationPolicy ConfirmationPolicy,
    MeetingActionOutcomeTarget OutcomeTarget,
    string AccessibleLabel);

public sealed record MeetingActionEligibility(
    MeetingActionCatalogEntry Entry,
    bool IsEligible,
    string? BlockReason,
    int SelectedCount,
    int EligibleCount,
    int BlockedCount);

public sealed record MeetingActionCatalogState(IReadOnlyList<MeetingActionEligibility> Actions)
{
    public MeetingActionEligibility this[MeetingActionId id] => Actions.Single(action => action.Entry.Id == id);

    public IReadOnlyList<MeetingActionEligibility> ForFamily(MeetingActionFamily family) =>
        Actions.Where(action => action.Entry.Family == family).ToArray();
}

/// <summary>
/// Canonical, metadata-only action inventory. UI surfaces may hide irrelevant entries,
/// but must not redefine an entry's family, eligibility, confirmation, or outcome.
/// </summary>
public sealed class MeetingActionCatalog
{
    private static readonly IReadOnlyList<MeetingActionCatalogEntry> Entries =
    [
        Entry(MeetingActionId.OpenDetails, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.MeetingDetail, "Open meeting details"),
        Entry(MeetingActionId.OpenTranscript, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.ExistingCommand, "Open transcript"),
        Entry(MeetingActionId.OpenAudio, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.ExistingCommand, "Open audio"),
        Entry(MeetingActionId.OpenContainingFolder, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.ExistingCommand, "Open containing folder"),
        Entry(MeetingActionId.CopyTranscriptPath, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.ExistingCommand, "Copy transcript path"),
        Entry(MeetingActionId.CopyAudioPath, MeetingActionFamily.Open, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.None, MeetingActionOutcomeTarget.ExistingCommand, "Copy audio path"),
        Entry(MeetingActionId.ReviewRecommendation, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.CleanupReview, "Review recommended action"),
        Entry(MeetingActionId.RetryTranscript, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Re-generate transcript"),
        Entry(MeetingActionId.ReTranscribeWithDifferentModel, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Re-transcribe with a different model"),
        Entry(MeetingActionId.AddSpeakerLabels, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Add speaker labels"),
        Entry(MeetingActionId.Split, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Split meeting"),
        Entry(MeetingActionId.Rename, MeetingActionFamily.Organize, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.MeetingDetail, "Rename meeting"),
        Entry(MeetingActionId.SuggestTitle, MeetingActionFamily.Organize, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Suggest meeting title"),
        Entry(MeetingActionId.EditProject, MeetingActionFamily.Organize, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Edit project"),
        Entry(MeetingActionId.Archive, MeetingActionFamily.Organize, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.Archive, "Archive meeting"),
        Entry(MeetingActionId.ProcessAsap, MeetingActionFamily.Processing, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.QueueDecision, "Process meeting as soon as possible"),
        Entry(MeetingActionId.ClearAsap, MeetingActionFamily.Processing, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.QueueDecision, "Clear meeting processing priority"),
        Entry(MeetingActionId.DeletePermanently, MeetingActionFamily.Danger, MeetingActionSelectionCardinality.ExactlyOne, MeetingActionConfirmationPolicy.TypedPermanentDelete, MeetingActionOutcomeTarget.PermanentDelete, "Delete meeting permanently"),
        Entry(MeetingActionId.ApplyRecommendations, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.CleanupReview, "Review selected recommendations"),
        Entry(MeetingActionId.MergeSelected, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.AtLeastTwo, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Merge selected meetings"),
        Entry(MeetingActionId.ReTranscribeSelectedWithModel, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Re-transcribe selected meetings with a different model"),
        Entry(MeetingActionId.AddSpeakerLabelsToSelected, MeetingActionFamily.Fix, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.ExistingCommand, "Add speaker labels to selected meetings"),
        Entry(MeetingActionId.ArchiveSelected, MeetingActionFamily.Organize, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.Archive, "Archive selected meetings"),
        Entry(MeetingActionId.DeleteSelectedPermanently, MeetingActionFamily.Danger, MeetingActionSelectionCardinality.OneOrMore, MeetingActionConfirmationPolicy.TypedPermanentDelete, MeetingActionOutcomeTarget.PermanentDelete, "Delete selected meetings permanently"),
        Entry(MeetingActionId.RushBacklog, MeetingActionFamily.Processing, MeetingActionSelectionCardinality.None, MeetingActionConfirmationPolicy.ExistingExplicitReview, MeetingActionOutcomeTarget.QueueDecision, "Rush processing backlog"),
    ];

    public IReadOnlyList<MeetingActionCatalogEntry> All => Entries;

    public MeetingActionCatalogState Resolve(MeetingActionCatalogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new MeetingActionCatalogState(Entries
            .Select(entry => ResolveEntry(entry, input))
            .ToArray());
    }

    private static MeetingActionEligibility ResolveEntry(
        MeetingActionCatalogEntry entry,
        MeetingActionCatalogInput input)
    {
        if (input.IsBusy)
        {
            return Block(entry, input, "Wait for the current meeting action to finish.");
        }

        if (!HasRequiredSelection(entry.SelectionCardinality, input.SelectedCount))
        {
            return Block(entry, input, SelectionBlockReason(entry.SelectionCardinality));
        }

        var artifactAvailable = entry.Id switch
        {
            MeetingActionId.OpenTranscript or MeetingActionId.CopyTranscriptPath => input.CanOpenTranscript,
            MeetingActionId.OpenAudio or MeetingActionId.CopyAudioPath => input.CanOpenAudio,
            MeetingActionId.OpenContainingFolder => input.CanOpenAudio || input.CanOpenTranscript,
            _ => true,
        };
        if (!artifactAvailable)
        {
            return Block(entry, input, "The required published artifact is unavailable. Refresh the meeting list and check the meeting details.");
        }

        var available = entry.Id switch
        {
            MeetingActionId.OpenDetails => input.HasFocusedMeeting,
            MeetingActionId.ReviewRecommendation => input.HasPrimaryRecommendation,
            MeetingActionId.RetryTranscript or MeetingActionId.ReTranscribeWithDifferentModel => input.CanRegenerateTranscript,
            MeetingActionId.Split => input.CanSplit,
            MeetingActionId.AddSpeakerLabels => input.CanAddSpeakerLabels,
            MeetingActionId.ProcessAsap => input.CanChangeRushProcessing && !input.IsMarkedAsap,
            MeetingActionId.ClearAsap => input.IsMarkedAsap,
            MeetingActionId.ApplyRecommendations => input.HasCleanupRecommendations,
            MeetingActionId.MergeSelected => input.CanMergeSelected,
            MeetingActionId.ReTranscribeSelectedWithModel => input.CanReTranscribeSelected,
            MeetingActionId.AddSpeakerLabelsToSelected => input.CanAddSpeakerLabelsToSelected,
            MeetingActionId.ArchiveSelected => input.CanArchiveSelected,
            MeetingActionId.DeleteSelectedPermanently => input.CanDeleteSelectedPermanently,
            MeetingActionId.RushBacklog => input.HasProcessingBacklog,
            _ => true,
        };

        if (!available)
        {
            return Block(entry, input, AvailabilityBlockReason(entry));
        }

        if (entry.SelectionCardinality is MeetingActionSelectionCardinality.OneOrMore or MeetingActionSelectionCardinality.AtLeastTwo &&
            input.SelectionAvailability is not null &&
            input.SelectionAvailability.TryGetValue(entry.Id, out var selectionAvailability))
        {
            var eligibleCount = Math.Clamp(selectionAvailability.EligibleCount, 0, input.SelectedCount);
            var blockedCount = Math.Clamp(selectionAvailability.BlockedCount, 0, input.SelectedCount - eligibleCount);
            return eligibleCount > 0
                ? new MeetingActionEligibility(
                    entry,
                    true,
                    blockedCount > 0 ? selectionAvailability.FirstBlockedReason : null,
                    input.SelectedCount,
                    eligibleCount,
                    blockedCount)
                : Block(entry, input, selectionAvailability.FirstBlockedReason ?? AvailabilityBlockReason(entry));
        }

        return new MeetingActionEligibility(entry, true, null, input.SelectedCount, input.SelectedCount, 0);
    }

    private static MeetingActionEligibility Block(
        MeetingActionCatalogEntry entry,
        MeetingActionCatalogInput input,
        string reason) =>
        new(entry, false, reason, input.SelectedCount, 0, Math.Max(1, input.SelectedCount));

    private static MeetingActionCatalogEntry Entry(
        MeetingActionId id,
        MeetingActionFamily family,
        MeetingActionSelectionCardinality selectionCardinality,
        MeetingActionConfirmationPolicy confirmationPolicy,
        MeetingActionOutcomeTarget outcomeTarget,
        string accessibleLabel) =>
        new(id, family, selectionCardinality, confirmationPolicy, outcomeTarget, accessibleLabel);

    private static bool HasRequiredSelection(MeetingActionSelectionCardinality cardinality, int selectedCount) =>
        cardinality switch
        {
            MeetingActionSelectionCardinality.None => true,
            MeetingActionSelectionCardinality.ExactlyOne => selectedCount == 1,
            MeetingActionSelectionCardinality.OneOrMore => selectedCount >= 1,
            MeetingActionSelectionCardinality.AtLeastTwo => selectedCount >= 2,
            _ => false,
        };

    private static string SelectionBlockReason(MeetingActionSelectionCardinality cardinality) =>
        cardinality switch
        {
            MeetingActionSelectionCardinality.ExactlyOne => "Select exactly one meeting first.",
            MeetingActionSelectionCardinality.OneOrMore => "Select at least one meeting first.",
            MeetingActionSelectionCardinality.AtLeastTwo => "Select at least two meetings first.",
            _ => "This action does not require a meeting selection.",
        };

    private static string AvailabilityBlockReason(MeetingActionCatalogEntry entry) =>
        entry.Id switch
        {
            MeetingActionId.ReviewRecommendation => "No actionable recommendation is available for this meeting.",
            MeetingActionId.ProcessAsap => "This meeting cannot be marked ASAP in the current queue state.",
            MeetingActionId.ClearAsap => "This meeting is not currently marked ASAP.",
            MeetingActionId.ApplyRecommendations => "No cleanup recommendations match the selected meetings.",
            MeetingActionId.AddSpeakerLabels or MeetingActionId.AddSpeakerLabelsToSelected => "Speaker labeling is not ready for the selected meeting or meetings.",
            MeetingActionId.RushBacklog => "There is no processing backlog to rush.",
            _ => $"{entry.AccessibleLabel} is not available for the selected meeting or meetings.",
        };
}
