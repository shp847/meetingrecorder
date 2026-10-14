using System.IO;
using System.Diagnostics;
using System.Text.Json;
using MeetingRecorder.ProcessingWorker;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Domain;

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
        var calibrationSource = File.ReadAllText(GetPath("src", "MeetingRecorder.Core", "Services", "DiarizationCalibrationEnvironment.cs"));

        Assert.Contains("LoadDiarizationThresholdOptions", source, StringComparison.Ordinal);
        Assert.Contains("CollapsedSpeakerClusteringThresholds", source, StringComparison.Ordinal);
        Assert.Contains("OverSegmentedSpeakerClusteringThresholds", source, StringComparison.Ordinal);
        Assert.Contains("0.55f", calibrationSource, StringComparison.Ordinal);
        Assert.Contains("0.8f", calibrationSource, StringComparison.Ordinal);
        Assert.Contains("0.95f", calibrationSource, StringComparison.Ordinal);
        Assert.Contains("0.99f", calibrationSource, StringComparison.Ordinal);
        Assert.Contains("IsAutomaticSpeakerCountSupported", source, StringComparison.Ordinal);
        Assert.DoesNotContain("currentSelection.SupportedSpeakerCount >= 2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return currentSelection;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Merges_Similar_Speaker_Clusters_Without_A_Speaker_Count_Hint()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));

        Assert.Contains("SpeakerClusterMergeService", source, StringComparison.Ordinal);
        Assert.Contains("DiarizationOversegmentedClusterRecoveryService", source, StringComparison.Ordinal);
        Assert.Contains("ShouldExtractSpeakerVoiceSamples", source, StringComparison.Ordinal);
        Assert.Contains("MergeSimilarClusters", source, StringComparison.Ordinal);
        Assert.Contains("RemapVoiceSamples", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ITargetedDiarizationProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiarizationSpeakerCountHint", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Attempts_Cluster_Merge_Recovery_Before_Skipping_OverSegmented_Labels()
    {
        var source = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));

        var recoveryIndex = source.IndexOf("_oversegmentedClusterRecoveryService.TryRecover", StringComparison.Ordinal);
        var skipIndex = source.IndexOf("Speaker labeling skipped. Provider=", StringComparison.Ordinal);

        Assert.True(recoveryIndex > 0);
        Assert.True(skipIndex > recoveryIndex);
        Assert.Contains("skipped without CPU retry", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DirectML speaker clustering was outside the supported automatic range; speaker labeling retried on CPU.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Probe_Command_Does_Not_Require_A_Manifest()
    {
        var program = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "Program.cs"));

        Assert.Contains("--probe-directml", program, StringComparison.Ordinal);
        Assert.Contains("ProbeDirectMl", program, StringComparison.Ordinal);
        Assert.Contains("DirectML probe succeeded.", program, StringComparison.Ordinal);
        Assert.Contains("WriteDirectMlProbeStatusAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_Exposes_Cpu_Diarization_Model_Probe_Command()
    {
        var program = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "Program.cs"));
        var provider = File.ReadAllText(GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs"));

        Assert.Contains("--probe-diarization-cpu", program, StringComparison.Ordinal);
        Assert.Contains("ProbeCpu", program, StringComparison.Ordinal);
        Assert.Contains("CPU speaker-labeling probe succeeded.", program, StringComparison.Ordinal);
        Assert.Contains("internal static void ProbeCpu", provider, StringComparison.Ordinal);
        Assert.Contains("\"cpu\"", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedWorker_Helper_Preserves_Dotnet_Entry_Assembly()
    {
        var dotnetHost = new ProcessStartInfo("C:\\Program Files\\dotnet\\dotnet.exe");
        BoundedLocalDiarizationProvider.ConfigureHelperHostArguments(
            dotnetHost,
            dotnetHost.FileName,
            "C:\\fixtures\\MeetingRecorder.ProcessingWorker.dll");
        Assert.Equal(new[] { "C:\\fixtures\\MeetingRecorder.ProcessingWorker.dll" }, dotnetHost.ArgumentList);

        var appHost = new ProcessStartInfo("C:\\fixtures\\MeetingRecorder.ProcessingWorker.exe");
        BoundedLocalDiarizationProvider.ConfigureHelperHostArguments(
            appHost,
            appHost.FileName,
            "C:\\fixtures\\MeetingRecorder.ProcessingWorker.dll");
        Assert.Empty(appHost.ArgumentList);
    }

    [Fact]
    public void BoundedWorker_Helper_Result_RoundTrips_Without_Ambiguous_Constructors()
    {
        var expected = new DiarizationResult(
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, "text")],
            false,
            "no labels");

        var serialized = JsonSerializer.Serialize(LocalDiarizationHelperResult.From(expected), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<LocalDiarizationHelperResult>(serialized, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        var roundTripped = restored.ToDiarizationResult();
        Assert.Equal(expected.AppliedSpeakerLabels, roundTripped.AppliedSpeakerLabels);
        Assert.Equal(expected.Message, roundTripped.Message);
        Assert.Equal(expected.Segments, roundTripped.Segments);
    }

    [Theory]
    [InlineData("Unable to load sherpa-onnx-c-api.dll", "native_runtime")]
    [InlineData("Sherpa model asset is unavailable", "model_or_asset")]
    [InlineData("Diarization model bundle is unavailable", "model_or_asset")]
    [InlineData("Could not find part of path", "input_or_file")]
    [InlineData("helper_failure phase=audio_preparation exception=file_not_found", "audio_preparation_file_not_found")]
    [InlineData("helper_failure phase=audio_preparation exception=invalid_operation", "audio_preparation_invalid_operation")]
    [InlineData("helper_failure phase=audio_preparation exception=audio_open_decoder", "audio_preparation_audio_open_decoder")]
    [InlineData("helper_failure phase=audio_preparation exception=naudio", "audio_preparation_naudio")]
    [InlineData("helper_failure phase=config exception=not_supported", "helper_startup_not_supported")]
    [InlineData("helper_failure phase=native_inference exception=other", "native_inference_other")]
    [InlineData("helper_failure phase=completed exception=io", "runtime_status_io")]
    [InlineData("", "no_diagnostic")]
    public void BoundedWorker_Helper_Redacts_Diagnostic_To_Fixed_Class(string error, string expected)
    {
        Assert.Equal(expected, BoundedLocalDiarizationProvider.ClassifyHelperFailure(error));
    }

    [Fact]
    public void BoundedWorker_Helper_Uses_Observed_Native_Phase_For_Model_Diagnostics()
    {
        Assert.Equal(
            "native_inference_model_or_asset",
            BoundedLocalDiarizationProvider.ClassifyHelperFailure("native model failure", "Native inference"));
    }

    [Fact]
    public void Worker_Progress_Snapshot_Failure_Does_Not_Abort_Processing()
    {
        var progress = new DiarizationProgress("Native inference", "CPU", 1, null, null, null, null, null, DateTimeOffset.UtcNow);

        Assert.False(Program.TryWriteHelperProgress("\0", progress));
    }

    [Fact]
    public void Worker_Helper_Reads_Web_Json_Request_Properties()
    {
        var segment = new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "speaker_01", "sample");
        var serialized = JsonSerializer.Serialize(
            new LocalDiarizationHelperRequest("C:\\fixtures\\input.m4a", new[] { segment }, "C:\\fixtures\\progress.json"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var request = Program.DeserializeLocalDiarizationHelperRequest(serialized);

        Assert.NotNull(request);
        Assert.Equal("C:\\fixtures\\input.m4a", request.AudioPath);
        Assert.Equal("C:\\fixtures\\progress.json", request.ProgressPath);
        Assert.Single(request.TranscriptSegments);
        Assert.Equal(segment, request.TranscriptSegments[0]);
    }

    [Fact]
    public void Worker_Prepared_Audio_Path_Excludes_Source_File_Name()
    {
        var path = LocalSpeakerDiarizationProvider.BuildPreparedAudioPath("C:\\fixtures\\private-meeting-title.m4a");

        Assert.Equal(".wav", Path.GetExtension(path));
        Assert.Contains("diarization-", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.DoesNotContain("private-meeting-title", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Worker_Bounds_Long_Native_Inference_Into_Contiguous_Windows()
    {
        const int sampleRate = 16_000;
        var windows = LocalSpeakerDiarizationProvider.BuildNativeInferenceWindows(
            sampleRate * (5 * 60 * 2 + 3),
            sampleRate);

        Assert.Collection(
            windows,
            window => Assert.Equal(new LocalSpeakerDiarizationProvider.NativeInferenceWindow(0, 0, sampleRate * 5 * 60), window),
            window => Assert.Equal(new LocalSpeakerDiarizationProvider.NativeInferenceWindow(1, sampleRate * 5 * 60, sampleRate * 5 * 60), window),
            window => Assert.Equal(new LocalSpeakerDiarizationProvider.NativeInferenceWindow(2, sampleRate * 5 * 60 * 2, sampleRate * 3), window));
        Assert.Equal(windows[0].EndSample, windows[1].StartSample);
        Assert.Equal(windows[1].EndSample, windows[2].StartSample);
    }

    [Fact]
    public void Worker_Namespaces_And_Normalizes_Window_Local_Speaker_Ids()
    {
        var namespaced = LocalSpeakerDiarizationProvider.NamespaceWindowSpeakerTurns(
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(10)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
            ],
            new LocalSpeakerDiarizationProvider.NativeInferenceWindow(2, 16_000 * 300 * 2, 16_000 * 300),
            16_000);

        Assert.Equal("chunk_002_speaker_00", namespaced[0].SpeakerId);
        Assert.Equal(TimeSpan.FromMinutes(10), namespaced[0].Start);
        Assert.Equal("chunk_002_speaker_01", namespaced[1].SpeakerId);

        var normalized = LocalSpeakerDiarizationProvider.NormalizeWindowSpeakerTurns(
            [
                namespaced[1],
                namespaced[0],
                new SpeakerTurn("chunk_003_speaker_01", TimeSpan.FromMinutes(11), TimeSpan.FromMinutes(12)),
            ]);

        Assert.Equal("speaker_00", normalized.SpeakerIdMap["chunk_002_speaker_00"]);
        Assert.Equal("speaker_01", normalized.SpeakerIdMap["chunk_002_speaker_01"]);
        Assert.Equal("speaker_02", normalized.SpeakerIdMap["chunk_003_speaker_01"]);
        Assert.Equal(new[] { "speaker_00", "speaker_01", "speaker_02" }, normalized.SpeakerTurns.Select(turn => turn.SpeakerId));
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
