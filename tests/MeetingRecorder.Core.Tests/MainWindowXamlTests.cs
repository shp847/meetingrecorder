using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void App_Shell_Primary_Navigation_Is_Task_First_With_Exactly_Two_Destinations()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var productModulePath = GetPath("src", "MeetingRecorder.Product", "MeetingRecorderProductModule.cs");

        var xaml = File.ReadAllText(xamlPath);
        var productModule = File.ReadAllText(productModulePath);
        var navigationStart = productModule.IndexOf("NavigationItems =", StringComparison.Ordinal);
        var settingsStart = productModule.IndexOf("SettingsSections =", StringComparison.Ordinal);
        var navigationBlock = productModule[navigationStart..settingsStart];

        Assert.Contains("Header=\"Home\"", xaml);
        Assert.Contains("Header=\"Meetings\"", xaml);
        Assert.Equal(2, CountOccurrences(xaml, "<TabItem x:Name="));
        Assert.DoesNotContain("Header=\"Setup\"", xaml);
        Assert.DoesNotContain("x:Name=\"ModelsTabItem\"", xaml);

        Assert.Contains("new(\"home\", \"Home\"", navigationBlock);
        Assert.Contains("new(\"meetings\", \"Meetings\"", navigationBlock);
        Assert.Equal(2, CountOccurrences(navigationBlock, "new(\""));
        Assert.DoesNotContain("new(\"setup\", \"Setup\"", navigationBlock);
    }

    [Fact]
    public void App_Shell_Uses_A_Visible_Segmented_Tab_Control_For_Primary_Navigation()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var themePath = GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var theme = File.ReadAllText(themePath);
        var itemStyleStart = theme.IndexOf("<Style x:Key=\"ShellSegmentedTabItemStyle\"", StringComparison.Ordinal);
        var itemStyleEnd = theme.IndexOf("<Style x:Key=\"ShellContentHostTabControlStyle\"", itemStyleStart, StringComparison.Ordinal);
        var itemStyleBlock = theme[itemStyleStart..itemStyleEnd];

        Assert.DoesNotContain("x:Name=\"HeaderHomeNavButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"HeaderMeetingsNavButton\"", xaml);
        Assert.Contains("Style=\"{StaticResource ShellSegmentedTabControlStyle}\"", xaml);
        Assert.Contains("ItemContainerStyle=\"{StaticResource ShellSegmentedTabItemStyle}\"", xaml);
        Assert.Contains("x:Key=\"ShellSegmentedTabControlStyle\"", theme);
        Assert.Contains("x:Key=\"ShellSegmentedTabItemStyle\"", theme);
        Assert.Contains("Width", itemStyleBlock);
        Assert.Contains("HorizontalContentAlignment", itemStyleBlock);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Center", itemStyleBlock);
        Assert.Contains("VerticalContentAlignment\" Value=\"Center", itemStyleBlock);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", itemStyleBlock);
    }

    [Fact]
    public void App_Shell_Defaults_To_Home_And_Uses_A_Stable_Visible_Tab_Host_For_Content_Routing()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var themePath = GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var theme = File.ReadAllText(themePath);
        var mainTabControlStart = xaml.IndexOf("<TabControl x:Name=\"MainTabControl\"", StringComparison.Ordinal);
        var mainTabControlEnd = xaml.IndexOf(">", mainTabControlStart, StringComparison.Ordinal);
        var mainTabControlTag = xaml[mainTabControlStart..mainTabControlEnd];
        var styleStart = theme.IndexOf("<Style x:Key=\"ShellSegmentedTabControlStyle\"", StringComparison.Ordinal);
        var styleEnd = theme.IndexOf("<Style x:Key=\"ShellSegmentedTabItemStyle\"", styleStart, StringComparison.Ordinal);
        var styleBlock = theme[styleStart..styleEnd];

        Assert.Contains("SelectedIndex=\"0\"", mainTabControlTag);
        Assert.DoesNotContain("ShellContentHostTabControlStyle", xaml);
        Assert.DoesNotContain("<Setter Property=\"Template\">", styleBlock);
        Assert.DoesNotContain("PART_SelectedContentHost", styleBlock);
        Assert.DoesNotContain("Visibility=\"Collapsed\"", styleBlock);
    }

    [Fact]
    public void App_Shell_Uses_Header_Level_Settings_And_Help_Host_Windows_From_The_Shared_Shell_Project()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var settingsWindowPath = GetPath("src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml");
        var helpWindowPath = GetPath("src", "AppPlatform.Shell.Wpf", "HelpHostWindow.xaml");
        var productModulePath = GetPath("src", "MeetingRecorder.Product", "MeetingRecorderProductModule.cs");

        var xaml = File.ReadAllText(xamlPath);
        var settingsWindowXaml = File.ReadAllText(settingsWindowPath);
        var helpWindowXaml = File.ReadAllText(helpWindowPath);
        var productModule = File.ReadAllText(productModulePath);

        Assert.Contains("x:Name=\"HeaderSettingsButton\"", xaml);
        Assert.Contains("x:Name=\"HeaderHelpButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"UnusedHelpContentBorder\"", xaml);

        Assert.Contains("x:Class=\"AppPlatform.Shell.Wpf.SettingsHostWindow\"", settingsWindowXaml);
        Assert.Contains("x:Name=\"SettingsSetupSectionButton\"", settingsWindowXaml);
        Assert.Contains("x:Name=\"SettingsGeneralSectionButton\"", settingsWindowXaml);
        Assert.Contains("x:Name=\"SettingsFilesSectionButton\"", settingsWindowXaml);
        Assert.Contains("x:Name=\"SettingsUpdatesSectionButton\"", settingsWindowXaml);
        Assert.Contains("x:Name=\"SettingsAdvancedSectionButton\"", settingsWindowXaml);
        Assert.Contains("Content=\"Setup\"", settingsWindowXaml);
        Assert.Contains("Content=\"General\"", settingsWindowXaml);
        Assert.Contains("Content=\"Files\"", settingsWindowXaml);
        Assert.Contains("Content=\"Updates\"", settingsWindowXaml);
        Assert.Contains("Content=\"Advanced\"", settingsWindowXaml);
        Assert.Contains("Content=\"Save Changes\"", settingsWindowXaml);
        Assert.Contains("Content=\"Close\"", settingsWindowXaml);
        Assert.Contains("Use Settings to manage setup, recording behavior, file locations, updates, and troubleshooting.", settingsWindowXaml);
        Assert.Contains("new(\"setup\", \"Setup\"", productModule);

        Assert.Contains("x:Class=\"AppPlatform.Shell.Wpf.HelpHostWindow\"", helpWindowXaml);
        Assert.Contains("Text=\"About\"", helpWindowXaml);
        Assert.Contains("Text=\"Support &amp; Troubleshooting\"", helpWindowXaml);
        Assert.Contains("Text=\"Runtime diagnostics\"", helpWindowXaml);
        Assert.Contains("x:Name=\"RuntimeDiagnosticsTextBlock\"", helpWindowXaml);
        Assert.Contains("Text=\"Version &amp; Release Notes\"", helpWindowXaml);
        Assert.Contains("Content=\"Close\"", helpWindowXaml);
    }

    [Fact]
    public void Help_Surface_Includes_Runtime_Install_Diagnostics_From_The_Running_App_And_Bundled_Manifest()
    {
        var mainWindowPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var mainWindowCode = File.ReadAllText(mainWindowPath);

        Assert.Contains("AppContext.BaseDirectory", mainWindowCode);
        Assert.Contains("MeetingRecorder.product.json", mainWindowCode);
    }

    [Fact]
    public void Settings_Speaker_Labeling_Gpu_Control_Is_OptIn_With_Test_Action()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var codePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("x:Name=\"ConfigDiarizationGpuAccelerationCheckBox\"", xaml);
        Assert.Contains("Content=\"Try GPU acceleration for speaker labeling (DirectML)\"", xaml);
        Assert.Contains("No separate user setup is required", xaml);
        Assert.DoesNotContain("Content=\"GPU acceleration unavailable for speaker labeling\"", xaml);
        Assert.DoesNotContain("Unavailable in this managed build", xaml);
        Assert.Contains("x:Name=\"TestDiarizationGpuAccelerationButton\"", xaml);
        Assert.Contains("Click=\"TestDiarizationGpuAccelerationButton_OnClick\"", xaml);

        Assert.Contains("ConfigDiarizationGpuAccelerationCheckBox.IsChecked = config.DiarizationAccelerationPreference == InferenceAccelerationPreference.Auto;", code);
        Assert.Contains("DiarizationAccelerationPreference = ConfigDiarizationGpuAccelerationCheckBox.IsChecked == true", code);
        Assert.Contains("TestDiarizationGpuAccelerationButton_OnClick", code);
        Assert.Contains("DirectML-enabled speaker-labeling runtime", code);
        Assert.DoesNotContain("last GPU probe fell back to CPU", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_Speaker_Labeling_Gpu_Test_Saves_Auto_And_Separates_Probe_Status_From_Last_Run()
    {
        var codePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var code = File.ReadAllText(codePath);

        Assert.Contains("EnableDiarizationGpuAccelerationAfterSuccessfulProbeAsync", code);
        Assert.Contains("DiarizationAccelerationPreference = InferenceAccelerationPreference.Auto", code);
        Assert.Contains("ConfigDiarizationGpuAccelerationCheckBox.IsChecked = true", code);
        Assert.Contains("Last GPU test succeeded.", code);
        Assert.Contains("Last GPU test failed", code);
        Assert.Contains("The last speaker-labeling run used CPU.", code);
        Assert.Contains("DirectML will apply to manual speaker labeling or future Throttled/Inline runs.", code);
    }

    [Fact]
    public void App_Shell_Uses_A_Global_Status_Surface_Outside_The_Home_Body()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"HeaderShellStatusBorder\"", xaml);
        Assert.Contains("x:Name=\"HeaderShellStatusLabelTextBlock\"", xaml);
        Assert.Contains("x:Name=\"HeaderShellStatusDetailTextBlock\"", xaml);
        Assert.Contains("x:Name=\"HeaderShellStatusActionButton\"", xaml);
        Assert.DoesNotContain("LOCAL-FIRST RECORDING CONSOLE", xaml);
    }

    [Fact]
    public void App_Shell_Adds_A_Compact_Header_Queue_Chip_Without_Replacing_The_Primary_Shell_Status()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"HeaderQueueStatusBorder\"", xaml);
        Assert.Contains("x:Name=\"HeaderQueueStatusLabelTextBlock\"", xaml);
        Assert.Contains("x:Name=\"HeaderQueueStatusDetailTextBlock\"", xaml);
        Assert.Contains("FontFamily=\"{StaticResource AppMonoFontFamily}\"", xaml);
        Assert.Contains("CornerRadius=\"4\"", xaml);
        Assert.Contains("x:Name=\"HeaderShellStatusBorder\"", xaml);
    }

    [Fact]
    public void Home_Page_Uses_A_Simplified_Recording_Console_With_Two_Quick_Settings()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"HomeRecordingConsoleBorder\"", xaml);
        Assert.Contains("x:Name=\"HomeQuickSettingsGrid\"", xaml);
        Assert.Contains("x:Name=\"HomeMicCaptureQuickSettingBorder\"", xaml);
        Assert.Contains("x:Name=\"HomeAutoDetectQuickSettingBorder\"", xaml);
        Assert.Contains("x:Name=\"HomeMicCaptureEnabledButton\"", xaml);
        Assert.Contains("x:Name=\"HomeMicCaptureDisabledButton\"", xaml);
        Assert.Contains("x:Name=\"HomeAutoDetectEnabledButton\"", xaml);
        Assert.Contains("x:Name=\"HomeAutoDetectDisabledButton\"", xaml);
        Assert.Contains("x:Name=\"HomePrimaryActionButton\"", xaml);
        Assert.Contains("x:Name=\"StopButton\"", xaml);
        Assert.Contains("x:Name=\"CurrentMeetingTitleTextBox\"", xaml);
        Assert.Contains("x:Name=\"CurrentMeetingProjectTextBox\"", xaml);
        Assert.Contains("x:Name=\"CurrentMeetingKeyAttendeesTextBox\"", xaml);
        Assert.Contains("x:Name=\"CurrentDetectedAudioSourceTextBlock\"", xaml);
        Assert.Contains("x:Name=\"AudioCaptureGraphCanvas\"", xaml);
        Assert.DoesNotContain("x:Name=\"HomeReadinessCard\"", xaml);
        Assert.DoesNotContain("x:Name=\"HomeNextBestActionCard\"", xaml);
        Assert.DoesNotContain("x:Name=\"HomeRecentActivityCard\"", xaml);
        Assert.DoesNotContain("x:Name=\"OpenTranscriptionSetupFromHomeButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"OpenSpeakerLabelingSetupFromHomeButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"OpenMicCaptureSettingsFromHomeButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"OpenAutoDetectSettingsFromHomeButton\"", xaml);
        Assert.DoesNotContain("x:Name=\"OpenMeetingFilesSettingsFromHomeButton\"", xaml);
        Assert.DoesNotContain("<ScrollViewer Margin=\"0,16,0,0\"", xaml);
    }

    [Fact]
    public void Home_Recording_Console_Uses_A_Stable_Wide_Layout_And_A_Single_Line_Title_Field()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var windowStart = xaml.IndexOf("<Window x:Class=\"MeetingRecorder.App.MainWindow\"", StringComparison.Ordinal);
        var windowEnd = xaml.IndexOf(">", windowStart, StringComparison.Ordinal);
        var windowTag = xaml[windowStart..windowEnd];
        var dashboardGridStart = xaml.IndexOf("<Grid x:Name=\"HomeDashboardGrid\"", StringComparison.Ordinal);
        var dashboardGridEnd = xaml.IndexOf(">", dashboardGridStart, StringComparison.Ordinal);
        var dashboardGridTag = xaml[dashboardGridStart..dashboardGridEnd];
        var recordingConsoleBorderStart = xaml.IndexOf("<Border x:Name=\"HomeRecordingConsoleBorder\"", StringComparison.Ordinal);
        var recordingConsoleBorderEnd = xaml.IndexOf(">", recordingConsoleBorderStart, StringComparison.Ordinal);
        var recordingConsoleBorderTag = xaml[recordingConsoleBorderStart..recordingConsoleBorderEnd];
        var titleTextBoxStart = xaml.IndexOf("<TextBox x:Name=\"CurrentMeetingTitleTextBox\"", StringComparison.Ordinal);
        var titleTextBoxEnd = xaml.IndexOf("/>", titleTextBoxStart, StringComparison.Ordinal);
        var titleTextBoxTag = xaml[titleTextBoxStart..titleTextBoxEnd];

        Assert.Contains("Height=\"920\"", windowTag);
        Assert.Contains("Width=\"1440\"", windowTag);
        Assert.Contains("MinWidth=\"1280\"", windowTag);
        Assert.Contains("MinHeight=\"860\"", windowTag);
        Assert.DoesNotContain("Width=\"{Binding ElementName=HomeDashboardScrollViewer, Path=ViewportWidth}\"", dashboardGridTag);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", dashboardGridTag);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", recordingConsoleBorderTag);
        Assert.Contains("MinWidth=\"1040\"", recordingConsoleBorderTag);
        Assert.Contains("AcceptsReturn=\"False\"", titleTextBoxTag);
        Assert.Contains("TextWrapping=\"NoWrap\"", titleTextBoxTag);
    }

    [Fact]
    public void Home_Page_Uses_A_Dedicated_Scroll_Viewer_So_The_Default_Window_Height_Can_Reach_The_Quick_Settings()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"HomeDashboardScrollViewer\"", xaml);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("<Grid x:Name=\"HomeDashboardGrid\"", xaml);
    }

    [Fact]
    public void Hidden_Activity_Log_TextBox_Is_ReadOnly_And_Does_Not_Keep_An_Undo_Stack()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var activityTextBoxStart = xaml.IndexOf("<TextBox x:Name=\"ActivityTextBox\"", StringComparison.Ordinal);
        var activityTextBoxEnd = xaml.IndexOf("/>", activityTextBoxStart, StringComparison.Ordinal);
        var activityTextBoxTag = xaml[activityTextBoxStart..activityTextBoxEnd];

        Assert.Contains("IsReadOnly=\"True\"", activityTextBoxTag);
        Assert.Contains("IsUndoEnabled=\"False\"", activityTextBoxTag);
        Assert.Contains("UndoLimit=\"0\"", activityTextBoxTag);
    }

    [Fact]
    public void Advanced_Settings_Expose_Background_Processing_And_Speaker_Labeling_Mode_Selectors()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"ConfigBackgroundProcessingModeComboBox\"", xaml);
        Assert.Contains("Text=\"Background processing mode\"", xaml);
        Assert.Contains("x:Name=\"ConfigBackgroundSpeakerLabelingModeComboBox\"", xaml);
        Assert.Contains("Text=\"Speaker labeling mode\"", xaml);
    }

    [Fact]
    public void General_Settings_Expose_Ai_Summary_Provider_Controls()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"AI Summaries\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryGenerationEnabledCheckBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryProviderPreferenceComboBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryModelProxyBaseUrlTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryModelProxyModelTextBox\"", xaml);
        Assert.Contains("x:Name=\"ValidateModelProxySummaryProviderButton\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryOpenAiModelTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryOpenAiKeyPasswordBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryOpenAiKeyStatusTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ValidateOpenAiSummaryProviderButton\"", xaml);
        Assert.Contains("x:Name=\"ClearOpenAiSummaryKeyButton\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryRequestTimeoutTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryTranscriptChunkTargetTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryTranscriptChunkOverlapTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigSummaryProviderStatusTextBlock\"", xaml);
        Assert.DoesNotContain("Backend override", xaml);
        Assert.DoesNotContain("Codex model", xaml);
        Assert.DoesNotContain("Local key", xaml);
        Assert.DoesNotContain("ModelProxy key:", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigSummaryModelProxyBackendTextBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigSummaryModelProxyCodexModelTextBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigSummaryModelProxyKeyPasswordBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigSummaryModelProxyKeyStatusTextBlock\"", xaml);
        Assert.DoesNotContain("x:Name=\"ClearModelProxySummaryKeyButton\"", xaml);
        Assert.DoesNotContain("Summary generation starts in the processing stage in a later sprint.", xaml);
    }

    [Fact]
    public void Meeting_Detail_Window_Exposes_Structured_Ai_Summary_Controls()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"AI Summary\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryStatusTextBlock\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryGeneratedContentPanel\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryOverviewTextBlock\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryKeyPointsItemsControl\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryDecisionsItemsControl\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryActionItemsControl\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryRisksItemsControl\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryProviderTextBlock\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryGeneratedAtTextBlock\"", xaml);
        Assert.Contains("x:Name=\"AiSummaryWarningTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ConfigureSummariesButton\"", xaml);
        Assert.Contains("x:Name=\"GenerateSummaryButton\"", xaml);
        Assert.Contains("x:Name=\"RetrySummaryButton\"", xaml);
        Assert.DoesNotContain("AiSummaryPlaceholderTextBlock", xaml);
        Assert.DoesNotContain("AI summary is reserved for a later update", xaml);
    }

    [Fact]
    public void Meeting_Detail_Window_Source_Wires_Summary_Actions()
    {
        var windowSource = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml.cs"));
        var mainWindowSource = File.ReadAllText(GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs"));

        Assert.Contains("ConfigureSummariesRequested", windowSource);
        Assert.Contains("GenerateSummaryRequested", windowSource);
        Assert.Contains("RetrySummaryRequested", windowSource);
        Assert.Contains("ConfigureSummariesButton_OnClick", windowSource);
        Assert.Contains("GenerateSummaryButton_OnClick", windowSource);
        Assert.Contains("RetrySummaryButton_OnClick", windowSource);
        Assert.Contains("GenerateOpenMeetingDetailSummaryAsync", mainWindowSource);
        Assert.Contains("OpenSettingsSurface(SettingsWindowSection.General)", mainWindowSource);
        Assert.Contains("RefreshOpenMeetingDetailWindow", mainWindowSource);
    }

    [Fact]
    public void Updates_Tab_Exposes_A_Secondary_Override_Button_For_Queued_Installs()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"InstallQueuedUpdateNowButton\"", xaml);
        Assert.Contains("Click=\"InstallQueuedUpdateNowButton_OnClick\"", xaml);
    }

    [Fact]
    public void Meetings_Tab_Uses_A_Technical_Processing_Strip_For_Queue_Status_And_Approximate_Etas()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"MeetingsProcessingStatusBorder\"", xaml);
        Assert.Contains("x:Name=\"MeetingsProcessingStatusLine1TextBlock\"", xaml);
        Assert.Contains("x:Name=\"MeetingsProcessingStatusLine2TextBlock\"", xaml);
        Assert.Contains("x:Name=\"MeetingsProcessingStatusLine3TextBlock\"", xaml);
        Assert.Contains("x:Name=\"MeetingsRefreshStateTextBlock\"", xaml);
        Assert.Contains("x:Name=\"RushBacklogButton\"", xaml);
        Assert.Contains("Click=\"RushBacklogButton_OnClick\"", xaml);
        Assert.Contains("FontFamily=\"{StaticResource AppMonoFontFamily}\"", xaml);
        Assert.Contains("Approximate processing status", xaml);
    }

    [Fact]
    public void Home_Dashboard_Uses_The_Scroll_Viewer_Viewport_Width_And_Places_Quick_Settings_Below_The_Main_Console()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var dashboardGridStart = xaml.IndexOf("<Grid x:Name=\"HomeDashboardGrid\"", StringComparison.Ordinal);
        var dashboardGridEnd = xaml.IndexOf(">", dashboardGridStart, StringComparison.Ordinal);
        var dashboardGridTag = xaml[dashboardGridStart..dashboardGridEnd];
        var quickSettingsGridStart = xaml.IndexOf("<Grid x:Name=\"HomeQuickSettingsGrid\"", StringComparison.Ordinal);
        var quickSettingsGridEnd = xaml.IndexOf(">", quickSettingsGridStart, StringComparison.Ordinal);
        var quickSettingsGridTag = xaml[quickSettingsGridStart..quickSettingsGridEnd];
        var scrollViewerStart = xaml.IndexOf("<ScrollViewer x:Name=\"HomeDashboardScrollViewer\"", StringComparison.Ordinal);
        var scrollViewerEnd = xaml.IndexOf(">", scrollViewerStart, StringComparison.Ordinal);
        var scrollViewerTag = xaml[scrollViewerStart..scrollViewerEnd];

        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", scrollViewerTag);
        Assert.DoesNotContain("Width=\"{Binding ElementName=HomeDashboardScrollViewer, Path=ViewportWidth}\"", dashboardGridTag);
        Assert.DoesNotContain("<ColumnDefinition Width=\"320\" />", xaml);
        Assert.DoesNotContain("Grid.Column=\"2\"", quickSettingsGridTag);
        Assert.Contains("Grid.Row=\"1\"", quickSettingsGridTag);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", xaml);
        Assert.Contains("Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public void Home_Dashboard_Stretches_Inside_The_Scroll_Viewer_Without_A_Direct_ViewportWidth_Binding()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var scrollViewerStart = xaml.IndexOf("<ScrollViewer x:Name=\"HomeDashboardScrollViewer\"", StringComparison.Ordinal);
        var scrollViewerEnd = xaml.IndexOf(">", scrollViewerStart, StringComparison.Ordinal);
        var scrollViewerTag = xaml[scrollViewerStart..scrollViewerEnd];
        var dashboardGridStart = xaml.IndexOf("<Grid x:Name=\"HomeDashboardGrid\"", StringComparison.Ordinal);
        var dashboardGridEnd = xaml.IndexOf(">", dashboardGridStart, StringComparison.Ordinal);
        var dashboardGridTag = xaml[dashboardGridStart..dashboardGridEnd];

        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", scrollViewerTag);
        Assert.DoesNotContain("Width=\"{Binding ElementName=HomeDashboardScrollViewer, Path=ViewportWidth}\"", dashboardGridTag);
    }

    [Fact]
    public void Home_Quick_Settings_Use_Compact_Copy_And_Show_An_Always_On_Selected_State()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var quickSettingStyleStart = xaml.IndexOf("<Style x:Key=\"QuickSettingToggleButtonStyle\"", StringComparison.Ordinal);
        var quickSettingStyleEnd = xaml.IndexOf("</Style>", quickSettingStyleStart, StringComparison.Ordinal) + "</Style>".Length;
        var quickSettingStyleBlock = xaml[quickSettingStyleStart..quickSettingStyleEnd];

        Assert.DoesNotContain("Text=\"Future recordings\"", xaml);
        Assert.DoesNotContain("Text=\"Supported meeting watch\"", xaml);
        Assert.Contains("HomeMicCaptureQuickSettingSummaryTextBlock", xaml);
        Assert.Contains("HomeAutoDetectQuickSettingSummaryTextBlock", xaml);
        Assert.Contains("<Trigger Property=\"Tag\" Value=\"Active\">", quickSettingStyleBlock);
        Assert.Contains("AppAccentBrush", quickSettingStyleBlock);
        Assert.Contains("AppAccentForegroundBrush", quickSettingStyleBlock);
        Assert.Contains("Property=\"Width\" Value=\"72\"", quickSettingStyleBlock);
        Assert.DoesNotContain("<WrapPanel Grid.Row=\"1\"", xaml);
        Assert.Contains("x:Name=\"HomeMicCaptureToggleGrid\"", xaml);
        Assert.Contains("x:Name=\"HomeAutoDetectToggleGrid\"", xaml);
    }

    [Fact]
    public void Home_Fields_Mark_Title_As_Required_And_Project_And_Attendees_As_Optional()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"SESSION TITLE (REQUIRED)\"", xaml);
        Assert.Contains("Text=\"CLIENT / PROJECT (OPTIONAL)\"", xaml);
        Assert.Contains("Text=\"KEY ATTENDEES (OPTIONAL)\"", xaml);
        Assert.DoesNotContain("Required. Client / project and key attendees are optional.", xaml);
    }

    [Fact]
    public void Home_Page_Includes_A_Live_Recording_Elapsed_Time_Readout()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"RECORDING TIME\"", xaml);
        Assert.Contains("x:Name=\"CurrentRecordingElapsedTextBlock\"", xaml);
    }

    [Fact]
    public void Home_Recording_Console_And_Quick_Settings_Use_The_Full_Tab_Content_Width()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var themePath = GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var theme = File.ReadAllText(themePath);
        var scrollViewerStart = xaml.IndexOf("<ScrollViewer x:Name=\"HomeDashboardScrollViewer\"", StringComparison.Ordinal);
        var scrollViewerEnd = xaml.IndexOf(">", scrollViewerStart, StringComparison.Ordinal);
        var scrollViewerTag = xaml[scrollViewerStart..scrollViewerEnd];
        var controlStyleStart = theme.IndexOf("<Style x:Key=\"ShellSegmentedTabControlStyle\"", StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", scrollViewerTag);
        Assert.Contains("PART_SelectedContentHost", theme);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", theme);
    }

    [Fact]
    public void Header_Shell_Status_Uses_A_Stable_Reserved_Footprint()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var statusBorderStart = xaml.IndexOf("<Border x:Name=\"HeaderShellStatusBorder\"", StringComparison.Ordinal);
        var statusBorderEnd = xaml.IndexOf(">", statusBorderStart, StringComparison.Ordinal);
        var statusBorderTag = xaml[statusBorderStart..statusBorderEnd];
        var detailTextStart = xaml.IndexOf("<TextBlock x:Name=\"HeaderShellStatusDetailTextBlock\"", StringComparison.Ordinal);
        var detailTextEnd = xaml.IndexOf(">", detailTextStart, StringComparison.Ordinal);
        var detailTextTag = xaml[detailTextStart..detailTextEnd];

        Assert.Contains("MinWidth=\"390\"", statusBorderTag);
        Assert.Contains("MinHeight=\"44\"", statusBorderTag);
        Assert.Contains("Width=\"180\"", detailTextTag);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", detailTextTag);
    }

    [Fact]
    public void Settings_Content_Is_Split_Into_Owned_Section_Panels_Within_The_Detached_Settings_Body()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"SettingsBodyContentBorder\"", xaml);
        Assert.Contains("x:Name=\"SettingsSetupSectionPanel\"", xaml);
        Assert.Contains("x:Name=\"SettingsGeneralSectionPanel\"", xaml);
        Assert.Contains("x:Name=\"SettingsFilesSectionPanel\"", xaml);
        Assert.Contains("x:Name=\"SettingsUpdatesSectionPanel\"", xaml);
        Assert.Contains("x:Name=\"SettingsAdvancedSectionPanel\"", xaml);
        Assert.DoesNotContain("x:Name=\"TranscriptionSetupBodyContentBorder\"", xaml);
        Assert.DoesNotContain("x:Name=\"SpeakerLabelingSetupBodyContentBorder\"", xaml);
        Assert.DoesNotContain("x:Name=\"SaveConfigButton\"", xaml);
    }

    [Fact]
    public void Setup_Settings_Include_ThirdParty_Focused_Teams_Integration_Probe_Controls()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var mainWindowCodePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var source = File.ReadAllText(mainWindowCodePath);

        Assert.Contains("Text=\"Teams Integration Probe\"", xaml);
        Assert.Contains("x:Name=\"ConfigPreferredTeamsIntegrationModeComboBox\"", xaml);
        Assert.Contains("x:Name=\"RunTeamsIntegrationProbeButton\"", xaml);
        Assert.Contains("x:Name=\"OpenTeamsThirdPartyApiGuideButton\"", xaml);
        Assert.Contains("x:Name=\"ConfigTeamsIntegrationStatusTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ConfigTeamsIntegrationDetailTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ConfigTeamsIntegrationBaselineTextBlock\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigTeamsGraphTenantIdTextBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigTeamsGraphClientIdTextBox\"", xaml);
        Assert.DoesNotContain("Graph calendar", source);
        Assert.DoesNotContain("Graph calendar + onlineMeeting", source);
    }

    [Fact]
    public void Setup_Settings_Show_Teams_Probe_Metadata_For_Outcome_Last_Probe_And_Promotable_Path()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var mainWindowCodePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var xaml = File.ReadAllText(xamlPath);
        var source = File.ReadAllText(mainWindowCodePath);

        Assert.Contains("x:Name=\"ConfigTeamsIntegrationMetadataTextBlock\"", xaml);
        Assert.Contains("Last probe:", source);
        Assert.Contains("Promotable path:", source);
        Assert.Contains("Block reason:", source);
    }

    [Fact]
    public void Meetings_Inspector_Includes_Detected_Audio_Source_Field()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MeetingDetailWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("Text=\"Detected Audio Source\"", xaml);
        Assert.Contains("x:Name=\"DetectedAudioSourceTextBlock\"", xaml);
    }

    [Fact]
    public void Meetings_Group_By_Control_Offers_Client_Project_And_Attendee_Options()
    {
        var mainWindowCodePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var source = File.ReadAllText(mainWindowCodePath);

        Assert.Contains("new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.ClientProject, \"Client / project\")", source);
        Assert.Contains("new SelectionOption<MeetingsGroupKey>(MeetingsGroupKey.Attendee, \"Attendee\")", source);
    }

    [Fact]
    public void Settings_Window_Uses_A_Full_Width_Section_Button_Strip_With_A_Stable_Default_Selection()
    {
        var settingsWindowPath = GetPath("src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml");
        var themePath = GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml");

        var settingsWindowXaml = File.ReadAllText(settingsWindowPath);
        var theme = File.ReadAllText(themePath);
        var styleStart = theme.IndexOf("<Style x:Key=\"DialogSectionNavButtonStyle\"", StringComparison.Ordinal);
        var styleEnd = theme.IndexOf("<Style x:Key=\"PrimaryActionButtonStyle\"", styleStart, StringComparison.Ordinal);
        var styleBlock = theme[styleStart..styleEnd];

        Assert.Contains("x:Name=\"SettingsSectionButtonStrip\"", settingsWindowXaml);
        Assert.Contains("<UniformGrid", settingsWindowXaml);
        Assert.Contains("Columns=\"5\"", settingsWindowXaml);
        Assert.Contains("Tag=\"Active\"", settingsWindowXaml);
        Assert.DoesNotContain("<StackPanel x:Name=\"SettingsSectionButtonStrip\"", settingsWindowXaml);
        Assert.DoesNotContain("x:Name=\"SettingsTabControl\"", settingsWindowXaml);
        Assert.DoesNotContain("MinWidth", styleBlock);
        Assert.Contains("Tag\" Value=\"Active", styleBlock);
    }

    [Fact]
    public void Shared_Combo_Box_Chrome_Keeps_Selection_Text_Vertically_Centered()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");
        var themePath = GetPath("src", "AppPlatform.Shell.Wpf", "ShellTheme.xaml");

        var xaml = File.ReadAllText(xamlPath);
        var theme = File.ReadAllText(themePath);
        var comboBoxStyleStart = theme.IndexOf("<Style x:Key=\"ShellFilterComboBoxStyle\"", StringComparison.Ordinal);
        var comboBoxStyleEnd = theme.IndexOf("</Style>", comboBoxStyleStart, StringComparison.Ordinal) + "</Style>".Length;
        var comboBoxStyleBlock = theme[comboBoxStyleStart..comboBoxStyleEnd];

        Assert.Contains("VerticalContentAlignment\" Value=\"Center", comboBoxStyleBlock);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Left", comboBoxStyleBlock);
        Assert.Contains("MinHeight\" Value=\"32", comboBoxStyleBlock);
        Assert.Contains("VerticalAlignment=\"Center\"", comboBoxStyleBlock);
        Assert.DoesNotContain("VerticalAlignment=\"Top\"", comboBoxStyleBlock);
        Assert.Contains("Style=\"{StaticResource ShellFilterComboBoxStyle}\"", xaml);
        Assert.Contains("x:Name=\"SelectedMeetingProjectComboBox\"", xaml);
        Assert.Equal(5, CountOccurrences(xaml, "Style=\"{StaticResource ShellFilterComboBoxStyle}\""));
    }

    [Fact]
    public void Detached_Setup_Bodies_Are_Removed_From_The_Main_Window()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("x:Name=\"TranscriptionSetupBodyContentBorder\"", xaml);
        Assert.DoesNotContain("x:Name=\"SpeakerLabelingSetupBodyContentBorder\"", xaml);
        Assert.DoesNotContain("x:Name=\"TranscriptionSetupSectionBorder\"", xaml);
        Assert.DoesNotContain("x:Name=\"SpeakerLabelingSetupSectionBorder\"", xaml);
    }

    [Fact]
    public void Setup_Settings_Expose_Curated_Standard_And_HigherAccuracy_Profile_Actions_For_Both_Model_Capabilities()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"UseStandardTranscriptionProfileButton\"", xaml);
        Assert.Contains("x:Name=\"UseHighAccuracyTranscriptionProfileButton\"", xaml);
        Assert.Contains("x:Name=\"UseStandardSpeakerLabelingProfileButton\"", xaml);
        Assert.Contains("x:Name=\"UseHighAccuracySpeakerLabelingProfileButton\"", xaml);
        Assert.Contains("x:Name=\"SkipSpeakerLabelingForNowButton\"", xaml);
        Assert.Contains("Content=\"Use Standard\"", xaml);
        Assert.Contains("Content=\"Use Higher Accuracy\"", xaml);
        Assert.Contains("Content=\"Skip for now\"", xaml);
        Assert.Contains("Import approved file", xaml);
    }

    [Fact]
    public void Home_Recording_And_AutoDetect_Stay_Gated_Behind_Transcription_Readiness()
    {
        var mainWindowCodePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        var source = File.ReadAllText(mainWindowCodePath);

        Assert.Contains("if (!HasReadyTranscriptionModel())", source, StringComparison.Ordinal);
        Assert.Contains("ShowTranscriptionSetupRequiredMessage();", source, StringComparison.Ordinal);
        Assert.Contains("HasReadyTranscriptionModel() &&", source, StringComparison.Ordinal);
        Assert.Contains("Blocked until transcription is ready. Finish Setup before automatic meeting detection can start.", source, StringComparison.Ordinal);
        Assert.Contains("Finish Setup before auto-detect can turn on.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Advanced_Settings_Replace_Raw_Model_Path_TextBoxes_With_ReadOnly_Model_Storage_Diagnostics()
    {
        var xamlPath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("x:Name=\"ConfigModelCacheDirTextBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigTranscriptionModelPathTextBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"ConfigDiarizationAssetPathTextBox\"", xaml);
        Assert.Contains("x:Name=\"ConfigModelStorageSummaryTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ConfigTranscriptionStorageTextBlock\"", xaml);
        Assert.Contains("x:Name=\"ConfigSpeakerLabelingStorageTextBlock\"", xaml);
    }

    private static string GetPath(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
