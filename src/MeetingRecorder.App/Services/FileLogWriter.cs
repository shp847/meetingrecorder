namespace MeetingRecorder.App.Services;

public sealed class FileLogWriter
{
    private readonly string _logPath;
    private readonly object _syncRoot = new();

    public FileLogWriter(string logPath)
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? throw new InvalidOperationException("Log path must include a directory."));
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
        lock (_syncRoot)
        {
            File.AppendAllText(_logPath, line);
        }
    }
}
