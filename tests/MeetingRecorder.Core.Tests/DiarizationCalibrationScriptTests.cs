using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationCalibrationScriptTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void AnalyzeDiarizationScript_Can_Report_ExpectedSpeakerCount_Without_Mutating_Manifests_Or_Printing_Transcript_Text()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var scriptPath = Path.Combine(repoRoot, "scripts", "Analyze-Diarization.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected diarization calibration script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);

        Assert.Contains("ExpectedSpeakerCount", scriptContents, StringComparison.Ordinal);
        Assert.Contains("SpeakerRunCount", scriptContents, StringComparison.Ordinal);
        Assert.Contains("LabelDistribution", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateManifest", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedSpeakerCount", scriptContents, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Content", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".text", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Segment.text", scriptContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalibrationEnvironment_Parses_Candidate_Overrides_Without_Process_Environment()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DiarizationCalibrationEnvironment.DefaultClusteringThresholdVariable] = "0.55",
            [DiarizationCalibrationEnvironment.CollapsedClusteringThresholdsVariable] = "0.42,0.38",
            [DiarizationCalibrationEnvironment.OverSegmentedClusteringThresholdsVariable] = "0.62;0.72",
            [DiarizationCalibrationEnvironment.MinimumSpeakerDurationSecondsVariable] = "3",
            [DiarizationCalibrationEnvironment.MaximumMinimumSpeakerDurationSecondsVariable] = "12",
            [DiarizationCalibrationEnvironment.MinimumSpeakerDurationShareVariable] = "0.01",
            [DiarizationCalibrationEnvironment.MaximumPreferredSpeakerCountVariable] = "6",
            [DiarizationCalibrationEnvironment.TinyClusterMaximumSecondsVariable] = "9",
            [DiarizationCalibrationEnvironment.SmallClusterMaximumSecondsVariable] = "25",
            [DiarizationCalibrationEnvironment.SmallClusterDurationShareVariable] = "0.04",
            [DiarizationCalibrationEnvironment.HighConfidenceMergeThresholdVariable] = "0.91",
            [DiarizationCalibrationEnvironment.SmallClusterMergeThresholdVariable] = "0.66",
            [DiarizationCalibrationEnvironment.SpeakerNameAutoApplyConfidenceThresholdVariable] = "0.90",
            [DiarizationCalibrationEnvironment.SpeakerNameSuggestionConfidenceThresholdVariable] = "0.80",
            [DiarizationCalibrationEnvironment.SpeakerNameMatchMarginThresholdVariable] = "0.08",
            [DiarizationCalibrationEnvironment.SpeakerNameMinimumAutoApplyProfileSamplesVariable] = "3",
            [DiarizationCalibrationEnvironment.SpeakerNameMinimumAutoApplySpeechSecondsVariable] = "11",
        };

        string? GetValue(string name) => values.TryGetValue(name, out var value) ? value : null;

        var diarization = DiarizationCalibrationEnvironment.LoadDiarizationThresholdOptions(GetValue);
        var selection = DiarizationCalibrationEnvironment.LoadClusterSelectionOptions(GetValue);
        var merge = DiarizationCalibrationEnvironment.LoadSpeakerClusterMergeOptions(GetValue);
        var speakerNames = DiarizationCalibrationEnvironment.LoadSpeakerNameRecognitionOptions(
            new SpeakerNameRecognitionOptions(0.86d, 0.78d, 0.05d, 2, TimeSpan.FromSeconds(8)),
            GetValue);

        Assert.Equal(0.55f, diarization.DefaultClusteringThreshold);
        Assert.Equal([0.42f, 0.38f], diarization.CollapsedSpeakerClusteringThresholds);
        Assert.Equal([0.62f, 0.72f], diarization.OverSegmentedSpeakerClusteringThresholds);
        Assert.Equal(TimeSpan.FromSeconds(3), selection.AbsoluteMinimumSpeakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(12), selection.MaximumMinimumSpeakerDuration);
        Assert.Equal(0.01d, selection.MinimumSpeakerDurationShare);
        Assert.Equal(6, selection.MaximumPreferredAutomaticSpeakerCount);
        Assert.Equal(TimeSpan.FromSeconds(9), merge.TinyClusterMaximumDuration);
        Assert.Equal(TimeSpan.FromSeconds(25), merge.SmallClusterMaximumDuration);
        Assert.Equal(0.04d, merge.SmallClusterDurationShare);
        Assert.Equal(0.91d, merge.HighConfidenceMergeThreshold);
        Assert.Equal(0.66d, merge.SmallClusterMergeThreshold);
        Assert.Equal(0.90d, speakerNames.AutoApplyConfidenceThreshold);
        Assert.Equal(0.80d, speakerNames.SuggestionConfidenceThreshold);
        Assert.Equal(0.08d, speakerNames.MatchMarginThreshold);
        Assert.Equal(3, speakerNames.MinimumAutoApplyProfileSampleCount);
        Assert.Equal(TimeSpan.FromSeconds(11), speakerNames.MinimumAutoApplySpeechDuration);
    }

    [Fact]
    public void CalibrationCandidatesTemplate_Is_Synthetic_And_Uses_Only_Allowed_Overrides()
    {
        var repoRoot = GetRepoRoot();
        var examplePath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "calibration-candidates.example.json");
        var schemaPath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "calibration-candidates.schema.json");

        Assert.True(File.Exists(examplePath), $"Expected calibration candidate example at '{examplePath}'.");
        Assert.True(File.Exists(schemaPath), $"Expected calibration candidate schema at '{schemaPath}'.");

        var exampleText = File.ReadAllText(examplePath);
        var schemaText = File.ReadAllText(schemaPath);
        using var document = JsonDocument.Parse(exampleText);
        var candidates = document.RootElement.GetProperty("candidates").EnumerateArray().ToArray();

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(candidates.Length >= 3);
        Assert.All(candidates, candidate => Assert.False(candidate.GetProperty("enabled").GetBoolean()));
        Assert.Contains("calibration-candidates.local.json", exampleText, StringComparison.Ordinal);
        Assert.Contains(DiarizationCalibrationEnvironment.HighConfidenceMergeThresholdVariable, schemaText, StringComparison.Ordinal);
        Assert.Contains(DiarizationCalibrationEnvironment.SpeakerNameAutoApplyConfidenceThresholdVariable, schemaText, StringComparison.Ordinal);
        Assert.DoesNotContain("Google Cloud VMO", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Khalid", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Terry", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expectedSpeakerCount", exampleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestDiarizationCalibrationScript_Runs_DryRun_MetadataOnly_And_Keeps_Defaults_When_Evidence_Is_Missing()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationCalibration.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "meeting-recorder-calibration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var transcriptPath = Path.Combine(tempRoot, "transcript.json");
            var manifestPath = Path.Combine(tempRoot, "manifest.json");
            var artifactPath = Path.Combine(tempRoot, "transcript.md");
            var catalogPath = Path.Combine(tempRoot, "catalog.json");
            var candidatePath = Path.Combine(tempRoot, "candidates.json");
            var reportPath = Path.Combine(tempRoot, "calibration-report.json");
            const string privateMarker = "PRIVATE_CALIBRATION_SENTINEL_DO_NOT_REPORT";

            File.WriteAllText(
                transcriptPath,
                JsonSerializer.Serialize(
                    new
                    {
                        segments = new[]
                        {
                            new { speakerLabel = "Speaker 1", text = privateMarker + " first private words" },
                            new { speakerLabel = "Speaker 2", text = privateMarker + " second private words" },
                        },
                    },
                    JsonOptions));
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        processingMetadata = new
                        {
                            speakerTurns = new[]
                            {
                                new { speakerId = "speaker-1", start = 0.0, end = 1.0 },
                                new { speakerId = "speaker-2", start = 1.0, end = 2.0 },
                            },
                            speakerVoiceSamples = Array.Empty<object>(),
                        },
                    },
                    JsonOptions));
            File.WriteAllText(artifactPath, privateMarker + " protected artifact");
            File.WriteAllText(
                catalogPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        fixtures = new[]
                        {
                            new
                            {
                                id = "synthetic-two-speaker",
                                title = "Synthetic two speaker",
                                category = "two-speaker",
                                mode = "stored-turn",
                                enabled = true,
                                manifestPath,
                                transcriptJsonPath = transcriptPath,
                                expectedSpeakerCount = 2,
                                expectedSpeakerNames = Array.Empty<string>(),
                                expectedNameMode = "none",
                                enableSpeakerNameMatching = false,
                                protectedArtifactPaths = new[] { artifactPath },
                            },
                        },
                    },
                    JsonOptions));
            File.WriteAllText(
                candidatePath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        candidates = new[]
                        {
                            new
                            {
                                name = "candidate-fragment-filter",
                                description = "Synthetic candidate",
                                enabled = true,
                                environment = new Dictionary<string, string>
                                {
                                    [DiarizationCalibrationEnvironment.MinimumSpeakerDurationSecondsVariable] = "3",
                                    [DiarizationCalibrationEnvironment.HighConfidenceMergeThresholdVariable] = "0.92",
                                },
                            },
                        },
                    },
                    JsonOptions));

            var result = RunPowerShell(
                scriptPath,
                "-CatalogPath", catalogPath,
                "-CandidatePath", candidatePath,
                "-DryRun",
                "-ReportPath", reportPath);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(reportPath), "Expected calibration report to be written.");
            Assert.Equal(privateMarker + " protected artifact", File.ReadAllText(artifactPath));

            var reportText = File.ReadAllText(reportPath);
            Assert.DoesNotContain(privateMarker, reportText, StringComparison.Ordinal);
            Assert.DoesNotContain("first private words", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("embedding", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorization", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api-key", reportText, StringComparison.OrdinalIgnoreCase);

            var report = JsonNode.Parse(reportText) ?? throw new InvalidOperationException("Calibration report was empty.");
            Assert.Equal("keep_current_defaults", (string?)report["recommendation"]?["action"]);
            Assert.Equal(2, report["runs"]?.AsArray().Count);
            Assert.Single(report["comparisons"]?.AsArray() ?? []);
            Assert.Contains(
                DiarizationCalibrationEnvironment.HighConfidenceMergeThresholdVariable,
                reportText,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TestDiarizationCalibrationScript_Rejects_Unsupported_Candidate_Variables()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationCalibration.ps1");
        var catalogPath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "fixture-catalog.example.json");
        var tempRoot = Path.Combine(Path.GetTempPath(), "meeting-recorder-calibration-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var candidatePath = Path.Combine(tempRoot, "bad-candidates.json");
            var reportPath = Path.Combine(tempRoot, "calibration-report.json");
            File.WriteAllText(
                candidatePath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        candidates = new[]
                        {
                            new
                            {
                                name = "bad-candidate",
                                description = "Bad candidate",
                                enabled = true,
                                environment = new Dictionary<string, string>
                                {
                                    ["MEETING_RECORDER_EXPECTED_SPEAKER_COUNT_HINT"] = "2",
                                },
                            },
                        },
                    },
                    JsonOptions));

            var result = RunPowerShell(
                scriptPath,
                "-CatalogPath", catalogPath,
                "-CandidatePath", candidatePath,
                "-DryRun",
                "-ReportPath", reportPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "Unsupported calibration variable",
                result.StandardError + result.StandardOutput,
                StringComparison.Ordinal);
            Assert.False(File.Exists(reportPath), "Unsupported candidate variables should fail before writing a comparison report.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static PowerShellRunResult RunPowerShell(string scriptPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new PowerShellRunResult(process.ExitCode, standardOutput, standardError);
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }

    private sealed record PowerShellRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
