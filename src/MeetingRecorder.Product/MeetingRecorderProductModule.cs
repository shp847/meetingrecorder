using AppPlatform.Abstractions;
using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Product;

public sealed class MeetingRecorderProductModule :
    IProductManifestProvider,
    IProductShellModule,
    IProductDeploymentModule
{
    public static MeetingRecorderProductModule Instance { get; } = new();

    private static readonly IReadOnlyList<ShellNavigationItemDefinition> NavigationItems =
    [
        new("home", "Home", "Recording readiness, status, and next actions."),
        new("meetings", "Meetings", "Meeting library, cleanup, and transcript workflows."),
    ];

    private static readonly IReadOnlyList<SettingsSectionDefinition> SettingsSections =
    [
        new("setup", "Setup", "Make transcription and speaker labeling ready."),
        new("general", "General", "Daily defaults and helper behavior."),
        new("files", "Files", "Output folders and managed storage."),
        new("updates", "Updates", "Release checks and installation behavior."),
        new("advanced", "Advanced", "Troubleshooting and infrastructure overrides."),
    ];

    private static readonly IReadOnlyList<SupportActionDefinition> SupportActions =
    [
        new("open-setup-guide", "Setup Guide", "Open the bundled setup guide.", "Open setup guide", "Opened the local speaker labeling setup guide."),
        new("open-logs-folder", "Logs Folder", "Open the app logs folder.", "Open logs folder", "Opened the app logs folder."),
        new("open-data-folder", "Data Folder", "Open the Meeting Recorder data folder.", "Open data folder", "Opened the Meeting Recorder data folder."),
        new("open-release-page", "Release Page", "Open the latest release page.", "Open release page", "Opened the latest release page."),
    ];

    private MeetingRecorderProductModule()
    {
    }

    public AppProductManifest GetManifest()
    {
        return new AppProductManifest(
            ProductId: "meeting-recorder",
            ProductName: AppBranding.ProductName,
            DisplayName: AppBranding.DisplayNameWithVersion,
            ExecutableName: "MeetingRecorder.App.exe",
            PortableLauncherFileName: "Run-MeetingRecorder.cmd",
            InstallerExecutableName: string.Empty,
            InstallerMsiName: "MeetingRecorderInstaller.msi",
            PortableArchivePrefix: "MeetingRecorder",
            UpdateFeedUrl: AppBranding.DefaultUpdateFeedUrl,
            ReleasePageUrl: AppBranding.DefaultReleasePageUrl,
            GitHubRepositoryOwner: AppBranding.GitHubRepositoryOwner,
            GitHubRepositoryName: AppBranding.GitHubRepositoryName,
            ManagedInstallLayout: GetManagedInstallLayout(),
            ReleaseChannelPolicy: new AppReleaseChannelPolicy(
                SupportsPerUserMsi: true,
                SupportsPortableZip: true,
                SupportsCommandBootstrap: true,
                SupportsExecutableBootstrap: false,
                SupportsAutoUpgrade: true),
            ShortcutPolicy: new ShellShortcutPolicy(
                DisplayName: "Meeting Recorder",
                DesktopShortcutFileName: "Meeting Recorder.lnk",
                StartMenuShortcutFileName: "Meeting Recorder.lnk",
                RunRegistryEntryName: "MeetingRecorder"));
    }

    public ManagedInstallLayout GetManagedInstallLayout()
    {
        return new ManagedInstallLayout(
            InstallRoot: AppDataPaths.GetManagedInstallRoot(),
            DataRoot: AppDataPaths.GetManagedAppRoot(),
            ConfigPath: AppDataPaths.GetManagedConfigPath(),
            PreservedDataDirectories: ["config", "logs", "audio", "transcripts", "work"],
            MergeWithoutOverwriteDirectories: ["models"],
            LegacyInstallRoots:
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "Meeting Recorder"),
            ]);
    }

    public IReadOnlyList<ShellNavigationItemDefinition> GetPrimaryNavigation() => NavigationItems;

    public IReadOnlyList<SettingsSectionDefinition> GetSettingsSections() => SettingsSections;

    public AboutContentDefinition GetAboutContent()
    {
        return new AboutContentDefinition(
            ProductName: AppBranding.ProductName,
            Version: AppBranding.Version,
            ProductDescription: "Local-first meeting recording and transcription for Teams, Google Meet, Zoom, Webex, and other conferencing apps, with portable deployment and automation-ready outputs.",
            AuthorName: AppBranding.AuthorName,
            AuthorEmail: AppBranding.AuthorEmail,
            SupportDescription: "Start with the bundled setup guide, then open logs or the full data root when you need to inspect what the app is using locally.",
            ReleaseDescription: "Use the release page for changelog details and the latest published installer metadata.",
            LegalNotice: AppBranding.RecordingLegalNotice);
    }

    public IReadOnlyList<SupportActionDefinition> GetSupportActions() => SupportActions;
}
