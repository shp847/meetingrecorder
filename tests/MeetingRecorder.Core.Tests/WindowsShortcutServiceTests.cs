using MeetingRecorder.Installer;

namespace MeetingRecorder.Core.Tests;

public sealed class WindowsShortcutServiceTests : IDisposable
{
    private readonly string _root;

    public WindowsShortcutServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryCreateShortcut_Writes_Lnk_Shortcut_File()
    {
        var shortcutPath = Path.Combine(_root, "Meeting Recorder.lnk");
        var targetPath = Path.Combine(_root, "Run-MeetingRecorder.cmd");
        var workingDirectory = Path.Combine(_root, "app");
        var iconPath = Path.Combine(_root, "MeetingRecorder.ico");

        Directory.CreateDirectory(workingDirectory);
        File.WriteAllText(targetPath, "@echo off");
        File.WriteAllText(iconPath, "icon");

        var service = new WindowsShortcutService();

        var result = service.TryCreateShortcut(shortcutPath, targetPath, workingDirectory, iconPath);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);
        Assert.True(File.Exists(shortcutPath));
        Assert.EndsWith(".lnk", shortcutPath, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
