using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

internal enum CleanupWorkState
{
    Pending = 0,
    Queued = 1,
    Processing = 2,
    Completed = 3,
    ManualReview = 4,
    Failed = 5,
    Retired = 6,
}

internal sealed record CleanupWorkLedgerEntry(
    string Fingerprint,
    CleanupWorkState State,
    DateTimeOffset UpdatedAtUtc,
    string? ManifestPath = null,
    string? Detail = null,
    MeetingCleanupAction? Action = null,
    IReadOnlyList<string>? AffectedStems = null,
    string? InputRevision = null,
    int AutomaticRetryCount = 0,
    bool RecoveryVerified = false,
    string? FailureInputRevision = null,
    bool PublishedRecoveryAttempted = false);

internal sealed record CleanupWorkLedgerDocument(IReadOnlyList<CleanupWorkLedgerEntry> Entries);

/// <summary>
/// Persists cleanup work separately from recommendation discovery so queue acceptance is not mistaken for completion.
/// </summary>
internal sealed class MeetingCleanupWorkLedgerService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, CleanupWorkLedgerEntry>? _entries;

    public MeetingCleanupWorkLedgerService(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataPaths.GetManagedAppRootForCurrentContext(), "cache", "meeting-cleanup-work-ledger-v1.json");
    }

    public IReadOnlyList<CleanupWorkLedgerEntry> GetEntries()
    {
        lock (_gate)
        {
            return Load().Values.OrderBy(entry => entry.Fingerprint, StringComparer.Ordinal).ToArray();
        }
    }

    public IReadOnlySet<string> GetQueuedManifestPaths()
    {
        lock (_gate)
        {
            return Load().Values
                .Where(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing)
                .Select(entry => entry.ManifestPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    public int RetireObsoleteSummaryWork()
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var (key, entry) in Load().ToArray())
            {
                if (entry.Action != MeetingCleanupAction.GenerateSummary || entry.State == CleanupWorkState.Retired) continue;
                _entries![key] = entry with { State = CleanupWorkState.Retired, UpdatedAtUtc = DateTimeOffset.UtcNow, Detail = null };
                count++;
            }
            if (count > 0) Save();
            return count;
        }
    }

    public bool ReleaseContainmentHold(string fingerprint, MeetingCleanupAction action)
    {
        lock (_gate)
        {
            if (!Load().TryGetValue(fingerprint, out var entry) || entry.State != CleanupWorkState.ManualReview ||
                entry.Action != action || entry.Detail is null ||
                !entry.Detail.StartsWith("Automatic archive, merge, and transcript regeneration are paused", StringComparison.Ordinal))
                return false;
            _entries![fingerprint] = entry with { State = CleanupWorkState.Pending, UpdatedAtUtc = DateTimeOffset.UtcNow, Detail = null };
            Save();
            return true;
        }
    }

    public bool IsEligibleForAutomaticApply(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        lock (_gate)
        {
            return !Load().TryGetValue(fingerprint.Trim(), out var entry) || entry.State == CleanupWorkState.Pending;
        }
    }

    public void Record(
        string fingerprint,
        CleanupWorkState state,
        string? manifestPath = null,
        string? detail = null,
        MeetingCleanupAction? action = null,
        IReadOnlyList<string>? affectedStems = null,
        string? inputRevision = null,
        bool? recoveryVerified = null)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            var key = fingerprint.Trim();
            var previous = Load().GetValueOrDefault(key);
            _entries![key] = new CleanupWorkLedgerEntry(
                key,
                state,
                DateTimeOffset.UtcNow,
                manifestPath ?? previous?.ManifestPath,
                string.IsNullOrWhiteSpace(detail) ? previous?.Detail : detail.Trim(),
                action ?? previous?.Action,
                affectedStems ?? previous?.AffectedStems,
                string.IsNullOrWhiteSpace(inputRevision) ? previous?.InputRevision : inputRevision.Trim(),
                previous?.AutomaticRetryCount ?? 0,
                recoveryVerified ?? previous?.RecoveryVerified ?? false,
                state == CleanupWorkState.Failed && previous?.State != CleanupWorkState.Failed
                    ? null : previous?.FailureInputRevision,
                previous?.PublishedRecoveryAttempted ?? false);
            Save();
        }
    }

    public void RecordContainmentHold(string fingerprint, MeetingCleanupAction action, string reason)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        lock (_gate)
        {
            var key = fingerprint.Trim();
            if (Load().TryGetValue(key, out var existing) &&
                existing.State == CleanupWorkState.ManualReview &&
                existing.Action == action &&
                string.Equals(existing.Detail, reason.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            _entries![key] = new CleanupWorkLedgerEntry(
                key,
                CleanupWorkState.ManualReview,
                DateTimeOffset.UtcNow,
                Action: action,
                Detail: reason.Trim());
            Save();
        }
    }

    public void RecordCompletionForManifest(string manifestPath, bool succeeded, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        lock (_gate)
        {
            var changed = false;
            foreach (var (fingerprint, entry) in Load().ToArray())
            {
                if (!string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal))
                {
                    continue;
                }

                _entries![fingerprint] = entry with
                {
                    State = succeeded ? CleanupWorkState.Completed : CleanupWorkState.Failed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Detail = string.IsNullOrWhiteSpace(detail) ? entry.Detail : detail.Trim(),
                };
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    public void RecordProcessingForManifest(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        lock (_gate)
        {
            var changed = false;
            foreach (var (fingerprint, entry) in Load().ToArray())
            {
                if (!string.Equals(entry.ManifestPath, manifestPath, StringComparison.Ordinal) ||
                    entry.State != CleanupWorkState.Queued)
                {
                    continue;
                }

                _entries![fingerprint] = entry with
                {
                    State = CleanupWorkState.Processing,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Repairs durable cleanup receipts after an interrupted app lifetime.
    /// Queue completion events are in-memory, so a published or failed manifest
    /// can otherwise leave its cleanup receipt permanently marked Processing.
    /// </summary>
    public async Task<int> ReconcileTerminalManifestStatesAsync(
        SessionManifestStore manifestStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestStore);

        CleanupWorkLedgerEntry[] entries;
        lock (_gate)
        {
            entries = Load().Values
                .Where(entry => entry.State is (CleanupWorkState.Queued or CleanupWorkState.Processing) ||
                    entry.State is (CleanupWorkState.Failed or CleanupWorkState.ManualReview) &&
                    entry.Action is (MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels or MeetingCleanupAction.RegenerateTranscript))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ManifestPath))
                .ToArray();
        }

        var reconciledCount = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var manifest = await manifestStore.LoadAsync(entry.ManifestPath!, cancellationToken);
                var outcome = ResolveTerminalOutcome(entry, manifest);
                if (outcome is null)
                {
                    continue;
                }

                if (entry.State is (CleanupWorkState.Failed or CleanupWorkState.ManualReview) && outcome.Value.State != CleanupWorkState.Completed)
                {
                    if (entry.State == CleanupWorkState.Failed && outcome.Value.State == CleanupWorkState.Failed)
                        UpdateFailureClassification(entry.Fingerprint, outcome.Value.Detail);
                    continue;
                }

                Record(entry.Fingerprint, outcome.Value.State, entry.ManifestPath, outcome.Value.Detail);
                reconciledCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Preserve the existing receipt when its manifest is not yet readable.
            }
        }

        return reconciledCount;
    }

    private void UpdateFailureClassification(string fingerprint, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return;
        lock (_gate)
        {
            if (!Load().TryGetValue(fingerprint, out var entry) || entry.State != CleanupWorkState.Failed ||
                string.Equals(entry.Detail, detail, StringComparison.Ordinal)) return;
            _entries![fingerprint] = entry with { Detail = detail };
            Save();
        }
    }

    /// <summary>
    /// Reconciles durable cleanup receipts with the queue's complete active set.
    /// A queue status exposes one display item, but several cleanup workers can
    /// process concurrently. Keep every active receipt truthful without
    /// guessing a result for unreadable or missing manifests.
    /// </summary>
    public async Task<int> ReconcileLiveManifestStatesAsync(
        SessionManifestStore manifestStore,
        IReadOnlySet<string>? activeManifestPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifestStore);
        var activePaths = activeManifestPaths ?? new HashSet<string>(StringComparer.Ordinal);
        CleanupWorkLedgerEntry[] entries;
        lock (_gate)
        {
            entries = Load().Values
                .Where(entry => entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing)
                .ToArray();
        }

        var reconciledCount = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.ManifestPath))
            {
                Record(
                    entry.Fingerprint,
                    CleanupWorkState.ManualReview,
                    detail: "Cleanup work has no durable manifest. Review source work before retrying.");
                reconciledCount++;
                continue;
            }

            var manifestPath = entry.ManifestPath;
            try
            {
                var manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken);
                var outcome = ResolveTerminalOutcome(entry, manifest);
                var targetState = activePaths.Contains(manifestPath)
                    ? CleanupWorkState.Processing
                    : outcome?.State ?? (manifest.State == SessionState.Queued ? CleanupWorkState.Queued : entry.State);
                if (targetState != entry.State)
                {
                    Record(entry.Fingerprint, targetState, manifestPath, outcome?.Detail);
                    reconciledCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                Record(
                    entry.Fingerprint,
                    CleanupWorkState.ManualReview,
                    manifestPath,
                    "Cleanup work manifest is unavailable. Review source work before retrying.");
                reconciledCount++;
            }
        }

        return reconciledCount;
    }

    public async Task ReconcileCompletionForManifestAsync(
        SessionManifestStore manifestStore, string manifestPath, bool workerSucceeded, string? detail,
        CancellationToken cancellationToken = default)
    {
        var entries = GetEntries().Where(entry =>
            entry.State is CleanupWorkState.Queued or CleanupWorkState.Processing &&
            string.Equals(entry.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length == 0) return;
        MeetingSessionManifest? manifest = null;
        try { manifest = await manifestStore.LoadAsync(manifestPath, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* Keep failure visible when the receipt cannot be verified. */ }
        foreach (var entry in entries)
        {
            var outcome = manifest is null ? null : ResolveTerminalOutcome(entry, manifest);
            if (outcome is { } resolved)
                Record(entry.Fingerprint, resolved.State, manifestPath, resolved.Detail);
            else if (!workerSucceeded)
                Record(entry.Fingerprint, CleanupWorkState.Failed, manifestPath, SafeFailureDetail(detail));
            else
                Record(entry.Fingerprint, CleanupWorkState.ManualReview, manifestPath,
                    "Work finished, but its requested result could not be verified.");
        }
    }

    private static (CleanupWorkState State, string? Detail)? ResolveTerminalOutcome(
        CleanupWorkLedgerEntry entry, MeetingSessionManifest manifest)
    {
        var stage = entry.Action switch
        {
            MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels => manifest.DiarizationStatus,
            MeetingCleanupAction.RegenerateTranscript => manifest.TranscriptionStatus,
            _ => null,
        };
        if (manifest.State == SessionState.Failed)
        {
            var cause = manifest.ErrorSummary ??
                (manifest.TranscriptionStatus.State == StageExecutionState.Failed ? manifest.TranscriptionStatus.Message : null) ??
                (manifest.PublishStatus.State == StageExecutionState.Failed ? manifest.PublishStatus.Message : null) ??
                stage?.Message;
            return (CleanupWorkState.Failed, SafeFailureDetail(cause));
        }
        if (manifest.State != SessionState.Published) return null;
        if (stage is null) return (CleanupWorkState.Completed, null);
        if (stage.State == StageExecutionState.Succeeded && manifest.PublishStatus.State == StageExecutionState.Succeeded)
            return (CleanupWorkState.Completed, null);
        if (stage.State is StageExecutionState.Failed or StageExecutionState.Skipped)
            return (CleanupWorkState.Failed, SafeFailureDetail(stage.Message));
        if (manifest.PublishStatus.State == StageExecutionState.Failed)
            return (CleanupWorkState.Failed, SafeFailureDetail(manifest.PublishStatus.Message));
        return null;
    }

    private static string SafeFailureDetail(string? detail) => IsTransientFailure(detail)
        ? "Temporary timeout or file access failure."
        : "The requested work did not complete. Review the meeting and its local setup.";

    public int ReleaseFailedSafeWork(IEnumerable<string> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var released = 0;
        lock (_gate)
        {
            foreach (var fingerprint in fingerprints.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            {
                if (!Load().TryGetValue(fingerprint.Trim(), out var entry) ||
                    entry.State != CleanupWorkState.Failed ||
                    !IsRetryableSafeWork(entry))
                {
                    continue;
                }

                _entries![fingerprint.Trim()] = entry with
                {
                    State = CleanupWorkState.Pending,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    AutomaticRetryCount = 0,
                    Detail = "Explicit retry requested after a prior safe-work failure.",
                };
                released++;
            }

            if (released > 0)
            {
                Save();
            }
        }

        return released;
    }

    public int ReleaseTransientFailures(DateTimeOffset nowUtc)
    {
        var delays = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };
        lock (_gate)
        {
            var released = 0;
            foreach (var (key, entry) in Load().ToArray())
            {
                if (entry.State != CleanupWorkState.Failed ||
                    !IsRetryableSafeWork(entry) ||
                    entry.AutomaticRetryCount < 0 || entry.AutomaticRetryCount >= delays.Length ||
                    nowUtc - entry.UpdatedAtUtc < delays[entry.AutomaticRetryCount] ||
                    !IsTransientFailure(entry.Detail))
                {
                    continue;
                }

                _entries![key] = entry with
                {
                    State = CleanupWorkState.Pending,
                    UpdatedAtUtc = nowUtc,
                    AutomaticRetryCount = entry.AutomaticRetryCount + 1,
                    Detail = "Automatic retry scheduled after a transient failure.",
                };
                released++;
            }

            if (released > 0) Save();
            return released;
        }
    }

    public bool ObserveFailureInputRevision(string fingerprint, string revision)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(revision)) return false;
        lock (_gate)
        {
            if (!Load().TryGetValue(fingerprint, out var entry) || entry.State != CleanupWorkState.Failed) return false;
            if (string.Equals(entry.FailureInputRevision, revision, StringComparison.Ordinal)) return false;
            var changed = entry.FailureInputRevision is not null && IsRetryableSafeWork(entry);
            _entries![fingerprint] = entry with
            {
                FailureInputRevision = revision,
                State = changed ? CleanupWorkState.Pending : entry.State,
                AutomaticRetryCount = changed ? 0 : entry.AutomaticRetryCount,
                UpdatedAtUtc = changed ? DateTimeOffset.UtcNow : entry.UpdatedAtUtc,
                Detail = changed ? "Meeting inputs changed. Work is ready for a fresh check." : entry.Detail,
            };
            Save();
            return changed;
        }
    }

    public bool ReleaseLegacyPublishedLabelFailure(string fingerprint)
    {
        lock (_gate)
        {
            if (!Load().TryGetValue(fingerprint, out var entry) || entry.State != CleanupWorkState.Failed ||
                entry.Action is not (MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels) ||
                entry.PublishedRecoveryAttempted || entry.RecoveryVerified)
                return false;
            _entries![fingerprint] = entry with
            {
                State = CleanupWorkState.Pending,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                AutomaticRetryCount = 0,
                PublishedRecoveryAttempted = true,
                Detail = "Published inputs are ready for a fresh speaker-labeling attempt.",
            };
            Save();
            return true;
        }
    }

    internal static bool IsTransientFailure(string? detail) => !string.IsNullOrWhiteSpace(detail) &&
        (detail.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("sharing violation", StringComparison.OrdinalIgnoreCase) ||
         detail.Contains("used by another process", StringComparison.OrdinalIgnoreCase));

    internal static bool IsRetryableSafeWork(CleanupWorkLedgerEntry entry) => entry.Action switch
    {
        MeetingCleanupAction.Archive or MeetingCleanupAction.Merge or MeetingCleanupAction.RegenerateTranscript => entry.RecoveryVerified,
        MeetingCleanupAction.GenerateSpeakerLabels or MeetingCleanupAction.RepairSpeakerLabels => true,
        _ => false,
    };

    public void MigrateLegacyEntries(IEnumerable<MeetingCleanupAutoApplyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_gate)
        {
            var changed = false;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Fingerprint) || Load().ContainsKey(entry.Fingerprint))
                {
                    continue;
                }

                var state = entry.LastFailureUtc.HasValue
                    ? CleanupWorkState.ManualReview
                    : CleanupWorkState.ManualReview;
                _entries![entry.Fingerprint] = new CleanupWorkLedgerEntry(
                    entry.Fingerprint,
                    state,
                    entry.LastFailureUtc ?? entry.LastQueuedSuccessUtc ?? DateTimeOffset.UtcNow,
                    Detail: entry.LastFailureUtc.HasValue ? entry.FailureMessage : "Legacy queued work needs reconciliation.");
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    private Dictionary<string, CleanupWorkLedgerEntry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            var document = File.Exists(_path)
                ? JsonSerializer.Deserialize<CleanupWorkLedgerDocument>(File.ReadAllText(_path), SerializerOptions)
                : null;
            _entries = (document?.Entries ?? Array.Empty<CleanupWorkLedgerEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Fingerprint))
                .ToDictionary(entry => entry.Fingerprint, StringComparer.Ordinal);
        }
        catch
        {
            _entries = new Dictionary<string, CleanupWorkLedgerEntry>(StringComparer.Ordinal);
        }

        return _entries;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Ledger path must include a parent directory.");
        Directory.CreateDirectory(directory);
        var tempPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new CleanupWorkLedgerDocument(_entries!.Values.OrderBy(entry => entry.Fingerprint, StringComparer.Ordinal).ToArray()), SerializerOptions));
        try
        {
            if (File.Exists(_path))
            {
                ReplaceWithRetry(tempPath);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void ReplaceWithRetry(string tempPath)
    {
        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(tempPath, _path, null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }
}
