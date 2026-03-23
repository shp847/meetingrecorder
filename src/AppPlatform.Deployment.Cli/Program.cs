using AppPlatform.Abstractions;
using AppPlatform.Deployment;
using System.Globalization;

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
        var installRoot = GetOptionalOption(options, "--install-root");
        var installer = CreatePortableBundleInstaller(logger);

        var result = await installer.InstallAsync(
            manifest,
            new InstallRequest(
                BundleRoot: bundleRoot,
                InstallRoot: installRoot,
                CreateDesktopShortcut: !HasSwitch(options, "--no-desktop-shortcut"),
                CreateStartMenuShortcut: !HasSwitch(options, "--no-start-menu-shortcut"),
                LaunchAfterInstall: !HasSwitch(options, "--no-launch"),
                ReleaseVersion: GetOptionalOption(options, "--release-version"),
                ReleasePublishedAtUtc: ParseDateTimeOffset(GetOptionalOption(options, "--release-published-at-utc")),
                ReleaseAssetSizeBytes: ParseInt64(GetOptionalOption(options, "--release-asset-size-bytes")),
                Channel: ParseInstallChannel(GetOptionalOption(options, "--install-channel"), InstallChannel.DirectCli)),
            CancellationToken.None);

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
            GetOptionalOption(options, "--install-root"),
            createDesktopShortcut: !HasSwitch(options, "--no-desktop-shortcut"),
            createStartMenuShortcut: !HasSwitch(options, "--no-start-menu-shortcut"),
            launchAfterInstall: !HasSwitch(options, "--no-launch"),
            installChannel: ParseInstallChannel(GetOptionalOption(options, "--install-channel"), InstallChannel.CommandBootstrap),
            CancellationToken.None);

        Console.WriteLine($"Installed latest release to {installResult.InstallRoot}");
        return 0;
    }

    private static async Task<int> ApplyUpdateAsync(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var result = await new UpdatePackageInstaller(
            CreatePortableBundleInstaller(logger),
            new InstallPathProcessManager(new InstallPathProcessController(), logger),
            logger).ApplyAsync(
            manifest,
            new UpdateRequest(
                ZipPath: GetRequiredOption(options, "--zip-path"),
                InstallRoot: GetRequiredOption(options, "--install-root"),
                SourceProcessId: ParseInt32(GetOptionalOption(options, "--source-process-id")) ?? 0,
                ReleaseVersion: GetOptionalOption(options, "--release-version"),
                ReleasePublishedAtUtc: ParseDateTimeOffset(GetOptionalOption(options, "--release-published-at-utc")),
                ReleaseAssetSizeBytes: ParseInt64(GetOptionalOption(options, "--release-asset-size-bytes")),
                Channel: ParseInstallChannel(GetOptionalOption(options, "--update-channel"), InstallChannel.AutoUpdate)),
            CancellationToken.None);

        Console.WriteLine($"Updated installation at {result.InstallRoot}");
        return result.Succeeded ? 0 : 1;
    }

    private static int RepairShortcuts(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifest = LoadManifest(options, logger);
        var installRoot = GetRequiredOption(options, "--install-root");
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
        var installRoot = GetOptionalOption(options, "--install-root") ?? manifest.ManagedInstallLayout.InstallRoot;
        Console.WriteLine(ManualInstallStepsBuilder.Build(manifest, installRoot));
        return 0;
    }

    private static AppProductManifest LoadManifest(
        IReadOnlyDictionary<string, string?> options,
        IDeploymentLogger logger)
    {
        var manifestPath = GetOptionalOption(options, "--manifest-path") ??
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

    private static string GetRequiredOption(IReadOnlyDictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option '{name}'.");
        }

        return value;
    }

    private static string? GetOptionalOption(IReadOnlyDictionary<string, string?> options, string name)
    {
        return options.TryGetValue(name, out var value) ? value : null;
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

    private static string ResolveLogPath(string command, IReadOnlyDictionary<string, string?> options)
    {
        var explicitLogPath = GetOptionalOption(options, "--log-path");
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
        Console.WriteLine("  --log-path <file>");
        Console.WriteLine("  --pause-on-error");
        Console.WriteLine("Commands:");
        Console.WriteLine("  install-bundle");
        Console.WriteLine("  install-latest");
        Console.WriteLine("  apply-update");
        Console.WriteLine("  repair-shortcuts");
        Console.WriteLine("  print-layout");
        Console.WriteLine("  emit-manual-steps");
        Console.WriteLine("Channel options:");
        Console.WriteLine("  --install-channel <Unknown|Msi|CommandBootstrap|ExecutableBootstrap|PortableZip|AutoUpdate|DirectCli>");
        Console.WriteLine("  --update-channel <Unknown|Msi|CommandBootstrap|ExecutableBootstrap|PortableZip|AutoUpdate|DirectCli>");
    }
}
