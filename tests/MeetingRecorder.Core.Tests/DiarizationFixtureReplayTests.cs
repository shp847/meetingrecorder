using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationFixtureReplayTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void ExternalFixture_Replays_SpeakerClusterMerge_When_Configured()
    {
        var transcriptJsonPath = Environment.GetEnvironmentVariable("MEETING_RECORDER_DIARIZATION_FIXTURE_JSON");
        if (string.IsNullOrWhiteSpace(transcriptJsonPath))
        {
            return;
        }

        var manifestPath = Environment.GetEnvironmentVariable("MEETING_RECORDER_DIARIZATION_FIXTURE_MANIFEST");
        var resultPath = Environment.GetEnvironmentVariable("MEETING_RECORDER_DIARIZATION_FIXTURE_RESULT_PATH");
        var expectedSpeakerCount = ParseOptionalSpeakerCount(
            Environment.GetEnvironmentVariable("MEETING_RECORDER_DIARIZATION_FIXTURE_EXPECTED_SPEAKERS"));
        var fixture = LoadFixture(transcriptJsonPath, manifestPath);
        var service = new SpeakerClusterMergeService();

        var mergeResult = service.MergeSimilarClusters(fixture.SpeakerTurns, fixture.SpeakerVoiceSamples);
        var result = DiarizationFixtureReplayResult.From(
            fixture,
            mergeResult,
            expectedSpeakerCount);
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            var resultDirectory = Path.GetDirectoryName(Path.GetFullPath(resultPath));
            if (!string.IsNullOrWhiteSpace(resultDirectory))
            {
                Directory.CreateDirectory(resultDirectory);
            }

            File.WriteAllText(resultPath, JsonSerializer.Serialize(result, JsonOptions));
        }

        if (expectedSpeakerCount is not null)
        {
            Assert.Equal(expectedSpeakerCount.Value, result.MergedSpeakerCount);
        }
    }

    [Fact]
    public void TestDiarizationFixtureScript_Runs_Focused_Replay_Test_Without_App_Or_Worker()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationFixture.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected fixture replay script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);
        Assert.Contains("MEETING_RECORDER_DIARIZATION_FIXTURE_JSON", scriptContents, StringComparison.Ordinal);
        Assert.Contains("DiarizationFixtureReplayTests", scriptContents, StringComparison.Ordinal);
        Assert.Contains("dotnet test", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MeetingRecorder.ProcessingWorker.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MeetingRecorder.App.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Build-Installer.ps1", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Content -LiteralPath $JsonPath", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Content -LiteralPath $ManifestPath", scriptContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestDiarizationFullAudioFixtureScript_Runs_Worker_In_Temp_Output_Root()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationFullAudioFixture.ps1");

        Assert.True(File.Exists(scriptPath), $"Expected full-audio fixture script at '{scriptPath}'.");

        var scriptContents = File.ReadAllText(scriptPath);
        Assert.Contains("meeting-recorder-full-diarization-probe-", scriptContents, StringComparison.Ordinal);
        Assert.Contains("probe-output", scriptContents, StringComparison.Ordinal);
        Assert.Contains("PublishedArtifactPath", scriptContents, StringComparison.Ordinal);
        Assert.Contains("ExpectedSpeakerName", scriptContents, StringComparison.Ordinal);
        Assert.Contains("EnableSpeakerNameMatching", scriptContents, StringComparison.Ordinal);
        Assert.Contains("SpeakerNameStatus", scriptContents, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", scriptContents, StringComparison.Ordinal);
        Assert.Contains("-split \",\"", scriptContents, StringComparison.Ordinal);
        Assert.Contains("skipSpeakerLabeling", scriptContents, StringComparison.Ordinal);
        Assert.Contains("MeetingRecorder.ProcessingWorker.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MeetingRecorder.App.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Build-Installer.ps1", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stop-Process", scriptContents, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Content -LiteralPath $ManifestPath", scriptContents, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestDiarizationFixtureCatalogTemplate_Covers_SprintTwo_Mix_With_Synthetic_Disabled_Fixtures()
    {
        var repoRoot = GetRepoRoot();
        var examplePath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "fixture-catalog.example.json");
        var schemaPath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "fixture-catalog.schema.json");

        Assert.True(File.Exists(examplePath), $"Expected fixture catalog example at '{examplePath}'.");
        Assert.True(File.Exists(schemaPath), $"Expected fixture catalog schema at '{schemaPath}'.");

        var exampleText = File.ReadAllText(examplePath);
        var schemaText = File.ReadAllText(schemaPath);
        using var document = JsonDocument.Parse(exampleText);
        var root = document.RootElement;
        var fixtures = root.GetProperty("fixtures").EnumerateArray().ToArray();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(fixtures.Length >= 6);
        Assert.All(fixtures, fixture => Assert.False(fixture.GetProperty("enabled").GetBoolean()));
        Assert.Contains(fixtures, fixture => fixture.GetProperty("category").GetString() == "two-speaker");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("category").GetString() == "one-speaker");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("category").GetString() == "three-plus-speaker");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("category").GetString() == "short-call");
        Assert.Contains(fixtures, fixture => fixture.GetProperty("category").GetString() == "noisy-similar-voices");
        Assert.Contains("Expected values are assertions only, not runtime hints", exampleText, StringComparison.Ordinal);
        Assert.Contains("\"expectedSpeakerCount\"", schemaText, StringComparison.Ordinal);
        Assert.Contains("\"expectedSpeakerNames\"", schemaText, StringComparison.Ordinal);
        Assert.Contains("\"protectedArtifactPaths\"", schemaText, StringComparison.Ordinal);

        Assert.DoesNotContain("Google Cloud VMO", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Khalid", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Terry", exampleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("psharm", exampleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestDiarizationFixtureCatalogScript_Runs_MetadataOnly_DryRun_Without_App_Worker_Or_Private_Text()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationFixtureCatalog.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "meeting-recorder-catalog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var transcriptPath = Path.Combine(tempRoot, "transcript.json");
            var manifestPath = Path.Combine(tempRoot, "manifest.json");
            var markdownPath = Path.Combine(tempRoot, "transcript.md");
            var catalogPath = Path.Combine(tempRoot, "catalog.json");
            var reportPath = Path.Combine(tempRoot, "report.json");
            const string privateMarker = "PRIVATE_TRANSCRIPT_SENTINEL_DO_NOT_REPORT";

            File.WriteAllText(
                transcriptPath,
                JsonSerializer.Serialize(
                    new
                    {
                        segments = new[]
                        {
                            new { speakerLabel = "Speaker 1", text = privateMarker + " first private words" },
                            new { speakerLabel = "Speaker 1", text = privateMarker + " more private words" },
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
            File.WriteAllText(markdownPath, privateMarker + " protected artifact");
            File.WriteAllText(
                catalogPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        fixtures = new object[]
                        {
                            new
                            {
                                id = "disabled-placeholder",
                                title = "Disabled placeholder",
                                category = "short-call",
                                mode = "stored-turn",
                                enabled = false,
                                disabledReason = "Synthetic disabled fixture.",
                                manifestPath,
                                transcriptJsonPath = transcriptPath,
                                expectedSpeakerCount = 2,
                                expectedSpeakerNames = Array.Empty<string>(),
                                expectedNameMode = "none",
                                enableSpeakerNameMatching = false,
                                protectedArtifactPaths = Array.Empty<string>(),
                            },
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
                                protectedArtifactPaths = new[] { markdownPath },
                            },
                        },
                    },
                    JsonOptions));

            var result = RunPowerShell(
                scriptPath,
                "-CatalogPath", catalogPath,
                "-IncludeDisabled",
                "-DryRun",
                "-ReportPath", reportPath);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(reportPath), "Expected catalog report to be written.");
            Assert.Equal(privateMarker + " protected artifact", File.ReadAllText(markdownPath));

            var reportText = File.ReadAllText(reportPath);
            Assert.DoesNotContain(privateMarker, reportText, StringComparison.Ordinal);
            Assert.DoesNotContain("first private words", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("embedding", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorization", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api-key", reportText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", reportText, StringComparison.OrdinalIgnoreCase);

            var report = JsonNode.Parse(reportText) ?? throw new InvalidOperationException("Report JSON was empty.");
            Assert.Equal(2, (int?)report["summary"]?["total"]);
            Assert.Equal(1, (int?)report["summary"]?["skipped"]);
            Assert.Equal(1, (int?)report["summary"]?["dryRun"]);

            var enabledResult = report["results"]?.AsArray()
                .OfType<JsonObject>()
                .Single(resultNode => (string?)resultNode["fixtureId"] == "synthetic-two-speaker");
            Assert.NotNull(enabledResult);
            Assert.Equal("dry_run", (string?)enabledResult!["status"]);
            Assert.Equal("Test-DiarizationFixture.ps1", (string?)enabledResult["dispatch"]?["script"]);
            Assert.Equal(3, (int?)enabledResult["segmentCount"]);
            Assert.Equal(2, (int?)enabledResult["rawSpeakerLabelCount"]);
            Assert.Equal(2, (int?)enabledResult["contiguousSpeakerRunCount"]);
            Assert.Equal(1, (int?)enabledResult["artifactSafety"]?["protectedArtifactCount"]);
            Assert.True((bool?)enabledResult["artifactSafety"]?["allUnchanged"]);

            var scriptContents = File.ReadAllText(scriptPath);
            Assert.Contains("Test-DiarizationFixture.ps1", scriptContents, StringComparison.Ordinal);
            Assert.Contains("Test-DiarizationFullAudioFixture.ps1", scriptContents, StringComparison.Ordinal);
            Assert.Contains("protectedArtifactPaths", scriptContents, StringComparison.Ordinal);
            Assert.Contains("Get-FileHash", scriptContents, StringComparison.Ordinal);
            Assert.DoesNotContain("MeetingRecorder.App.exe", scriptContents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Stop-Process", scriptContents, StringComparison.OrdinalIgnoreCase);
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
    public void TestDiarizationFixtureCatalogScript_Rejects_Name_Assertions_When_Matching_Is_Disabled()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationFixtureCatalog.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "meeting-recorder-catalog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var transcriptPath = Path.Combine(tempRoot, "transcript.json");
            var manifestPath = Path.Combine(tempRoot, "manifest.json");
            var catalogPath = Path.Combine(tempRoot, "catalog.json");
            var reportPath = Path.Combine(tempRoot, "report.json");
            File.WriteAllText(transcriptPath, JsonSerializer.Serialize(new { segments = Array.Empty<object>() }, JsonOptions));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { processingMetadata = new { speakerTurns = Array.Empty<object>() } }, JsonOptions));
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
                                id = "invalid-name-assertion",
                                title = "Invalid name assertion",
                                category = "two-speaker",
                                mode = "stored-turn",
                                enabled = true,
                                manifestPath,
                                transcriptJsonPath = transcriptPath,
                                expectedSpeakerCount = 2,
                                expectedSpeakerNames = new[] { "Private Example Name" },
                                expectedNameMode = "assert",
                                enableSpeakerNameMatching = false,
                                protectedArtifactPaths = Array.Empty<string>(),
                            },
                        },
                    },
                    JsonOptions));

            var result = RunPowerShell(
                scriptPath,
                "-CatalogPath", catalogPath,
                "-DryRun",
                "-ReportPath", reportPath);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(reportPath), "Expected failed validation to still write a metadata report.");

            var reportText = File.ReadAllText(reportPath);
            Assert.Contains("supplies expected speaker names but enableSpeakerNameMatching is false", reportText, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Example Name", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Private Example Name", result.StandardError, StringComparison.Ordinal);
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
    public void TestDiarizationFixtureCatalogScript_Rejects_Unsafe_Report_Paths_And_Invalid_Counts()
    {
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "Test-DiarizationFixtureCatalog.ps1");
        var exampleCatalogPath = Path.Combine(repoRoot, "docs", "diarization-fixtures", "fixture-catalog.example.json");
        var unsafeReportPath = Path.Combine(repoRoot, "fixture-report-should-not-write.json");

        var unsafeReportResult = RunPowerShell(
            scriptPath,
            "-CatalogPath", exampleCatalogPath,
            "-ReportPath", unsafeReportPath,
            "-DryRun");

        Assert.NotEqual(0, unsafeReportResult.ExitCode);
        Assert.Contains(
            "must write under .artifacts or the system temp directory",
            unsafeReportResult.StandardError + unsafeReportResult.StandardOutput,
            StringComparison.Ordinal);
        Assert.False(File.Exists(unsafeReportPath), "Unsafe report path should not be created.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "meeting-recorder-catalog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var catalogPath = Path.Combine(tempRoot, "catalog.json");
            var reportPath = Path.Combine(tempRoot, "report.json");
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
                                id = "invalid-speaker-count",
                                title = "Invalid speaker count",
                                category = "two-speaker",
                                mode = "stored-turn",
                                enabled = true,
                                transcriptJsonPath = Path.Combine(tempRoot, "missing-transcript.json"),
                                manifestPath = Path.Combine(tempRoot, "missing-manifest.json"),
                                expectedSpeakerCount = 99,
                                expectedSpeakerNames = Array.Empty<string>(),
                                expectedNameMode = "none",
                                enableSpeakerNameMatching = false,
                                protectedArtifactPaths = Array.Empty<string>(),
                            },
                        },
                    },
                    JsonOptions));

            var invalidCountResult = RunPowerShell(
                scriptPath,
                "-CatalogPath", catalogPath,
                "-ReportPath", reportPath,
                "-DryRun");

            Assert.NotEqual(0, invalidCountResult.ExitCode);
            Assert.True(File.Exists(reportPath), "Expected invalid fixture validation to still write a metadata report.");
            Assert.Contains(
                "expectedSpeakerCount must be an integer from 1 through 16",
                File.ReadAllText(reportPath),
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

    private static DiarizationFixtureReplayFixture LoadFixture(string transcriptJsonPath, string? manifestPath)
    {
        var resolvedTranscriptPath = ResolveRequiredPath(transcriptJsonPath, "Transcript JSON");
        var transcriptRoot = LoadJsonRoot(resolvedTranscriptPath);
        var resolvedManifestPath = string.IsNullOrWhiteSpace(manifestPath)
            ? null
            : ResolveRequiredPath(manifestPath, "Manifest");
        using var manifestDocument = resolvedManifestPath is null ? null : JsonDocument.Parse(File.ReadAllText(resolvedManifestPath));
        var manifestRoot = manifestDocument?.RootElement;

        var speakerTurns = ReadArray<SpeakerTurn>(transcriptRoot, "speakerTurns")
            ?? ReadArray<SpeakerTurn>(manifestRoot, "processingMetadata", "speakerTurns")
            ?? ReadArray<SpeakerTurn>(manifestRoot, "speakerTurns")
            ?? Array.Empty<SpeakerTurn>();
        var speakerVoiceSamples = ReadArray<SpeakerVoiceSample>(manifestRoot, "processingMetadata", "speakerVoiceSamples")
            ?? ReadArray<SpeakerVoiceSample>(manifestRoot, "speakerVoiceSamples")
            ?? ReadArray<SpeakerVoiceSample>(transcriptRoot, "speakerVoiceSamples")
            ?? Array.Empty<SpeakerVoiceSample>();

        if (speakerTurns.Count == 0)
        {
            throw new InvalidOperationException("Fixture does not contain speakerTurns in the transcript JSON or manifest.");
        }

        return new DiarizationFixtureReplayFixture(
            resolvedTranscriptPath,
            resolvedManifestPath,
            speakerTurns,
            speakerVoiceSamples);
    }

    private static JsonElement LoadJsonRoot(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<T>? ReadArray<T>(JsonElement? root, string propertyName)
    {
        if (root is null ||
            !TryGetProperty(root.Value, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return JsonSerializer.Deserialize<IReadOnlyList<T>>(property.GetRawText(), JsonOptions);
    }

    private static IReadOnlyList<T>? ReadArray<T>(JsonElement? root, string parentPropertyName, string propertyName)
    {
        if (root is null ||
            !TryGetProperty(root.Value, parentPropertyName, out var parent) ||
            parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadArray<T>(parent, propertyName);
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement property)
    {
        if (root.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string ResolveRequiredPath(string path, string description)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description} file does not exist.", fullPath);
        }

        return fullPath;
    }

    private static int? ParseOptionalSpeakerCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var speakerCount) || speakerCount is < 2 or > 16)
        {
            throw new InvalidOperationException("Expected speaker count must be an integer from 2 through 16.");
        }

        return speakerCount;
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(DiarizationFixtureReplayTests).Assembly.Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
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

    private sealed record DiarizationFixtureReplayFixture(
        string TranscriptJsonPath,
        string? ManifestPath,
        IReadOnlyList<SpeakerTurn> SpeakerTurns,
        IReadOnlyList<SpeakerVoiceSample> SpeakerVoiceSamples);

    private sealed record PowerShellRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record DiarizationFixtureReplayResult(
        string TranscriptJsonPath,
        string? ManifestPath,
        int OriginalSpeakerCount,
        int MergedSpeakerCount,
        int OriginalSpeakerTurnCount,
        int MergedSpeakerTurnCount,
        int VoiceSampleCount,
        int? ExpectedSpeakerCount,
        string Status,
        string? DiagnosticMessage)
    {
        public static DiarizationFixtureReplayResult From(
            DiarizationFixtureReplayFixture fixture,
            SpeakerClusterMergeResult mergeResult,
            int? expectedSpeakerCount)
        {
            var mergedSpeakerCount = CountSpeakers(mergeResult.SpeakerTurns);
            return new DiarizationFixtureReplayResult(
                fixture.TranscriptJsonPath,
                fixture.ManifestPath,
                CountSpeakers(fixture.SpeakerTurns),
                mergedSpeakerCount,
                fixture.SpeakerTurns.Count,
                mergeResult.SpeakerTurns.Count,
                fixture.SpeakerVoiceSamples.Count,
                expectedSpeakerCount,
                BuildStatus(mergedSpeakerCount, expectedSpeakerCount),
                mergeResult.DiagnosticMessage);
        }

        private static int CountSpeakers(IReadOnlyList<SpeakerTurn> speakerTurns)
        {
            return speakerTurns
                .Select(static turn => turn.SpeakerId)
                .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        private static string BuildStatus(int mergedSpeakerCount, int? expectedSpeakerCount)
        {
            if (expectedSpeakerCount is null)
            {
                return "no expected speaker count supplied";
            }

            if (mergedSpeakerCount == expectedSpeakerCount.Value)
            {
                return "matches expected speaker count";
            }

            return mergedSpeakerCount > expectedSpeakerCount.Value
                ? "over-segmented after merge"
                : "collapsed below expected speaker count after merge";
        }
    }
}
