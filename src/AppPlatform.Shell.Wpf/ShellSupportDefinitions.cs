using AppPlatform.Abstractions;

namespace AppPlatform.Shell.Wpf;

public static class ShellSupportDefinitions
{
    public static IReadOnlyList<SettingsSectionDefinition> CreateDefaultSettingsSections()
    {
        return
        [
            new SettingsSectionDefinition("setup", "Setup", "Make transcription and speaker labeling ready."),
            new SettingsSectionDefinition("recording", "Recording", "Recording assistance, microphone scope, and startup behavior."),
            new SettingsSectionDefinition("processing", "Processing", "Transcript, backlog, speaker-labeling, and local learning behavior."),
            new SettingsSectionDefinition("summaries", "Summaries", "Summary mode, readiness, and provider boundary."),
            new SettingsSectionDefinition("files-and-updates", "Files & Updates", "Output folders, release checks, and update controls."),
            new SettingsSectionDefinition("advanced", "Advanced", "Troubleshooting and infrastructure overrides."),
        ];
    }
}
