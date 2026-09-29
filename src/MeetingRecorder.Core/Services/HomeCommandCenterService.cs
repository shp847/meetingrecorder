namespace MeetingRecorder.Core.Services;

public enum HomeCommandCenterTarget
{
    None = 0,
    SettingsSetup = 1,
    SettingsRecording = 2,
    SettingsFilesAndUpdates = 3,
    SettingsUpdates = 4,
    SettingsSummaries = 5,
    SettingsAdvanced = 6,
    Meetings = 7,
}

public enum HomeCommandCenterSeverity
{
    Neutral = 0,
    Information = 1,
    Warning = 2,
    Danger = 3,
}

public enum HomeCaptureTruth
{
    StaticReadiness = 0,
    LiveOutput = 1,
    FallbackOutput = 2,
    Degraded = 3,
    Unavailable = 4,
}

public enum HomeStateFreshness
{
    Current = 0,
    Stale = 1,
    Unavailable = 2,
}

public sealed record HomeReadinessState(
    RecordingReadinessSnapshot Snapshot,
    DateTimeOffset ObservedAtUtc);

public sealed record HomeRecordingState(
    bool IsRecording,
    DateTimeOffset ObservedAtUtc);

public sealed record HomeCaptureState(
    HomeCaptureTruth Truth,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeQueueState(
    int ActionableCount,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeUpdateState(
    bool IsActionable,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeProviderState(
    bool NeedsAttention,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeRecoveryState(
    bool IsRequested,
    HomeCommandCenterTarget Target,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeCurrentTaskState(
    bool RequiresSetup,
    HomeCommandCenterTarget Target,
    DateTimeOffset? ObservedAtUtc);

public sealed record HomeCommandCenterInput(
    HomeRecordingState Recording,
    HomeReadinessState Readiness,
    HomeCaptureState Capture,
    HomeRecoveryState Recovery,
    HomeCurrentTaskState CurrentTask,
    HomeQueueState Queue,
    HomeUpdateState Update,
    HomeProviderState Provider,
    DateTimeOffset NowUtc);

/// <summary>
/// Single, read-only Home recommendation. Targets only name an owning surface;
/// the presentation layer decides how to navigate there and never executes work.
/// </summary>
public sealed record HomeCommandCenterState(
    string Headline,
    string Reason,
    string? ActionLabel,
    HomeCommandCenterTarget Target,
    HomeCommandCenterSeverity Severity,
    HomeStateFreshness Freshness,
    HomeCaptureTruth CaptureTruth,
    IReadOnlyList<string> SuppressedCandidateReasons);

/// <summary>
/// Pure priority policy for the Home and shell status surfaces. It does not read
/// config, inspect hardware, start capture, download anything, or execute a
/// recovery action. Callers must provide timestamped, already-known state.
/// </summary>
public sealed class NextBestActionResolver
{
    private static readonly TimeSpan MaximumCurrentAge = TimeSpan.FromMinutes(2);

    public HomeCommandCenterState Resolve(HomeCommandCenterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candidates = BuildCandidates(input).ToArray();
        var selected = candidates[0];
        return new HomeCommandCenterState(
            selected.Headline,
            selected.Reason,
            selected.ActionLabel,
            selected.Target,
            selected.Severity,
            selected.Freshness,
            input.Capture.Truth,
            candidates.Skip(1).Select(candidate => candidate.Reason).ToArray());
    }

    private static IEnumerable<Candidate> BuildCandidates(HomeCommandCenterInput input)
    {
        if (input.Recording.IsRecording)
        {
            var captureFreshness = GetFreshness(input.Capture.ObservedAtUtc, input.NowUtc);
            var captureTruth = captureFreshness == HomeStateFreshness.Stale
                ? HomeCaptureTruth.Degraded
                : input.Capture.Truth;
            if (captureTruth is HomeCaptureTruth.Degraded or HomeCaptureTruth.Unavailable)
            {
                yield return new Candidate(
                    "Capture needs attention",
                    captureTruth == HomeCaptureTruth.Unavailable
                        ? "Recording is active, but no usable capture source is reported."
                        : "Recording is active, but capture state needs review.",
                    "Review recording",
                    HomeCommandCenterTarget.SettingsRecording,
                    HomeCommandCenterSeverity.Danger,
                    captureFreshness);
            }
            else
            {
                yield return new Candidate(
                    "Recording in progress",
                    captureTruth == HomeCaptureTruth.FallbackOutput
                        ? "Fallback capture output is active."
                        : "Live capture output is active.",
                    null,
                    HomeCommandCenterTarget.None,
                    captureTruth == HomeCaptureTruth.FallbackOutput
                        ? HomeCommandCenterSeverity.Warning
                        : HomeCommandCenterSeverity.Information,
                    captureFreshness);
            }
        }

        if (input.Readiness.Snapshot.PrimaryBlocker is { } blocker)
        {
            yield return new Candidate(
                "Recording setup needs attention",
                blocker.Summary,
                GetReadinessActionLabel(blocker.RemediationTarget),
                MapReadinessTarget(blocker.RemediationTarget),
                HomeCommandCenterSeverity.Warning,
                GetFreshness(input.Readiness.ObservedAtUtc, input.NowUtc));
        }

        if (input.Recovery.IsRequested)
        {
            yield return new Candidate(
                "Meeting recovery needs attention",
                "A selected meeting needs your review before its next step.",
                "Open meeting",
                input.Recovery.Target,
                HomeCommandCenterSeverity.Warning,
                GetFreshness(input.Recovery.ObservedAtUtc, input.NowUtc));
        }

        if (input.CurrentTask.RequiresSetup)
        {
            yield return new Candidate(
                "Finish setup for current task",
                "The selected task needs setup before it can continue.",
                "Open setup",
                input.CurrentTask.Target,
                HomeCommandCenterSeverity.Warning,
                GetFreshness(input.CurrentTask.ObservedAtUtc, input.NowUtc));
        }

        if (input.Queue.ActionableCount > 0)
        {
            yield return new Candidate(
                "Meeting work is queued",
                $"{input.Queue.ActionableCount} meeting work item(s) need review.",
                "Open meetings",
                HomeCommandCenterTarget.Meetings,
                HomeCommandCenterSeverity.Information,
                GetFreshness(input.Queue.ObservedAtUtc, input.NowUtc));
        }

        if (input.Update.IsActionable)
        {
            yield return new Candidate(
                "Update available",
                "A newer app release is ready for review.",
                "Open updates",
                HomeCommandCenterTarget.SettingsUpdates,
                HomeCommandCenterSeverity.Information,
                GetFreshness(input.Update.ObservedAtUtc, input.NowUtc));
        }

        if (input.Provider.NeedsAttention)
        {
            yield return new Candidate(
                "Summary provider needs review",
                "Summaries need a validated provider before they can run.",
                "Open summaries",
                HomeCommandCenterTarget.SettingsSummaries,
                HomeCommandCenterSeverity.Information,
                GetFreshness(input.Provider.ObservedAtUtc, input.NowUtc));
        }

        yield return new Candidate(
            "Ready to record",
            "Local transcription and output locations are ready.",
            null,
            HomeCommandCenterTarget.None,
            HomeCommandCenterSeverity.Neutral,
            GetFreshness(input.Readiness.ObservedAtUtc, input.NowUtc));
    }

    private static HomeStateFreshness GetFreshness(DateTimeOffset? observedAtUtc, DateTimeOffset nowUtc)
    {
        if (!observedAtUtc.HasValue)
        {
            return HomeStateFreshness.Unavailable;
        }

        return nowUtc - observedAtUtc.Value > MaximumCurrentAge
            ? HomeStateFreshness.Stale
            : HomeStateFreshness.Current;
    }

    private static HomeCommandCenterTarget MapReadinessTarget(RecordingReadinessRemediationTarget target)
    {
        return target switch
        {
            RecordingReadinessRemediationTarget.SettingsSetup => HomeCommandCenterTarget.SettingsSetup,
            RecordingReadinessRemediationTarget.SettingsFilesAndUpdates => HomeCommandCenterTarget.SettingsFilesAndUpdates,
            RecordingReadinessRemediationTarget.WindowsRecordingPrivacy => HomeCommandCenterTarget.SettingsRecording,
            RecordingReadinessRemediationTarget.SettingsRecording => HomeCommandCenterTarget.SettingsRecording,
            RecordingReadinessRemediationTarget.SettingsAdvanced => HomeCommandCenterTarget.SettingsAdvanced,
            _ => HomeCommandCenterTarget.None,
        };
    }

    private static string? GetReadinessActionLabel(RecordingReadinessRemediationTarget target)
    {
        return target switch
        {
            RecordingReadinessRemediationTarget.SettingsSetup => "Open setup",
            RecordingReadinessRemediationTarget.SettingsFilesAndUpdates => "Open output locations",
            RecordingReadinessRemediationTarget.WindowsRecordingPrivacy => "Open recording settings",
            RecordingReadinessRemediationTarget.SettingsRecording => "Open recording settings",
            RecordingReadinessRemediationTarget.SettingsAdvanced => "Open advanced settings",
            _ => null,
        };
    }

    private sealed record Candidate(
        string Headline,
        string Reason,
        string? ActionLabel,
        HomeCommandCenterTarget Target,
        HomeCommandCenterSeverity Severity,
        HomeStateFreshness Freshness);
}
