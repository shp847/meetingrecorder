using MeetingRecorder.Core.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using MeetingRecorder.Core.Domain;
using NAudio.Wave;
using System.Windows.Threading;
using System.Globalization;

namespace MeetingRecorder.App;

internal sealed class MeetingDetailTextEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

internal sealed class MeetingDetailSpeakerLabelsEventArgs(IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> rows, Action? onSaved = null) : EventArgs
{
    public IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> Rows { get; } = rows;
    public Action? OnSaved { get; } = onSaved;
}

internal sealed class MeetingDetailSpeakerMergeEventArgs(
    IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> rows,
    string? canonicalSpeakerId) : EventArgs
{
    public IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> Rows { get; } = rows;
    public string? CanonicalSpeakerId { get; } = canonicalSpeakerId;
}

internal sealed class MeetingDetailSpeakerEvidenceCueEventArgs(
    MeetingDetailSpeakerLabelEditorRow row,
    SpeakerEvidenceCue cue) : EventArgs
{
    public MeetingDetailSpeakerLabelEditorRow Row { get; } = row;
    public SpeakerEvidenceCue Cue { get; } = cue;
}

internal sealed class MeetingDetailSegmentAttributionEventArgs(
    MeetingTranscriptSegmentRow segment, SegmentAttributionOverrideChoice? choice = null, Action? onSaved = null) : EventArgs
{
    public MeetingTranscriptSegmentRow Segment { get; } = segment;
    public SegmentAttributionOverrideChoice? Choice { get; } = choice;
    public Action? OnSaved { get; } = onSaved;
}

internal sealed class MeetingDetailPassageSplitEventArgs(
    MeetingTranscriptSegmentRow segment, int offset, bool undo, Action? onSaved = null) : EventArgs
{
    public MeetingTranscriptSegmentRow Segment { get; } = segment;
    public int Offset { get; } = offset;
    public bool Undo { get; } = undo;
    public Action? OnSaved { get; } = onSaved;
}

internal sealed class MeetingDetailSelectionCorrectionEventArgs(
    IReadOnlyList<TranscriptSelectionSlice> slices, SegmentAttributionOverrideChoice? choice,
    string snapshot, string operationId, bool undo, Action? onSaved = null) : EventArgs
{
    public IReadOnlyList<TranscriptSelectionSlice> Slices { get; } = slices;
    public SegmentAttributionOverrideChoice? Choice { get; } = choice;
    public string Snapshot { get; } = snapshot;
    public string OperationId { get; } = operationId;
    public bool Undo { get; } = undo;
    public Action? OnSaved { get; } = onSaved;
}

internal sealed record MeetingDetailSpeakerMergeChoice(string SpeakerId, string Label)
{
    public override string ToString() => Label;
}

internal sealed class MeetingDetailSpeakerLabelEditorRow(
    string originalLabel,
    string provenance = "",
    string? suggestedDisplayName = null,
    string? speakerId = null,
    string? profileId = null,
    bool hasProfileAttribution = false,
    string artifactRevision = "",
    SpeakerNameSource expectedNameSource = SpeakerNameSource.None,
    SpeakerReviewRow? reviewSnapshot = null,
    IReadOnlyList<string>? nameChoices = null,
    SpeakerEvidenceSnapshot? evidenceSnapshot = null,
    int affectedTurnCount = 0)
{
    public string OriginalLabel { get; } = originalLabel;

    public string EditedLabel { get; set; } = originalLabel;

    public string Provenance { get; } = provenance;

    public string? SuggestedDisplayName { get; } = suggestedDisplayName;

    public string? SpeakerId { get; } = speakerId;

    public string? ProfileId { get; } = profileId;

    public bool HasProfileAttribution { get; } = hasProfileAttribution;

    public string ArtifactRevision { get; } = artifactRevision;

    public SpeakerNameSource ExpectedNameSource { get; } = expectedNameSource;

    public IReadOnlyList<string> NameChoices { get; } = nameChoices ?? Array.Empty<string>();

    public SpeakerEvidenceSnapshot? EvidenceSnapshot { get; } = evidenceSnapshot;

    public IReadOnlyList<SpeakerEvidenceCue> EvidenceCues => EvidenceSnapshot?.Cues ?? Array.Empty<SpeakerEvidenceCue>();

    public bool HasEvidenceCues => EvidenceCues.Count > 0;

    public bool IsEvidenceVisible { get; set; }

    public string PlaybackText { get; set; } = "Play clip";

    public string PlaybackProgressText { get; set; } = string.Empty;

    public bool IsSelectedForMerge { get; set; }

    public int AffectedTurnCount { get; } = Math.Max(0, affectedTurnCount);

    public string EvidenceToggleText => HasEvidenceCues
        ? IsEvidenceVisible ? "Hide evidence" : $"View evidence ({EvidenceCues.Count})"
        : "Evidence unavailable";

    public string EvidenceSummary => EvidenceSnapshot?.Summary ?? "Local transcript evidence is unavailable.";

    public string AnonymousLabel => reviewSnapshot?.AnonymousLabel ?? OriginalLabel;

    public string SourceExplanation => reviewSnapshot?.Explanation ?? Provenance;

    public string ReviewStatus => reviewSnapshot is null
        ? string.Empty
        : $"Evidence: {reviewSnapshot.EvidenceAvailability}. Learning: {reviewSnapshot.LearningEligibility}.";

    public SpeakerReviewConfidence ReviewConfidence => reviewSnapshot?.Confidence ?? SpeakerReviewConfidence.Unavailable;

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(SuggestedDisplayName);

    public bool IsSuggestionRejected { get; private set; }

    public string SuggestionText => HasSuggestion
        ? SuggestedDisplayName!
        : string.Empty;

    public void AcceptSuggestion()
    {
        if (!HasSuggestion)
        {
            return;
        }

        EditedLabel = SuggestedDisplayName!;
        IsSuggestionRejected = false;
    }

    public void RejectSuggestion()
    {
        if (!HasSuggestion)
        {
            return;
        }

        EditedLabel = OriginalLabel;
        IsSuggestionRejected = true;
    }
}

public partial class MeetingDetailWindow : Window
{
    private MeetingTranscriptSegmentRow[] _allTranscriptSegments = Array.Empty<MeetingTranscriptSegmentRow>();
    private MeetingTranscriptSegmentRow[] _allTranscriptBlocks = Array.Empty<MeetingTranscriptSegmentRow>();
    private string _transcriptSnapshot = string.Empty;
    private string? _latestSelectionCorrectionId;
    private bool _isMaintenanceBusy;
    private bool _restoringTranscriptSelection;
    private ToggleButton? _activeInlineSpeakerToggle;
    private ToggleButton? _activePassageToggle;
    private ToggleButton? _activeSplitToggle;
    private bool _canConfigureSummaries;
    private bool _canGenerateSummary;
    private bool _canRetrySummary;
    private bool _canRetryTranscript;
    private bool _canReTranscribe;
    private bool _canAddSpeakerLabels;
    private bool _canProcessAsap;
    private bool _canSplit;
    private bool _canApplySpeakerNames;
    private bool _canMergeSpeakerClusters;
    private string? _latestSpeakerMergeOperationId;
    private bool _canUndoSpeakerNameRecognition;
    private bool _canArchive;
    private bool _canDelete;
    private MeetingDetailSnapshotRevision? _appliedTaskCenterRevision;
    private string _appliedTitleDraft = string.Empty;
    private string _appliedProjectDraft = string.Empty;
    private WaveOutEvent? _speakerReviewOutput;
    private AudioFileReader? _speakerReviewReader;
    private IDisposable? _speakerReviewLease;
    private MeetingDetailSpeakerLabelEditorRow? _playingSpeakerRow;
    private readonly DispatcherTimer _speakerReviewProgressTimer;

