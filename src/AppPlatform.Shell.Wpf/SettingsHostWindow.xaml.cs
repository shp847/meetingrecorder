using AppPlatform.Abstractions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AppPlatform.Shell.Wpf;

public partial class SettingsHostWindow : Window
{
    private readonly Dictionary<string, Button> _sectionButtonById;
    private readonly Button[] _sectionButtons;
    private bool _suppressSectionNavigationChange;

    public SettingsHostWindow(IReadOnlyList<SettingsSectionDefinition>? sections = null)
    {
        InitializeComponent();

        var effectiveSections = sections ?? ShellSupportDefinitions.CreateDefaultSettingsSections();
        _sectionButtonById = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        _sectionButtons =
        [
            SettingsSetupSectionButton,
            SettingsGeneralSectionButton,
            SettingsFilesSectionButton,
            SettingsUpdatesSectionButton,
            SettingsAdvancedSectionButton,
        ];

        for (var index = 0; index < effectiveSections.Count && index < _sectionButtons.Length; index++)
        {
            var button = _sectionButtons[index];
            var section = effectiveSections[index];
            button.Content = section.Title;
            button.Uid = section.Id;
            _sectionButtonById[section.Id] = button;
        }

        NavigateTo(effectiveSections.FirstOrDefault()?.Id ?? "setup");
    }

    public event Action<string>? SectionRequested;

    public event EventHandler? SaveRequested;

    public void AttachBody(UIElement body)
    {
        SettingsBodyHost.Content = body;
    }

    public UIElement? DetachBody()
    {
        var body = SettingsBodyHost.Content as UIElement;
        SettingsBodyHost.Content = null;
        return body;
    }

    public void NavigateTo(string sectionId)
    {
        if (!_sectionButtonById.TryGetValue(sectionId, out var activeButton))
        {
            activeButton = _sectionButtons[0];
        }

        _suppressSectionNavigationChange = true;
        foreach (var button in _sectionButtons)
        {
            button.Tag = ReferenceEquals(button, activeButton) ? "Active" : null;
        }

        _suppressSectionNavigationChange = false;
    }

    public void SetFooterStatus(string? statusText)
    {
        FooterStatusTextBlock.Text = statusText ?? string.Empty;
    }

    public void SetSaveActionState(string content, bool isEnabled)
    {
        SaveChangesButton.Content = content;
        SaveChangesButton.IsEnabled = isEnabled;
    }

    private void SettingsSectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressSectionNavigationChange ||
            !IsLoaded ||
            sender is not Button { Uid: { Length: > 0 } sectionId })
        {
            return;
        }

        NavigateTo(sectionId);
        SectionRequested?.Invoke(sectionId);
    }

    private void SaveChangesButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
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
