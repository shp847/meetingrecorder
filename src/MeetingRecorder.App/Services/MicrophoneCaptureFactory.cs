using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingRecorder.App.Services;

internal sealed record MicrophoneCaptureSelection(
    Role Role,
    string DeviceId,
    string FriendlyName,
    double EndpointPeakLevel,
    bool IsEndpointActive,
    string Reason,
    bool IsFallbackCapture);

internal sealed record MicrophoneCaptureProbeSnapshot(
    Role Role,
    string DeviceId,
    string FriendlyName,
    double EndpointPeakLevel,
    bool IsEndpointActive);

internal sealed record MicrophoneCaptureEvaluation(
    MicrophoneCaptureSelection PreferredSelection,
    MicrophoneCaptureProbeSnapshot? Multimedia,
    MicrophoneCaptureProbeSnapshot? Communications);

internal interface IMicrophoneCaptureFactory
{
    MicrophoneCaptureEvaluation Evaluate(double activityThreshold);

    IWaveIn Create(MicrophoneCaptureSelection selection);
}

internal sealed class SystemMicrophoneCaptureFactory : IMicrophoneCaptureFactory
{
    private readonly Func<Role, double, MicrophoneCaptureProbeSnapshot?> _probeSnapshot;
    private readonly Func<Role, MMDevice?> _resolveCaptureEndpoint;
    private readonly Func<MMDevice, IWaveIn> _captureFactory;
    private readonly Func<IWaveIn> _fallbackCaptureFactory;
    private readonly Action<string>? _log;

    public SystemMicrophoneCaptureFactory(Action<string>? log = null)
        : this(
            CreateProbeSnapshot,
            ResolveCaptureEndpoint,
            static device => new OwnedWaveIn(new WasapiCapture(device), device),
            CreateDefaultCapture,
            log)
    {
    }

    internal SystemMicrophoneCaptureFactory(
        Func<Role, double, MicrophoneCaptureProbeSnapshot?> probeSnapshot,
        Func<Role, MMDevice?> resolveCaptureEndpoint,
        Func<MMDevice, IWaveIn> captureFactory,
        Func<IWaveIn> fallbackCaptureFactory,
        Action<string>? log = null)
    {
        _probeSnapshot = probeSnapshot ?? throw new ArgumentNullException(nameof(probeSnapshot));
        _resolveCaptureEndpoint = resolveCaptureEndpoint ?? throw new ArgumentNullException(nameof(resolveCaptureEndpoint));
        _captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
        _fallbackCaptureFactory = fallbackCaptureFactory ?? throw new ArgumentNullException(nameof(fallbackCaptureFactory));
        _log = log;
    }

    public MicrophoneCaptureEvaluation Evaluate(double activityThreshold)
    {
        var multimedia = _probeSnapshot(Role.Multimedia, activityThreshold);
        var communications = _probeSnapshot(Role.Communications, activityThreshold);
        return new MicrophoneCaptureEvaluation(
            BuildPreferredSelection(multimedia, communications),
            multimedia,
            communications);
    }

    public IWaveIn Create(MicrophoneCaptureSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsFallbackCapture)
        {
            _log?.Invoke("Using fallback microphone capture because no explicit capture endpoint could be resolved.");
            return _fallbackCaptureFactory();
        }

        var device = ResolveCaptureEndpointForSelection(selection);
        if (device is null)
        {
            throw new InvalidOperationException(
                $"Unable to resolve microphone capture endpoint '{selection.FriendlyName}' ({selection.DeviceId}) for role '{selection.Role}'.");
        }