    public MeetingDetailWindow()
    {
        InitializeComponent();
        _speakerReviewProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _speakerReviewProgressTimer.Tick += (_, _) => UpdateSpeakerReviewProgress();
    }

    public IReadOnlyList<string> InlineSpeakerNameChoices { get; private set; } = Array.Empty<string>();

    internal event EventHandler? OpenTranscriptRequested;

    internal event EventHandler? OpenAudioRequested;

    internal event EventHandler? OpenFolderRequested;

    internal event EventHandler<MeetingDetailTextEventArgs>? RenameRequested;

    internal event EventHandler? SuggestTitleRequested;

    internal event EventHandler<MeetingDetailTextEventArgs>? ApplyProjectRequested;

    internal event EventHandler? ClearProjectRequested;

    internal event EventHandler? RetryTranscriptRequested;

    internal event EventHandler? ReTranscribeRequested;

    internal event EventHandler? ConfigureSummariesRequested;

    internal event EventHandler? GenerateSummaryRequested;

    internal event EventHandler? RetrySummaryRequested;

    internal event EventHandler? AddSpeakerLabelsRequested;

    internal event EventHandler? ProcessAsapRequested;

    internal event EventHandler<MeetingDetailTextEventArgs>? SplitRequested;

    internal event EventHandler<MeetingDetailSpeakerLabelsEventArgs>? ApplySpeakerNamesRequested;

    internal event EventHandler? UndoInlineSpeakerNameRequested;

    internal event EventHandler<MeetingDetailSpeakerMergeEventArgs>? MergeSpeakerClustersRequested;

    internal event EventHandler<MeetingDetailSpeakerEvidenceCueEventArgs>? PlaySpeakerEvidenceRequested;

    internal event EventHandler<MeetingDetailSegmentAttributionEventArgs>? ApplyParagraphOverrideRequested;

    internal event EventHandler<MeetingDetailSegmentAttributionEventArgs>? RevertParagraphOverrideRequested;

    internal event EventHandler<MeetingDetailPassageSplitEventArgs>? ChangePassageSplitRequested;
    internal event EventHandler<MeetingDetailSelectionCorrectionEventArgs>? ChangeSelectionCorrectionRequested;

    internal event EventHandler<MeetingDetailTextEventArgs>? UndoSpeakerMergeRequested;

    internal event EventHandler? RefreshSpeakerNamesRequested;

    internal event EventHandler? UndoSpeakerNameRecognitionRequested;

    internal event EventHandler? ArchiveRequested;

    internal event EventHandler? DeleteRequested;

    internal void ApplyState(
        MeetingDetailWindowState state,
        IReadOnlyList<string> projectOptions,
        IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> speakerLabelRows)
    {
        StopSpeakerReviewPlayback();
        Title = $"Meeting Details - {state.Title}";
        MeetingTitleTextBlock.Text = state.Title;
        MeetingSubtitleTextBlock.Text = state.Subtitle;
        ApplySummaryState(state.Summary);
        ProjectTextBlock.Text = state.ProjectName;
        StatusTextBlock.Text = state.Status;
        TranscriptModelTextBlock.Text = state.TranscriptionModelFileName;
        SpeakerStateTextBlock.Text = state.SpeakerLabelState;
        DetectedAudioSourceTextBlock.Text = state.DetectedAudioSourceSummary;
        CaptureDiagnosticsTextBlock.Text = state.CaptureDiagnosticsSummary;
        RecommendationItemsControl.ItemsSource = state.RecommendationBadges;
        NoRecommendationsTextBlock.Text = state.RecommendationBadges.Count == 0
            ? "No recommendations are active for this meeting."
            : string.Empty;
        AttendeeItemsControl.ItemsSource = state.AttendeeNames;
        NoAttendeesTextBlock.Text = state.AttendeeNames.Count == 0
            ? "No attendees captured yet."
            : string.Empty;

        OpenTranscriptButton.IsEnabled = state.CanOpenTranscript;
        OpenAudioButton.IsEnabled = state.CanOpenAudio;
        OpenFolderButton.IsEnabled = state.CanOpenTranscript || state.CanOpenAudio;
        _canRetryTranscript = state.CanRegenerateTranscript;
        _canReTranscribe = state.CanReTranscribeWithDifferentModel;
        _canAddSpeakerLabels = state.CanAddSpeakerLabels;
        _canProcessAsap = state.CanProcessAsap || state.CanClearAsap;
        _canSplit = state.CanSplit;
        _canArchive = state.CanArchive;
        _canDelete = state.CanDeletePermanently;
        AddSpeakerLabelsButton.Content = state.SpeakerLabelActionLabel;
        ProcessAsapButton.Content = state.CanClearAsap ? "Clear ASAP" : "Process ASAP";

        TitleDraftTextBox.Text = state.Title;
        ProjectComboBox.ItemsSource = projectOptions;
        ProjectComboBox.Text = state.ProjectName == "None" ? string.Empty : state.ProjectName;
        _appliedTitleDraft = TitleDraftTextBox.Text;
        _appliedProjectDraft = ProjectComboBox.Text;
        SpeakerLabelsDataGrid.ItemsSource = speakerLabelRows;
        SpeakerLabelsDataGrid.Visibility = speakerLabelRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpeakerNameActionsPanel.Visibility = speakerLabelRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpeakerMergePanel.Visibility = speakerLabelRows.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        InlineSpeakerNameChoices = speakerLabelRows.SelectMany(row => row.NameChoices)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        TranscriptSegmentsListBox.Tag = speakerLabelRows
            .Where(row => !string.IsNullOrWhiteSpace(row.SpeakerId))
            .Select(row => new MeetingDetailSpeakerMergeChoice(row.SpeakerId!, row.OriginalLabel))
            .ToArray();
        _canApplySpeakerNames = speakerLabelRows.Count > 0;
        _canMergeSpeakerClusters = speakerLabelRows.Count > 1;
        _canUndoSpeakerNameRecognition = speakerLabelRows.Any(row => row.HasProfileAttribution);
        UpdateSpeakerReviewStatus(speakerLabelRows);
        UpdateMaintenanceButtonAvailability();

        _allTranscriptSegments = state.Transcript.Segments.ToArray();
        _allTranscriptBlocks = TranscriptSpeakerBlockBuilder.Build(_allTranscriptSegments).Select(block => block.AsRow()).ToArray();
        var priorEdits = state.Transcript.PriorAttributionOverrideCount + state.Transcript.PriorPassageSplitCount;
        FooterStatusTextBlock.Text = state.Transcript.InvalidPassageSplitCount > 0
            ? "Some saved passage splits need review. The source transcript remains available."
            : priorEdits > 0
            ? $"{priorEdits} earlier transcript edit(s) need review after reprocessing."
            : _allTranscriptSegments.Length == 0 ? state.Transcript.StatusText : string.Empty;
        ApplyTranscriptFilter();
    }

