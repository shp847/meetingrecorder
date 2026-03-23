using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingWorkspaceRefreshXamlTests
{
    [Fact]
    public void Meetings_Tab_Includes_Project_Metadata_Controls_And_Group_Expansion_Actions()
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

        Assert.Contains("x:Name=\"ExpandAllMeetingGroupsButton\"", xaml);
        Assert.Contains("x:Name=\"CollapseAllMeetingGroupsButton\"", xaml);
        Assert.Contains("x:Name=\"SelectedMeetingProjectComboBox\"", xaml);
        Assert.Contains("x:Name=\"ApplyMeetingProjectButton\"", xaml);
        Assert.Contains("x:Name=\"ClearMeetingProjectButton\"", xaml);
        Assert.Contains("x:Name=\"SelectedMeetingInspectorProjectTextBlock\"", xaml);
        Assert.Contains("Header=\"Project\"", xaml);
        Assert.Contains("Header=\"Started\"", xaml);

        Assert.True(
            xaml.IndexOf("Header=\"Project\"", StringComparison.Ordinal) <
            xaml.IndexOf("Header=\"Started\"", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("Header=\"Transcript\"", StringComparison.Ordinal) <
            xaml.LastIndexOf("Header=\"Recommended\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Meetings_Tab_Uses_A_Split_Workspace_So_The_List_Remains_Visible_Alongside_Inspector_Tools()
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
        var splitterStart = xaml.IndexOf("x:Name=\"MeetingsWorkspaceGridSplitter\"", StringComparison.Ordinal);
        var splitterEnd = xaml.IndexOf("/>", splitterStart, StringComparison.Ordinal);
        var splitterBlock = xaml[splitterStart..(splitterEnd + 2)];

        Assert.Contains("x:Name=\"MeetingsContentGrid\"", xaml);
        Assert.Contains("x:Name=\"MeetingsInspectorScrollViewer\"", xaml);
        Assert.Contains("x:Name=\"MeetingsWorkspaceGridSplitter\"", xaml);
        Assert.Contains("Grid.Column=\"0\"", xaml);
        Assert.Contains("Grid.Column=\"2\"", xaml);
        Assert.DoesNotContain("<ScrollViewer Grid.Row=\"3\"", xaml);
        Assert.DoesNotContain("Visibility=\"Collapsed\"", splitterBlock);
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
