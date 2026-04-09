using MeetingRecorder.Core.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingRecorder.App.Services;

internal sealed record LoopbackCaptureProbeSnapshot(
    Role Role,
    string DeviceId,
    string FriendlyName,
    double EndpointPeakLevel,
    bool IsEndpointActive,
    IReadOnlyList<AudioSourceSessionSnapshot> Sessions);

internal sealed record LoopbackCaptureSelection(
    Role Role,
    string DeviceId,
    string FriendlyName,
    double EndpointPeakLevel,
    bool IsEndpointActive,
    int MeetingSessionMatches,
    int RelevantActiveSessionCount,
    string Reason,
    bool IsFallbackCapture);

internal sealed record LoopbackCaptureEvaluation(
    LoopbackCaptureSelection PreferredSelection,
    LoopbackCaptureProbeSnapshot? Multimedia,
    LoopbackCaptureProbeSnapshot? Communications);

internal interface ILoopbackCaptureFactory
{
    LoopbackCaptureEvaluation Evaluate(MeetingPlatform platform, DetectedAudioSource? detectedAudioSource, double activityThreshold);

    IWaveIn Create(LoopbackCaptureSelection selection);
}

internal sealed class SystemLoopbackCaptureFactory : ILoopbackCaptureFactory
{
    private readonly Func<Role, double, LoopbackCaptureProbeSnapshot?> _probeSnapshot;
    private readonly Func<Role, MMDevice?> _resolveRenderEndpoint;
    private readonly Func<MMDevice, IWaveIn> _captureFactory;
    private readonly Action<string>? _log;

    public SystemLoopbackCaptureFactory(Action<string>? log = null)
        : this(
            CreateProbeSnapshot,
            ResolveRenderEndpoint,
            static device => new OwnedWaveIn(new WasapiLoopbackCapture(device), device),
            log)
    {
    }

    internal SystemLoopbackCaptureFactory(
        Func<Role, double, LoopbackCaptureProbeSnapshot?> probeSnapshot,
        Func<Role, MMDevice?> resolveRenderEndpoint,
        Func<MMDevice, IWaveIn> captureFactory,
        Action<string>? log = null)
    {
        _probeSnapshot = probeSnapshot ?? throw new ArgumentNullException(nameof(probeSnapshot));
        _resolveRenderEndpoint = resolveRenderEndpoint ?? throw new ArgumentNullException(nameof(resolveRenderEndpoint));
        _captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
        _log = log;
    }

    public LoopbackCaptureEvaluation Evaluate(MeetingPlatform platform, DetectedAudioSource? detectedAudioSource, double activityThreshold)
    {
        var normalizedThreshold = Math.Clamp(activityThreshold, 0.001d, 1d);
        var multimedia = _probeSnapshot(Role.Multimedia, normalizedThreshold);
        var communications = _probeSnapshot(Role.Communications, normalizedThreshold);
        var preferredRole = SelectPreferredEndpointRole(platform, detectedAudioSource, multimedia, communications);
        var preferredSelection = BuildPreferredSelection(preferredRole, multimedia, communications, platform, detectedAudioSource);
        return new LoopbackCaptureEvaluation(preferredSelection, multimedia, communications);
    }

    public IWaveIn Create(LoopbackCaptureSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsFallbackCapture)
        {
            _log?.Invoke(
                $"Loopback capture is using the Windows default loopback device. Reason: {selection.Reason}");
            return new WasapiLoopbackCapture();
        }

        var endpoint = _resolveRenderEndpoint(selection.Role);
        if (endpoint is null)
        {
            _log?.Invoke(
                $"Loopback capture could not resolve the preferred render endpoint '{selection.Role}'. Falling back to the default loopback device.");
            return new WasapiLoopbackCapture();
        }