    internal void SetSpeakerQualityStatus(string text)
    {
        SpeakerQualityTextBlock.Text = text;
    }

    internal void SetInlineSpeakerUndoAvailable(bool available) =>
        UndoInlineSpeakerNameButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

    internal void SetTranscriptSelectionState(string snapshot, string? latestCorrectionId)
    {
        _transcriptSnapshot = snapshot;
        _latestSelectionCorrectionId = latestCorrectionId;
        UndoSelectionCorrectionButton.Visibility = string.IsNullOrWhiteSpace(latestCorrectionId) ? Visibility.Collapsed : Visibility.Visible;
    }

    internal bool HasInlineDraft => _activeInlineSpeakerToggle is not null || _activePassageToggle is not null || _activeSplitToggle is not null;

    private void UndoInlineSpeakerNameButton_OnClick(object sender, RoutedEventArgs e) =>
        UndoInlineSpeakerNameRequested?.Invoke(this, EventArgs.Empty);

    internal void SetSpeakerMergeUndoState(string? operationId)
    {
        _latestSpeakerMergeOperationId = string.IsNullOrWhiteSpace(operationId) ? null : operationId.Trim();
        UpdateMaintenanceButtonAvailability();
    }

    internal MeetingDetailRefreshDisposition GetRefreshDisposition(MeetingDetailTaskCenterState incoming)
    {
        if (_appliedTaskCenterRevision is null)
        {
            return MeetingDetailRefreshDisposition.ApplyReadOnlyRefresh;
        }

        var speakerDraft = _activeInlineSpeakerToggle?.IsChecked == true || _activePassageToggle?.IsChecked == true ||
            _activeSplitToggle?.IsChecked == true ||
            SpeakerLabelsDataGrid.ItemsSource is IEnumerable<MeetingDetailSpeakerLabelEditorRow> rows &&
            rows.Any(row => !string.Equals(row.OriginalLabel, row.EditedLabel, StringComparison.Ordinal));
        return MeetingDetailTaskCenterResolver.ResolveRefresh(
            _appliedTaskCenterRevision,
            incoming.Revision,
            new MeetingDetailDraftState(
                !string.Equals(_appliedTitleDraft, TitleDraftTextBox.Text, StringComparison.Ordinal),
                !string.Equals(_appliedProjectDraft, ProjectComboBox.Text, StringComparison.Ordinal),
                speakerDraft,
                HasSplitDraft()),
            isArchivedOrDeleted: false);
    }

    internal void ApplyTaskCenterState(MeetingDetailTaskCenterState state, bool markAsApplied = true)
    {
        DetailTaskCenterHeadlineTextBlock.Text = state.Headline;
        DetailTaskCenterReasonTextBlock.Text = state.PrimaryReason;
        DetailTaskCenterFreshnessTextBlock.Text = state.FreshnessText;
        DetailTaskCenterBorder.Visibility = Visibility.Visible;
        if (markAsApplied)
        {
            _appliedTaskCenterRevision = state.Revision;
        }
    }

    private bool HasSplitDraft() =>
        !string.IsNullOrWhiteSpace(SplitPointTextBox.Text) &&
        !string.Equals(SplitPointTextBox.Text, "00:00", StringComparison.Ordinal);

    private void ApplySummaryState(MeetingDetailSummaryState summary)
    {
        ConfigureSummariesButton.Content = UserActionCopyResolver.Resolve(UserActionIntent.ConfigureSummary).Label;
        GenerateSummaryButton.Content = summary.ShowGeneratedContent && summary.CanGenerate
            ? "Regenerate Summary"
            : UserActionCopyResolver.Resolve(UserActionIntent.GenerateSummary).Label;
        RetrySummaryButton.Content = UserActionCopyResolver.Resolve(UserActionIntent.RetrySummary).Label;
        AiSummaryStatusTextBlock.Text = summary.StatusText;
        AiSummaryGeneratedContentPanel.Visibility = summary.ShowGeneratedContent ? Visibility.Visible : Visibility.Collapsed;
        AiSummaryOverviewTextBlock.Text = summary.Overview ?? string.Empty;
        AiSummaryKeyPointsItemsControl.ItemsSource = summary.KeyPoints;
        AiSummaryDecisionsItemsControl.ItemsSource = summary.Decisions;
        AiSummaryActionItemsControl.ItemsSource = summary.ActionItems;
        AiSummaryRisksItemsControl.ItemsSource = summary.RisksAndOpenQuestions;
        AiSummaryProviderTextBlock.Text = string.IsNullOrWhiteSpace(summary.ProviderText)
            ? string.Empty
            : $"Provider: {summary.ProviderText}";
        AiSummaryGeneratedAtTextBlock.Text = string.IsNullOrWhiteSpace(summary.GeneratedAtText)
            ? string.Empty
            : $"Generated: {summary.GeneratedAtText}";
        AiSummaryWarningTextBlock.Text = summary.WarningText;
        AiSummaryWarningTextBlock.Visibility = string.IsNullOrWhiteSpace(summary.WarningText)
            ? Visibility.Collapsed
            : Visibility.Visible;
        AiSummaryKeyPointsHeaderTextBlock.Visibility = summary.KeyPoints.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryKeyPointsItemsControl.Visibility = summary.KeyPoints.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryDecisionsHeaderTextBlock.Visibility = summary.Decisions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryDecisionsItemsControl.Visibility = summary.Decisions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryActionItemsHeaderTextBlock.Text = summary.IsHistoric ? "Historic Action Items" : "Action Items";
        AiSummaryActionItemsHeaderTextBlock.Visibility = summary.ActionItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryActionItemsControl.Visibility = summary.ActionItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryRisksHeaderTextBlock.Visibility = summary.RisksAndOpenQuestions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AiSummaryRisksItemsControl.Visibility = summary.RisksAndOpenQuestions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _canConfigureSummaries = summary.CanConfigure;
        _canGenerateSummary = summary.CanGenerate;
        _canRetrySummary = summary.CanRetry;
    }

    internal void SetMaintenanceBusy(bool isBusy)
    {
        _isMaintenanceBusy = isBusy;
        if (isBusy)
        {
            StopSpeakerReviewPlayback();
        }

        UpdateMaintenanceButtonAvailability();
    }

    internal void SetMaintenanceStatus(string statusText)
    {
        MaintenanceStatusTextBlock.Text = statusText;
        FooterStatusTextBlock.Text = statusText;
    }

    internal void SetTitleDraft(string title, string statusText)
    {
        TitleDraftTextBox.Text = title;
        SetMaintenanceStatus(statusText);
        MeetingToolsToggle.IsChecked = true;
        MaintenanceExpander.IsExpanded = true;
        TitleDraftTextBox.Focus();
        TitleDraftTextBox.SelectAll();
    }

