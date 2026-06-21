using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using MeetingRecorder.ProcessingWorker;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class ExternalCliProviderTests
{
    [Fact]
    public async Task ProbeAsync_Succeeds_When_Cli_Returns_Ok()
    {
        var root = CreateTempRoot();
        var cliPath = CreateCli(root, "probe-ok.cmd", """{"ok":true,"message":"ready"}""");

        var snapshot = await CliProviderProcess.ProbeAsync(cliPath, "Transcription", CancellationToken.None);

        Assert.True(snapshot.Succeeded);
        Assert.Equal(cliPath, snapshot.ExecutablePath);
        Assert.Equal("ready", snapshot.Message);
        Assert.NotNull(snapshot.LastProbeUtc);
    }

    [Fact]
    public async Task ProbeAsync_Fails_Cleanly_For_Missing_Bad_And_Negative_Cli()
    {
        var root = CreateTempRoot();
        var badJson = CreateCli(root, "bad-json.cmd", "not-json");
        var negative = CreateCli(root, "negative.cmd", """{"ok":false,"message":"no device"}""");
        var nonzero = CreateCli(root, "nonzero.cmd", """{"ok":true,"message":"ignored"}""", exitCode: 3);

        var missingSnapshot = await CliProviderProcess.ProbeAsync(Path.Combine(root, "missing.cmd"), "Transcription", CancellationToken.None);
        var badJsonSnapshot = await CliProviderProcess.ProbeAsync(badJson, "Transcription", CancellationToken.None);
        var negativeSnapshot = await CliProviderProcess.ProbeAsync(negative, "Transcription", CancellationToken.None);
        var nonzeroSnapshot = await CliProviderProcess.ProbeAsync(nonzero, "Transcription", CancellationToken.None);

        Assert.False(missingSnapshot.Succeeded);
        Assert.False(badJsonSnapshot.Succeeded);
        Assert.False(negativeSnapshot.Succeeded);
        Assert.False(nonzeroSnapshot.Succeeded);
        Assert.Contains("not found", missingSnapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid JSON", badJsonSnapshot.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no device", negativeSnapshot.Message);
        Assert.Contains("failed", nonzeroSnapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Program_Transcription_Probe_Persists_Snapshot()
    {
        var root = CreateTempRoot();
        var cliPath = CreateCli(root, "probe-ok.cmd", """{"ok":true,"message":"ready"}""");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, Path.Combine(root, "documents"));
        var config = await store.LoadOrCreateAsync();
        await store.SaveAsync(config with
        {
            TranscriptionProviderPreference = TranscriptionProviderPreference.LocalCli,
            TranscriptionCliPath = cliPath,
        });

        var exitCode = await Program.Main(["--probe-transcription-cli", "--config", configPath]);
        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(0, exitCode);
        Assert.True(reloaded.TranscriptionCliProviderProbe.Succeeded);
        Assert.Equal(cliPath, reloaded.TranscriptionCliProviderProbe.ExecutablePath);
        Assert.Equal("ready", reloaded.TranscriptionCliProviderProbe.Message);
    }

    [Fact]
    public async Task Program_Diarization_Probe_Persists_Snapshot()
    {
        var root = CreateTempRoot();
        var cliPath = CreateCli(root, "probe-ok.cmd", """{"ok":true,"message":"ready"}""");
        var configPath = Path.Combine(root, "config", "appsettings.json");
        var store = new AppConfigStore(configPath, Path.Combine(root, "documents"));
        var config = await store.LoadOrCreateAsync();
        await store.SaveAsync(config with
        {
            DiarizationProviderPreference = DiarizationProviderPreference.LocalCli,
            DiarizationCliPath = cliPath,
        });

        var exitCode = await Program.Main(["--probe-diarization-cli", "--config", configPath]);
        var reloaded = await store.LoadOrCreateAsync();

        Assert.Equal(0, exitCode);
        Assert.True(reloaded.DiarizationCliProviderProbe.Succeeded);
        Assert.Equal(cliPath, reloaded.DiarizationCliProviderProbe.ExecutablePath);
        Assert.Equal("ready", reloaded.DiarizationCliProviderProbe.Message);
    }

    [Fact]
    public async Task LocalCliTranscriptionProvider_Parses_Result()
    {
        var root = CreateTempRoot();
        var resultPath = Path.Combine(root, "transcription.json");
        await File.WriteAllTextAsync(
            resultPath,
            """
            {
              "segments": [
                { "start": "00:00:00", "end": "00:00:01", "speakerId": null, "speakerLabel": null, "text": "hello" }
              ],
              "language": "en",
              "message": "ok"
            }
            """);
        var cliPath = CreateCliFromFile(root, "transcribe.cmd", resultPath);
        var audioPath = CreateWaveFile(root);
        var logger = new FileLogWriter(Path.Combine(root, "log.txt"));
        var provider = new LocalCliTranscriptionProvider(cliPath, string.Empty, logger);

        var result = await provider.TranscribeAsync(audioPath, CancellationToken.None);

        Assert.Equal("en", result.Language);
        Assert.Equal("hello", result.Segments.Single().Text);
    }

    [Fact]
    public async Task LocalCliDiarizationProvider_Validates_Result()
    {
        var root = CreateTempRoot();
        var validResultPath = Path.Combine(root, "diarization.json");
        await File.WriteAllTextAsync(
            validResultPath,
            """
            {
              "segments": [
                { "start": "00:00:00", "end": "00:00:01", "speakerId": "speaker-1", "speakerLabel": "Speaker 1", "text": "hello" }
              ],
              "appliedSpeakerLabels": true,
              "message": "ok",
              "speakers": [
                { "id": "speaker-1", "displayName": "Speaker 1", "isUserEdited": false }
              ],
              "speakerTurns": [
                { "speakerId": "speaker-1", "start": "00:00:00", "end": "00:00:01" }
              ],
              "metadata": {
                "provider": "external-cli",
                "segmentationModelFileName": "external",
                "embeddingModelFileName": "external",
                "bundleVersion": "external",
                "attributionMode": "SegmentOverlap",
                "executionProvider": "Cpu",
                "gpuAccelerationRequested": false,
                "gpuAccelerationAvailable": false,
                "diagnosticMessage": "ok"
              }
            }
            """);
        var cliPath = CreateCliFromFile(root, "diarize.cmd", validResultPath);
        var audioPath = CreateWaveFile(root);
        var logger = new FileLogWriter(Path.Combine(root, "log.txt"));
        var provider = new LocalCliDiarizationProvider(cliPath, string.Empty, logger);
        var transcript = new[]
        {
            new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, "hello"),
        };

        var result = await provider.ApplySpeakerLabelsAsync(audioPath, transcript, CancellationToken.None);

        Assert.True(result.AppliedSpeakerLabels);
        Assert.Single(result.Speakers!);
        Assert.Single(result.SpeakerTurns!);
        Assert.NotNull(result.Metadata);
    }

    [Fact]
    public async Task LocalCliDiarizationProvider_Rejects_Labeled_Result_Without_Metadata()
    {
        var root = CreateTempRoot();
        var resultPath = Path.Combine(root, "diarization-invalid.json");
        await File.WriteAllTextAsync(
            resultPath,
            """
            {
              "segments": [
                { "start": "00:00:00", "end": "00:00:01", "speakerId": "speaker-1", "speakerLabel": "Speaker 1", "text": "hello" }
              ],
              "appliedSpeakerLabels": true,
              "message": "bad"
            }
            """);
        var cliPath = CreateCliFromFile(root, "diarize-invalid.cmd", resultPath);
        var audioPath = CreateWaveFile(root);
        var logger = new FileLogWriter(Path.Combine(root, "log.txt"));
        var provider = new LocalCliDiarizationProvider(cliPath, string.Empty, logger);
        var transcript = new[]
        {
            new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), null, "hello"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ApplySpeakerLabelsAsync(audioPath, transcript, CancellationToken.None));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateCli(string root, string fileName, string output, int exitCode = 0)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(
            path,
            $"@echo off{Environment.NewLine}echo {output}{Environment.NewLine}exit /b {exitCode}{Environment.NewLine}");
        return path;
    }

    private static string CreateCliFromFile(string root, string fileName, string resultPath)
    {
        var path = Path.Combine(root, fileName);
        File.WriteAllText(
            path,
            $"@echo off{Environment.NewLine}type \"{resultPath}\"{Environment.NewLine}exit /b 0{Environment.NewLine}");
        return path;
    }

    private static string CreateWaveFile(string root)
    {
        var path = Path.Combine(root, "audio.wav");
        var format = WaveFormat.CreateIeeeFloatWaveFormat(16_000, 1);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new float[16_000];
        writer.WriteSamples(buffer, 0, buffer.Length);
        return path;
    }
}
