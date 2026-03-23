using AppPlatform.Abstractions;

namespace AppPlatform.Shell.Wpf;

public static class ShellSupportDefinitions
{
    public static IReadOnlyList<SettingsSectionDefinition> CreateDefaultSettingsSections()
    {
        return
        [
            new SettingsSectionDefinition("setup", "Setup", "Make transcription and speaker labeling ready."),
            new SettingsSectionDefinition("general", "General", "Daily defaults and helper behavior."),
            new SettingsSectionDefinition("files", "Files", "Output folders and managed storage."),
            new SettingsSectionDefinition("updates", "Updates", "Release checks and installation behavior."),
            new SettingsSectionDefinition("advanced", "Advanced", "Troubleshooting and infrastructure overrides."),
        ];
    }
}
