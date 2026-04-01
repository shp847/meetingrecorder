using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class ProcessingWorkerPerformanceSourceTests
{
    [Fact]
    public void Processing_Worker_Uses_Explicit_Thread_Budgets_For_Transcription_And_Diarization()
    {
        var programPath = GetPath("src", "MeetingRecorder.ProcessingWorker", "Program.cs");
        var whisperPath = GetPath("src", "MeetingRecorder.ProcessingWorker", "WhisperNetTranscriptionProvider.cs");
        var diarizationPath = GetPath("src", "MeetingRecorder.ProcessingWorker", "OptionalSidecarDiarizationProvider.cs");

        var program = File.ReadAllText(programPath);
        var whisper = File.ReadAllText(whisperPath);
        var diarization = File.ReadAllText(diarizationPath);

        Assert.Contains("BackgroundProcessingPolicy.GetTranscriptionThreadCount", program);
        Assert.Contains("BackgroundProcessingPolicy.GetDiarizationThreadCount", program);
        Assert.Contains(".WithThreads(_threadCount)", whisper);
        Assert.Contains("threadCount", diarization);
        Assert.DoesNotContain("NumThreads = Math.Max(1, Environment.ProcessorCount)", diarization);
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
