using NAudio.Wave;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal sealed record RecorderUnexpectedStopInfo(
    DateTimeOffset OccurredAtUtc,
    string Message);

internal sealed class ChunkedWaveRecorder : IDisposable
{
    private const int DefaultLevelHistoryCapacity = 180;
    private readonly Func<IWaveIn> _captureFactory;
    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly TimeSpan _rotationInterval;
    private readonly FileLogWriter _logger;
    private readonly object _syncRoot = new();
    private readonly List<string> _chunkPaths = [];
    private IWaveIn? _capture;
    private WaveFileWriter? _writer;
    private Timer? _rotationTimer;
    private int _chunkIndex;
    private bool _isStarted;
    private bool _isStopping;
    private long _totalBytesRecorded;
    private bool _loggedFirstAudioPacket;
    private RecorderUnexpectedStopInfo? _unexpectedStop;
    private readonly AudioLevelHistory _levelHistory = new(DefaultLevelHistoryCapacity);

    public ChunkedWaveRecorder(
        Func<IWaveIn> captureFactory,
        string directory,
        string filePrefix,
        TimeSpan rotationInterval,
        FileLogWriter logger)
    {
        _captureFactory = captureFactory;
        _directory = directory;
        _filePrefix = filePrefix;
        _rotationInterval = rotationInterval;
        _logger = logger;
    }

    public IReadOnlyList<string> ChunkPaths => _chunkPaths;

    public long TotalBytesRecorded => Interlocked.Read(ref _totalBytesRecorded);

    public AudioLevelHistory LevelHistory => _levelHistory;

    public bool NeedsRecovery
    {
        get
        {
            lock (_syncRoot)
            {
                return _unexpectedStop is not null;
            }
        }
    }

    public RecorderUnexpectedStopInfo? UnexpectedStop
    {
        get
        {
            lock (_syncRoot)
            {
                return _unexpectedStop;
            }
        }
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_isStarted)
            {
                return;
            }

            _isStopping = false;
            _unexpectedStop = null;
            Directory.CreateDirectory(_directory);
            _capture = _captureFactory();
            _capture.DataAvailable += Capture_OnDataAvailable;
            _capture.RecordingStopped += Capture_OnRecordingStopped;
            OpenNewChunk();
            _rotationTimer = new Timer(_ => RotateChunk(), null, _rotationInterval, _rotationInterval);
            try
            {
                _capture.StartRecording();
                _isStarted = true;
                _logger.Log($"Recorder '{_filePrefix}' started. Format={_capture.WaveFormat}; rotation={_rotationInterval.TotalSeconds:0}s; directory='{_directory}'.");
            }
            catch
            {
                _rotationTimer?.Dispose();
                _rotationTimer = null;
                _writer?.Dispose();
                _writer = null;
                _capture.DataAvailable -= Capture_OnDataAvailable;
                _capture.RecordingStopped -= Capture_OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        IWaveIn? capture;
        lock (_syncRoot)
        {
            if (!_isStarted && _capture is null)
            {
                return;
            }

            _isStopping = true;
            capture = _capture;
            _capture = null;
            _rotationTimer?.Dispose();
            _rotationTimer = null;
            _logger.Log($"Recorder '{_filePrefix}' stopping. ChunkCount={_chunkPaths.Count}; audioBytes={TotalBytesRecorded}.");
            _writer?.Dispose();
            _writer = null;
            _isStarted = false;
        }

        if (capture is not null)
        {
            capture.DataAvailable -= Capture_OnDataAvailable;
            capture.RecordingStopped -= Capture_OnRecordingStopped;
            try
            {
                capture.StopRecording();
            }
            catch (Exception exception)
            {
                _logger.Log($"Recorder '{_filePrefix}' failed while stopping: {exception.Message}");
            }

            capture.Dispose();
        }

        lock (_syncRoot)
        {
            _isStopping = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void Capture_OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        Interlocked.Add(ref _totalBytesRecorded, e.BytesRecorded);
        if (_capture is not null)
        {
            _levelHistory.AddSample(AudioLevelMeter.MeasurePeakLevel(e.Buffer, e.BytesRecorded, _capture.WaveFormat));
        }

        if (!_loggedFirstAudioPacket && e.BytesRecorded > 0)
        {
            _loggedFirstAudioPacket = true;
            _logger.Log($"Recorder '{_filePrefix}' received its first audio payload. Bytes={e.BytesRecorded}.");
        }

        lock (_syncRoot)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            _writer?.Flush();
        }
    }

    private void Capture_OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var stoppedAtUtc = DateTimeOffset.UtcNow;
        if (e.Exception is not null)
        {
            _logger.Log($"Recorder '{_filePrefix}' stopped with an exception: {e.Exception.Message}");
        }
        else
        {
            _logger.Log($"Recorder '{_filePrefix}' stopped unexpectedly without an exception.");
        }

        IWaveIn? capture;
        var wasStopping = false;
        lock (_syncRoot)
        {
            wasStopping = _isStopping;
            capture = _capture;
            _capture = null;
            _rotationTimer?.Dispose();
            _rotationTimer = null;
            _writer?.Dispose();
            _writer = null;
            _isStarted = false;
            if (!wasStopping)
            {
                _unexpectedStop = new RecorderUnexpectedStopInfo(
                    stoppedAtUtc,
                    e.Exception?.Message ?? "Capture stopped unexpectedly.");
            }
        }

        if (capture is not null)
        {
            capture.DataAvailable -= Capture_OnDataAvailable;
            capture.RecordingStopped -= Capture_OnRecordingStopped;
            capture.Dispose();
        }
    }

    private void RotateChunk()
    {
        lock (_syncRoot)
        {
            if (_capture is null)
            {
                return;
            }

            _writer?.Dispose();
            _writer = null;
            OpenNewChunk();
        }
    }

    private void OpenNewChunk()
    {
        if (_capture is null)
        {
            throw new InvalidOperationException("Capture must be initialized before opening a chunk.");
        }

        var filePath = Path.Combine(_directory, $"{_filePrefix}-chunk-{++_chunkIndex:0000}.wav");
        _writer = new WaveFileWriter(filePath, _capture.WaveFormat);
        _chunkPaths.Add(filePath);
        _logger.Log($"Recorder '{_filePrefix}' opened chunk '{filePath}'.");
    }
}
