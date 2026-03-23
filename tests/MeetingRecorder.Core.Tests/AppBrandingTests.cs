using MeetingRecorder.Core.Branding;
using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class AppBrandingTests
{
    [Fact]
    public void Branding_Exposes_Release_0_3_Metadata_From_Assembly_Metadata()
    {
        var informationalVersion = typeof(AppBranding).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal("Meeting Recorder", AppBranding.ProductName);
        Assert.Equal("0.3", AppBranding.Version);
        Assert.Equal("0.3", informationalVersion);
        Assert.Equal(AppBranding.Version, informationalVersion);
        Assert.Equal("Meeting Recorder v0.3", AppBranding.DisplayNameWithVersion);
        Assert.Equal("Pranav Sharma", AppBranding.AuthorName);
        Assert.Equal("shp847@gmail.com", AppBranding.AuthorEmail);
    }

    [Fact]
    public void Branding_Exposes_Recording_Legal_Notice()
    {
        Assert.Contains("comply with all applicable recording", AppBranding.RecordingLegalNotice);
        Assert.Contains("Tell participants", AppBranding.RecordingLegalNotice);
        Assert.Contains("obtain consent where required", AppBranding.RecordingLegalNotice);
        Assert.Contains("not legal advice", AppBranding.RecordingLegalNotice);
    }
}