    private void ApplyTranscriptFilter()
    {
        var selectedRow = TranscriptSegmentsListBox.SelectedItem as MeetingTranscriptSegmentRow;
        var selectedId = selectedRow?.SegmentId;
        var selectedContainer = selectedRow is null ? null : TranscriptSegmentsListBox.ItemContainerGenerator.ContainerFromItem(selectedRow);
        var selectedText = selectedContainer is null ? null : FindVisualChild<RichTextBox>(selectedContainer, item => item.Name == "TranscriptBlockText");
        var selectionAnchor = selectedRow is null || selectedText is null ? null : CaptureSelectionAnchor(selectedRow, selectedText);
        var scroll = FindVisualChild<ScrollViewer>(TranscriptSegmentsListBox);
        var previousOffset = scroll?.VerticalOffset ?? 0;
        var hadFocus = TranscriptSegmentsListBox.IsKeyboardFocusWithin;
        var focusedElement = hadFocus ? Keyboard.FocusedElement as FrameworkElement : null;
        var focusedSegmentId = (focusedElement?.DataContext as MeetingTranscriptSegmentRow)?.SegmentId;
        var focusedName = focusedElement?.Name;
        var focusedAutomationName = focusedElement is null ? null : System.Windows.Automation.AutomationProperties.GetName(focusedElement);
        var query = TranscriptSearchTextBox.Text?.Trim();
        var filteredSegments = string.IsNullOrWhiteSpace(query)
            ? _allTranscriptBlocks
            : _allTranscriptBlocks
                .Where(segment =>
                    segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    segment.SpeakerLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (segment.BlockParts?.Any(part => part.Row.Timestamp.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? segment.Timestamp.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        TranscriptSegmentsListBox.ItemsSource = filteredSegments;
        if (selectionAnchor is not null)
        {
            TranscriptSegmentsListBox.SelectedItem = filteredSegments.FirstOrDefault(block =>
                FindAnchorOffset(block, selectionAnchor.StartSourceId, selectionAnchor.StartOffset, false) is not null &&
                FindAnchorOffset(block, selectionAnchor.EndSourceId, selectionAnchor.EndOffset, true) is not null);
        }
        if (TranscriptSegmentsListBox.SelectedItem is null && !string.IsNullOrWhiteSpace(selectedId))
        {
            TranscriptSegmentsListBox.SelectedItem = filteredSegments.FirstOrDefault(segment =>
                HasSegment(segment, selectedId));
        }
        Dispatcher.BeginInvoke(() =>
        {
            FindVisualChild<ScrollViewer>(TranscriptSegmentsListBox)?.ScrollToVerticalOffset(previousOffset);
            if (selectionAnchor is not null && TranscriptSegmentsListBox.SelectedItem is MeetingTranscriptSegmentRow selectedBlock &&
                TranscriptSegmentsListBox.ItemContainerGenerator.ContainerFromItem(selectedBlock) is { } selectionContainer &&
                FindVisualChild<RichTextBox>(selectionContainer, item => item.Name == "TranscriptBlockText") is { } restoredText &&
                restoredText.Document.Blocks.FirstBlock is Paragraph paragraph &&
                FindAnchorOffset(selectedBlock, selectionAnchor.StartSourceId, selectionAnchor.StartOffset, false) is { } startOffset &&
                FindAnchorOffset(selectedBlock, selectionAnchor.EndSourceId, selectionAnchor.EndOffset, true) is { } endOffset &&
                FindTextPosition(paragraph.ContentStart, startOffset) is { } startPointer &&
                FindTextPosition(paragraph.ContentStart, endOffset) is { } endPointer)
                restoredText.Selection.Select(startPointer, endPointer);
            if (hadFocus)
            {
                var focusedRow = filteredSegments.FirstOrDefault(segment =>
                    !string.IsNullOrWhiteSpace(focusedSegmentId) && HasSegment(segment, focusedSegmentId)) ??
                    (selectionAnchor is not null ? TranscriptSegmentsListBox.SelectedItem as MeetingTranscriptSegmentRow : null);
                var container = focusedRow is null ? null : TranscriptSegmentsListBox.ItemContainerGenerator.ContainerFromItem(focusedRow);
                var target = container is null ? null : FindVisualChild<FrameworkElement>(container, candidate =>
                    candidate.Focusable && candidate.IsVisible &&
                    ((!string.IsNullOrWhiteSpace(focusedName) && string.Equals(candidate.Name, focusedName, StringComparison.Ordinal)) ||
                     (!string.IsNullOrWhiteSpace(focusedAutomationName) && string.Equals(
                         System.Windows.Automation.AutomationProperties.GetName(candidate), focusedAutomationName, StringComparison.Ordinal))));
                if (target is null || !target.Focus()) TranscriptSegmentsListBox.Focus();
            }
        }, DispatcherPriority.Loaded);
        TranscriptStatusTextBlock.Text = string.IsNullOrWhiteSpace(query)
            ? $"{_allTranscriptBlocks.Length} speaker blocks"
            : $"{filteredSegments.Length} of {_allTranscriptBlocks.Length} speaker blocks";
    }

    private static bool HasSegment(MeetingTranscriptSegmentRow block, string segmentId) =>
        block.BlockParts?.Any(part => string.Equals(part.Row.SegmentId, segmentId, StringComparison.Ordinal)) ??
        string.Equals(block.SegmentId, segmentId, StringComparison.Ordinal);

    private sealed record TranscriptSelectionAnchor(string StartSourceId, int StartOffset, string EndSourceId, int EndOffset);

    private static TranscriptSelectionAnchor? CaptureSelectionAnchor(MeetingTranscriptSegmentRow block, RichTextBox text)
    {
        var selection = GetTextSelection(text);
        if (selection.Length == 0 || block.BlockParts is null) return null;
        var end = selection.Start + selection.Length;
        var parts = block.BlockParts.Where(part => selection.Start < part.Start + part.Length && end > part.Start).ToArray();
        if (parts.Length == 0 || parts[0].Row.SourceSegmentId is not { Length: > 0 } firstId ||
            parts[^1].Row.SourceSegmentId is not { Length: > 0 } lastId) return null;
        var first = parts[0];
        var last = parts[^1];
        return new TranscriptSelectionAnchor(firstId, first.Row.SourceTextOffset + Math.Max(0, selection.Start - first.Start),
            lastId, last.Row.SourceTextOffset + Math.Min(last.Length, end - last.Start));
    }

    private static int? FindAnchorOffset(MeetingTranscriptSegmentRow block, string sourceId, int sourceOffset, bool isEnd)
    {
        foreach (var part in block.BlockParts ?? [])
        {
            if (!string.Equals(part.Row.SourceSegmentId, sourceId, StringComparison.Ordinal)) continue;
            var relative = sourceOffset - part.Row.SourceTextOffset;
            if (relative >= 0 && relative <= part.Length &&
                (isEnd || relative < part.Length) && (!isEnd || relative > 0))
                return part.Start + relative;
        }
        return null;
    }

    private void TranscriptBlockText_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RichTextBox text) RenderTranscriptBlock(text);
    }

    private void TranscriptBlockText_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is RichTextBox text) RenderTranscriptBlock(text);
    }

    private void RenderTranscriptBlock(RichTextBox text)
    {
        if (text.DataContext is not MeetingTranscriptSegmentRow block) return;
        var paragraph = new Paragraph(new Run(block.Text)) { Margin = new Thickness(0) };
        text.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
        var query = TranscriptSearchTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        var position = 0;
        while (position < block.Text.Length)
        {
            var index = block.Text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            var first = FindTextPosition(paragraph.ContentStart, index);
            var last = FindTextPosition(paragraph.ContentStart, index + query.Length);
            if (first is not null && last is not null)
                new TextRange(first, last).ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 230, 130)));
            position = index + query.Length;
        }
    }

    private static TextPointer? FindTextPosition(TextPointer start, int offset)
    {
        var position = start;
        while (position is not null)
        {
            if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var length = position.GetTextRunLength(LogicalDirection.Forward);
                if (offset <= length) return position.GetPositionAtOffset(offset, LogicalDirection.Forward);
                offset -= length;
            }
            position = position.GetNextContextPosition(LogicalDirection.Forward);
        }
        return null;
    }

    private static (int Start, int Length) GetTextSelection(RichTextBox text)
    {
        if (text.Document.Blocks.FirstBlock is not Paragraph paragraph) return (0, 0);
        var first = text.Selection.Start.CompareTo(paragraph.ContentStart) < 0 ? paragraph.ContentStart : text.Selection.Start;
        var last = text.Selection.End.CompareTo(paragraph.ContentEnd) > 0 ? paragraph.ContentEnd : text.Selection.End;
        if (first.CompareTo(last) > 0) return (0, 0);
        var start = new TextRange(paragraph.ContentStart, first).Text.Length;
        var length = new TextRange(first, last).Text.Length;
        return (start, length);
    }

    private void TranscriptBlockText_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is RichTextBox { DataContext: MeetingTranscriptSegmentRow block } text)
        {
            TranscriptSegmentsListBox.SelectedItem = block;
            TranscriptBlockText_OnSelectionChanged(text, new RoutedEventArgs());
        }
    }

    private void TranscriptSegmentsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringTranscriptSelection) return;
        var active = _activeInlineSpeakerToggle ?? _activePassageToggle ?? _activeSplitToggle;
        if (active?.DataContext is not MeetingTranscriptSegmentRow draft)
        {
            if (TranscriptSelectionTimeTextBlock is not null) TranscriptSelectionTimeTextBlock.Text = string.Empty;
            return;
        }
        if (ReferenceEquals(TranscriptSegmentsListBox.SelectedItem, draft) || !TranscriptSegmentsListBox.Items.Contains(draft)) return;
        _restoringTranscriptSelection = true;
        try { TranscriptSegmentsListBox.SelectedItem = draft; }
        finally { _restoringTranscriptSelection = false; }
        SetMaintenanceStatus("Finish or cancel the open edit before selecting another block.");
        Dispatcher.BeginInvoke(() => active.Focus(), DispatcherPriority.Input);
    }

    private void TranscriptBlockText_OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox { DataContext: MeetingTranscriptSegmentRow block } text || TranscriptSelectionTimeTextBlock is null) return;
        if (!ReferenceEquals(TranscriptSegmentsListBox.SelectedItem, block)) return;
        var selection = GetTextSelection(text);
        var end = selection.Start + selection.Length;
        var matchingParts = selection.Length == 0 ? [] : block.BlockParts?.Where(part =>
            selection.Start < part.Start + part.Length && end > part.Start).ToArray() ?? [];
        TranscriptSelectionTimeTextBlock.Text = matchingParts.Length switch
        {
            0 => string.Empty,
            1 => $"Selected passage: {matchingParts[0].Row.Timestamp}",
            _ => $"Selected passages: {matchingParts[0].Row.Timestamp}–{matchingParts[^1].Row.Timestamp}",
        };
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static RichTextBox? FindBlockText(DependencyObject child)
    {
        var container = FindVisualAncestor<ListBoxItem>(child);
        return container is null ? null : FindVisualChild<RichTextBox>(container, candidate => candidate.Name == "TranscriptBlockText");
    }

    private static MeetingTranscriptSegmentRow? FindPassageAtCursor(MeetingTranscriptSegmentRow block, int offset) =>
        block.BlockParts?.FirstOrDefault(part => offset >= part.Start && offset <= part.Start + part.Length)?.Row ??
        (block.BlockParts is null ? block : null);

    private void UndoSelectionCorrectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMaintenanceBusy || string.IsNullOrWhiteSpace(_latestSelectionCorrectionId)) return;
        ChangeSelectionCorrectionRequested?.Invoke(this, new MeetingDetailSelectionCorrectionEventArgs(
            [], null, _transcriptSnapshot, _latestSelectionCorrectionId, true));
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match && (predicate is null || predicate(match))) return match;
            if (FindVisualChild<T>(child, predicate) is { } nested) return nested;
        }
        return null;
    }

    private void UpdateMaintenanceButtonAvailability()
    {
        var canUseMaintenance = !_isMaintenanceBusy;
        RenameButton.IsEnabled = canUseMaintenance;
        SuggestTitleButton.IsEnabled = canUseMaintenance;
        ConfigureSummariesButton.IsEnabled = canUseMaintenance && _canConfigureSummaries;
        GenerateSummaryButton.IsEnabled = canUseMaintenance && _canGenerateSummary;
        RetrySummaryButton.IsEnabled = canUseMaintenance && _canRetrySummary;
        ApplyProjectButton.IsEnabled = canUseMaintenance;
        ClearProjectButton.IsEnabled = canUseMaintenance;
        RetryTranscriptButton.IsEnabled = canUseMaintenance && _canRetryTranscript;
        ReTranscribeButton.IsEnabled = canUseMaintenance && _canReTranscribe;
        AddSpeakerLabelsButton.IsEnabled = canUseMaintenance && _canAddSpeakerLabels;
        ProcessAsapButton.IsEnabled = canUseMaintenance && _canProcessAsap;
        SplitButton.IsEnabled = canUseMaintenance && _canSplit;
        ApplySpeakerNamesButton.IsEnabled = canUseMaintenance && _canApplySpeakerNames;
        MergeSelectedSpeakersButton.IsEnabled = canUseMaintenance && _canMergeSpeakerClusters &&
            SpeakerLabelsDataGrid.ItemsSource is IEnumerable<MeetingDetailSpeakerLabelEditorRow> rows &&
            rows.Count(row => row.IsSelectedForMerge) >= 2 &&
            SpeakerMergeCanonicalComboBox.SelectedItem is MeetingDetailSpeakerMergeChoice;
        UndoSpeakerMergeButton.IsEnabled = canUseMaintenance && !string.IsNullOrWhiteSpace(_latestSpeakerMergeOperationId);
        RefreshSpeakerNamesButton.IsEnabled = canUseMaintenance && _canApplySpeakerNames;
        UndoSpeakerNameRecognitionButton.IsEnabled = canUseMaintenance && _canUndoSpeakerNameRecognition;
        ArchiveButton.IsEnabled = canUseMaintenance && _canArchive;
        DeleteButton.IsEnabled = canUseMaintenance && _canDelete;
    }

    private void UpdateSpeakerReviewStatus(IEnumerable<MeetingDetailSpeakerLabelEditorRow>? rows = null)
    {
        var reviewRows = rows ?? SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow>;
        var pendingCount = reviewRows?.Count(row =>
            row.IsSuggestionRejected ||
            !string.Equals(row.OriginalLabel.Trim(), row.EditedLabel.Trim(), StringComparison.Ordinal)) ?? 0;
        SpeakerReviewStatusTextBlock.Text = _canApplySpeakerNames
            ? pendingCount == 0
                ? "Name a speaker here. Suggestions are local and need confirmation."
                : $"{pendingCount} name change(s) pending. Apply names updates every turn for each speaker in this revision."
            : "Labels are unavailable until this meeting has a published transcript and diarization labels.";
        SpeakerReviewPendingTextBlock.Text = pendingCount == 0 ? string.Empty : $"{pendingCount} pending";
    }

    private void TranscriptSearchTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyTranscriptFilter();
    }

    private void OpenTranscriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenTranscriptRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenAudioButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenAudioRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        RenameRequested?.Invoke(this, new MeetingDetailTextEventArgs(TitleDraftTextBox.Text));
    }

    private void SuggestTitleButton_OnClick(object sender, RoutedEventArgs e)
    {
        SuggestTitleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyProjectRequested?.Invoke(this, new MeetingDetailTextEventArgs(ProjectComboBox.Text));
    }

    private void ClearProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearProjectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RetryTranscriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        RetryTranscriptRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReTranscribeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ReTranscribeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigureSummariesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConfigureSummariesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void GenerateSummaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        GenerateSummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RetrySummaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        RetrySummaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddSpeakerLabelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddSpeakerLabelsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ProcessAsapButton_OnClick(object sender, RoutedEventArgs e)
    {
        ProcessAsapRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke(this, new MeetingDetailTextEventArgs(SplitPointTextBox.Text));
    }

    private void ApplySpeakerNamesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var rows = SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow>;
        ApplySpeakerNamesRequested?.Invoke(
            this,
            new MeetingDetailSpeakerLabelsEventArgs(rows?.ToArray() ?? Array.Empty<MeetingDetailSpeakerLabelEditorRow>()));
    }

    private void MergeSelectedSpeakersButton_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = (SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow> ?? [])
            .Where(row => row.IsSelectedForMerge)
            .ToArray();
        MergeSpeakerClustersRequested?.Invoke(
            this,
            new MeetingDetailSpeakerMergeEventArgs(
                selected,
                (SpeakerMergeCanonicalComboBox.SelectedItem as MeetingDetailSpeakerMergeChoice)?.SpeakerId));
    }

    private void UndoSpeakerMergeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestSpeakerMergeOperationId))
        {
            UndoSpeakerMergeRequested?.Invoke(this, new MeetingDetailTextEventArgs(_latestSpeakerMergeOperationId));
        }
    }

    private void SpeakerLabelsDataGrid_OnCellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateSpeakerReviewStatus);
    }

    private void SpeakerMergeSelectionChanged_OnClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateSpeakerMergeControls);
    }

    private void SpeakerMergeCanonicalComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateMaintenanceButtonAvailability();
    }

    private void UpdateSpeakerMergeControls()
    {
        var selected = (SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow> ?? [])
            .Where(row => row.IsSelectedForMerge && !string.IsNullOrWhiteSpace(row.SpeakerId))
            .ToArray();
        var previousCanonical = (SpeakerMergeCanonicalComboBox.SelectedItem as MeetingDetailSpeakerMergeChoice)?.SpeakerId;
        var choices = selected
            .OrderBy(row => row.ExpectedNameSource == SpeakerNameSource.None ? 0 : 1)
            .ThenBy(row => row.AnonymousLabel, StringComparer.Ordinal)
            .Select(row => new MeetingDetailSpeakerMergeChoice(row.SpeakerId!, $"{row.AnonymousLabel}: {row.EditedLabel}"))
            .ToArray();
        SpeakerMergeCanonicalComboBox.ItemsSource = choices;
        SpeakerMergeCanonicalComboBox.SelectedItem = choices.FirstOrDefault(choice => choice.SpeakerId == previousCanonical) ?? choices.FirstOrDefault();
        SpeakerMergeStatusTextBlock.Text = selected.Length < 2
            ? "Select two or more normal duplicate clusters. Repair remains safer for fragmented labels."
            : $"{selected.Length} speakers selected; {selected.Sum(row => row.AffectedTurnCount)} turn(s) affected. Choose canonical speaker; merge does not teach local profiles.";
        UpdateMaintenanceButtonAvailability();
    }

    private void ToggleSpeakerEvidenceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingDetailSpeakerLabelEditorRow row && row.HasEvidenceCues)
        {
            row.IsEvidenceVisible = !row.IsEvidenceVisible;
            SpeakerLabelsDataGrid.Items.Refresh();
        }
    }

    private void ShowSpeakerEvidenceCueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SpeakerEvidenceCue cue)
        {
            var indexedSegmentId = cue.SourceKind == SpeakerEvidenceSourceKind.StructuredTranscript && cue.SegmentIndex >= 0 &&
                cue.SegmentIndex < _allTranscriptSegments.Length && TimestampMatches(_allTranscriptSegments[cue.SegmentIndex].Timestamp, cue.Start)
                    ? _allTranscriptSegments[cue.SegmentIndex].SegmentId : null;
            var target = TranscriptSegmentsListBox.Items.OfType<MeetingTranscriptSegmentRow>()
                .FirstOrDefault(segment => indexedSegmentId is not null ? HasSegment(segment, indexedSegmentId) :
                    segment.BlockParts?.Any(part => TimestampMatches(part.Row.Timestamp, cue.Start)) ?? TimestampMatches(segment.Timestamp, cue.Start));
            if (target is null)
            {
                SetMaintenanceStatus("The evidence passage is not in the current results. Clear the transcript search to find it.");
                return;
            }

            TranscriptSegmentsListBox.SelectedItem = target;
            TranscriptSegmentsListBox.ScrollIntoView(target);
            var selectedPart = target.BlockParts?.FirstOrDefault(part =>
                indexedSegmentId is not null ? part.Row.SegmentId == indexedSegmentId : TimestampMatches(part.Row.Timestamp, cue.Start));
            Dispatcher.BeginInvoke(() =>
            {
                if (selectedPart is null || TranscriptSegmentsListBox.ItemContainerGenerator.ContainerFromItem(target) is not { } container ||
                    FindVisualChild<RichTextBox>(container, item => item.Name == "TranscriptBlockText") is not { } editor ||
                    editor.Document.Blocks.FirstBlock is not Paragraph paragraph ||
                    FindTextPosition(paragraph.ContentStart, selectedPart.Start) is not { } start ||
                    FindTextPosition(paragraph.ContentStart, selectedPart.Start + selectedPart.Length) is not { } end) return;
                editor.Selection.Select(start, end);
                editor.Focus();
                editor.BringIntoView(start.GetCharacterRect(LogicalDirection.Forward));
            }, DispatcherPriority.Loaded);
        }
    }

    private static bool TimestampMatches(string text, TimeSpan time) =>
        TimeSpan.TryParseExact(text, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var parsed) &&
        parsed == TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds));

    private void InlineSpeakerToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (toggle.IsChecked == true)
        {
            if (_activePassageToggle is not null || _activeSplitToggle is not null ||
                _activeInlineSpeakerToggle is { } previous && !ReferenceEquals(previous, toggle))
            {
                toggle.IsChecked = false;
                SetMaintenanceStatus("Finish or cancel the open speaker name before editing another.");
                return;
            }
            _activeInlineSpeakerToggle = toggle;
        }
        else if (ReferenceEquals(_activeInlineSpeakerToggle, toggle))
        {
            _activeInlineSpeakerToggle = null;
        }
        TranscriptSearchTextBox.IsEnabled = !HasInlineDraft;
    }

    private void InlinePassageToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (toggle.IsChecked == true)
        {
            if (_activeInlineSpeakerToggle is not null || _activeSplitToggle is not null ||
                _activePassageToggle is { } previous && !ReferenceEquals(previous, toggle))
            {
                toggle.IsChecked = false;
                SetMaintenanceStatus("Finish or cancel the open edit before starting another.");
                return;
            }
            _activePassageToggle = toggle;
        }
        else if (ReferenceEquals(_activePassageToggle, toggle))
        {
            _activePassageToggle = null;
        }
        TranscriptSearchTextBox.IsEnabled = !HasInlineDraft;
    }

    private void InlineSplitToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (toggle.IsChecked == true)
        {
            if (_activeInlineSpeakerToggle is not null || _activePassageToggle is not null ||
                _activeSplitToggle is { } previous && !ReferenceEquals(previous, toggle))
            {
                toggle.IsChecked = false;
                SetMaintenanceStatus("Finish or cancel the open edit before starting another.");
                return;
            }
            _activeSplitToggle = toggle;
        }
        else if (ReferenceEquals(_activeSplitToggle, toggle))
        {
            _activeSplitToggle = null;
        }
        TranscriptSearchTextBox.IsEnabled = !HasInlineDraft;
    }

    private void SplitPassageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MeetingTranscriptSegmentRow segment, Tag: TextBox text } || _isMaintenanceBusy) return;
        var part = segment.BlockParts?.FirstOrDefault(item => text.CaretIndex > item.Start && text.CaretIndex < item.Start + item.Length);
        var target = part?.Row ?? (segment.BlockParts is null ? segment : null);
        if (target is null) { SetMaintenanceStatus("Choose a position inside one original passage."); return; }
        ChangePassageSplitRequested?.Invoke(this, new MeetingDetailPassageSplitEventArgs(target, text.CaretIndex - (part?.Start ?? 0), false, () =>
        {
            if (_activeSplitToggle is { } toggle) toggle.IsChecked = false;
        }));
    }

    private void UndoPassageSplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MeetingTranscriptSegmentRow segment } button || _isMaintenanceBusy) return;
        var text = FindBlockText(button);
        var target = FindPassageAtCursor(segment, text is null ? 0 : GetTextSelection(text).Start);
        if (target?.CanUndoPassageSplit == true) ChangePassageSplitRequested?.Invoke(this, new MeetingDetailPassageSplitEventArgs(target, 0, true));
        else SetMaintenanceStatus("Place the cursor in a split passage to undo its split.");
    }

    private void SaveInlineSpeakerNameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MeetingTranscriptSegmentRow segment } button ||
            button.CommandParameter is not string proposedName || _isMaintenanceBusy)
        {
            return;
        }

        var name = proposedName.Trim();
        var target = (SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow>)?
            .FirstOrDefault(row => string.Equals(row.SpeakerId, segment.InlineRenameSpeakerId ?? segment.SpeakerId, StringComparison.Ordinal));
        if (!segment.CanRenameSpeakerInline || target is null || string.IsNullOrWhiteSpace(name))
        {
            SetMaintenanceStatus("Enter a name for an identified speaker, or correct this passage instead.");
            return;
        }

        // Keep the review grid's own drafts untouched while submitting this one name.
        var change = new MeetingDetailSpeakerLabelEditorRow(target.OriginalLabel, target.Provenance,
            target.SuggestedDisplayName, target.SpeakerId, target.ProfileId, target.HasProfileAttribution,
            target.ArtifactRevision, target.ExpectedNameSource) { EditedLabel = name };
        if (string.Equals(name, target.OriginalLabel, StringComparison.Ordinal))
        {
            if (button.Tag is ToggleButton unchanged) unchanged.IsChecked = false;
            return;
        }
        ApplySpeakerNamesRequested?.Invoke(this, new MeetingDetailSpeakerLabelsEventArgs([change], () =>
        {
            if (button.Tag is ToggleButton toggle) toggle.IsChecked = false;
        }));
    }

    private void ApplyInlinePassageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MeetingTranscriptSegmentRow segment, Tag: ComboBox choices } button ||
            _isMaintenanceBusy) return;
        var name = (button.CommandParameter as string)?.Trim() ?? string.Empty;
        var selected = choices.SelectedItem as MeetingDetailSpeakerMergeChoice;
        if ((selected is null && (string.IsNullOrWhiteSpace(name) || name.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase))) ||
            (selected is not null && name.Length > 0))
        {
            SetMaintenanceStatus("Choose a speaker or enter a name for this passage.");
            return;
        }
        var choice = selected is not null
            ? new SegmentAttributionOverrideChoice(SegmentAttributionOverrideTargetKind.Speaker, selected.SpeakerId)
            : new SegmentAttributionOverrideChoice(SegmentAttributionOverrideTargetKind.DisplayName, name);
        var text = FindBlockText(button);
        var selection = text is null ? (Start: 0, Length: 0) : GetTextSelection(text);
        var slices = TranscriptSpeakerBlockBuilder.GetSelection(segment, selection.Start, selection.Length);
        if (slices.Count == 0)
        {
            SetMaintenanceStatus("Select transcript words before choosing a speaker.");
            return;
        }
        ChangeSelectionCorrectionRequested?.Invoke(this, new MeetingDetailSelectionCorrectionEventArgs(slices, choice,
            _transcriptSnapshot, Guid.NewGuid().ToString("N"), false, () =>
        {
            if (_activePassageToggle is { } toggle) toggle.IsChecked = false;
        }));
    }

    private void CancelInlineSpeakerNameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToggleButton toggle })
        {
            toggle.IsChecked = false;
            toggle.Focus();
        }
    }

    private void OpenSpeakerReviewFromTranscriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MeetingTranscriptSegmentRow segment ||
            !segment.CanOpenSpeakerReview)
        {
            SetMaintenanceStatus("Speaker review is unavailable for this transcript label.");
            return;
        }

        var rows = SpeakerLabelsDataGrid.ItemsSource as IEnumerable<MeetingDetailSpeakerLabelEditorRow>;
        var target = rows?.FirstOrDefault(row => string.Equals(row.SpeakerId, segment.InlineRenameSpeakerId ?? segment.SpeakerId, StringComparison.Ordinal));
        if (target is null)
        {
            SetMaintenanceStatus("This transcript label is no longer current. Refresh meeting details and try again.");
            return;
        }

        MeetingToolsToggle.IsChecked = true;
        SpeakerReviewExpander.IsExpanded = true;
        SpeakerLabelsDataGrid.SelectedItem = target;
        SpeakerLabelsDataGrid.ScrollIntoView(target);
        if (SpeakerLabelsDataGrid.Columns.Count > 2)
        {
            SpeakerLabelsDataGrid.CurrentCell = new DataGridCellInfo(target, SpeakerLabelsDataGrid.Columns[2]);
        }

        SpeakerLabelsDataGrid.Focus();
        SetMaintenanceStatus($"Speaker Review is ready for {target.AnonymousLabel}.");
    }

    private void ApplyParagraphOverrideButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingTranscriptSegmentRow segment && segment.CanApplyParagraphOverride)
        {
            ApplyParagraphOverrideRequested?.Invoke(this, new MeetingDetailSegmentAttributionEventArgs(segment));
        }
    }

    private void ViewOriginalAttributionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MeetingTranscriptSegmentRow segment } element)
        {
            var text = FindBlockText(element);
            var target = FindPassageAtCursor(segment, text is null ? 0 : GetTextSelection(text).Start);
            if (target?.HasManualAttributionOverride == true)
                SetMaintenanceStatus($"Original passage attribution: {target.OriginalSpeakerLabel ?? target.SpeakerId ?? "Speaker"}.");
            else SetMaintenanceStatus("Place the cursor in a corrected passage to see its original speaker.");
        }
    }

    private void RevertParagraphOverrideButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MeetingTranscriptSegmentRow segment } element)
        {
            var text = FindBlockText(element);
            var target = FindPassageAtCursor(segment, text is null ? 0 : GetTextSelection(text).Start);
            if (target?.CanRevertParagraphOverride == true)
                RevertParagraphOverrideRequested?.Invoke(this, new MeetingDetailSegmentAttributionEventArgs(target));
            else SetMaintenanceStatus("Place the cursor in a corrected passage to undo it.");
        }
    }

    private void PlaySpeakerEvidenceCueButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SpeakerEvidenceCue cue ||
            (sender as FrameworkElement)?.Tag is not MeetingDetailSpeakerLabelEditorRow row)
        {
            return;
        }

        if (ReferenceEquals(_playingSpeakerRow, row))
        {
            if (_speakerReviewOutput?.PlaybackState == PlaybackState.Playing)
            {
                _speakerReviewOutput.Pause();
                row.PlaybackText = "Resume clip";
                SpeakerLabelsDataGrid.Items.Refresh();
            }
            else if (_speakerReviewOutput?.PlaybackState == PlaybackState.Paused)
            {
                _speakerReviewOutput.Play();
                row.PlaybackText = "Stop clip";
                SpeakerLabelsDataGrid.Items.Refresh();
            }
            else
            {
                StopSpeakerReviewPlayback();
            }
            return;
        }

        PlaySpeakerEvidenceRequested?.Invoke(this, new MeetingDetailSpeakerEvidenceCueEventArgs(row, cue));
    }

    internal void PlaySpeakerReviewClip(MeetingDetailSpeakerLabelEditorRow row, string clipPath, IDisposable lease)
    {
        StopSpeakerReviewPlayback();
        try
        {
            _speakerReviewReader = new AudioFileReader(clipPath);
            _speakerReviewLease = lease;
            _speakerReviewOutput = new WaveOutEvent();
            _speakerReviewOutput.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(StopSpeakerReviewPlayback);
            _speakerReviewOutput.Init(_speakerReviewReader);
            _speakerReviewOutput.Play();
            _playingSpeakerRow = row;
            row.PlaybackText = "Stop clip";
            row.PlaybackProgressText = "Playing 00:00";
            _speakerReviewProgressTimer.Start();
            SpeakerLabelsDataGrid.Items.Refresh();
        }
        catch
        {
            StopSpeakerReviewPlayback();
            SetMaintenanceStatus("Audio clip could not be played.");
        }
    }

    private void StopSpeakerReviewPlayback()
    {
        var previous = _playingSpeakerRow;
        _playingSpeakerRow = null;
        _speakerReviewOutput?.Stop();
        _speakerReviewOutput?.Dispose();
        _speakerReviewOutput = null;
        _speakerReviewReader?.Dispose();
        _speakerReviewReader = null;
        _speakerReviewLease?.Dispose();
        _speakerReviewLease = null;
        _speakerReviewProgressTimer.Stop();
        if (previous is not null)
        {
            previous.PlaybackText = "Play clip";
            previous.PlaybackProgressText = string.Empty;
            SpeakerLabelsDataGrid.Items.Refresh();
        }
    }

    private void UpdateSpeakerReviewProgress()
    {
        if (_playingSpeakerRow is null || _speakerReviewReader is null)
        {
            _speakerReviewProgressTimer.Stop();
            return;
        }

        _playingSpeakerRow.PlaybackProgressText = $"{FormatPlaybackTime(_speakerReviewReader.CurrentTime)} / {FormatPlaybackTime(_speakerReviewReader.TotalTime)}";
        SpeakerLabelsDataGrid.Items.Refresh();
    }

    private static string FormatPlaybackTime(TimeSpan value) => value.TotalHours >= 1d
        ? value.ToString(@"hh\:mm\:ss")
        : value.ToString(@"mm\:ss");

    private void RefreshSpeakerNamesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshSpeakerNamesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UndoSpeakerNameRecognitionButton_OnClick(object sender, RoutedEventArgs e)
    {
        UndoSpeakerNameRecognitionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UseSpeakerSuggestionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingDetailSpeakerLabelEditorRow row)
        {
            row.AcceptSuggestion();
            SpeakerLabelsDataGrid.Items.Refresh();
            UpdateSpeakerReviewStatus();
        }
    }

    private void RejectSpeakerSuggestionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingDetailSpeakerLabelEditorRow row)
        {
            row.RejectSuggestion();
            SpeakerLabelsDataGrid.Items.Refresh();
            UpdateSpeakerReviewStatus();
        }
    }

    private void ArchiveButton_OnClick(object sender, RoutedEventArgs e)
    {
        ArchiveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSpeakerReviewPlayback();
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_activeInlineSpeakerToggle is { } toggle)
            {
                toggle.IsChecked = false;
                toggle.Focus();
                e.Handled = true;
                return;
            }
            if (_activePassageToggle is { } passageToggle)
            {
                passageToggle.IsChecked = false;
                passageToggle.Focus();
                e.Handled = true;
                return;
            }
            if (_activeSplitToggle is { } splitToggle)
            {
                splitToggle.IsChecked = false;
                splitToggle.Focus();
                e.Handled = true;
                return;
            }
            if (MeetingToolsToggle.IsChecked == true)
            {
                MeetingToolsToggle.IsChecked = false;
                MeetingToolsToggle.Focus();
                e.Handled = true;
                return;
            }
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
