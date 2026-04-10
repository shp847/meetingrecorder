using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.App.Services;

internal sealed record LoopbackCaptureRefreshResult(
    bool SwapPerformed,
    bool SwapFailed,
    string? StatusMessage);

internal sealed record MicrophoneCaptureRefreshResult(
    bool SwapPerformed,
    bool SwapFailed,
    string? StatusMessage);

internal sealed record LoopbackCaptureStatusSnapshot(
    bool IsRecording,
    bool IsSwapPending,
    bool IsFallbackActive,
    LoopbackCaptureSelection? ActiveSelection,
    LoopbackCaptureSelection? PreferredSelection,
    LoopbackCaptureSelection? PendingSelection,
    int PendingSelectionStableCount,
    IReadOnlyList<CaptureTimelineEntry> RecentTimeline,
    DateTimeOffset? LastSuccessfulSwapAtUtc);

internal sealed class RecordingSessionCoordinator
{
    private readonly LiveAppConfig _config;
    private readonly SessionManifestStore _manifestStore;
    private readonly ArtifactPathBuilder _pathBuilder;
    private readonly FileLogWriter _logger;
    private readonly ILoopbackCaptureFactory _loopbackCaptureFactory;
    private readonly IMicrophoneCaptureFactory _microphoneCaptureFactory;
    private LoopbackCaptureEvaluation? _lastLoopbackEvaluation;
    private MicrophoneCaptureEvaluation? _lastMicrophoneEvaluation;

    public RecordingSessionCoordinator(
        LiveAppConfig config,
        SessionManifestStore manifestStore,
        ArtifactPathBuilder pathBuilder,
        FileLogWriter logger,
        Func<IWaveIn>? legacyMicrophoneCaptureFactory = null,
        ILoopbackCaptureFactory? loopbackCaptureFactory = null,
        IMicrophoneCaptureFactory? microphoneCaptureFactory = null)
    {
        _config = config;
        _manifestStore = manifestStore;
        _pathBuilder = pathBuilder;
        _logger = logger;
        _loopbackCaptureFactory = loopbackCaptureFactory ?? new SystemLoopbackCaptureFactory(logger.Log);
        _microphoneCaptureFactory = microphoneCaptureFactory ??
            (legacyMicrophoneCaptureFactory is not null
                ? new LegacyMicrophoneCaptureFactory(legacyMicrophoneCaptureFactory)
                : new SystemMicrophoneCaptureFactory(logger.Log));
    }

    public ActiveRecordingSession? ActiveSession { get; private set; }

    public bool IsRecording => ActiveSession is not null;

    public async Task StartAsync(
        MeetingPlatform platform,
        string title,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        bool autoStarted,
        DetectedAudioSource? detectedAudioSource = null,
        CancellationToken cancellationToken = default)
    {
        if (ActiveSession is not null)
        {
            throw new InvalidOperationException("A recording session is already active.");
        }

        var currentConfig = _config.Current;
        var initialManifest = await _manifestStore.CreateAsync(currentConfig.WorkDir, platform, title, detectionEvidence, cancellationToken);
        if (detectedAudioSource is not null)
        {
            initialManifest = initialManifest with
            {
                DetectedAudioSource = detectedAudioSource,
            };
        }

        var sessionRoot = _pathBuilder.BuildSessionRoot(currentConfig.WorkDir, initialManifest.SessionId);
        var manifestPath = Path.Combine(sessionRoot, "manifest.json");
        var rawDir = Path.Combine(sessionRoot, "raw");

        var loopbackEvaluation = _loopbackCaptureFactory.Evaluate(
            platform,
            detectedAudioSource,
            _config.Current.AutoDetectAudioPeakThreshold);
        var loopbackSelection = loopbackEvaluation.PreferredSelection;
        var loopbackRecorder = CreateLoopbackRecorder(rawDir, segmentSequence: 1, loopbackSelection);

        var microphoneEvaluation = _microphoneCaptureFactory.Evaluate(_config.Current.AutoDetectAudioPeakThreshold);
        ChunkedWaveRecorder? microphoneRecorder = null;
        MicrophoneCaptureSelection? activeMicrophoneSelection = null;
        DateTimeOffset? activeMicrophoneSegmentStartedAtUtc = null;
        if (currentConfig.MicCaptureEnabled)
        {
            activeMicrophoneSelection = microphoneEvaluation.PreferredSelection;
            microphoneRecorder = CreateMicrophoneRecorder(rawDir, segmentSequence: 1, activeMicrophoneSelection);
            activeMicrophoneSegmentStartedAtUtc = initialManifest.StartedAtUtc;
        }

        try
        {
            loopbackRecorder.Start();
            microphoneRecorder?.Start();

            var captureTimeline = BuildInitialCaptureTimeline(loopbackSelection, initialManifest.StartedAtUtc);
            var manifest = initialManifest with
            {
                State = SessionState.Recording,
                LoopbackCaptureSegments = BuildLoopbackCaptureSegments(
                    completedSegments: [],
                    activeLoopbackRecorder: loopbackRecorder,
                    activeLoopbackSegmentStartedAtUtc: initialManifest.StartedAtUtc,
                    activeLoopbackSelection: loopbackSelection),
                CaptureTimeline = captureTimeline,
                MicrophoneCaptureSegments = BuildMicrophoneCaptureSegments(
                    completedSegments: [],
                    microphoneRecorder,
                    activeMicrophoneSegmentStartedAtUtc),
            };
            await _manifestStore.SaveAsync(manifest, manifestPath, cancellationToken);

            ActiveSession = new ActiveRecordingSession
            {
                Manifest = manifest,
                ManifestPath = manifestPath,
                LoopbackRecorder = loopbackRecorder,
                ActiveLoopbackSelection = loopbackSelection,
                ActiveLoopbackSegmentStartedAtUtc = initialManifest.StartedAtUtc,
                CompletedLoopbackCaptureSegments = [],
                CaptureTimelineEntries = captureTimeline.ToList(),
                NextLoopbackSegmentSequence = 2,
                MicrophoneRecorder = microphoneRecorder,
                ActiveMicrophoneSelection = activeMicrophoneSelection,
                ActiveMicrophoneSegmentStartedAtUtc = activeMicrophoneSegmentStartedAtUtc,
                NextMicrophoneSegmentSequence = microphoneRecorder is null ? 1 : 2,
                AutoStarted = autoStarted,
                MeetingLifecycleManaged = autoStarted,
            };

            _lastLoopbackEvaluation = loopbackEvaluation;
            _lastMicrophoneEvaluation = microphoneEvaluation;
            var evidence = detectionEvidence.Count == 0
                ? "none"
                : string.Join("; ", detectionEvidence.Select(signal => $"{signal.Source}='{signal.Value}' w={signal.Weight:0.00}"));
            _logger.Log(
                $"Recording started for session '{manifest.SessionId}' ({platform}). Title='{title}'. AutoStarted={autoStarted}. " +
                $"Loopback='{loopbackSelection.FriendlyName}' ({loopbackSelection.Role}). " +
                $"Microphone='{activeMicrophoneSelection?.FriendlyName ?? "disabled"}'. DetectionEvidence={evidence}.");
        }
        catch
        {
            loopbackRecorder.Dispose();
            microphoneRecorder?.Dispose();
            throw;
        }
    }

