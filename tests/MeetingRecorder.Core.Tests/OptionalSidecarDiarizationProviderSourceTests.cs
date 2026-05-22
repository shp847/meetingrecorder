using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class OptionalSidecarDiarizationProviderSourceTests
{
    [Fact]
    public void Worker_Attempts_DirectMl_Only_For_Auto_And_Falls_Back_To_Cpu()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));

        Assert.Contains("InferenceAccelerationPreference.Auto", source, StringComparison.Ordinal);
        Assert.Contains("DiarizationExecutionProvider.Directml", source, StringComparison.Ordinal);
        Assert.Contains("DiarizationExecutionProvider.Cpu", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception) when (exception is not OperationCanceledException)", source, StringComparison.Ordinal);
        Assert.Contains("IsSherpaDirectMlRuntimeEnabled", source, StringComparison.Ordinal);
        Assert.Contains("DirectML", source, StringComparison.Ordinal);
        Assert.Contains("fallback", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Worker_Detects_When_Bundled_Sherpa_Runtime_Cannot_Enable_DirectMl()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));
        var program = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "Program.cs"));

        Assert.Contains("DirectML is for Windows only", source, StringComparison.Ordinal);
        Assert.Contains("Failed to enable DirectML", source, StringComparison.Ordinal);
        Assert.Contains("DirectML-enabled speaker-labeling runtime", source, StringComparison.Ordinal);
        Assert.Contains("DirectML-enabled speaker-labeling runtime", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Searches_Higher_Thresholds_For_OverSegmented_Clusters()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));

        Assert.Contains("CollapsedSpeakerClusteringThresholds", source, StringComparison.Ordinal);
        Assert.Contains("OverSegmentedSpeakerClusteringThresholds", source, StringComparison.Ordinal);
        Assert.Contains("0.55f", source, StringComparison.Ordinal);
        Assert.Contains("0.8f", source, StringComparison.Ordinal);
        Assert.Contains("IsAutomaticSpeakerCountSupported", source, StringComparison.Ordinal);
        Assert.DoesNotContain("currentSelection.SupportedSpeakerCount >= 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Probe_Command_Does_Not_Require_A_Manifest()
    {
        var program = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "Program.cs"));

        Assert.Contains("--probe-directml", program, StringComparison.Ordinal);
        Assert.Contains("ProbeDirectMl", program, StringComparison.Ordinal);
        Assert.Contains("DirectML probe succeeded.", program, StringComparison.Ordinal);
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
}
