using MeetingRecorder.Core.Services;
using System.Windows;
using System.Windows.Input;

namespace MeetingRecorder.App;

internal sealed class MeetingDetailTextEventArgs(string text) : EventArgs
{
    public string Text { get; } = text;
}

internal sealed class MeetingDetailSpeakerLabelsEventArgs(IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> rows) : EventArgs
{
    public IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> Rows { get; } = rows;
}

internal sealed class MeetingDetailSpeakerLabelEditorRow(
    string originalLabel,
    string provenance = "",
    string? suggestedDisplayName = null,
    string? speakerId = null,
    string? profileId = null,
    bool hasProfileAttribution = false)
{
    public string OriginalLabel { get; } = originalLabel;

    public string EditedLabel { get; set; } = originalLabel;

    public string Provenance { get; } = provenance;

    public string? SuggestedDisplayName { get; } = suggestedDisplayName;

    public string? SpeakerId { get; } = speakerId;

    public string? ProfileId { get; } = profileId;

    public bool HasProfileAttribution { get; } = hasProfileAttribution;

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
    private bool _isMaintenanceBusy;
    private bool _canConfigureSummaries;
    private bool _canGenerateSummary;
    private bool _canRetrySummary;
    private bool _canRetryTranscript;
    private bool _canReTranscribe;
    private bool _canAddSpeakerLabels;
    private bool _canProcessAsap;
    private bool _canSplit;
    private bool _canApplySpeakerNames;
    private bool _canUndoSpeakerNameRecognition;
    private bool _canArchive;
    private bool _canDelete;

    public MeetingDetailWindow()
    {
        InitializeComponent();
    }

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

    internal event EventHandler? RefreshSpeakerNamesRequested;

    internal event EventHandler? UndoSpeakerNameRecognitionRequested;

    internal event EventHandler? ArchiveRequested;

    internal event EventHandler? DeleteRequested;

    internal void ApplyState(
        MeetingDetailWindowState state,
        IReadOnlyList<string> projectOptions,
        IReadOnlyList<MeetingDetailSpeakerLabelEditorRow> speakerLabelRows)
    {
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
        SpeakerLabelsDataGrid.ItemsSource = speakerLabelRows;
        _canApplySpeakerNames = speakerLabelRows.Count > 0;
        _canUndoSpeakerNameRecognition = speakerLabelRows.Any(row => row.HasProfileAttribution);
        MaintenanceStatusTextBlock.Text = speakerLabelRows.Count == 0
            ? "No editable speaker labels are available for this transcript yet."
            : "Edit speaker display names, then apply changes.";
        UpdateMaintenanceButtonAvailability();

        _allTranscriptSegments = state.Transcript.Segments.ToArray();
        FooterStatusTextBlock.Text = state.Transcript.StatusText;
        ApplyTranscriptFilter();
    }

    private void ApplySummaryState(MeetingDetailSummaryState summary)
    {
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
        MaintenanceExpander.IsExpanded = true;
        TitleDraftTextBox.Focus();
        TitleDraftTextBox.SelectAll();
    }

    private void ApplyTranscriptFilter()
    {
        var query = TranscriptSearchTextBox.Text?.Trim();
        var filteredSegments = string.IsNullOrWhiteSpace(query)
            ? _allTranscriptSegments
            : _allTranscriptSegments
                .Where(segment =>
                    segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    segment.SpeakerLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    segment.Timestamp.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        TranscriptSegmentsListBox.ItemsSource = filteredSegments;
        TranscriptStatusTextBlock.Text = string.IsNullOrWhiteSpace(query)
            ? $"{_allTranscriptSegments.Length} paragraph(s)"
            : $"{filteredSegments.Length} of {_allTranscriptSegments.Length} paragraph(s)";
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
        RefreshSpeakerNamesButton.IsEnabled = canUseMaintenance && _canApplySpeakerNames;
        UndoSpeakerNameRecognitionButton.IsEnabled = canUseMaintenance && _canUndoSpeakerNameRecognition;
        ArchiveButton.IsEnabled = canUseMaintenance && _canArchive;
        DeleteButton.IsEnabled = canUseMaintenance && _canDelete;
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
        }
    }

    private void RejectSpeakerSuggestionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MeetingDetailSpeakerLabelEditorRow row)
        {
            row.RejectSuggestion();
            SpeakerLabelsDataGrid.Items.Refresh();
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
