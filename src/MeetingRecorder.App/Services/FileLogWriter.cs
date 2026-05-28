namespace MeetingRecorder.App.Services;

public sealed class FileLogWriter
{
    private const long DefaultMaxLogBytes = 8L * 1024L * 1024L;

    private readonly string _logPath;
    private readonly object _syncRoot = new();
    private readonly long _maxLogBytes;
    private readonly Action<string, string> _appendText;

    public FileLogWriter(string logPath)
        : this(logPath, DefaultMaxLogBytes, File.AppendAllText)
    {
    }

    internal FileLogWriter(string logPath, long maxLogBytes, Action<string, string> appendText)
    {
        _logPath = logPath;
        _maxLogBytes = maxLogBytes > 0
            ? maxLogBytes
            : throw new ArgumentOutOfRangeException(nameof(maxLogBytes), "The maximum log size must be greater than zero.");
        _appendText = appendText ?? throw new ArgumentNullException(nameof(appendText));
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? throw new InvalidOperationException("Log path must include a directory."));
    }

    public void Log(string message)
    {
        try
        {
            var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
            lock (_syncRoot)
            {
                RotateIfNeeded();
                _appendText(_logPath, line);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath))
        {
            return;
        }

        var logInfo = new FileInfo(_logPath);
        if (logInfo.Length <= _maxLogBytes)
        {
            return;
        }

        var previousLogPath = _logPath + ".old";
        if (File.Exists(previousLogPath))
        {
            File.Delete(previousLogPath);
        }

        File.Move(_logPath, previousLogPath);
    }
}
