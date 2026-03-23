using System.Reflection;
using System.Text.Json;
using MeetingRecorder.Product;

namespace MeetingRecorder.Core.Tests;

public sealed class ManagedInstallLayoutTests
{
    [Fact]
    public void ProductModule_Uses_Documents_MeetingRecorder_As_Managed_Install_Root()
    {
        var expectedInstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents",
            "MeetingRecorder");

        var layout = MeetingRecorderProductModule.Instance.GetManagedInstallLayout();

        Assert.Equal(expectedInstallRoot, layout.InstallRoot);
    }

    [Fact]
    public void ProductManifest_Uses_Documents_MeetingRecorder_As_Managed_Install_Root()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var manifestPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Product", "MeetingRecorder.product.json");

        Assert.True(File.Exists(manifestPath), $"Expected product manifest at '{manifestPath}'.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var installRoot = document.RootElement
            .GetProperty("managedInstallLayout")
            .GetProperty("installRoot")
            .GetString();

        Assert.Equal("%USERPROFILE%\\Documents\\MeetingRecorder", installRoot);
    }

    [Fact]
    public void ProductModule_Declares_LocalPrograms_As_A_Legacy_Install_Root()
    {
        var expectedLegacyInstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Meeting Recorder");

        var layout = MeetingRecorderProductModule.Instance.GetManagedInstallLayout();

        Assert.Contains(expectedLegacyInstallRoot, layout.LegacyInstallRoots);
    }

    [Fact]
    public void ProductModule_Uses_Lnk_Shortcuts_For_Desktop_And_StartMenu()
    {
        var manifest = MeetingRecorderProductModule.Instance.GetManifest();

        Assert.Equal("Meeting Recorder.lnk", manifest.ShortcutPolicy.DesktopShortcutFileName);
        Assert.Equal("Meeting Recorder.lnk", manifest.ShortcutPolicy.StartMenuShortcutFileName);
    }

    [Fact]
    public void ProductManifest_Declares_LocalPrograms_As_A_Legacy_Install_Root()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var manifestPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Product", "MeetingRecorder.product.json");

        Assert.True(File.Exists(manifestPath), $"Expected product manifest at '{manifestPath}'.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var legacyInstallRoots = document.RootElement
            .GetProperty("managedInstallLayout")
            .GetProperty("legacyInstallRoots")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("%LOCALAPPDATA%\\Programs\\Meeting Recorder", legacyInstallRoots);
    }

    [Fact]
    public void ProductManifest_Uses_Lnk_Shortcuts_For_Desktop_And_StartMenu()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var manifestPath = Path.Combine(repoRoot, "src", "MeetingRecorder.Product", "MeetingRecorder.product.json");

        Assert.True(File.Exists(manifestPath), $"Expected product manifest at '{manifestPath}'.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var shortcutPolicy = document.RootElement.GetProperty("shortcutPolicy");

        Assert.Equal("Meeting Recorder.lnk", shortcutPolicy.GetProperty("desktopShortcutFileName").GetString());
        Assert.Equal("Meeting Recorder.lnk", shortcutPolicy.GetProperty("startMenuShortcutFileName").GetString());
    }

    [Fact]
    public void AutomaticUpdateCycle_Reconciles_Pending_Update_Metadata_Before_Checking_For_Updates()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
        var mainWindowPath = Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MainWindow.xaml.cs");

        Assert.True(File.Exists(mainWindowPath), $"Expected main window code at '{mainWindowPath}'.");

        var contents = File.ReadAllText(mainWindowPath);
        const string methodSignature = "private async Task RunAutomaticUpdateCycleAsync(";
        var methodStart = contents.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Expected RunAutomaticUpdateCycleAsync to exist.");

        var methodEnd = contents.IndexOf("private async Task RunExternalAudioImportCycleAsync(", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "Expected the next method after RunAutomaticUpdateCycleAsync.");

        var methodBody = contents[methodStart..methodEnd];
        var installIndex = methodBody.IndexOf("await TryInstallAvailableUpdateIfIdleAsync(source, cancellationToken);", StringComparison.Ordinal);
        var checkIndex = methodBody.IndexOf("await CheckForUpdatesAsync(source, manual: false, cancellationToken);", StringComparison.Ordinal);

        Assert.True(installIndex >= 0, "Expected the automatic update cycle to try idle installation.");
        Assert.True(checkIndex >= 0, "Expected the automatic update cycle to check for updates.");
        Assert.True(
            installIndex < checkIndex,
            "Expected the automatic update cycle to reconcile/install pending updates before checking GitHub for the same version again.");
    }
}
