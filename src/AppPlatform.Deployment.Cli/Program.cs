using AppPlatform.Abstractions;
using AppPlatform.Deployment;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;
using System.Globalization;
using System.Diagnostics;

namespace AppPlatform.Deployment.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        FileDeploymentLogger? logger = null;
        IReadOnlyDictionary<string, string?> options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var command = args[0];
        var pauseOnError = false;

        try
        {
            options = ParseOptions(args.Skip(1));
            pauseOnError = HasSwitch(options, "--pause-on-error");

            var logPath = ResolveLogPath(command, options);
            logger = new FileDeploymentLogger(logPath);
            logger.Info($"Command '{command}' starting.");
            logger.Info($"Raw arguments: {string.Join(" ", args)}");
            Console.WriteLine($"Diagnostic log: {logPath}");

            var exitCode = command switch
            {
                "install-bundle" => await InstallBundleAsync(options, logger),
                "install-latest" => await InstallLatestAsync(options, logger),
                "apply-update" => await ApplyUpdateAsync(options, logger),
                "provision-models" => await ProvisionModelsAsync(options, logger),
                "repair-shortcuts" => RepairShortcuts(options, logger),
                "print-layout" => PrintLayout(options, logger),
                "emit-manual-steps" => EmitManualSteps(options, logger),
                _ => Fail($"Unknown command '{command}'."),
            };

            logger.Info($"Command '{command}' completed with exit code {exitCode}.");

            if (exitCode != 0 && pauseOnError)
            {
                PauseOnError();
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            logger ??= new FileDeploymentLogger(ResolveFallbackLogPath(command));
            logger.Error(exception.ToString());
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine($"Detailed log: {logger.LogPath}");

            if (pauseOnError)
            {
                PauseOnError();
            }

            return 1;
        }
    }

    private static async Task<int> InstallBundleAsync(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var bundleRoot = GetRequiredOption(options, "--bundle-root");
        var installRoot = GetOptionalOption(options, "--install-root", "-i");
        var installer = CreatePortableBundleInstaller(logger);
        var launchAfterInstall = !HasSwitch(options, "--no-launch");

        var result = await installer.InstallAsync(
            manifest,
            new InstallRequest(
                BundleRoot: bundleRoot,
                InstallRoot: installRoot,
                CreateDesktopShortcut: !HasSwitch(options, "--no-desktop-shortcut"),
                CreateStartMenuShortcut: !HasSwitch(options, "--no-start-menu-shortcut"),
                LaunchAfterInstall: false,
                ReleaseVersion: GetOptionalOption(options, "--release-version"),
                ReleasePublishedAtUtc: ParseDateTimeOffset(GetOptionalOption(options, "--release-published-at-utc")),
                ReleaseAssetSizeBytes: ParseInt64(GetOptionalOption(options, "--release-asset-size-bytes")),
                Channel: ParseInstallChannel(GetOptionalOption(options, "--install-channel"), InstallChannel.DirectCli)),
            CancellationToken.None);

        await ProvisionInstalledModelsAsync(manifest, result.InstallRoot, logger, CancellationToken.None);
        if (launchAfterInstall)
        {
            LaunchInstalledApp(manifest, result.InstallRoot, logger);
        }

        Console.WriteLine($"Installed to {result.InstallRoot}");
        return 0;
    }

    private static async Task<int> InstallLatestAsync(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var feedOverride = GetOptionalOption(options, "--feed-url");
        if (!string.IsNullOrWhiteSpace(feedOverride))
        {
            manifest = manifest with
            {
                UpdateFeedUrl = feedOverride,
            };
        }

        using var downloadClient = new DeploymentDownloadClient(
            manifest.ProductName.Replace(" ", string.Empty, StringComparison.Ordinal),
            "1.0",
            logger);
        var installer = new LatestReleaseInstaller(
            new GitHubReleaseCatalogService(downloadClient, logger),
            CreatePortableBundleInstaller(logger),
            logger);
        var (_, installResult) = await installer.InstallLatestAsync(
            manifest,
            GetOptionalOption(options, "--install-root", "-i"),
            createDesktopShortcut: !HasSwitch(options, "--no-desktop-shortcut"),
            createStartMenuShortcut: !HasSwitch(options, "--no-start-menu-shortcut"),
            launchAfterInstall: false,
            installChannel: ParseInstallChannel(GetOptionalOption(options, "--install-channel"), InstallChannel.CommandBootstrap),
            CancellationToken.None);

        await ProvisionInstalledModelsAsync(manifest, installResult.InstallRoot, logger, CancellationToken.None);
        if (!HasSwitch(options, "--no-launch"))
        {
            LaunchInstalledApp(manifest, installResult.InstallRoot, logger);
        }

        Console.WriteLine($"Installed latest release to {installResult.InstallRoot}");
        return 0;
    }

    private static async Task<int> ApplyUpdateAsync(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var launchAfterInstall = !HasSwitch(options, "--no-launch");
        var result = await new UpdatePackageInstaller(
            CreatePortableBundleInstaller(logger),
            new InstallPathProcessManager(new InstallPathProcessController(), logger),
            logger).ApplyAsync(
            manifest,
            new UpdateRequest(
                ZipPath: GetRequiredOption(options, "--zip-path"),
                InstallRoot: GetRequiredOption(options, "--install-root", "-i"),
                SourceProcessId: ParseInt32(GetOptionalOption(options, "--source-process-id")) ?? 0,
                ReleaseVersion: GetOptionalOption(options, "--release-version"),
                ReleasePublishedAtUtc: ParseDateTimeOffset(GetOptionalOption(options, "--release-published-at-utc")),
                ReleaseAssetSizeBytes: ParseInt64(GetOptionalOption(options, "--release-asset-size-bytes")),
                Channel: ParseInstallChannel(GetOptionalOption(options, "--update-channel"), InstallChannel.AutoUpdate),
                LaunchAfterInstall: false),
            CancellationToken.None);

        if (result.Succeeded)
        {
            await ProvisionInstalledModelsAsync(manifest, result.InstallRoot, logger, CancellationToken.None);
            if (launchAfterInstall)
            {
                LaunchInstalledApp(manifest, result.InstallRoot, logger);
            }
        }

        Console.WriteLine($"Updated installation at {result.InstallRoot}");
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ProvisionModelsAsync(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var installRoot = GetRequiredOption(options, "--install-root", "-i");
        var transcriptionProfile = ParseTranscriptionProfile(GetRequiredOption(options, "--transcription-profile", "-t"));
        var speakerLabelingProfile = ParseSpeakerLabelingProfile(GetRequiredOption(options, "--speaker-labeling-profile", "-s"));
        var modelCatalogPath = Path.Combine(installRoot, MeetingRecorderModelCatalogService.CatalogFileName);
        logger.Info($"Loading curated model catalog from '{modelCatalogPath}'.");

        var configStore = new AppConfigStore(manifest.ManagedInstallLayout.ConfigPath);
        var resultStore = new ModelProvisioningResultStore(configStore.ConfigPath);
        var catalogService = new MeetingRecorderModelCatalogService();
        var whisperModelService = new WhisperModelService(new WhisperNetModelDownloader());
        var feedClient = new HttpAppUpdateFeedClient();
        var diarizationCatalogService = new DiarizationAssetCatalogService();
        var provisioningService = new ModelProvisioningService(
            configStore,
            resultStore,
            catalogService,
            whisperModelService,
            new WhisperModelReleaseCatalogService(feedClient, whisperModelService),
            diarizationCatalogService,
            new DiarizationAssetReleaseCatalogService(feedClient, diarizationCatalogService));

        var provisioningResult = await provisioningService.ProvisionAsync(
            new ModelProvisioningRequest(
                InstallRoot: installRoot,
                ModelCatalogPath: modelCatalogPath,
                UpdateFeedUrl: manifest.UpdateFeedUrl,
                TranscriptionProfile: transcriptionProfile,
                SpeakerLabelingProfile: speakerLabelingProfile),
            CancellationToken.None);

        logger.Info(
            $"Provisioned models. Transcription requested={provisioningResult.Result.Transcription.RequestedProfile}, active={provisioningResult.Result.Transcription.ActiveProfile}, retryRecommended={provisioningResult.Result.Transcription.RetryRecommended}.");
        logger.Info(
            $"Provisioned models. Speaker labeling requested={provisioningResult.Result.SpeakerLabeling.RequestedProfile}, active={provisioningResult.Result.SpeakerLabeling.ActiveProfile}, retryRecommended={provisioningResult.Result.SpeakerLabeling.RetryRecommended}.");

        Console.WriteLine($"Transcription requested profile: {provisioningResult.Result.Transcription.RequestedProfile}");
        Console.WriteLine($"Transcription active profile: {provisioningResult.Result.Transcription.ActiveProfile}");
        Console.WriteLine($"Speaker labeling requested profile: {provisioningResult.Result.SpeakerLabeling.RequestedProfile}");
        Console.WriteLine($"Speaker labeling active profile: {provisioningResult.Result.SpeakerLabeling.ActiveProfile}");
        return 0;
    }

    private static int RepairShortcuts(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var installRoot = GetRequiredOption(options, "--install-root", "-i");
        var shortcutService = new WindowsShortcutService();
        var targetPath = ResolveShortcutTargetPath(manifest, installRoot);
        var iconPath = Path.Combine(installRoot, "MeetingRecorder.ico");

        var createDesktopShortcut = !HasSwitch(options, "--start-menu-only");
        var createStartMenuShortcut = !HasSwitch(options, "--desktop-only");

        shortcutService.RemoveLegacyShortcuts(
            manifest.ShortcutPolicy,
            removeDesktopShortcut: createDesktopShortcut,
            removeStartMenuShortcut: createStartMenuShortcut);

        if (createDesktopShortcut)
        {
            shortcutService.TryCreateShortcut(
                shortcutService.GetDesktopShortcutPath(manifest.ShortcutPolicy),
                targetPath,
                installRoot,
                iconPath);
        }

        if (createStartMenuShortcut)
        {
            shortcutService.TryCreateShortcut(
                shortcutService.GetStartMenuShortcutPath(manifest.ShortcutPolicy),
                targetPath,
                installRoot,
                iconPath);
        }

        Console.WriteLine("Shortcut repair completed.");
        return 0;
    }

    private static int PrintLayout(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        Console.WriteLine($"Install root: {manifest.ManagedInstallLayout.InstallRoot}");
        Console.WriteLine($"Data root: {manifest.ManagedInstallLayout.DataRoot}");
        Console.WriteLine($"Config path: {manifest.ManagedInstallLayout.ConfigPath}");
        return 0;
    }

    private static int EmitManualSteps(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var installRoot = GetOptionalOption(options, "--install-root", "-i") ?? manifest.ManagedInstallLayout.InstallRoot;
        Console.WriteLine(ManualInstallStepsBuilder.Build(manifest, installRoot));
        return 0;
    }

    private static async Task ProvisionInstalledModelsAsync(
        AppProductManifest manifest,
        string installRoot,
        IDeploymentLogger logger,
        CancellationToken cancellationToken)
    {
        logger.Info($"Provisioning installed models from '{installRoot}'.");
        var configStore = new AppConfigStore(manifest.ManagedInstallLayout.ConfigPath);
        var currentConfig = await configStore.LoadOrCreateAsync(cancellationToken);
        var resultStore = new ModelProvisioningResultStore(configStore.ConfigPath);
        var catalogService = new MeetingRecorderModelCatalogService();
        var whisperModelService = new WhisperModelService(new WhisperNetModelDownloader());
        var feedClient = new HttpAppUpdateFeedClient();
        var diarizationCatalogService = new DiarizationAssetCatalogService();
        var provisioningService = new ModelProvisioningService(
            configStore,
            resultStore,
            catalogService,
            whisperModelService,
            new WhisperModelReleaseCatalogService(feedClient, whisperModelService),
            diarizationCatalogService,
            new DiarizationAssetReleaseCatalogService(feedClient, diarizationCatalogService));

        var provisioningResult = await provisioningService.ProvisionAsync(
            new ModelProvisioningRequest(
                InstallRoot: installRoot,
                ModelCatalogPath: Path.Combine(installRoot, MeetingRecorderModelCatalogService.CatalogFileName),
                UpdateFeedUrl: manifest.UpdateFeedUrl,
                TranscriptionProfile: currentConfig.TranscriptionModelProfilePreference,
                SpeakerLabelingProfile: currentConfig.SpeakerLabelingModelProfilePreference),
            cancellationToken);

        logger.Info(
            $"Installed model provisioning completed. Transcription active={provisioningResult.Result.Transcription.ActiveProfile}; speaker-labeling active={provisioningResult.Result.SpeakerLabeling.ActiveProfile}.");
    }

    private static void LaunchInstalledApp(
        AppProductManifest manifest,
        string installRoot,
        IDeploymentLogger logger)
    {
        var launcherPath = Path.Combine(installRoot, manifest.PortableLauncherFileName);
        if (!File.Exists(launcherPath))
        {
            launcherPath = Path.Combine(installRoot, manifest.ExecutableName);
        }

        logger.Info($"Launching installed app from '{launcherPath}'.");
        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = installRoot,
            UseShellExecute = true,
        });
    }

    private static AppProductManifest LoadManifest(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifestPath = GetOptionalOption(options, "--manifest-path", "-m") ??
            Path.Combine(AppContext.BaseDirectory, "MeetingRecorder.product.json");
        logger.Info($"Loading product manifest from '{manifestPath}'.");
        return AppProductManifestFileLoader.Load(manifestPath);
    }

    private static PortableBundleInstaller CreatePortableBundleInstaller(IDeploymentLogger logger)
    {
        return new PortableBundleInstaller(
            new InstallPathProcessManager(new InstallPathProcessController(), logger),
            new WindowsShortcutService(),
            logger);
    }

    private static string ResolveShortcutTargetPath(AppProductManifest manifest, string installRoot)
    {
        var launcherPath = Path.Combine(installRoot, manifest.PortableLauncherFileName);
        if (File.Exists(launcherPath))
        {
            return launcherPath;
        }

        return Path.Combine(installRoot, manifest.ExecutableName);
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? pendingKey = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pendingKey is not null)
                {
                    options[pendingKey] = null;
                }

                pendingKey = arg;
                continue;
            }

            if (pendingKey is null)
            {
                throw new InvalidOperationException($"Unexpected value '{arg}' without an option name.");
            }

            options[pendingKey] = arg;
            pendingKey = null;
        }

        if (pendingKey is not null)
        {
            options[pendingKey] = null;
        }

        return options;
    }

    private static string GetRequiredOption(IReadOnlyDictionary<string, string?> options, params string[] names)
    {
        var value = GetOptionalOption(options, names);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option '{GetPrimaryOptionName(names)}'.");
        }

        return value;
    }

    private static string? GetOptionalOption(IReadOnlyDictionary<string, string?> options, params string[] names)
    {
        foreach (var name in names)
        {
            if (options.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool HasSwitch(IReadOnlyDictionary<string, string?> options, string name)
    {
        return options.ContainsKey(name);
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private static long? ParseInt64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int? ParseInt32(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static InstallChannel ParseInstallChannel(string? value, InstallChannel fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!Enum.TryParse<InstallChannel>(value, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"Unknown install channel '{value}'.");
        }

        return parsed;
    }

    private static TranscriptionModelProfilePreference ParseTranscriptionProfile(string value)
    {
        if (!Enum.TryParse<TranscriptionModelProfilePreference>(value, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"Unknown transcription profile '{value}'.");
        }

        return parsed;
    }

    private static SpeakerLabelingModelProfilePreference ParseSpeakerLabelingProfile(string value)
    {
        if (!Enum.TryParse<SpeakerLabelingModelProfilePreference>(value, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException($"Unknown speaker-labeling profile '{value}'.");
        }

        return parsed;
    }

    private static string ResolveLogPath(string command, IReadOnlyDictionary<string, string?> options)
    {
        var explicitLogPath = GetOptionalOption(options, "--log-path", "-l");
        if (!string.IsNullOrWhiteSpace(explicitLogPath))
        {
            return Path.GetFullPath(explicitLogPath);
        }

        return ResolveFallbackLogPath(command);
    }

    private static string ResolveFallbackLogPath(string command)
    {
        var safeCommand = string.Concat(
            command.Select(static character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        var logDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderInstaller");
        return Path.Combine(
            logDirectory,
            $"{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{safeCommand}.log");
    }

    private static void PauseOnError()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.Write("Press Enter to close this window.");

        try
        {
            Console.ReadLine();
        }
        catch
        {
            // Best-effort pause only.
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("AppPlatform deployment CLI");
        Console.WriteLine("Common options:");
        Console.WriteLine("  --log-path <file> | -l <file>");
        Console.WriteLine("  --pause-on-error");
        Console.WriteLine("Commands:");
        Console.WriteLine("  install-bundle");
        Console.WriteLine("  install-latest");
        Console.WriteLine("  apply-update");
        Console.WriteLine("  provision-models");
        Console.WriteLine("  repair-shortcuts");
        Console.WriteLine("  print-layout");
        Console.WriteLine("  emit-manual-steps");
        Console.WriteLine("Common path aliases:");
        Console.WriteLine("  --install-root <path> | -i <path>");
        Console.WriteLine("  --manifest-path <file> | -m <file>");
        Console.WriteLine("Provision-models aliases:");
        Console.WriteLine("  --transcription-profile <profile> | -t <profile>");
        Console.WriteLine("  --speaker-labeling-profile <profile> | -s <profile>");
        Console.WriteLine("Channel options:");
        Console.WriteLine("  --install-channel <Unknown|Msi|CommandBootstrap|ExecutableBootstrap|PortableZip|AutoUpdate|DirectCli>");
        Console.WriteLine("  --update-channel <Unknown|Msi|CommandBootstrap|ExecutableBootstrap|PortableZip|AutoUpdate|DirectCli>");
    }

    private static string GetPrimaryOptionName(IReadOnlyList<string> names)
    {
        return names.Count > 0 ? names[0] : "<unknown>";
    }
}
