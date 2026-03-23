using AppPlatform.Abstractions;
using System.Windows;
using System.Windows.Input;

namespace AppPlatform.Shell.Wpf;

public partial class HelpHostWindow : Window
{
    private readonly Action _openSetupGuide;
    private readonly Action _openLogsFolder;
    private readonly Action _openDataFolder;
    private readonly Action _openReleasePage;

    public HelpHostWindow(
        AboutContentDefinition aboutContent,
        Action openSetupGuide,
        Action openLogsFolder,
        Action openDataFolder,
        Action openReleasePage,
        string? runtimeDiagnosticsText = null)
    {
        _openSetupGuide = openSetupGuide;
        _openLogsFolder = openLogsFolder;
        _openDataFolder = openDataFolder;
        _openReleasePage = openReleasePage;

        InitializeComponent();

        AboutDisplayNameTextBlock.Text = $"{aboutContent.ProductName} v{aboutContent.Version}";
        AboutDescriptionTextBlock.Text = aboutContent.ProductDescription;
        AboutAuthorTextBlock.Text = $"Author: {aboutContent.AuthorName} | {aboutContent.AuthorEmail}";
        SupportDescriptionTextBlock.Text = aboutContent.SupportDescription;
        ReleaseDescriptionTextBlock.Text = aboutContent.ReleaseDescription;
        LegalNoticeTextBlock.Text = aboutContent.LegalNotice;
        SetRuntimeDiagnostics(runtimeDiagnosticsText);
    }

    public void SetFooterStatus(string? statusText)
    {
        FooterStatusTextBlock.Text = statusText ?? string.Empty;
    }

    public void SetRuntimeDiagnostics(string? runtimeDiagnosticsText)
    {
        var hasRuntimeDiagnostics = !string.IsNullOrWhiteSpace(runtimeDiagnosticsText);
        RuntimeDiagnosticsBorder.Visibility = hasRuntimeDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        RuntimeDiagnosticsTextBlock.Text = hasRuntimeDiagnostics ? runtimeDiagnosticsText : string.Empty;
    }

    private void OpenSetupGuideButton_OnClick(object sender, RoutedEventArgs e)
    {
        _openSetupGuide();
        SetFooterStatus("Opened the local speaker labeling setup guide.");
    }

    private void OpenLogsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _openLogsFolder();
        SetFooterStatus("Opened the app logs folder.");
    }

    private void OpenDataFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _openDataFolder();
        SetFooterStatus("Opened the Meeting Recorder data folder.");
    }

    private void OpenReleasePageButton_OnClick(object sender, RoutedEventArgs e)
    {
        _openReleasePage();
        SetFooterStatus("Opened the latest release page.");
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