        _log?.Invoke(
            $"Loopback capture selected {selection.Role} render endpoint '{endpoint.FriendlyName}'.");
        return _captureFactory(endpoint);
    }

    internal static Role SelectPreferredEndpointRole(
        MeetingPlatform platform,
        DetectedAudioSource? detectedAudioSource,
        LoopbackCaptureProbeSnapshot? multimedia,
        LoopbackCaptureProbeSnapshot? communications)
    {
        if (communications is null)
        {
            return Role.Multimedia;
        }

        if (multimedia is null)
        {
            return Role.Communications;
        }

        if (string.Equals(multimedia.DeviceId, communications.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return Role.Multimedia;
        }

        var multimediaScore = ScoreEndpoint(multimedia, platform, detectedAudioSource);
        var communicationsScore = ScoreEndpoint(communications, platform, detectedAudioSource);

        if (communicationsScore.MeetingSessionMatches != multimediaScore.MeetingSessionMatches)
        {
            return communicationsScore.MeetingSessionMatches > multimediaScore.MeetingSessionMatches
                ? Role.Communications
                : Role.Multimedia;
        }

        if (communicationsScore.RelevantActiveSessionCount != multimediaScore.RelevantActiveSessionCount)
        {
            return communicationsScore.RelevantActiveSessionCount > multimediaScore.RelevantActiveSessionCount
                ? Role.Communications
                : Role.Multimedia;
        }

        if (communicationsScore.EndpointActive != multimediaScore.EndpointActive)
        {
            return communicationsScore.EndpointActive ? Role.Communications : Role.Multimedia;
        }

        if (Math.Abs(communicationsScore.EndpointPeakLevel - multimediaScore.EndpointPeakLevel) > 0.0001d)
        {
            return communicationsScore.EndpointPeakLevel > multimediaScore.EndpointPeakLevel
                ? Role.Communications
                : Role.Multimedia;
        }

        if (platform is not MeetingPlatform.Manual and not MeetingPlatform.Unknown)
        {
            return Role.Communications;
        }

        return Role.Multimedia;
    }

    private static LoopbackEndpointScore ScoreEndpoint(
        LoopbackCaptureProbeSnapshot? snapshot,
        MeetingPlatform platform,
        DetectedAudioSource? detectedAudioSource)
    {
        if (snapshot is null)
        {
            return new LoopbackEndpointScore(0, 0, false, 0d);
        }

        var relevantActiveSessionCount = 0;
        var meetingSessionMatches = 0;
        foreach (var session in snapshot.Sessions)
        {
            if (!session.IsActive || session.IsCurrentProcess || session.IsSystemSounds)
            {
                continue;
            }

            relevantActiveSessionCount++;
            if (LooksLikeMeetingSession(platform, detectedAudioSource, session))
            {
                meetingSessionMatches++;
            }
        }

        return new LoopbackEndpointScore(
            meetingSessionMatches,
            relevantActiveSessionCount,
            snapshot.IsEndpointActive,
            snapshot.EndpointPeakLevel);
    }

    internal static LoopbackCaptureProbeSnapshot? GetSnapshotForSelection(
        LoopbackCaptureEvaluation evaluation,
        LoopbackCaptureSelection selection)
    {
        if (selection.IsFallbackCapture)
        {
            return null;
        }

        return selection.Role switch
        {
            Role.Multimedia => evaluation.Multimedia is { } multimedia &&
                               string.Equals(multimedia.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase)
                ? multimedia
                : null,
            Role.Communications => evaluation.Communications is { } communications &&
                                   string.Equals(communications.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase)
                ? communications
                : null,
            _ => null,
        };
    }

    private static bool LooksLikeMeetingSession(
        MeetingPlatform platform,
        DetectedAudioSource? detectedAudioSource,
        AudioSourceSessionSnapshot session)
    {
        var processName = (session.ProcessName ?? string.Empty).Trim();
        var displayName = session.DisplayName ?? string.Empty;
        var sessionIdentifier = session.SessionIdentifier ?? string.Empty;

        if (platform == MeetingPlatform.Teams ||
            string.Equals(detectedAudioSource?.AppName, "Microsoft Teams", StringComparison.OrdinalIgnoreCase))
        {
            return processName.Equals("ms-teams", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("teams", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("msteams", StringComparison.OrdinalIgnoreCase) ||
                displayName.Contains("teams", StringComparison.OrdinalIgnoreCase) ||
                sessionIdentifier.Contains("teams", StringComparison.OrdinalIgnoreCase);
        }

        if (platform == MeetingPlatform.GoogleMeet ||
            string.Equals(detectedAudioSource?.AppName, "Google Meet", StringComparison.OrdinalIgnoreCase))
        {
            return displayName.Contains("google meet", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("Meet -", StringComparison.OrdinalIgnoreCase) ||
                sessionIdentifier.Contains("meet.google.com", StringComparison.OrdinalIgnoreCase) ||
                sessionIdentifier.Contains("google meet", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static LoopbackCaptureProbeSnapshot? CreateProbeSnapshot(Role role, double activityThreshold)
    {
        MMDevice? device = null;
        try
        {
            device = ResolveRenderEndpoint(role);
            if (device is null)
            {
                return null;
            }

            var snapshot = AudioActivityProbeSupport.CaptureDefaultEndpoint(DataFlow.Render, role, activityThreshold);
            return new LoopbackCaptureProbeSnapshot(
                role,
                device.ID,
                device.FriendlyName,
                snapshot.EndpointPeakLevel,
                snapshot.IsEndpointActive,
                snapshot.Sessions);
        }
        catch
        {
            return null;
        }
        finally
        {
            device?.Dispose();
        }
    }

    private static MMDevice? ResolveRenderEndpoint(Role role)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
        }
        catch
        {
            return null;
        }
    }

    private static LoopbackCaptureSelection BuildPreferredSelection(
        Role preferredRole,
        LoopbackCaptureProbeSnapshot? multimedia,
        LoopbackCaptureProbeSnapshot? communications,
        MeetingPlatform platform,
        DetectedAudioSource? detectedAudioSource)
    {
        var preferredSnapshot = preferredRole == Role.Communications
            ? communications
            : multimedia;
        if (preferredSnapshot is null)
        {
            return new LoopbackCaptureSelection(
                preferredRole,
                string.Empty,
                "Windows default render device",
                0d,
                false,
                0,
                0,
                "Falling back to the Windows default loopback device because the preferred endpoint could not be resolved.",
                true);
        }

        var score = ScoreEndpoint(preferredSnapshot, platform, detectedAudioSource);
        var reason = preferredSnapshot.Role == Role.Communications
            ? "Communications render endpoint has the stronger meeting-audio signal."
            : "Preferred multimedia render endpoint remains the safest loopback target.";
        if (score.MeetingSessionMatches > 0)
        {
            reason = preferredSnapshot.Role == Role.Communications
                ? "Communications endpoint has supported meeting audio."
                : "Multimedia endpoint has supported meeting audio.";
        }

        return new LoopbackCaptureSelection(
            preferredSnapshot.Role,
            preferredSnapshot.DeviceId,
            preferredSnapshot.FriendlyName,
            preferredSnapshot.EndpointPeakLevel,
            preferredSnapshot.IsEndpointActive,
            score.MeetingSessionMatches,
            score.RelevantActiveSessionCount,
            reason,
            false);
    }

    private sealed record LoopbackEndpointScore(
        int MeetingSessionMatches,
        int RelevantActiveSessionCount,
        bool EndpointActive,
        double EndpointPeakLevel);

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
