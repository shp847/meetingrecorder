using MeetingRecorder.Core.Tests;
using System.IO;
using System.Security.Cryptography;

var state = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "setup-blocked";
var scale = args.Length > 1 ? args[1].Trim() : "100";
var outputRoot = args.Length > 2
    ? Path.GetFullPath(args[2])
    : Path.Combine(Directory.GetCurrentDirectory(), ".artifacts", "ux-audits", "whole-app-sprint-0");
var viewport = args.Length > 3 ? args[3].Trim().ToLowerInvariant() : "1280x800";
var viewportParts = viewport.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

if (viewportParts.Length != 2
    || !int.TryParse(viewportParts[0], out var logicalWidth)
    || !int.TryParse(viewportParts[1], out var logicalHeight)
    || logicalWidth <= 0
    || logicalHeight <= 0)
{
    throw new ArgumentException("Viewport must be a positive WIDTHxHEIGHT value, for example 1280x800.");
}

var syntheticState = state switch
{
    "empty-healthy" => WpfRenderHarness.SyntheticShellState.EmptyHealthy,
    "setup-blocked" => WpfRenderHarness.SyntheticShellState.SetupBlocked,
    "processing" => WpfRenderHarness.SyntheticShellState.Processing,
    "selection-active" => WpfRenderHarness.SyntheticShellState.SelectionActive,
    "cleanup-recommendation" => WpfRenderHarness.SyntheticShellState.CleanupRecommendation,
    "settings-recording" => WpfRenderHarness.SyntheticShellState.SettingsRecording,
    "settings-recording-saved" => WpfRenderHarness.SyntheticShellState.SettingsRecordingSaved,
    "permanent-delete-cancelled" => WpfRenderHarness.SyntheticShellState.PermanentDeleteCancelled,
    "hosted-summary-consent-cancelled" => WpfRenderHarness.SyntheticShellState.HostedSummaryConsentCancelled,
    "voice-profile-delete-cancelled" => WpfRenderHarness.SyntheticShellState.VoiceProfileDeleteCancelled,
    _ => throw new ArgumentException($"Unknown synthetic state '{state}'."),
};

if (scale is not ("100" or "125" or "200"))
{
    throw new ArgumentException("Scale must be 100, 125, or 200.");
}

Directory.CreateDirectory(outputRoot);
Environment.SetEnvironmentVariable("MEETINGRECORDER_WPF_HARNESS_EVIDENCE_ROOT", outputRoot);

var evidence = WpfRenderHarness.CaptureShell(
    syntheticState,
    logicalWidth,
    logicalHeight,
    scale switch
    {
        "125" => 1.25d,
        "200" => 2d,
        _ => 1d,
    });

Console.WriteLine($"State: {state}");
Console.WriteLine($"Scale: {scale}%");
Console.WriteLine($"Viewport: {logicalWidth}x{logicalHeight}");
Console.WriteLine($"Screenshot: {evidence.ScreenshotPath}");
Console.WriteLine($"Screenshot SHA-256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidence.ScreenshotPath))).ToLowerInvariant()}");
Console.WriteLine($"Automation trace: {evidence.AutomationTracePath}");
Console.WriteLine($"Keyboard trace: {evidence.KeyboardTracePath}");
