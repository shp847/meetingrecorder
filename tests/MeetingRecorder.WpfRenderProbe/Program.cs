using MeetingRecorder.Core.Tests;
using System.IO;
using System.Security.Cryptography;

var state = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "setup-blocked";
var scale = args.Length > 1 ? args[1].Trim() : "100";
var outputRoot = args.Length > 2
    ? Path.GetFullPath(args[2])
    : Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "ux-audits", "whole-app-sprint-0");

var syntheticState = state switch
{
    "empty-healthy" => WpfRenderHarness.SyntheticShellState.EmptyHealthy,
    "setup-blocked" => WpfRenderHarness.SyntheticShellState.SetupBlocked,
    "processing" => WpfRenderHarness.SyntheticShellState.Processing,
    "selection-active" => WpfRenderHarness.SyntheticShellState.SelectionActive,
    "cleanup-recommendation" => WpfRenderHarness.SyntheticShellState.CleanupRecommendation,
    _ => throw new ArgumentException($"Unknown synthetic state '{state}'."),
};

if (scale is not ("100" or "125"))
{
    throw new ArgumentException("Scale must be 100 or 125.");
}

Directory.CreateDirectory(outputRoot);
Environment.SetEnvironmentVariable("MEETINGRECORDER_WPF_HARNESS_EVIDENCE_ROOT", outputRoot);

var evidence = scale == "125"
    ? WpfRenderHarness.CaptureShellAt125Dpi(syntheticState)
    : syntheticState switch
    {
        WpfRenderHarness.SyntheticShellState.EmptyHealthy => WpfRenderHarness.CaptureEmptyHealthyShell(),
        WpfRenderHarness.SyntheticShellState.SetupBlocked => WpfRenderHarness.CaptureHomeShell(),
        WpfRenderHarness.SyntheticShellState.Processing => WpfRenderHarness.CaptureProcessingShell(),
        WpfRenderHarness.SyntheticShellState.SelectionActive => WpfRenderHarness.CaptureSelectionActiveShell(),
        WpfRenderHarness.SyntheticShellState.CleanupRecommendation => WpfRenderHarness.CaptureCleanupRecommendationShell(),
        _ => throw new InvalidOperationException("Synthetic state dispatch is incomplete."),
    };

Console.WriteLine($"State: {state}");
Console.WriteLine($"Scale: {scale}%");
Console.WriteLine($"Screenshot: {evidence.ScreenshotPath}");
Console.WriteLine($"Screenshot SHA-256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidence.ScreenshotPath))).ToLowerInvariant()}");
Console.WriteLine($"Automation trace: {evidence.AutomationTracePath}");
Console.WriteLine($"Keyboard trace: {evidence.KeyboardTracePath}");