    public async Task<string?> StopAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (ActiveSession is null)
        {
            return null;
        }

        var active = ActiveSession;
        ActiveSession = null;

        active.LoopbackRecorder.Stop();
        var stoppedAtUtc = DateTimeOffset.UtcNow;
        var finalizedLoopbackCaptureSegments = FinalizeLoopbackCaptureSegments(active, stoppedAtUtc);
        var finalizedMicrophoneCaptureSegments = FinalizeMicrophoneCaptureSegments(active, stoppedAtUtc);
        var finalizedTimeline = FinalizeCaptureTimeline(active, stoppedAtUtc);

        var finalized = active.Manifest with
        {
            State = SessionState.Queued,
            EndedAtUtc = stoppedAtUtc,
            LoopbackCaptureSegments = finalizedLoopbackCaptureSegments,
            RawChunkPaths = finalizedLoopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            CaptureTimeline = finalizedTimeline,
            MicrophoneCaptureSegments = finalizedMicrophoneCaptureSegments,
            MicrophoneChunkPaths = finalizedMicrophoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
        };

        await _manifestStore.SaveAsync(finalized, active.ManifestPath, cancellationToken);
        var loopbackDiagnostics = DescribeChunkSet(finalized.RawChunkPaths, finalized.RawChunkPaths.Sum(GetAudioFileLength), "loopback");
        var microphoneDiagnostics = finalizedMicrophoneCaptureSegments.Count == 0
            ? "microphone=disabled"
            : DescribeChunkSet(finalized.MicrophoneChunkPaths, finalized.MicrophoneChunkPaths.Sum(GetAudioFileLength), "microphone");
        _logger.Log($"Recording stopped for session '{finalized.SessionId}'. Reason: {reason}. {loopbackDiagnostics}; {microphoneDiagnostics}");
        return active.ManifestPath;
    }

    public async Task<LoopbackCaptureRefreshResult> RefreshLoopbackCaptureAsync(
        MeetingPlatform platform,
        DetectedAudioSource? detectedAudioSource = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return new LoopbackCaptureRefreshResult(false, false, null);
        }

        var evaluation = _loopbackCaptureFactory.Evaluate(
            platform,
            detectedAudioSource ?? ActiveSession.Manifest.DetectedAudioSource,
            _config.Current.AutoDetectAudioPeakThreshold);
        _lastLoopbackEvaluation = evaluation;

        var activeSession = ActiveSession;
        var preferredSelection = evaluation.PreferredSelection;
        var requiresRecovery = activeSession.LoopbackRecorder.NeedsRecovery;
        if (LoopbackSelectionsEqual(activeSession.ActiveLoopbackSelection, preferredSelection) &&
            !requiresRecovery)
        {
            ClearPendingLoopbackSelection(activeSession);
            return new LoopbackCaptureRefreshResult(false, false, null);
        }

        if (requiresRecovery)
        {
            ClearPendingLoopbackSelection(activeSession);
            return await TrySwapLoopbackCaptureAsync(activeSession, preferredSelection, recoveryRequired: true, cancellationToken: cancellationToken);
        }

        var currentSnapshot = SystemLoopbackCaptureFactory.GetSnapshotForSelection(evaluation, activeSession.ActiveLoopbackSelection);
        var shouldSwapImmediately = ShouldSwapImmediately(activeSession.ActiveLoopbackSelection, preferredSelection, currentSnapshot);
        var requiredStableCount = shouldSwapImmediately ? 1 : 2;
        UpdatePendingLoopbackSelection(activeSession, preferredSelection);
        if (activeSession.PendingLoopbackSelectionStableCount < requiredStableCount)
        {
            return new LoopbackCaptureRefreshResult(
                false,
                false,
                $"Watching {preferredSelection.FriendlyName} ({preferredSelection.Role}); stability {activeSession.PendingLoopbackSelectionStableCount}/{requiredStableCount}.");
        }

        return await TrySwapLoopbackCaptureAsync(activeSession, preferredSelection, recoveryRequired: false, cancellationToken: cancellationToken);
    }

    public LoopbackCaptureStatusSnapshot GetLoopbackCaptureStatusSnapshot()
    {
        var activeSession = ActiveSession;
        var recentTimeline = activeSession?.CaptureTimelineEntries.TakeLast(3).ToArray() ?? Array.Empty<CaptureTimelineEntry>();
        var lastSuccessfulSwapAtUtc = activeSession?.CaptureTimelineEntries
            .LastOrDefault(entry => entry.Kind is CaptureTimelineEventKind.Swapped or CaptureTimelineEventKind.Fallback)
            ?.OccurredAtUtc;

        return new LoopbackCaptureStatusSnapshot(
            IsRecording: activeSession is not null,
            IsSwapPending: activeSession?.PendingLoopbackSelection is not null && activeSession.PendingLoopbackSelectionStableCount > 0,
            IsFallbackActive: activeSession?.ActiveLoopbackSelection.IsFallbackCapture == true,
            ActiveSelection: activeSession?.ActiveLoopbackSelection,
            PreferredSelection: _lastLoopbackEvaluation?.PreferredSelection,
            PendingSelection: activeSession?.PendingLoopbackSelection,
            PendingSelectionStableCount: activeSession?.PendingLoopbackSelectionStableCount ?? 0,
            RecentTimeline: recentTimeline,
            LastSuccessfulSwapAtUtc: lastSuccessfulSwapAtUtc);
    }

    public async Task<bool> SetMicrophoneCaptureEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        if (enabled)
        {
            if (ActiveSession.MicrophoneRecorder is not null)
            {
                return false;
            }

            var rawDir = Path.Combine(
                Path.GetDirectoryName(ActiveSession.ManifestPath)
                    ?? throw new InvalidOperationException("Active manifest path must include a session directory."),
                "raw");
            var microphoneEvaluation = _microphoneCaptureFactory.Evaluate(_config.Current.AutoDetectAudioPeakThreshold);
            var microphoneSelection = microphoneEvaluation.PreferredSelection;
            var microphoneRecorder = CreateMicrophoneRecorder(
                rawDir,
                ActiveSession.NextMicrophoneSegmentSequence,
                microphoneSelection);
            microphoneRecorder.Start();
            ActiveSession.MicrophoneRecorder = microphoneRecorder;
            ActiveSession.ActiveMicrophoneSelection = microphoneSelection;
            ActiveSession.ActiveMicrophoneSegmentStartedAtUtc = DateTimeOffset.UtcNow;
            ActiveSession.NextMicrophoneSegmentSequence++;
            _lastMicrophoneEvaluation = microphoneEvaluation;
            await PersistActiveRecordingStateAsync(ActiveSession, cancellationToken);
            _logger.Log(
                $"Microphone capture enabled for active session '{ActiveSession.Manifest.SessionId}' from now on using '{microphoneSelection.FriendlyName}' ({microphoneSelection.Role}).");
            return true;
        }

        if (ActiveSession.MicrophoneRecorder is null || !ActiveSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue)
        {
            return false;
        }

        var completedSegment = CompleteCurrentMicrophoneSegment(ActiveSession, DateTimeOffset.UtcNow);
        ActiveSession.CompletedMicrophoneCaptureSegments.Add(completedSegment);
        await PersistActiveRecordingStateAsync(ActiveSession, cancellationToken);
        _logger.Log($"Microphone capture disabled for active session '{ActiveSession.Manifest.SessionId}' from now on.");
        return true;
    }

    public async Task<MicrophoneCaptureRefreshResult> RefreshMicrophoneCaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession?.MicrophoneRecorder is null || !ActiveSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue)
        {
            return new MicrophoneCaptureRefreshResult(false, false, null);
        }

        var evaluation = _microphoneCaptureFactory.Evaluate(_config.Current.AutoDetectAudioPeakThreshold);
        _lastMicrophoneEvaluation = evaluation;

        var activeSession = ActiveSession;
        var preferredSelection = evaluation.PreferredSelection;
        var requiresRecovery = activeSession.MicrophoneRecorder.NeedsRecovery;
        if (MicrophoneSelectionsEqual(activeSession.ActiveMicrophoneSelection, preferredSelection) &&
            !requiresRecovery)
        {
            ClearPendingMicrophoneSelection(activeSession);
            return new MicrophoneCaptureRefreshResult(false, false, null);
        }

        if (requiresRecovery)
        {
            ClearPendingMicrophoneSelection(activeSession);
            return await TrySwapMicrophoneCaptureAsync(activeSession, preferredSelection, recoveryRequired: true, cancellationToken: cancellationToken);
        }

        var currentSnapshot = SystemMicrophoneCaptureFactory.GetSnapshotForSelection(evaluation, activeSession.ActiveMicrophoneSelection);
        var shouldSwapImmediately = ShouldSwapMicrophoneImmediately(activeSession.ActiveMicrophoneSelection, preferredSelection, currentSnapshot);
        var requiredStableCount = shouldSwapImmediately ? 1 : 2;
        UpdatePendingMicrophoneSelection(activeSession, preferredSelection);
        if (activeSession.PendingMicrophoneSelectionStableCount < requiredStableCount)
        {
            return new MicrophoneCaptureRefreshResult(false, false, null);
        }

        return await TrySwapMicrophoneCaptureAsync(activeSession, preferredSelection, recoveryRequired: false, cancellationToken: cancellationToken);
    }

    public async Task<bool> RenameActiveSessionAsync(string newTitle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        var trimmedTitle = newTitle.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(newTitle));
        }

        var updatedManifest = ActiveSession.Manifest with
        {
            DetectedTitle = trimmedTitle,
        };

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log($"Active session '{updatedManifest.SessionId}' renamed to '{trimmedTitle}'.");
        return true;
    }

    public async Task<bool> UpdateActiveSessionMetadataAsync(
        string title,
        string? projectName,
        IReadOnlyList<string> keyAttendees,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null)
        {
            return false;
        }

        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            throw new ArgumentException("A meeting title is required.", nameof(title));
        }

        var normalizedProjectName = string.IsNullOrWhiteSpace(projectName)
            ? null
            : projectName.Trim();
        var normalizedKeyAttendees = MeetingMetadataNameMatcher.MergeNames(keyAttendees, Array.Empty<string>());
        var updatedManifest = ActiveSession.Manifest with
        {
            DetectedTitle = trimmedTitle,
            ProjectName = normalizedProjectName,
            KeyAttendees = normalizedKeyAttendees,
        };
        if (updatedManifest == ActiveSession.Manifest)
        {
            return false;
        }

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Updated active session '{updatedManifest.SessionId}' metadata. " +
            $"Title='{trimmedTitle}', Project='{normalizedProjectName ?? string.Empty}', KeyAttendees={normalizedKeyAttendees.Count}.");
        return true;
    }

    public async Task<bool> ReclassifyActiveSessionAsync(
        MeetingPlatform platform,
        string detectedTitle,
        IReadOnlyList<DetectionSignal> detectionEvidence,
        DetectedAudioSource? detectedAudioSource = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null ||
            platform == MeetingPlatform.Unknown)
        {
            return false;
        }

        var trimmedDetectedTitle = detectedTitle.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDetectedTitle))
        {
            throw new ArgumentException("A detected meeting title is required.", nameof(detectedTitle));
        }

        var previousPlatform = ActiveSession.Manifest.Platform;
        var previousTitle = ActiveSession.Manifest.DetectedTitle;
        var updatedManifest = ActiveSession.Manifest with
        {
            Platform = platform,
            DetectedTitle = trimmedDetectedTitle,
            DetectionEvidence = detectionEvidence.ToArray(),
            DetectedAudioSource = detectedAudioSource ?? ActiveSession.Manifest.DetectedAudioSource,
        };
        if (updatedManifest == ActiveSession.Manifest)
        {
            return false;
        }

        await _manifestStore.SaveAsync(updatedManifest, ActiveSession.ManifestPath, cancellationToken);
        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Active session '{updatedManifest.SessionId}' reclassified from '{previousPlatform}'/'{previousTitle}' to '{platform}'/'{trimmedDetectedTitle}'.");
        return true;
    }

    public async Task<bool> MergeActiveSessionAttendeesAsync(
        string expectedSessionId,
        IReadOnlyList<MeetingAttendee> attendees,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ActiveSession is null ||
            !string.Equals(ActiveSession.Manifest.SessionId, expectedSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (attendees.Count == 0)
        {
            return false;
        }

        var updatedManifest = await TeamsLiveAttendeeCaptureService.MergeAttendeesIntoManifestAsync(
            _manifestStore,
            ActiveSession.Manifest,
            ActiveSession.ManifestPath,
            attendees,
            cancellationToken);

        if (ReferenceEquals(updatedManifest, ActiveSession.Manifest))
        {
            return false;
        }

        ActiveSession.Manifest = updatedManifest;
        _logger.Log(
            $"Updated active session '{updatedManifest.SessionId}' with {updatedManifest.Attendees.Count} persisted attendee(s) from live Teams capture.");
        return true;
    }

    private async Task<LoopbackCaptureRefreshResult> TrySwapLoopbackCaptureAsync(
        ActiveRecordingSession activeSession,
        LoopbackCaptureSelection preferredSelection,
        bool recoveryRequired,
        CancellationToken cancellationToken)
    {
        var rawDir = Path.Combine(
            Path.GetDirectoryName(activeSession.ManifestPath)
                ?? throw new InvalidOperationException("Active manifest path must include a session directory."),
            "raw");

        var previousRecorder = activeSession.LoopbackRecorder;
        var previousSelection = activeSession.ActiveLoopbackSelection;
        var previousStartedAtUtc = activeSession.ActiveLoopbackSegmentStartedAtUtc;
        var previousUnexpectedStop = previousRecorder.UnexpectedStop;
        ChunkedWaveRecorder? nextRecorder = null;
        try
        {
            _loopbackCaptureFactory.Validate(preferredSelection);
            nextRecorder = CreateLoopbackRecorder(rawDir, activeSession.NextLoopbackSegmentSequence, preferredSelection);

            previousRecorder.Stop();
            var previousEndedAtUtc = GetRecorderEndedAtUtc(previousRecorder, DateTimeOffset.UtcNow);
            activeSession.CompletedLoopbackCaptureSegments.Add(
                BuildLoopbackCaptureSegment(
                    previousRecorder,
                    previousSelection,
                    previousStartedAtUtc,
                    previousEndedAtUtc));

            nextRecorder.Start();
            var swappedAtUtc = DateTimeOffset.UtcNow;

            activeSession.LoopbackRecorder = nextRecorder;
            activeSession.ActiveLoopbackSelection = preferredSelection;
            activeSession.ActiveLoopbackSegmentStartedAtUtc = swappedAtUtc;
            activeSession.NextLoopbackSegmentSequence++;
            activeSession.CaptureTimelineEntries.Add(
                new CaptureTimelineEntry(
                    swappedAtUtc,
                    preferredSelection.IsFallbackCapture ? CaptureTimelineEventKind.Fallback : CaptureTimelineEventKind.Swapped,
                    recoveryRequired
                        ? $"Recovered loopback capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                        : $"Swapped to {preferredSelection.FriendlyName} ({preferredSelection.Role}).",
                    recoveryRequired
                        ? $"Previous endpoint: {previousSelection.FriendlyName} ({previousSelection.Role}) stopped unexpectedly at {previousEndedAtUtc:O}. {previousUnexpectedStop?.Message ?? preferredSelection.Reason}"
                        : $"Previous endpoint: {previousSelection.FriendlyName} ({previousSelection.Role}). Reason: {preferredSelection.Reason}"));
            ClearPendingLoopbackSelection(activeSession);
            await PersistActiveRecordingStateAsync(activeSession, cancellationToken);

            _logger.Log(
                recoveryRequired
                    ? $"Loopback capture recovered for session '{activeSession.Manifest.SessionId}' on '{preferredSelection.FriendlyName}'."
                    : $"Loopback capture swapped for session '{activeSession.Manifest.SessionId}' from '{previousSelection.FriendlyName}' to '{preferredSelection.FriendlyName}'.");
            return new LoopbackCaptureRefreshResult(
                true,
                false,
                recoveryRequired
                    ? $"Recovered loopback capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                    : $"Swapped to {preferredSelection.FriendlyName} ({preferredSelection.Role}).");
        }
        catch (Exception exception)
        {
            nextRecorder?.Dispose();
            activeSession.CaptureTimelineEntries.Add(
                new CaptureTimelineEntry(
                    DateTimeOffset.UtcNow,
                    CaptureTimelineEventKind.SwapFailed,
                    recoveryRequired
                        ? $"Failed to recover loopback capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                        : $"Failed to swap loopback capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).",
                    exception.Message));
            ClearPendingLoopbackSelection(activeSession);
            await PersistActiveRecordingStateAsync(activeSession, cancellationToken);
            _logger.Log(
                recoveryRequired
                    ? $"Loopback capture recovery failed for session '{activeSession.Manifest.SessionId}': {exception.Message}"
                    : $"Loopback capture swap failed for session '{activeSession.Manifest.SessionId}': {exception.Message}");
            return new LoopbackCaptureRefreshResult(
                false,
                true,
                recoveryRequired
                    ? $"Failed to recover loopback capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                    : $"Failed to swap loopback capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).");
        }
    }

    private async Task<MicrophoneCaptureRefreshResult> TrySwapMicrophoneCaptureAsync(
        ActiveRecordingSession activeSession,
        MicrophoneCaptureSelection preferredSelection,
        bool recoveryRequired,
        CancellationToken cancellationToken)
    {
        var rawDir = Path.Combine(
            Path.GetDirectoryName(activeSession.ManifestPath)
                ?? throw new InvalidOperationException("Active manifest path must include a session directory."),
            "raw");

        var previousRecorder = activeSession.MicrophoneRecorder
            ?? throw new InvalidOperationException("An active microphone recorder is required.");
        var previousSelection = activeSession.ActiveMicrophoneSelection
            ?? throw new InvalidOperationException("An active microphone selection is required.");
        var previousStartedAtUtc = activeSession.ActiveMicrophoneSegmentStartedAtUtc
            ?? throw new InvalidOperationException("An active microphone segment start time is required.");
        var previousUnexpectedStop = previousRecorder.UnexpectedStop;
        ChunkedWaveRecorder? nextRecorder = null;
        try
        {
            _microphoneCaptureFactory.Validate(preferredSelection);
            nextRecorder = CreateMicrophoneRecorder(rawDir, activeSession.NextMicrophoneSegmentSequence, preferredSelection);

            previousRecorder.Stop();
            var previousEndedAtUtc = GetRecorderEndedAtUtc(previousRecorder, DateTimeOffset.UtcNow);
            activeSession.CompletedMicrophoneCaptureSegments.Add(
                new MicrophoneCaptureSegment(
                    previousStartedAtUtc,
                    previousEndedAtUtc,
                    previousRecorder.ChunkPaths.ToArray()));

            nextRecorder.Start();
            var swappedAtUtc = DateTimeOffset.UtcNow;

            activeSession.MicrophoneRecorder = nextRecorder;
            activeSession.ActiveMicrophoneSelection = preferredSelection;
            activeSession.ActiveMicrophoneSegmentStartedAtUtc = swappedAtUtc;
            activeSession.NextMicrophoneSegmentSequence++;
            activeSession.CaptureTimelineEntries.Add(
                new CaptureTimelineEntry(
                    swappedAtUtc,
                    preferredSelection.IsFallbackCapture ? CaptureTimelineEventKind.Fallback : CaptureTimelineEventKind.Swapped,
                    recoveryRequired
                        ? $"Recovered microphone capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                        : $"Swapped microphone capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).",
                    recoveryRequired
                        ? $"Previous microphone: {previousSelection.FriendlyName} ({previousSelection.Role}) stopped unexpectedly at {previousEndedAtUtc:O}. {previousUnexpectedStop?.Message ?? preferredSelection.Reason}"
                        : $"Previous microphone: {previousSelection.FriendlyName} ({previousSelection.Role}). Reason: {preferredSelection.Reason}"));
            ClearPendingMicrophoneSelection(activeSession);
            await PersistActiveRecordingStateAsync(activeSession, cancellationToken);

            _logger.Log(
                recoveryRequired
                    ? $"Microphone capture recovered for session '{activeSession.Manifest.SessionId}' on '{preferredSelection.FriendlyName}'."
                    : $"Microphone capture swapped for session '{activeSession.Manifest.SessionId}' from '{previousSelection.FriendlyName}' to '{preferredSelection.FriendlyName}'.");
            return new MicrophoneCaptureRefreshResult(
                true,
                false,
                recoveryRequired
                    ? $"Recovered microphone capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                    : $"Swapped microphone capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).");
        }
        catch (Exception exception)
        {
            nextRecorder?.Dispose();
            activeSession.CaptureTimelineEntries.Add(
                new CaptureTimelineEntry(
                    DateTimeOffset.UtcNow,
                    CaptureTimelineEventKind.SwapFailed,
                    recoveryRequired
                        ? $"Failed to recover microphone capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                        : $"Failed to swap microphone capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).",
                    exception.Message));
            ClearPendingMicrophoneSelection(activeSession);
            await PersistActiveRecordingStateAsync(activeSession, cancellationToken);
            _logger.Log(
                recoveryRequired
                    ? $"Microphone capture recovery failed for session '{activeSession.Manifest.SessionId}': {exception.Message}"
                    : $"Microphone capture swap failed for session '{activeSession.Manifest.SessionId}': {exception.Message}");
            return new MicrophoneCaptureRefreshResult(
                false,
                true,
                recoveryRequired
                    ? $"Failed to recover microphone capture on {preferredSelection.FriendlyName} ({preferredSelection.Role})."
                    : $"Failed to swap microphone capture to {preferredSelection.FriendlyName} ({preferredSelection.Role}).");
        }
    }

    private async Task PersistActiveRecordingStateAsync(
        ActiveRecordingSession activeSession,
        CancellationToken cancellationToken)
    {
        var loopbackCaptureSegments = BuildLoopbackCaptureSegments(
            activeSession.CompletedLoopbackCaptureSegments,
            activeSession.LoopbackRecorder,
            activeSession.ActiveLoopbackSegmentStartedAtUtc,
            activeSession.ActiveLoopbackSelection);
        var microphoneCaptureSegments = BuildMicrophoneCaptureSegments(
            activeSession.CompletedMicrophoneCaptureSegments,
            activeSession.MicrophoneRecorder,
            activeSession.ActiveMicrophoneSegmentStartedAtUtc);
        var updatedManifest = activeSession.Manifest with
        {
            LoopbackCaptureSegments = loopbackCaptureSegments,
            RawChunkPaths = loopbackCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
            CaptureTimeline = activeSession.CaptureTimelineEntries.ToArray(),
            MicrophoneCaptureSegments = microphoneCaptureSegments,
            MicrophoneChunkPaths = microphoneCaptureSegments.SelectMany(segment => segment.ChunkPaths).ToArray(),
        };
        await _manifestStore.SaveAsync(updatedManifest, activeSession.ManifestPath, cancellationToken);
        activeSession.Manifest = updatedManifest;
    }

    private ChunkedWaveRecorder CreateLoopbackRecorder(string rawDir, int segmentSequence, LoopbackCaptureSelection selection)
    {
        return new ChunkedWaveRecorder(
            () => _loopbackCaptureFactory.Create(selection),
            rawDir,
            $"loopback-{segmentSequence:0000}",
            TimeSpan.FromSeconds(30),
            _logger);
    }

    private ChunkedWaveRecorder CreateMicrophoneRecorder(string rawDir, int segmentSequence, MicrophoneCaptureSelection selection)
    {
        return new ChunkedWaveRecorder(
            () => _microphoneCaptureFactory.Create(selection),
            rawDir,
            $"microphone-{segmentSequence:0000}",
            TimeSpan.FromSeconds(30),
            _logger);
    }

    private static IReadOnlyList<CaptureTimelineEntry> BuildInitialCaptureTimeline(
        LoopbackCaptureSelection selection,
        DateTimeOffset startedAtUtc)
    {
        return
        [
            new CaptureTimelineEntry(
                startedAtUtc,
                CaptureTimelineEventKind.Started,
                $"Started on {selection.FriendlyName} ({selection.Role}).",
                selection.Reason),
        ];
    }

    private static IReadOnlyList<CaptureTimelineEntry> FinalizeCaptureTimeline(
        ActiveRecordingSession activeSession,
        DateTimeOffset stoppedAtUtc)
    {
        var timeline = activeSession.CaptureTimelineEntries.ToList();
        timeline.Add(
            new CaptureTimelineEntry(
                stoppedAtUtc,
                CaptureTimelineEventKind.Stopped,
                $"Stopped on {activeSession.ActiveLoopbackSelection.FriendlyName} ({activeSession.ActiveLoopbackSelection.Role}).",
                "Recording stopped."));
        return timeline.ToArray();
    }

    private static IReadOnlyList<LoopbackCaptureSegment> FinalizeLoopbackCaptureSegments(
        ActiveRecordingSession activeSession,
        DateTimeOffset endedAtUtc)
    {
        var segments = activeSession.CompletedLoopbackCaptureSegments.ToList();
        segments.Add(
            BuildLoopbackCaptureSegment(
                activeSession.LoopbackRecorder,
                activeSession.ActiveLoopbackSelection,
                activeSession.ActiveLoopbackSegmentStartedAtUtc,
                GetRecorderEndedAtUtc(activeSession.LoopbackRecorder, endedAtUtc)));
        return segments.ToArray();
    }

    private static IReadOnlyList<LoopbackCaptureSegment> BuildLoopbackCaptureSegments(
        IReadOnlyList<LoopbackCaptureSegment> completedSegments,
        ChunkedWaveRecorder activeLoopbackRecorder,
        DateTimeOffset activeLoopbackSegmentStartedAtUtc,
        LoopbackCaptureSelection activeLoopbackSelection)
    {
        var segments = completedSegments.ToList();
        segments.Add(
            BuildLoopbackCaptureSegment(
                activeLoopbackRecorder,
                activeLoopbackSelection,
                activeLoopbackSegmentStartedAtUtc,
                endedAtUtc: null));
        return segments;
    }

    private static LoopbackCaptureSegment BuildLoopbackCaptureSegment(
        ChunkedWaveRecorder recorder,
        LoopbackCaptureSelection selection,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc)
    {
        return new LoopbackCaptureSegment(
            startedAtUtc,
            endedAtUtc,
            recorder.ChunkPaths.ToArray(),
            selection.DeviceId,
            selection.FriendlyName,
            selection.Role.ToString());
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> FinalizeMicrophoneCaptureSegments(
        ActiveRecordingSession activeSession,
        DateTimeOffset endedAtUtc)
    {
        if (activeSession.MicrophoneRecorder is not null && activeSession.ActiveMicrophoneSegmentStartedAtUtc.HasValue)
        {
            var completedSegment = CompleteCurrentMicrophoneSegment(activeSession, endedAtUtc);
            activeSession.CompletedMicrophoneCaptureSegments.Add(completedSegment);
        }

        return activeSession.CompletedMicrophoneCaptureSegments.ToArray();
    }

    private static MicrophoneCaptureSegment CompleteCurrentMicrophoneSegment(
        ActiveRecordingSession activeSession,
        DateTimeOffset endedAtUtc)
    {
        var microphoneRecorder = activeSession.MicrophoneRecorder
            ?? throw new InvalidOperationException("An active microphone recorder is required.");
        var startedAtUtc = activeSession.ActiveMicrophoneSegmentStartedAtUtc
            ?? throw new InvalidOperationException("An active microphone segment start time is required.");
        microphoneRecorder.Stop();
        var resolvedEndedAtUtc = GetRecorderEndedAtUtc(microphoneRecorder, endedAtUtc);
        activeSession.MicrophoneRecorder = null;
        activeSession.ActiveMicrophoneSelection = null;
        activeSession.ActiveMicrophoneSegmentStartedAtUtc = null;
        return new MicrophoneCaptureSegment(
            startedAtUtc,
            resolvedEndedAtUtc,
            microphoneRecorder.ChunkPaths.ToArray());
    }

    private static IReadOnlyList<MicrophoneCaptureSegment> BuildMicrophoneCaptureSegments(
        IReadOnlyList<MicrophoneCaptureSegment> completedSegments,
        ChunkedWaveRecorder? activeMicrophoneRecorder,
        DateTimeOffset? activeMicrophoneSegmentStartedAtUtc)
    {
        var segments = completedSegments.ToList();
        if (activeMicrophoneRecorder is not null && activeMicrophoneSegmentStartedAtUtc.HasValue)
        {
            segments.Add(new MicrophoneCaptureSegment(
                activeMicrophoneSegmentStartedAtUtc.Value,
                null,
                activeMicrophoneRecorder.ChunkPaths.ToArray()));
        }

        return segments;
    }

    private static bool LoopbackSelectionsEqual(LoopbackCaptureSelection left, LoopbackCaptureSelection right)
    {
        return left.Role == right.Role &&
               left.IsFallbackCapture == right.IsFallbackCapture &&
               string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MicrophoneSelectionsEqual(MicrophoneCaptureSelection? left, MicrophoneCaptureSelection right)
    {
        return left is not null &&
               left.Role == right.Role &&
               left.IsFallbackCapture == right.IsFallbackCapture &&
               string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset GetRecorderEndedAtUtc(ChunkedWaveRecorder recorder, DateTimeOffset fallbackEndedAtUtc)
    {
        return recorder.UnexpectedStop?.OccurredAtUtc ?? fallbackEndedAtUtc;
    }

    private static bool ShouldSwapImmediately(
        LoopbackCaptureSelection currentSelection,
        LoopbackCaptureSelection preferredSelection,
        LoopbackCaptureProbeSnapshot? currentSnapshot)
    {
        if (preferredSelection.MeetingSessionMatches <= 0)
        {
            return false;
        }

        if (currentSelection.IsFallbackCapture)
        {
            return true;
        }

        return currentSnapshot is { IsEndpointActive: false };
    }

    private static bool ShouldSwapMicrophoneImmediately(
        MicrophoneCaptureSelection? currentSelection,
        MicrophoneCaptureSelection preferredSelection,
        MicrophoneCaptureProbeSnapshot? currentSnapshot)
    {
        if (currentSelection is null)
        {
            return false;
        }

        if (currentSelection.IsFallbackCapture && !preferredSelection.IsFallbackCapture)
        {
            return true;
        }

        if (preferredSelection.IsFallbackCapture)
        {
            return currentSnapshot is { IsEndpointActive: false };
        }

        return currentSnapshot is { IsEndpointActive: false } && preferredSelection.IsEndpointActive;
    }

    private static void UpdatePendingLoopbackSelection(ActiveRecordingSession activeSession, LoopbackCaptureSelection preferredSelection)
    {
        if (activeSession.PendingLoopbackSelection is not null &&
            LoopbackSelectionsEqual(activeSession.PendingLoopbackSelection, preferredSelection))
        {
            activeSession.PendingLoopbackSelectionStableCount++;
            return;
        }

        activeSession.PendingLoopbackSelection = preferredSelection;
        activeSession.PendingLoopbackSelectionStableCount = 1;
    }

    private static void ClearPendingLoopbackSelection(ActiveRecordingSession activeSession)
    {
        activeSession.PendingLoopbackSelection = null;
        activeSession.PendingLoopbackSelectionStableCount = 0;
    }

    private static void UpdatePendingMicrophoneSelection(ActiveRecordingSession activeSession, MicrophoneCaptureSelection preferredSelection)
    {
        if (activeSession.PendingMicrophoneSelection is not null &&
            MicrophoneSelectionsEqual(activeSession.PendingMicrophoneSelection, preferredSelection))
        {
            activeSession.PendingMicrophoneSelectionStableCount++;
            return;
        }

        activeSession.PendingMicrophoneSelection = preferredSelection;
        activeSession.PendingMicrophoneSelectionStableCount = 1;
    }

    private static void ClearPendingMicrophoneSelection(ActiveRecordingSession activeSession)
    {
        activeSession.PendingMicrophoneSelection = null;
        activeSession.PendingMicrophoneSelectionStableCount = 0;
    }

    private static long GetAudioFileLength(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0L;
    }

    private static string DescribeChunkSet(IReadOnlyList<string> chunkPaths, long audioBytesRecorded, string label)
    {
        var fileSizes = chunkPaths
            .Select(path => $"{Path.GetFileName(path)}:{(File.Exists(path) ? new FileInfo(path).Length : 0)}")
            .ToArray();
        var joinedSizes = fileSizes.Length == 0 ? "none" : string.Join(", ", fileSizes);
        return $"{label}ChunkCount={chunkPaths.Count}; {label}AudioBytes={audioBytesRecorded}; {label}Files=[{joinedSizes}]";
    }
}
