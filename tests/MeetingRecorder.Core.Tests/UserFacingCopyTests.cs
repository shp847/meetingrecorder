using System.Reflection;

namespace MeetingRecorder.Core.Tests;

public sealed class UserFacingCopyTests
{
    [Fact]
    public void User_Facing_Copy_Avoids_Locked_Down_And_Corporate_Laptop_Messaging()
    {
        var prohibitedPhrases = new[]
        {
            "locked-down",
            "locked down",
            "corporate laptop",
            "corporate laptops",
            "corporate-friendly",
            "corporate endpoint",
            "endpoint policy",
            "endpoint-security",
            "endpoint security",
            "restricted corporate",
        };

        foreach (var path in GetUserFacingPaths())
        {
            var contents = File.ReadAllText(path);

            foreach (var phrase in prohibitedPhrases)
            {
                Assert.DoesNotContain(phrase, contents, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Setup_And_Releasing_Guides_Explain_That_Msi_Uninstall_Preserves_User_Data_For_Fresh_Reinstall()
    {
        var repoRoot = GetRepoRoot();
        var setupPath = Path.Combine(repoRoot, "SETUP.md");
        var releasingPath = Path.Combine(repoRoot, "RELEASING.md");

        var setupContents = File.ReadAllText(setupPath);
        var releasingContents = File.ReadAllText(releasingPath);

        Assert.Contains("uninstall", setupContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preserve", setupContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%LOCALAPPDATA%\\MeetingRecorder", setupContents, StringComparison.Ordinal);
        Assert.Contains("fresh install", setupContents, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("uninstall", releasingContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh install", releasingContents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preserve", releasingContents, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetUserFacingPaths()
    {
        var repoRoot = GetRepoRoot();

        return
        [
            Path.Combine(repoRoot, "README.md"),
            Path.Combine(repoRoot, "SETUP.md"),
            Path.Combine(repoRoot, "ARCHITECTURE.md"),
            Path.Combine(repoRoot, "RELEASE_NOTES_v0.3.md"),
            Path.Combine(repoRoot, "src", "MeetingRecorder.Installer", "MainWindow.xaml"),
            Path.Combine(repoRoot, "src", "MeetingRecorder.Installer", "MainWindow.xaml.cs"),
            Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MainWindow.xaml"),
            Path.Combine(repoRoot, "src", "MeetingRecorder.App", "MainWindow.xaml.cs"),
            Path.Combine(repoRoot, "src", "MeetingRecorder.App", "SetupWindow.xaml"),
            Path.Combine(repoRoot, "src", "AppPlatform.Shell.Wpf", "SettingsHostWindow.xaml"),
            Path.Combine(repoRoot, "src", "AppPlatform.Shell.Wpf", "HelpHostWindow.xaml"),
        ];
    }

    private static string GetRepoRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", ".."));
    }
}
