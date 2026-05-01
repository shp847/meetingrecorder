using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingWorkspaceRefreshXamlTests
{
    [Fact]
    public void Meetings_Tab_Includes_Project_Metadata_Controls_And_Group_Expansion_Actions()
    {
        var mainWindowXamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.App",
            "MainWindow.xaml"));
        var detailWindowXamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.App",
            "MeetingDetailWindow.xaml"));

        var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
        var detailWindowXaml = File.ReadAllText(detailWindowXamlPath);

        Assert.Contains("x:Name=\"ExpandAllMeetingGroupsButton\"", mainWindowXaml);
        Assert.Contains("x:Name=\"CollapseAllMeetingGroupsButton\"", mainWindowXaml);
        Assert.Contains("x:Name=\"OpenMeetingDetailsButton\"", mainWindowXaml);
        Assert.Contains("x:Name=\"ProjectComboBox\"", detailWindowXaml);
        Assert.Contains("x:Name=\"ApplyProjectButton\"", detailWindowXaml);
        Assert.Contains("x:Name=\"ClearProjectButton\"", detailWindowXaml);
        Assert.Contains("x:Name=\"ProjectTextBlock\"", detailWindowXaml);
        Assert.Contains("Header=\"Project\"", mainWindowXaml);
        Assert.Contains("Header=\"Started\"", mainWindowXaml);

        Assert.True(
            mainWindowXaml.IndexOf("Header=\"Project\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Header=\"Started\"", StringComparison.Ordinal));
        Assert.True(
            mainWindowXaml.IndexOf("Header=\"Transcript\"", StringComparison.Ordinal) <
            mainWindowXaml.LastIndexOf("Header=\"Recommended\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Meetings_Tab_Uses_A_Full_Width_Library_With_A_Detail_Window_Entry_Point()
    {
        var mainWindowXamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.App",
            "MainWindow.xaml"));
        var detailWindowXamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.App",
            "MeetingDetailWindow.xaml"));

        var mainWindowXaml = File.ReadAllText(mainWindowXamlPath);
        var detailWindowXaml = File.ReadAllText(detailWindowXamlPath);
        var splitterStart = mainWindowXaml.IndexOf("x:Name=\"MeetingsWorkspaceGridSplitter\"", StringComparison.Ordinal);
        var splitterEnd = mainWindowXaml.IndexOf("/>", splitterStart, StringComparison.Ordinal);
        var splitterBlock = mainWindowXaml[splitterStart..(splitterEnd + 2)];

        Assert.Contains("x:Name=\"MeetingsContentGrid\"", mainWindowXaml);
        Assert.Contains("x:Name=\"MeetingSelectionCommandBarBorder\"", mainWindowXaml);
        Assert.Contains("x:Name=\"OpenMeetingDetailsButton\"", mainWindowXaml);
        Assert.Contains("MouseDoubleClick=\"MeetingsDataGrid_OnMouseDoubleClick\"", mainWindowXaml);
        Assert.Contains("<ScrollViewer x:Name=\"MeetingsInspectorScrollViewer\"", mainWindowXaml);
        Assert.Contains("Visibility=\"Collapsed\"", splitterBlock);
        Assert.Contains("x:Name=\"TranscriptSegmentsListBox\"", detailWindowXaml);
        Assert.Contains("AI summary is reserved", File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.Core",
            "Services",
            "MainWindowInteractionLogic.cs"))));
    }

    [Fact]
    public void Config_Optional_Helpers_Expose_Attendee_Enrichment_Setting()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "MeetingRecorder.App",
            "MainWindow.xaml"));

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("x:Name=\"ConfigMeetingAttendeeEnrichmentCheckBox\"", xaml);
        Assert.Contains("Capture attendees from Outlook when available", xaml);
        Assert.Contains("Live Teams roster capture stays off by default until you turn it on.", xaml);
    }
}
