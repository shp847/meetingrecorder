using System.Text;

namespace AppPlatform.Deployment;

public interface IDeploymentLogger
{
    void Info(string message);

    void Error(string message);
}

public sealed class NullDeploymentLogger : IDeploymentLogger
{
    public static NullDeploymentLogger Instance { get; } = new();

    private NullDeploymentLogger()
    {
    }

    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }
}

public sealed class FileDeploymentLogger : IDeploymentLogger
{
    private readonly object _syncRoot = new();

    public FileDeploymentLogger(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            throw new ArgumentException("A deployment log path is required.", nameof(logPath));
        }

        LogPath = Path.GetFullPath(logPath);
        var logDirectory = Path.GetDirectoryName(LogPath)
            ?? throw new InvalidOperationException("Deployment log path must have a parent directory.");
        Directory.CreateDirectory(logDirectory);
    }

    public string LogPath { get; }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    private void Write(string level, string message)
    {
        lock (_syncRoot)
        {
            File.AppendAllText(
                LogPath,
                $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
    }
}
