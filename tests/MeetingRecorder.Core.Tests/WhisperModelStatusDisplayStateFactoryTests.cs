using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class WhisperModelStatusDisplayStateFactoryTests
{
    [Fact]
    public void Create_Returns_Healthy_State_For_Valid_Model()
    {
        var status = new WhisperModelStatus(
            @"C:\models\ggml-base.bin",
            WhisperModelStatusKind.Valid,
            150_000_000,
            "The configured Whisper model file looks valid.");

        var displayState = WhisperModelStatusDisplayStateFactory.Create(status);

        Assert.Equal("Model status: ready", displayState.StatusText);
        Assert.True(displayState.IsHealthy);
        Assert.Null(displayState.DashboardBannerText);
        Assert.Contains("150 MB", displayState.DetailsText);
    }

    [Fact]
    public void Create_Returns_Banner_For_Invalid_Model()
    {
        var status = new WhisperModelStatus(
            @"C:\models\ggml-base.bin",
            WhisperModelStatusKind.Invalid,
            8_350,
            "The configured Whisper model file is invalid.");

        var displayState = WhisperModelStatusDisplayStateFactory.Create(status);

        Assert.Equal("Model status: invalid", displayState.StatusText);
        Assert.False(displayState.IsHealthy);
        Assert.NotNull(displayState.DashboardBannerText);
        Assert.Contains("Models tab", displayState.DashboardBannerText);
        Assert.Contains("8.35 KB", displayState.DetailsText);
    }
}
