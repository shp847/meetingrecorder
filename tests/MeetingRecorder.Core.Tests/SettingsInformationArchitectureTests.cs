using MeetingRecorder.App;

namespace MeetingRecorder.Core.Tests;

public sealed class SettingsInformationArchitectureTests
{
    [Theory]
    [InlineData("setup", 0, null)]
    [InlineData("recording", 1, null)]
    [InlineData("processing", 2, null)]
    [InlineData("summaries", 3, null)]
    [InlineData("files-and-updates", 4, null)]
    [InlineData("advanced", 5, null)]
    [InlineData("general", 1, null)]
    [InlineData("files", 4, null)]
    [InlineData("updates", 4, "CheckForUpdatesButton")]
    public void ResolveRoute_Preserves_New_And_Legacy_Routes(
        string route,
        int expectedSection,
        string? expectedControl)
    {
        var target = SettingsInformationArchitecture.ResolveRoute(route);

        Assert.True(target.IsKnownRoute);
        Assert.Equal((SettingsWindowSection)expectedSection, target.Section);
        Assert.Equal(expectedControl, target.ControlId);
    }

    [Fact]
    public void ResolveRoute_Uses_Recording_For_An_Unknown_Route_Without_Claiming_It_Is_Valid()
    {
        var target = SettingsInformationArchitecture.ResolveRoute("not-a-settings-section");

        Assert.Equal(SettingsWindowSection.Recording, target.Section);
        Assert.False(target.IsKnownRoute);
        Assert.Null(target.ControlId);
    }

    [Fact]
    public void Every_Configurable_Control_Has_Exactly_One_Section_And_Conservative_Timing()
    {
        var controls = SettingsInformationArchitecture.Controls;

        Assert.NotEmpty(controls);
        Assert.Equal(
            controls.Count,
            controls.Select(control => control.ControlId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(controls, control =>
        {
            Assert.False(string.IsNullOrWhiteSpace(control.ControlId));
            Assert.True(Enum.IsDefined(control.Section));
            Assert.True(Enum.IsDefined(control.ApplyTiming));
        });
    }

    [Fact]
    public void ResolveControl_Provides_A_Visible_Section_Without_Config_Mutation()
    {
        var target = SettingsInformationArchitecture.ResolveControl("ConfigMicCaptureCheckBox");

        Assert.True(target.IsKnownRoute);
        Assert.Equal(SettingsWindowSection.Recording, target.Section);
        Assert.Equal("ConfigMicCaptureCheckBox", target.ControlId);
    }

    [Theory]
    [InlineData("ConfigAutoDetectThresholdTextBox")]
    [InlineData("ConfigMeetingStopTimeoutTextBox")]
    public void Recording_Assistance_Custom_Controls_Route_To_Recording(string controlId)
    {
        var target = SettingsInformationArchitecture.ResolveControl(controlId);

        Assert.True(target.IsKnownRoute);
        Assert.Equal(SettingsWindowSection.Recording, target.Section);
        Assert.Equal(controlId, target.ControlId);
    }
}
