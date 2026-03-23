using System.Windows;
using System.Windows.Input;

namespace MeetingRecorder.App;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
    }

    internal event EventHandler? OpenSettingsRequested;

    internal void AttachTranscriptionBody(UIElement body)
    {
        TranscriptionSetupBodyHost.Content = body;
    }

    internal UIElement? DetachTranscriptionBody()
    {
        var body = TranscriptionSetupBodyHost.Content as UIElement;
        TranscriptionSetupBodyHost.Content = null;
        return body;
    }

    internal void AttachSpeakerLabelingBody(UIElement body)
    {
        SpeakerLabelingSetupBodyHost.Content = body;
    }

    internal UIElement? DetachSpeakerLabelingBody()
    {
        var body = SpeakerLabelingSetupBodyHost.Content as UIElement;
        SpeakerLabelingSetupBodyHost.Content = null;
        return body;
    }

    internal void NavigateTo(SetupWindowSection section)
    {
        SetupTabControl.SelectedIndex = section switch
        {
            SetupWindowSection.SpeakerLabeling => 1,
            _ => 0,
        };
    }

    private void OpenSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
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
