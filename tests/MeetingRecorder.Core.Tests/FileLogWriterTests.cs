using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class FileLogWriterTests
{
    [Fact]
    public void Log_Does_Not_Throw_When_Append_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-log-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var writer = new FileLogWriter(
                Path.Combine(root, "logs", "app.log"),
                maxLogBytes: 1024,
                appendText: (_, _) => throw new IOException("disk full"));

            writer.Log("message that cannot be written");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Log_Rotates_Oversized_Log_Before_Appending()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meeting-log-rotation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var logPath = Path.Combine(root, "logs", "app.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, new string('x', 64));

            var writer = new FileLogWriter(logPath, maxLogBytes: 16, appendText: File.AppendAllText);

            writer.Log("fresh line");

            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(logPath + ".old"));
            Assert.Contains("fresh line", File.ReadAllText(logPath));
            Assert.Equal(64, new FileInfo(logPath + ".old").Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
