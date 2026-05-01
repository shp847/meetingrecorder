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

internal sealed class MeetingDetailSpeakerLabelEditorRow(string originalLabel)
{
    public string OriginalLabel { get; } = originalLabel;

    public string EditedLabel { get; set; } = originalLabel;
}

public partial class MeetingDetailWindow : Window
{
    private MeetingTranscriptSegmentRow[] _allTranscriptSegments = Array.Empty<MeetingTranscriptSegmentRow>();
    private bool _isMaintenanceBusy;
    private bool _canRetryTranscript;
    private bool _canReTranscribe;
    private bool _canAddSpeakerLabels;
    private bool _canProcessAsap;
    private bool _canSplit;
    private bool _canApplySpeakerNames;
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

    internal event EventHandler? AddSpeakerLabelsRequested;

    internal event EventHandler? ProcessAsapRequested;

    internal event EventHandler<MeetingDetailTextEventArgs>? SplitRequested;

    internal event EventHandler<MeetingDetailSpeakerLabelsEventArgs>? ApplySpeakerNamesRequested;

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
        AiSummaryPlaceholderTextBlock.Text = state.AiSummaryPlaceholderText;
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
        ProcessAsapButton.Content = state.CanClearAsap ? "Clear ASAP" : "Process ASAP";

        TitleDraftTextBox.Text = state.Title;
        ProjectComboBox.ItemsSource = projectOptions;
        ProjectComboBox.Text = state.ProjectName == "None" ? string.Empty : state.ProjectName;
        SpeakerLabelsDataGrid.ItemsSource = speakerLabelRows;
        _canApplySpeakerNames = speakerLabelRows.Count > 0;
        MaintenanceStatusTextBlock.Text = speakerLabelRows.Count == 0
            ? "No editable speaker labels are available for this transcript yet."
            : "Edit speaker display names, then apply changes.";
        UpdateMaintenanceButtonAvailability();

        _allTranscriptSegments = state.Transcript.Segments.ToArray();
        FooterStatusTextBlock.Text = state.Transcript.StatusText;
        ApplyTranscriptFilter();
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
            ? $"{_allTranscriptSegments.Length} segment(s)"
            : $"{filteredSegments.Length} of {_allTranscriptSegments.Length} segment(s)";
    }

    private void UpdateMaintenanceButtonAvailability()
    {
        var canUseMaintenance = !_isMaintenanceBusy;
        RenameButton.IsEnabled = canUseMaintenance;
        SuggestTitleButton.IsEnabled = canUseMaintenance;
        ApplyProjectButton.IsEnabled = canUseMaintenance;
        ClearProjectButton.IsEnabled = canUseMaintenance;
        RetryTranscriptButton.IsEnabled = canUseMaintenance && _canRetryTranscript;
        ReTranscribeButton.IsEnabled = canUseMaintenance && _canReTranscribe;
        AddSpeakerLabelsButton.IsEnabled = canUseMaintenance && _canAddSpeakerLabels;
        ProcessAsapButton.IsEnabled = canUseMaintenance && _canProcessAsap;
        SplitButton.IsEnabled = canUseMaintenance && _canSplit;
        ApplySpeakerNamesButton.IsEnabled = canUseMaintenance && _canApplySpeakerNames;
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
