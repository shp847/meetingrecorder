# Adaptive Backlog Drain Baseline

**Recorded:** 2026-09-27  
**Scope:** Sprint 0 audit only. This document records current behavior; it does
not authorize a scheduler, worker, profile, or UI change.

## Safety boundary

"Active recording" is the app-owned capture state supplied to
`ProcessingQueueService` through its `isRecordingProvider`. It is not inferred
from operating-system process inspection. In `Responsive` mode, a live
recording pauses new background work. The only existing exception is an
explicit persisted `RunNextIgnoreRecordingPause` ASAP request for that meeting.
No capacity, power, activity, or process probe exists today.

## Current policy ledger

| Concern | Current authoritative behavior | Evidence |
| --- | --- | --- |
| New configuration | New installs save `Normal`, `Responsive`, deferred speaker labels, configured stages, and a `22:00`–`06:00` local window. | `AppConfigStore.BuildDefaultConfig`; `AppConfigStoreTests.LoadOrCreateAsync_Creates_Default_Config_When_Missing` |
| Legacy `OvernightDrain` | On the next save/load normalization, legacy profile data becomes `Normal` plus an explicit overnight `TranscriptFirst` strategy. The original profile is not retained as the active saved setting. | `AppConfigStore.Normalize`; `AppConfigStoreTests.SaveAsync_Migrates_Legacy_Overnight_Profile_To_Explicit_Transcript_First_Window` |
| Direct legacy caller | A not-yet-migrated in-memory `OvernightDrain` configuration becomes `TranscriptOnlyDrain` only while inside the window. This compatibility branch can use two workers. Its strategy overload delegates through `GetEffectiveSpeedProfile`, so its supplied local-time argument is ignored in favor of the runtime clock. | `BackgroundProcessingPolicy.GetEffectiveSpeedProfile`; `BackgroundProcessingPolicyTests.Legacy_Overnight_Profile_Uses_Its_Runtime_Clock_Compatibility_Path` |
| Migrated current caller | An explicit `TranscriptFirst` overnight strategy skips optional labels and summaries while the window is active. Worker count otherwise follows `BackgroundProcessingMode`; the default responsive mode is one worker. | `BackgroundProcessingPolicy.GetEffectiveInitialProcessingStrategy`, `GetMaxWorkerCount`, `ShouldSkip*`; `BackgroundProcessingPolicyTests.Migrated_Overnight_Strategy_Uses_Transcript_First_Only_Inside_Window` |
| Window boundary | Start is inclusive and end is exclusive. Cross-midnight windows work. Invalid text falls back to `22:00`–`06:00`. | `BackgroundProcessingPolicy.IsOvernightDrainWindowActiveCore`; `BackgroundProcessingPolicyTests.IsOvernightDrainWindowActive_Uses_Configured_Local_Window`; `...Uses_Defaults_For_Invalid_Clock_Text_And_Has_Exclusive_End` |
| DST/clock limit | The current policy receives only a local `TimeSpan` (normally `DateTimeOffset.Now.TimeOfDay`). It has no time-zone, ambiguous-time, missing-time, or clock-regression model. This is a known constraint for a later resolver—not proof of DST-safe behavior. | `BackgroundProcessingPolicy.IsOvernightDrainWindowActive` |
| Worker priority and budgets | Workers are always `BelowNormal`. Responsive, balanced, fastest-drain, and maximum-throughput transcription budgets are 2/4/8/12; diarization budgets are 1/2/4/6. | `BackgroundProcessingPolicy`; `BackgroundProcessingPolicyTests` |
| Capture protection | The queue waits before launch when responsive mode has app-owned live recording. It reports a paused status and does not kill an active worker merely because recording began. | `ProcessingQueueService.WaitForBackgroundProcessingPermitAsync`; `ProcessingQueueServiceTests.EnqueueAsync_Does_Not_Start_A_New_Worker_While_Responsive_Mode_Recording_Is_Active`; `...GetStatusSnapshot_Reports_Paused_State...` |
| Restart and persisted user intent | A persisted ASAP request is re-applied on resume; stale persisted ASAP is cleared. Recovery also repairs selected stale/interrupted sessions before requeue. | `ProcessingQueueService.ResumePendingSessionsAsync`; `ProcessingQueueServiceTests.ResumePendingSessionsAsync_Honors_A_Persisted_Rush_Request_After_Restart`; `...Repairs_Interrupted_Diarization_Crash_Sessions...` |