        return _captureFactory(device);
    }

    internal static MicrophoneCaptureProbeSnapshot? GetSnapshotForSelection(
        MicrophoneCaptureEvaluation evaluation,
        MicrophoneCaptureSelection? selection)
    {
        if (selection is null || selection.IsFallbackCapture)
        {
            return null;
        }

        if (selection.Role == Role.Communications &&
            evaluation.Communications is { } communications &&
            string.Equals(communications.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return communications;
        }

        if (selection.Role == Role.Multimedia &&
            evaluation.Multimedia is { } multimedia &&
            string.Equals(multimedia.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return multimedia;
        }

        if (evaluation.Communications is { } alternateCommunications &&
            string.Equals(alternateCommunications.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return alternateCommunications;
        }

        if (evaluation.Multimedia is { } alternateMultimedia &&
            string.Equals(alternateMultimedia.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return alternateMultimedia;
        }

        return null;
    }

    private MMDevice? ResolveCaptureEndpointForSelection(MicrophoneCaptureSelection selection)
    {
        var primary = _resolveCaptureEndpoint(selection.Role);
        if (primary is not null &&
            string.Equals(primary.ID, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        primary?.Dispose();

        var alternateRole = selection.Role == Role.Communications ? Role.Multimedia : Role.Communications;
        var alternate = _resolveCaptureEndpoint(alternateRole);
        if (alternate is not null &&
            string.Equals(alternate.ID, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return alternate;
        }

        alternate?.Dispose();
        return null;
    }

    private static MicrophoneCaptureSelection BuildPreferredSelection(
        MicrophoneCaptureProbeSnapshot? multimedia,
        MicrophoneCaptureProbeSnapshot? communications)
    {
        var preferred = SelectPreferredSnapshot(multimedia, communications);
        if (preferred is null)
        {
            return new MicrophoneCaptureSelection(
                Role.Communications,
                "microphone-fallback",
                "Default microphone",
                0d,
                false,
                "No explicit capture endpoint could be resolved. Using fallback microphone capture.",
                true);
        }

        var isSharedDevice = multimedia is not null &&
            communications is not null &&
            string.Equals(multimedia.DeviceId, communications.DeviceId, StringComparison.OrdinalIgnoreCase);
        var reason = isSharedDevice
            ? $"Using shared microphone endpoint '{preferred.FriendlyName}' for both multimedia and communications roles."
            : preferred.Role == Role.Communications
                ? $"Communications microphone '{preferred.FriendlyName}' is preferred."
                : $"Multimedia microphone '{preferred.FriendlyName}' is preferred.";

        return new MicrophoneCaptureSelection(
            preferred.Role,
            preferred.DeviceId,
            preferred.FriendlyName,
            preferred.EndpointPeakLevel,
            preferred.IsEndpointActive,
            reason,
            false);
    }

    private static MicrophoneCaptureProbeSnapshot? SelectPreferredSnapshot(
        MicrophoneCaptureProbeSnapshot? multimedia,
        MicrophoneCaptureProbeSnapshot? communications)
    {
        if (communications is null)
        {
            return multimedia;
        }

        if (multimedia is null)
        {
            return communications;
        }

        if (string.Equals(communications.DeviceId, multimedia.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return ShouldPreferSnapshot(communications, multimedia)
                ? communications
                : multimedia;
        }

        if (communications.IsEndpointActive != multimedia.IsEndpointActive)
        {
            return communications.IsEndpointActive ? communications : multimedia;
        }

        if (Math.Abs(communications.EndpointPeakLevel - multimedia.EndpointPeakLevel) > 0.0001d)
        {
            return communications.EndpointPeakLevel >= multimedia.EndpointPeakLevel
                ? communications
                : multimedia;
        }

        return communications;
    }

    private static bool ShouldPreferSnapshot(
        MicrophoneCaptureProbeSnapshot candidate,
        MicrophoneCaptureProbeSnapshot current)
    {
        if (candidate.IsEndpointActive != current.IsEndpointActive)
        {
            return candidate.IsEndpointActive;
        }

        if (Math.Abs(candidate.EndpointPeakLevel - current.EndpointPeakLevel) > 0.0001d)
        {
            return candidate.EndpointPeakLevel > current.EndpointPeakLevel;
        }

        return candidate.Role == Role.Communications;
    }

    private static MicrophoneCaptureProbeSnapshot? CreateProbeSnapshot(Role role, double threshold)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
            var peakLevel = device.AudioMeterInformation.MasterPeakValue;
            return new MicrophoneCaptureProbeSnapshot(
                role,
                device.ID,
                device.FriendlyName,
                peakLevel,
                peakLevel >= threshold);
        }
        catch
        {
            return null;
        }
    }

    private static MMDevice? ResolveCaptureEndpoint(Role role)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
        }
        catch
        {
            return null;
        }
    }

    private static IWaveIn CreateDefaultCapture()
    {
        return new WaveInEvent
        {
            BufferMilliseconds = 250,
            WaveFormat = new WaveFormat(16000, 16, 1),
        };
    }

    private sealed class OwnedWaveIn : IWaveIn
    {
        private readonly IWaveIn _inner;
        private readonly IDisposable _ownedResource;

        public OwnedWaveIn(IWaveIn inner, IDisposable ownedResource)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
        }

        public WaveFormat WaveFormat
        {
            get => _inner.WaveFormat;
            set => _inner.WaveFormat = value;
        }

        public event EventHandler<WaveInEventArgs>? DataAvailable
        {
            add => _inner.DataAvailable += value;
            remove => _inner.DataAvailable -= value;
        }

        public event EventHandler<StoppedEventArgs>? RecordingStopped
        {
            add => _inner.RecordingStopped += value;
            remove => _inner.RecordingStopped -= value;
        }

        public void StartRecording()
        {
            _inner.StartRecording();
        }

        public void StopRecording()
        {
            _inner.StopRecording();
        }

        public void Dispose()
        {
            _inner.Dispose();
            _ownedResource.Dispose();
        }
    }
}

internal sealed class LegacyMicrophoneCaptureFactory : IMicrophoneCaptureFactory
{
    private readonly Func<IWaveIn> _captureFactory;

    public LegacyMicrophoneCaptureFactory(Func<IWaveIn> captureFactory)
    {
        _captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
    }

    public MicrophoneCaptureEvaluation Evaluate(double activityThreshold)
    {
        return new MicrophoneCaptureEvaluation(
            new MicrophoneCaptureSelection(
                Role.Communications,
                "microphone-fallback",
                "Default microphone",
                0d,
                false,
                "Using legacy default microphone capture.",
                true),
            Multimedia: null,
            Communications: null);
    }

    public IWaveIn Create(MicrophoneCaptureSelection selection)
    {
        return _captureFactory();
    }
}