## Synthetic stage corpus

The following redacted fixtures are the current executable corpus. They use
temporary synthetic sessions only; no recordings, transcript words, user paths,
or installed-machine state are evidence.

| Fixture state | Current result | Executable evidence |
| --- | --- | --- |
| Transcript missing / queued | Full-pass worker starts at transcription; queue exposes transcription as current stage. | `ProcessingQueueServiceTests.GetStatusSnapshot_Tracks_Queued_Counts_Separately_From_The_Active_Item...` |
| Transcript current, labels missing | Whole-session processor may run diarization unless it is disabled, deferred, skipped, or unavailable. | `SessionProcessorTests.ProcessAsync_Publishes_Transcript_When_Optional_Diarization_Times_Out`; `...Summarizes_When_Diarization_Fails` |
| Transcript-first / legacy overnight effective | Whole-session processor marks diarization and summary skipped; it does not schedule a later enrichment pass. | `SessionProcessorTests.ProcessAsync_TranscriptOnlyDrain_Skips_Diarization_And_Summaries`; policy tests above |
| Diarization succeeded, summary enabled | Full-pass processor creates the summary after diarization and republishes the normal artifacts. | `SessionProcessorTests.ProcessAsync_Generates_Summary_After_Diarization_And_Publishes_Artifacts` |
| Optional diarization failed or timed out | Transcript publication remains available; optional diarization outcome is explicit. | `SessionProcessorTests.ProcessAsync_Publishes_Transcript_When_Optional_Diarization_Times_Out`; `...Summarizes_When_Diarization_Fails` |
| Summary disabled | Summary is marked skipped and its provider is not invoked. | `SessionProcessorTests.ProcessAsync_Skips_Summary_When_Disabled_Without_Calling_Provider` |
| Queue status / stale source | Fresh queue state determines processing truth; stale state must ask for refresh rather than claim idle or a false ETA. | `BacklogExperienceResolverTests.Resolve_Stale_Live_Snapshot_Requires_Refresh_And_Never_Claims_Idle_Or_Eta` |
| Resume / mixed interrupted backlog | Resume repairs supported stale sessions, then requeues eligible work without discarding it. | `ProcessingQueueServiceTests.ResumePendingSessionsAsync_Repairs_Stale_Post_Transcription_Sessions...`; `...Applies_Deferred_Speaker_Labeling...` |

## Limitation reproduced

The focused policy and processor fixtures reproduce the current compatibility
limit: inside a legacy or migrated transcript-first overnight window, the
primary pass publishes the transcript and records speaker labeling and summary
as skipped. There is no durable stage-specific queue item, barrier, lease, or
automatic follow-on label/summary work. Therefore, the current overnight
configuration is transcript-first—not a full staged backlog drain.

The live queue monitors transcription, diarization, and publication statuses
for ETA/status projection. Summary state remains a whole-session manifest
concern rather than an independently dispatched queue stage. Sprint 1 must add
that work model before any window or capacity acceleration changes.

Sprint 1's isolated model is now documented in
[`adaptive-backlog-drain-work-model.md`](adaptive-backlog-drain-work-model.md).
It intentionally remains disconnected from the live queue until stage-worker
and queue-adoption work is complete.

## No-regression boundary for later work

- Preserve app-owned recording protection, `BelowNormal` priority, explicit
  ASAP behavior, and no process inspection.
- Preserve full-pass/manual behavior until stage work has durable currentness,
  lease, retry, and conflict rules.
- Report blocked, failed, skipped, and stale work truthfully; do not present a
  transcript-first pass as a completed enrichment backlog.
- Treat DST, invalid/changed clocks, restart, cancellation, and stale queue
  state as explicit policy inputs before replacing the current local-time check.
