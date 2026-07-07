# Meeting Recorder Architecture

This document describes the current implementation architecture for the Meeting Recorder app as it exists in the repository today. Product behavior and scope live in [PRODUCT_REQUIREMENTS.md](C:\code\Meeting Recorder\PRODUCT_REQUIREMENTS.md). Deployment and first-run guidance live in [SETUP.md](C:\code\Meeting Recorder\SETUP.md).

## 1. Architectural Intent

The architecture is optimized for a constrained Windows laptop:

- standard user permissions
- local-first recording and processing
- portable deployment support
- no Windows service requirement
- resilient batch transcription after the meeting
- graceful behavior when model download, diarization, or browser integration are unavailable

## 2. Runtime Shape

The app is now split between reusable platform projects and Meeting Recorder-specific runtime projects.

### Reusable platform projects

- `AppPlatform.Abstractions`
  - neutral app manifest, release-channel, install-layout, shell-integration, settings, and about/support records
- `AppPlatform.Configuration`
  - reusable config-store contracts plus generic live JSON config plumbing
- `AppPlatform.Deployment`
  - reusable deployment engine: release-feed parsing, bundle integrity validation, install/update orchestration, shortcut repair, install provenance persistence, and install-path release coordination
- `AppPlatform.Deployment.Cli`
  - shared CLI entry point and canonical install/update execution path
  - the only shipped writer/updater of the managed install tree
- `AppPlatform.Deployment.WpfHost`
  - shared WPF host layer for branded installer and updater shells
- `AppPlatform.Deployment.Wix`
  - reusable WiX-facing packaging assets for per-user installs
- `AppPlatform.Shell.Wpf`
  - shared WPF shell resources plus Settings and Help host windows
  - also provides the Technical Studio brushes, lines, typography, and action styles reused by shared deployment-facing shell surfaces
- `MeetingRecorder.Product`
  - product adapter that owns the manifest, shell registrations, about/support content, and default install/data layout

For the shipped MSI flow, the managed install root in `MeetingRecorder.Product` and the bundled `MeetingRecorder.product.json` are expected to stay aligned with `%USERPROFILE%\Documents\MeetingRecorder`. Writable runtime data remains outside the install root under `%LOCALAPPDATA%\MeetingRecorder`.
Managed-install update repair is also responsible for cleaning up stale launch surfaces around that canonical root: it now quarantines both the older `%LOCALAPPDATA%\Programs\Meeting Recorder` location and the legacy `%USERPROFILE%\Documents\Meeting Recorder` alias when those exist, rewrites existing Desktop or Start Menu shortcuts back to the canonical launcher after an update, and repairs an already-pinned taskbar shortcut to the installed app executable plus installed `MeetingRecorder.ico` without creating a new taskbar pin.
The MSI finish-launch path is intentionally not a raw second launch of `MeetingRecorder.App.exe`; it uses an installed relaunch wrapper plus a short-lived marker under `%LOCALAPPDATA%\MeetingRecorder` so the app can distinguish installer relaunches from normal user activations and coordinate a clean close-and-reopen of an idle existing instance.

### Meeting Recorder-specific runtime projects

- `MeetingRecorder.App`
  - WPF desktop UI
  - recording orchestration
  - local meeting detection for Teams and Google Meet, including visible browser-window title heuristics plus endpoint-level render audio activity without per-app Core Audio session enumeration
- guarded Google Meet browser-audio attribution that still refuses generic Chromium playback in shared-content browser windows, but allows an explicit active `Meet - ...` top-level browser window to auto-start from browser-family audio when exact tab attribution is unavailable; silent explicit Meet windows now flow through sustained quiet-start debounce instead of starting immediately
- in-place reclassification of an auto-started live session from stale Google Meet browser evidence to a stronger Teams meeting window, so the recording is preserved as one session
- continued background detection during manual recordings so a user-started session can still reclassify in place when a stronger live Teams or Google Meet signal appears later, while auto-stop remains limited to auto-started sessions
- quiet specific Teams detections with matched Teams audio-session evidence can now also reclassify an active manual session even while they remain below the stronger auto-start threshold, so a live Teams call can take over from a generic manual title during low-audio stretches
- when a qualifying manual-to-meeting takeover signal has been observed but the active manifest has not switched yet, the Home pending stem uses that deferred reclassification context immediately and the stop/live-metadata paths retry the same reclassification before publish so artifacts do not remain labeled as `manual`
- same-platform live meeting takeovers now split into two paths: manual sessions can still reclassify in place when a stronger supported call identity appears, but already lifecycle-managed sessions now roll over by stopping the earlier meeting and starting a fresh session for the newly detected one
- manual stop suppression for supported meetings, so once a user explicitly stops an active Teams or Google Meet session, auto-detection will not immediately restart that same meeting just because its tab or window is still visible or the microphone is noisy
- continuity protection for active auto-started Teams sessions when a weak unrelated Google Meet browser candidate is also visible, so quiet patches do not falsely age the Teams session into auto-stop
- continuity protection for active auto-started Google Meet sessions when the Meet window is temporarily obscured by a weak Teams chat/navigation candidate, including a three-minute silent grace for sessions pinned to a specific Meet code, using a bounded stop-timeout extension instead of resetting the positive-signal clock forever
- sustained specific Teams or Google Meet meeting-window evidence can now auto-start a quiet call even before render audio rises above the normal auto-start threshold; Google Meet requires a stable specific Meet code so the detector does not rely on a one-scan title flash alone
- Teams detection now also treats newer in-call window titles that are just the attendee or meeting name as meeting surfaces when they come from a Teams host window, then relies on endpoint render activity plus the existing playback/chat demotion path to distinguish live calls from ordinary Teams content
- once an auto-started Teams session is already live, a stale same-title quiet Teams window now extends the stop timeout only for a bounded grace period instead of resetting the positive-signal clock forever; the same specific Teams meeting can refresh the positive signal when Windows still attributes audio to that Teams session, or when attribution is temporarily unavailable because the audio probe timed out while recent captured audio activity continues, while weaker matching Teams shell/chat surfaces still need recent capture activity and the pinned Teams sharing-control-bar surface is treated as a continuation of the active specific meeting so an in-progress presentation does not fragment the session
- Google Meet fallback no longer inspects Chromium tab UI directly; it now relies on explicit browser titles plus endpoint render activity so Edge or Chrome are not probed through cross-process UI Automation during detection
- Windows render audio probing now also runs behind a `1.5 s` timeout and `15 s` cooldown so a hung Core Audio query cannot starve later detection cycles before Teams auto-start has a chance to react
- Windows render audio probing now merges the multimedia and communications default render endpoints so speaker crashes, Bluetooth headset handoffs, and other output-device switches can move capture to the active output path
- Windows render audio probing now reads endpoint peak levels only and does not enumerate per-app Core Audio sessions, so endpoint tooling should no longer see meeting detection as App.exe probing service-hosted Windows audio processes such as `svchost.exe`
- Visible-window detection intentionally keeps process names blank and relies on supported window classes and titles instead of opening window owner processes or matching Core Audio session PIDs, so service-hosted shell windows are not probed just to classify Teams or Meet surfaces
- live recording loopback capture now also prefers the active communications render endpoint when a meeting is routed there, so the recorded meeting audio does not fall back to microphone-only room pickup while a headset or communications-only output device is carrying the real call audio
- live recording loopback capture now evaluates candidate render endpoints explicitly, persists the selected endpoint metadata into the session manifest, and can hot-swap to a stronger endpoint mid-recording without splitting the meeting into separate sessions
- live microphone capture compares active input identity by fallback state plus device ID, not by Windows default role, so the same physical microphone flapping between `Multimedia` and `Communications` does not repeatedly restart capture or re-enter Core Audio service-hosted paths
- candidate ranking now prefers a specific Teams meeting with attributed Teams render audio over a stale Google Meet browser window that no longer has attributed Meet audio, which prevents old visible Meet tabs from stealing a real Teams auto-start
  - resilient Teams shell-title parsing so bare navigation surfaces like `Chat | Microsoft Teams` do not crash the background detection loop
  - config editing and hot reload
  - meetings library plus an owned meeting detail window for transcript reading, rename, retry, split, project, speaker-label, archive, and delete actions
  - Whisper model setup UI
- `MeetingRecorder.Core`
  - shared domain models
  - path and filename rules
  - work-manifest persistence
  - output catalog logic
- WAV merge and mix logic, including loopback-aware microphone bleed reduction, short-lag raw correlation checks, and a rolling `20 ms` envelope-history detector that can suppress delayed `80-400 ms` speaker pickup during published-audio preparation
  - transcript rendering
  - transcript document reading for the Meetings detail window, preferring structured JSON sidecars and falling back to the app-owned Markdown transcript format
  - publish helpers
  - Whisper model inspection, download, and import logic
- `MeetingRecorder.ProcessingWorker`
  - separate background process for post-recording work
  - final audio preparation
  - Whisper transcription
  - optional diarization sidecar integration
  - optional configured meeting summarization after diarization
  - transcript and ready-marker publishing

The worker is launched as a separate process so transcription or diarization failures do not destabilize the desktop UI.
The default transcription provider remains Whisper.NET and the default diarization provider remains the local Sherpa sidecar. Experimental GPU transcription and alternate diarization backends are represented as Advanced-settings local CLI providers: worker probes persist success/failure for the configured executable, startup uses an external provider only when the saved probe still matches the executable path, prepared audio flows through `{audioPath}`, transcript segments flow through `{transcriptPath}` where needed, and provider output keeps the existing `TranscriptionResult` or `DiarizationResult` shape.
For self-contained release bundles, `MeetingRecorder.App` is now published as a loose apphost plus `MeetingRecorder.App.dll`, `.deps.json`, and `.runtimeconfig.json`, while `AppPlatform.Deployment.Cli`, the full `MeetingRecorder.ProcessingWorker` publish output, scripts, and `MeetingRecorder.product.json` stay external so bootstrap, install, and update flows can keep invoking those sidecars directly. That worker payload is expected to include `MeetingRecorder.Core.dll` plus the worker `.deps.json` and `.runtimeconfig.json`, and managed-install repair now restores those sidecars when they are missing from an existing install.
Runtime paths that persist launch targets resolve them from `Environment.ProcessPath` so launch-on-login, worker startup, and updater handoff stay rooted at the canonical managed install under `%USERPROFILE%\Documents\MeetingRecorder`.

## 3. Windows Deployment Constraints

The implementation assumes all of the following may be true:

- the user has no local admin rights
- browser extensions are blocked
- startup apps or services are blocked
- outbound model downloads are filtered or replaced with HTML error pages
- the laptop is CPU-only
- OneDrive is unavailable or not approved

As a result, the design keeps the core workflow working with:

- manual launch
- manual recording fallback across conferencing apps that use the normal Windows audio stack
- portable deployment
- local file-based automation handoff
- explicit model import when downloads fail

## 4. UI Architecture

The current WPF surface is split across two primary task destinations, with Home acting as the landing page:

- `Home`
- `Meetings`
Capability setup is launched from `Settings > Setup` instead of living in the top navigation row.

Secondary maintenance and support live in header-level `Settings` and `Help` surfaces rather than the primary navigation row.

Startup now favors shell responsiveness over full Meetings analysis. The initial load waits for the first shell render, performs a fast meeting refresh, and starts the long-lived detection/update timers only after that first paint so `Home` becomes interactive sooner. Heavier attendee enrichment, manifest inspection, and cleanup-analysis work stay deferred until `Meetings` is actually activated.
The Updates section now treats local install facts and release-package facts as separate concepts. `Current Installation` reflects the machine’s actual installed-on time plus the on-disk install footprint, while installed package published-at and package size are shown from trusted managed install provenance when available. If an older or MSI-origin install arrives without provenance, the first successful `UpToDate` release check now backfills those package fields into `install-provenance.json` so later renders and update comparison can use durable local metadata instead of keeping those fields blank. The GitHub section keeps showing the remote package publish timestamp and installer asset size used for update comparison.
Auto-stop now follows the same responsiveness rule: the Home console shows a visible countdown for auto-started sessions, flips into an immediate `Auto-stopping` transition when the timeout expires, and pushes attendee enrichment into the background queue so stop finalization no longer waits on Outlook lookups. Once that countdown is already active, weak quiet-continuation evidence no longer clears it; only a real resumed meeting signal or same-title Teams audio attribution resets the countdown and preserves the live session.
That resume rule now includes narrow continuity cases that had been splitting live calls: when the active session still has a specific Teams meeting title and Windows still attributes audio to that Teams session, or the audio probe times out while recent captured audio activity is still present, the app treats that as a real Teams continuation even if loopback level history is briefly quiet. For an active Google Meet pinned to a specific Meet code, weak Teams chat/navigation occlusion can now hold the stop window for up to three silent minutes without refreshing the positive-signal timestamp, so short focus changes and quiet patches do not fragment into multiple sessions.
Detection follows the same rule now: the dispatcher timer only schedules scans, the expensive window/audio probe runs off-thread, overlapping scans are skipped instead of queued, browser-tab and audio-session subprobes are both bounded by short timeouts/backoff windows, and only the final decision is applied back on the UI thread.
The in-app activity surface is also bounded. `AppendActivity` writes the complete event to the file logger, then updates a small rolling UI buffer instead of appending forever to a hidden editable WPF text document. The hidden TextBox is read-only, keeps no undo stack, and only scrolls when visible, so frequent detection changes cannot grow UI-thread state without bound.
Meetings refreshes are now treated as coalesced foreground-sensitive work. Config churn, stop-time publish, and similar non-urgent refresh requests can stay marked dirty while a recording is active or the user is still on `Home`, then one catch-up refresh runs automatically once `Meetings` is visible again.
Backlog visibility is now additive to that shell responsiveness work: `MainWindow` subscribes to a queue-status snapshot from `ProcessingQueueService`, drives a compact header queue chip plus a fuller `Meetings` processing strip from the latest in-memory snapshot, and uses a dedicated 1-second UI timer only to refresh relative elapsed/ETA strings without rescanning manifests on every tick. If that live snapshot is temporarily empty during startup or worker recovery, the shell falls back to persisted queued/processing meeting rows so backlog state stays visible with `ETA unavailable` instead of disappearing.
That queue snapshot now also carries a single persisted `ASAP` request. `Meetings` can mark one eligible queued or in-progress manifest to run next, optionally let only that rushed item bypass the normal `Responsive` live-recording pause rule, and surface the rushed title plus any interrupted-and-requeued backlog item through the existing queue chip and Meetings strip.

### Home

Responsibilities:

- show the current shell status through the header-level status capsule
- keep the editable session metadata visible
- surface a live elapsed recording timer while capture is active
- render the live audio graph inside a single recording console, using a shorter graph well in the default desktop shell so the active recording deck is less likely to need immediate vertical scrolling
- surface a compact capture-status well that shows the active loopback endpoint, current capture mode, and the most recent capture timeline entries
- expose manual start and stop controls
- expose immediate-save quick settings for microphone capture and auto-detection, using side-by-side summary-plus-toggle cards so those controls stay visible in the default Home viewport more often
- apply microphone capture changes to the active recording from the save/click moment forward while also updating the saved default
- clarify that manual recording works beyond Teams and Google Meet, while auto-detection is narrower
- allow editing the current meeting title, client/project, and key attendees while recording
- persist client/project and key-attendee edits back into the active session during recording instead of waiting only for stop-time publish
- parse comma- or semicolon-delimited key attendees into separate stored attendee names before later Teams/Outlook reconciliation upgrades them
- let a manually started recording adopt meeting-lifecycle auto-stop once it is strongly reclassified to a supported Teams or Google Meet call, so unrelated post-call system audio does not leak into the same session
- reset the current meeting title draft to the new detected meeting title when a clearly different supported call takes over the active session, so the editor reflects the live meeting identity instead of preserving a stale prior call label
- keep setup, update, and warning actions out of the Home body so the recording deck stays visually simple

### Meetings

Responsibilities:

- list published meetings from output artifacts and surface recent ended work-session manifests when publish artifacts are still missing
- provide a toolbar-driven workspace with search, explicit sorting, and a default grouped view that older configs are migrated into once
- support grouped browsing by week, month, platform, status, client / project, or attendee without changing the underlying meeting source
- initialize grouped browsing with only the first visible group expanded, keep the rest collapsed, and offer explicit `Expand All` and `Collapse All` controls when grouping is active
- publish the baseline meeting list immediately, then defer cleanup suggestion analysis and attendee enrichment so opening `Meetings` does not block the shell
- avoid awaiting routine catalog refreshes on the stop path or on unrelated config saves; those now flow through the same deferred refresh coalescer
- show queue state, pause reason, remaining count, current processing stage, and approximate current-item plus overall queue ETA in a compact technical strip above the Meetings toolbar
- surface title, project, local started time, duration, platform, status, and compact audio/transcript actions
- let search match title, project, key attendees, and captured attendee names
- surface cleanup recommendation badges independently from publish status
- host the one-time historical cleanup review banner and ongoing recommendation block
- keep the Meetings tab itself as a library/work-queue surface with a compact selection command strip instead of a mixed selected-meeting right rail
- open one reusable owned `MeetingDetailWindow` for a single selected meeting when the user double-clicks a row or chooses `Open Details`
- render persisted capture diagnostics in the detail window so finished sessions still show which loopback endpoint was used, when swaps happened, and whether capture fallback or swap-failure events occurred
- route detail-window maintenance events back through `MainWindow` service methods, then refresh the meeting library and open detail state after mutations
- keep the lower Meetings area focused on collapsible cleanup recommendation review rather than single-meeting metadata and controls
- render transcript paragraphs in the detail window by coalescing consecutive same-speaker segments from local JSON sidecars, with app-owned Markdown fallback when structured JSON is not available
- render meeting summaries in the detail window when summary generation is enabled and a summary is available
- show a clear disabled, unconfigured, unavailable, or failed summary state without implying that transcript generation failed
- allow one-meeting summary generation or retry from the detail window without re-running transcription or speaker labeling
- rename published meetings and keep stems aligned
- expose transcript re-generation for failed sessions and audio-only sessions
- allow project metadata edits for one meeting from the detail window

### Settings-hosted setup

Responsibilities:

- provide the first Settings section for transcription and optional speaker labeling readiness
- split setup into dedicated `Transcription` and `Speaker labeling` sections
- stay focused on capability readiness only, while pointing behavior, storage, updates, and troubleshooting back to `Settings`
- keep transcription in one consolidated setup section instead of split recommendation panels
- drive both capabilities from a shipped curated model catalog with `Standard`, `Higher Accuracy`, and `Custom` profile states
- default the lay-user flow to `Use Standard`, `Use Higher Accuracy`, `Import approved file`, and open-folder diagnostics actions instead of raw path editing
- show `Needs setup`, `Standard ready`, `Higher Accuracy ready`, or `Custom ready` status plus retry guidance when an optional Higher Accuracy install-time download fell back to Standard
- keep raw storage paths read-only in the normal settings flow and tuck infrastructure-heavy details under diagnostics-oriented surfaces
- keep speaker labeling optional and separate from core transcription readiness
- use GitHub as the only built-in automatic download source for Whisper models and speaker-labeling assets
- open bundled local setup help first for speaker labeling, then fall back to GitHub guidance if the local guide is unavailable
- show curated alternate public speaker-labeling download locations when configured, or an explicit empty state when none are available
- provide quick access to model and diarization asset folders plus advanced diagnostics

### Settings and Help

Responsibilities:

- edit the app configuration file from the header-level `Settings` dialog
- hot reload supported settings into the running app
- use a dedicated section-button strip instead of a custom clipped tab header template
- group capability readiness under `Setup`, everyday defaults under `General` and `Files`, and release/troubleshooting paths under `Updates` and `Advanced`
- make the distinction from `Setup` explicit in copy: `Setup` is for readiness, while the other Settings sections are for behavior, storage, updates, and troubleshooting
- expose mode-based performance controls in `Advanced` instead of raw numeric worker knobs
- default those performance controls to `Responsive` background work plus `Deferred` speaker labeling so the app biases toward machine responsiveness first
- keep speaker-labeling GPU acceleration explicit and opt-in: new installs default to CPU-only, legacy `Auto` configs migrate to CPU once, user-saved `Auto` choices and successful GPU tests are preserved, release engineering owns the pinned DirectML-enabled Sherpa native runtime under `assets\native\sherpa-onnx-directml\win-x64`, the worker only reports DirectML when that bundled runtime is actually enabled, unavailable-GPU paths fall back to CPU without blocking transcript publication, and DirectML over-segmented speaker counts skip CPU retry once the automatic range is already unsupported
- keep automatic speaker labeling as an explicit opt-in: legacy `Throttled` or `Inline` configs are migrated back to `Deferred` once, setup no longer auto-promotes `Deferred`, DirectML worker crashes retry labels on CPU while preserving the run mode, and repeated or CPU-only diarization crashes push future processing back to `Deferred`
- keep speaker-label repair transcript-first: suspicious already-published speaker explosions become `Repair Speaker Labels` recommendations, and repair queues seed `transcription.snapshot.json` from the existing JSON or Markdown transcript with old speaker IDs removed before the worker re-runs diarization
- keep over-segmented diarization recoverable: before the worker rejects an unsupported too-many-speakers result, it extracts local speaker embeddings for the candidate turns and lets the cluster-merge service collapse acoustically similar over-split clusters; labels are still skipped if the rescued catalog remains outside the supported automatic range
- keep speaker-name refresh and undo metadata-only: `Refresh Suggestions` re-matches existing speaker voice samples against the local profile store without transcription, diarization, audio writes, or worker queueing, while `Undo Name Recognition` clears profile-sourced attribution from the meeting artifacts and stores only scoped negative feedback for `meetingId + speakerId + profileId`
- keep AI summary generation explicit and configurable under Settings, with local-first ModelProxy behavior and hosted OpenAI fallback only when the user has provided an API key; transcript-only drain profiles skip automatic summaries while leaving manual per-meeting summary generation available later
- store summary API keys and local bearer keys outside plaintext app config, and never write key material into logs, transcript artifacts, or status text
- keep update-check behavior, manual update controls, and the update feed URL inside `Updates` and `Advanced`
- keep infrastructure-heavy paths and troubleshooting overrides hidden by default under `Advanced`
- expose About details, setup/help entry points, logs/data shortcuts, and release-page links from the header-level `Help` dialog
- control launch-on-login registration
- keep dependency and warning messaging attached to the relevant setting without changing the underlying config schema

## 5. Session Lifecycle

The durable session lifecycle uses these states:

- `Idle`
- `Recording`
- `Finalizing`
- `Queued`
- `Processing`
- `Published`
- `Failed`

### Lifecycle flow

1. The app creates a work folder and manifest when a session starts.
2. During recording, loopback chunks and optional microphone chunks are written to `raw`.
3. On stop, the manifest is updated with chunk paths and moved to `Queued`.
4. Any optional attendee enrichment is attempted in the background queue, not in the foreground stop path.
5. The UI launches the worker with the manifest path and config path.
   Worker discovery prefers the installed app directory derived from `Environment.ProcessPath` so background processing stays rooted at the managed install bundle.
6. The worker loads the manifest, merges audio, and publishes the final WAV.
7. If transcription succeeds, the worker persists a durable per-session transcript snapshot before optional speaker labeling begins.
8. If speaker labeling succeeds, the worker can compute local speaker-cluster embeddings, retry over-segmented clustering with progressively stricter thresholds, prefer compact automatic speaker counts, merge acoustically similar over-split clusters, drop tiny fragment-only speaker IDs from the published catalog, match clusters against user-confirmed voice profiles, auto-apply only mature high-confidence names, keep lower-confidence matches as suggestions with decision reasons, and render transcript artifacts from the labeled segments. Later meeting-detail refresh or undo actions update only speaker-name attribution and scoped profile feedback; they do not re-run transcription or diarization.
9. If summary generation is enabled and the active processing speed profile is not transcript-only drain, the worker runs summarization after speaker labeling succeeds, skips, or fails safely. The summary stage uses the final speaker-labeled transcript when labels are available and the plain transcript otherwise.
10. If ModelProxy is configured and reachable, the summary stage sends a text-only, non-streaming Chat Completions request to the local OpenAI-compatible endpoint with web search disabled. If configured policy allows hosted fallback and the user has saved an OpenAI API key, the stage can retry through OpenAI when the local provider is unavailable.
11. If summarization is disabled, unconfigured, unavailable, times out, or fails after all configured providers are tried, the manifest records a safe summary status and transcript publication continues. Summary failure never suppresses audio, transcript Markdown, transcript JSON, or `.ready` output.
12. If optional speaker labeling crashes the worker process while DirectML acceleration is enabled, the queue first retries the same speaker-labeling work with CPU acceleration and preserves the selected speaker-labeling run mode. If CPU speaker labeling also crashes, or the original run was already CPU-only, the queue stamps the manifest with an internal skip-label override, switches future speaker labeling back to `Deferred`, and retries that manifest once without diarization, reusing the saved transcript snapshot instead of retranscribing the whole session.
13. If microphone merging fails while loopback chunks are still readable, source-audio preparation falls back to a loopback-only WAV so transcript publishing can continue. If no usable source audio can be prepared, the processor and queue mark the manifest `Failed` instead of leaving it `Queued` for repeated startup retries.
14. On startup, the queue first seals stale live-recording manifests that still have preserved raw chunks, stamps the end time from the newest usable audio chunk, requeues them for normal processing, then scans pending manifests for older stale post-transcription sessions and requeues those recoverable sessions once with the same skip-label override so backlog repair is durable across restarts.
15. Pending-session resume order gives already-transcribed not-yet-published sessions the highest priority, so repaired backlog items publish before fresh untouched queue work.
16. In `Responsive` mode, new background queue work pauses while a live recording is active, worker launches run at reduced OS priority, and primary publish can complete without waiting on optional speaker labeling when that mode is `Deferred`. In `Transcript only` speed profile, the queue can run up to two transcript-only workers, skips speaker labels and automatic summaries, and publishes transcripts first.
17. Startup and pre-worker maintenance clean stale unlocked files from the diarization and transcription temp roots, with a one-time more aggressive cleanup pass after upgrade so orphaned temp files do not grow without bound. Published-session maintenance also normalizes manifests onto the retained published audio and deletes redundant bulky local work-cache files once a matching published recording exists.
18. Before pending sessions are re-enqueued on startup, queued imported-source reprocessing manifests whose original published transcript artifacts already exist are archived out of `work` into `%LOCALAPPDATA%\MeetingRecorder\maintenance\archived-imported-source-work`, so stale reprocess jobs do not masquerade as the live backlog.
19. When a published meeting row shares a stem with one of those stale imported-source manifests, the published artifacts remain the source of truth for display/openability instead of being downgraded by the queued manifest state.
20. Imported-source reprocessing manifests are keyed back to the original published-audio stem when that source path is known, so a retry manifest with a filename-like title such as `Tyler Colin.wav` cannot appear as a second logical meeting row beside the original published session.
21. `ProcessingQueueService` now maintains an immutable queue-status snapshot with exact in-memory queued counts, current-item stage metadata, pause reason, approximate ETA estimates, and the optional persisted `ASAP` request. If the worker snapshot is not yet available but the meeting catalog still contains persisted `Queued`, `Processing`, or `Finalizing` rows, `MainWindowInteractionLogic` synthesizes a backlog-only shell fallback so the user still sees active queue state.
22. When a different meeting is marked `ASAP` while work is already running, `ProcessingQueueService` preempts that worker, resets the interrupted manifest back to a queued stage-safe state, and re-inserts it directly behind the rushed meeting ahead of the normal backlog.
23. A rushed meeting can either run next while still respecting the normal `Responsive` pause rule, or run next even while a live recording is active. That pause bypass applies only to the single rushed item and is cleared automatically once the meeting finishes or the request becomes stale.
24. If transcription fails, the manifest becomes `Failed` and the final WAV remains available.
25. A retry action can move a failed manifest back to `Queued` and relaunch the worker.

## 6. Work Folders and Persistence

Each session has a dedicated work folder:

- `<workDir>\<session-id>\manifest.json`
- `<workDir>\<session-id>\raw\`
- `<workDir>\<session-id>\processing\`
- `<workDir>\<session-id>\logs\`

The processing folder carries fixed stage snapshots for repaired retries: `transcription.snapshot.json` whenever transcription finishes successfully, and `summary.snapshot.json` when summary generation succeeds. Repaired retries can continue from saved transcript text even when a later optional diarization or summarization step crashes the worker.

After a session publishes successfully, the retained audio path is the published recording. The `raw` folder and redundant bulky `processing` cache are best-effort cleanup targets on successful publish and on later startup maintenance once the matching published recording exists; published recordings, transcripts, repair snapshots, and manifest metadata remain the durable outputs.

`manifest.json` is the durable source of truth for:

- session ID
- platform
- canonical meeting title
- optional project name
- optional curated key-attendee list
- optional detected audio source summary
- attendee list
- detection evidence
- raw chunk paths
- loopback capture segments, including endpoint identity and ordered chunk ownership across hot-swapped loopback recorders
- capture timeline entries that explain endpoint choices, swaps, fallback decisions, and stop-time capture state
- microphone chunk paths
- processing overrides
- whether speaker labeling should be skipped for a repaired or recovered processing run
- processing metadata such as the transcription model file name used, whether speaker labels were present, speaker catalog entries, raw diarization turns, and local-only speaker voice samples used for later user-confirmed name learning
- summarization state, including skipped, running, succeeded, failed, provider used, model used, generated timestamp, transcript fingerprint, and safe diagnostic text
- processing state
- error summary

The Meetings tab uses the shared filename stem to reconnect published output files back to their work manifests when those manifests still exist.
Catalog refresh, cleanup analysis, and external audio import skip offline or reparse-point audio files, so OneDrive Files On-Demand placeholders are not hydrated just because the Meetings tab lists or analyzes older recordings.
If a stale imported-source reprocessing manifest survives for a published stem, the catalog prefers the artifact-backed published row over that queued manifest state so the meeting remains openable and truthful in the UI.
If the original work manifest is missing but the published audio file still exists, the app can synthesize a new queued manifest in the work folder to support transcript regeneration.

Published transcript JSON sidecars now also persist attendee, project, key-attendee, detected-audio-source, processing metadata, speaker-name provenance, and summary metadata/content when available so meeting rows can still recover those details even when the original work manifest is gone. Voice-profile embeddings stay out of published transcript JSON and remain in the local work manifest/profile store boundary. Profile rejection and undo feedback is stored as scoped correction data in the local profile store, not as global profile deletion or disablement.

When Outlook appointment matching succeeds, the queue can stamp calendar attendee metadata into the manifest before publish so both the raw attendee list and the curated key-attendee list survive into the published transcript JSON sidecar as durable fallbacks without slowing down the foreground stop action.

The Meetings workspace can also perform a best-effort Outlook attendee backfill pass for listed meetings that still lack attendee metadata but retain enough timing and platform context to match a calendar item. That merged attendee metadata is persisted back into the meeting manifest and transcript sidecars when possible, but only when the matched calendar item still looks like the same meeting by title or attendee identity so unrelated overlapping appointments do not pollute a published meeting row.
That backfill now runs recent-first in background batches, keeps the list visible while enrichment is in flight, and stores a managed-data no-match cache under `%LOCALAPPDATA%\MeetingRecorder\cache` so unchanged historical misses are not retried on every Meetings refresh.

During active Teams recordings, the app can also attempt best-effort live attendee capture through Windows UI Automation. That capture runs off the UI thread on a bounded polling cadence, merges discovered names into the active manifest, and soft-fails without affecting recording stability when Teams does not expose a usable roster tree.

Outlook attendee backfill and live Teams roster capture are both gated by a default-on config flag so users can disable attendee enrichment without turning off calendar title fallback.
The Outlook calendar provider now keeps a per-day in-memory appointment cache so repeated live title fallback checks and same-day attendee backfill reads can reuse one Outlook calendar snapshot instead of repeatedly reopening COM automation work. The live Outlook read now restricts the `Items` view to the requested time window and cancels timed-out STA background reads instead of letting them continue after the caller has already backed off. If Outlook throws during one of those reads, the provider enters a temporary backoff window and soft-fails subsequent requests until that backoff expires.

## 6.1 Meeting Cleanup Recommendation System

The current Meetings architecture now has a recommendation-driven maintenance layer.

Core pieces:

- `MeetingCleanupRecommendationEngine`
  - inspects published meetings using artifact presence, manifest evidence, detection history, transcript state, and narrow audio-identity checks
- `MeetingCleanupExecutionService`
  - executes archive, merge, rename, and other recommended cleanup actions
- `PublishedMeetingRepairService`
- runs a versioned one-time repair pass on startup against the durable app-data root under `%LOCALAPPDATA%\MeetingRecorder`, loads preserved work manifests for stronger continuity evidence, mutates published artifacts archive-first, can collapse longer same-title split chains in one execution instead of only healing isolated adjacent pairs, can also auto-merge a short-gap exact-title split when those manifests still agree on the same specific meeting identity, republish repairable historical microphone sessions from preserved raw chunks under `work`, and writes an `echo-repair-report.txt` summary alongside archived backup WAVs under the versioned archive directory
- persistent dismissed recommendation state in `AppConfig`
  - keeps the system unobtrusive until the underlying meeting changes enough to produce a new fingerprint

Recommendation classes currently target:

- high-confidence false starts such as generic short Teams shell meetings
- editor-generated false meetings
- transcript-only orphan rows
- duplicate publishes
- likely split meeting pairs
- generic titles with a stronger local suggestion
- missing transcript cases that can be retried safely
- missing speaker labels when the diarization sidecar is already ready and the published meeting can be reprocessed deterministically
- suspicious existing speaker labels when the published JSON sidecar has an unusually large, fragment-heavy speaker distribution and the meeting can be re-labeled from its existing transcript text
- strong split candidates derived from manifest title-history evidence

The recommendation layer is intentionally separate from meeting publish status. `Status` continues to describe artifact state, while recommendation badges and the recommendation block describe cleanup suggestions.

Historical behavior:

- the old silent repair path has been replaced with a one-time historical review prompt in the Meetings experience
- users can review suggestions first or apply only the high-confidence safe fixes
- safe fixes are never permanent deletes; archive-style cleanup moves artifacts into the Meetings archive
- current archive-style flows use a single `Documents\Meetings\Archive` root, and any older parallel legacy archive roots are treated as migration inputs rather than ongoing destinations
- archive-style flows mark archived artifacts with the Windows unpinned file attribute after writing them, allowing OneDrive Files On-Demand to dehydrate large recovery WAVs while preserving cloud recovery
- startup maintenance prunes generated repair backup folders after 14 days for `published-meeting-repair-v*` and timestamped `*-echo-repair-*` archives, while leaving manual archive folders user-managed
- permanent delete is a separate manual Meetings-tab context-menu action guarded by typed `DELETE` confirmation and implemented outside the cleanup recommendation pipeline

## 7. Audio Pipeline

### Capture

The app records:

- system output via Windows loopback capture
- optional microphone input via a separate capture path

When Windows routes meeting playback to a communications-only device such as a headset, the loopback recorder now prefers that active communications render endpoint instead of assuming the multimedia default speaker path.
When microphone capture is enabled, the recorder now also binds to an explicit default Windows capture endpoint instead of a one-time generic mapper. During an active recording, the coordinator reevaluates the preferred default microphone endpoint and can hot-swap to a new headset, dock, or communications microphone by closing the current mic segment, starting a new mic segment on the replacement device, and keeping both segments in the same session manifest.
During an active recording, the coordinator now reevaluates the preferred loopback endpoint on each detection cycle. A stronger alternate endpoint must generally win for two consecutive cycles before the app swaps, unless the current loopback endpoint has gone inactive and the alternate endpoint has stronger meeting-session evidence. Successful swaps start a new loopback recorder first, preserve the prior segment and chunk list, then retire the old recorder so the session can continue without losing already-captured audio. Failed swaps do not stop the session; they are recorded into the capture timeline and the current recorder stays active.
If a live loopback or microphone recorder stops unexpectedly because Windows invalidates the underlying device, the coordinator treats that as a required recovery instead of a normal no-op. The dead segment is closed at the recorder's actual stop time, the best current endpoint is validated, and the replacement recorder is started only after the previous client has been fully stopped so overlapping capture clients do not keep audio devices wedged in a broken state.

Because capture is based on the Windows audio stack rather than a product-specific conferencing SDK, manual recording works for any meeting app whose audio is present on the normal Windows render path. The current platform-specific logic is only in auto-detection and platform labeling, not in the audio capture pipeline itself.

### Chunking

Audio is written as rolling WAV chunks during recording to reduce data loss from crashes or abrupt shutdowns.
Each loopback endpoint activation now owns its own loopback segment prefix, so hot-swapped loopback recorders do not collide on chunk filenames and the final merge can preserve the original capture order across multiple loopback devices.

### Final merge

The worker merges loopback chunks into a session-level track.
If microphone chunks exist, the worker:

- merges microphone chunks
- resamples and channel-matches the microphone stream to the loopback format
- mixes both into one final WAV

This final WAV is the canonical audio artifact used for publishing and transcription.

## 8. Meeting Naming and Artifact Identity

Each meeting has one canonical title stored in the manifest. The filename stem is derived from:

- start timestamp
- platform token
- slugified canonical title

Stem format:

- `YYYY-MM-DD_HHMMSS_<platform>_<session-slug>`

Important consequences:

- the Home title editor changes the future publish stem
- published rename updates all sibling artifacts together
- when possible, published rename also updates the underlying work manifest title
- retry relies on the stem to locate the correct manifest
- when persisted title metadata exists, the Meetings workspace preserves the original capitalization verbatim instead of re-humanizing the slug

## 9. Publish Contract

A successful transcript publish produces:

- `<stem>.wav`
- `<stem>.md`
- `<stem>.json`
- `<stem>.ready`

Publish ordering:

1. write temporary files in the destination folders
2. rename the final `.wav`, `.md`, and `.json` into place
3. create `.ready` last

Summaries are supplemental to this publish contract. When summary generation is enabled and succeeds, the transcript Markdown and JSON outputs should include the summary content and provider metadata. When summary generation is disabled, unconfigured, or fails, the app still publishes transcript artifacts and creates `.ready` as long as transcription succeeded.

If transcription fails:

- the final `.wav` is still published
- transcript artifacts are not published
- `.ready` is not created
- the manifest remains retryable

## 9.1 Summary Provider Architecture

Meeting summaries use a provider abstraction separate from the WPF detail window and separate from transcript rendering. The processing worker owns automatic generation for newly processed meetings, and the app owns manual one-meeting generation or retry for already-published transcripts.

Provider order is driven by Settings:

- `LocalThenOpenAi` tries ModelProxy first and OpenAI second only when an OpenAI API key is configured.
- `LocalOnly` tries ModelProxy and never sends transcript content to OpenAI.
- `OpenAiOnly` skips ModelProxy and uses OpenAI only when the user has configured an API key.

ModelProxy is treated as a local HTTP dependency, not an imported library or bundled service. Meeting Recorder now uses the portable Responses contract for transcript enrichment, keeps the provider abstraction isolated so a hosted OpenAI path can evolve independently, sends only the transcript and minimal meeting metadata needed for a summary, treats `GET /v1/models` as an OpenAI-shaped model list only, defaults to `gpt-5.4-mini` unless Settings names another model, and sends no-search summary and validation requests with `X-ModelProxy-Backend: app-server` and `X-ModelProxy-Web-Search: false`. Local-only summary requests also send `X-ModelProxy-Cloud: deny`, which can surface a structured `config_error` until the local backend is proved ready. Real ModelProxy transcript summary requests use an effective minimum timeout of 240 seconds because long no-search local requests can legitimately run longer than the lightweight synthetic validation path. The client reads ModelProxy routing headers (`X-ModelProxy-Request-Id`, requested/effective backend, web-search backend, app-server search support, and fallback reason), exposes them as safe metadata, and persists them with summary provider metadata when present. Structured `backend_busy` responses are retried with short backoff and reported as temporary saturation if retries are exhausted, not as generic endpoint reachability failures. Structured `cli_timeout` web-search failures advise narrowing the query or retrying without web search, while capability failures are treated as request-shape or feature-support problems. If a future ModelProxy request intentionally enables web search, the client tolerates CLI fallback and extends too-short web-search timeouts above ModelProxy's 45-second CLI-search bound. The streaming parser ignores SSE comment lines that start with `:` so keepalive/progress metadata cannot become assistant text, and terminal `event: error` frames or typed Responses stream errors are parsed like non-streaming structured ModelProxy errors instead of being classified as endpoint connectivity failures. Forced app-server web-search `400` responses are treated as a capability message with a retry-without-web-search path. Audio remains local-first: remote audio stays parked until `GET /v1/models` advertises `gpt-4o-transcribe` or `gpt-4o-transcribe-diarize`, and local Whisper plus local diarization remain the primary fallback on `audio_disabled`, `unsupported_model`, `backend_unavailable`, `backend_busy`, `timeout`, `quota`, `config_error`, or protocol mismatch.

Hosted OpenAI fallback is opt-in because transcript text can leave the machine. API keys are entered in Settings, stored outside plaintext app config, never written to logs or transcript artifacts, and never echoed back after save.

The summary schema is app-owned. A successful summary contains an overview, key points, decisions, action items, risks or open questions, provider/model metadata, generated timestamp, and transcript fingerprint. Long transcripts are summarized through deterministic chunking followed by a final combine pass. The processing worker persists matching successful summaries to `summary.snapshot.json` so repaired processing attempts can reuse summary content without another provider call.

The WPF meeting detail window reads summary status and content from the published transcript JSON sidecar. It renders generated summaries as read-only sections and maps disabled, unconfigured, unavailable, failed, and in-progress states into non-error UI status. Manual generate/retry runs in the app process against the existing structured transcript JSON, updates the JSON sidecar and Markdown summary section on success, updates the manifest and `processing\summary.snapshot.json` when the original work manifest is still present, and never re-runs transcription or speaker labeling. Markdown-only transcripts remain displayable but are not eligible for manual summary generation because they do not preserve the structured sidecar schema.

## 10. Model Provisioning Architecture

The app now uses a shared Whisper model service in `MeetingRecorder.Core`.

### Shared responsibilities

- inspect a configured model path
- detect `Missing`, `Invalid`, and `Valid` model states
- reject obviously invalid tiny files
- download the Whisper base model to a temp path and validate it before replacing the configured target
- import a user-provided `.bin` file and validate it before use

### Why the service is shared

The same model rules are used by:

- the Settings-hosted `Setup` surface in the desktop app
- the transcription worker during actual processing

This avoids drift between what the UI says is valid and what the worker will actually accept.

### Current behavior

- if the worker sees a valid model, it uses it immediately
- if the model is missing, it may attempt a first-run download
- if the model is invalid, it fails clearly and preserves the session for retry
- if the configured model path is unusable and another valid managed model exists, the app can switch to that fallback model
- the UI provides a friendlier path to fix the model and retry processing

## 11. Retry Flow

Retry is intentionally simple and local.

### Preconditions

Transcript regeneration is available when:

- the Meetings row can be mapped back to a work manifest that can be retried, or
- the app can synthesize a new work manifest from an existing published audio file

### Flow

1. User selects a meeting in the Meetings tab.
2. User clicks `Re-Generate Transcript`.
3. The app either reuses the existing manifest or synthesizes a new queued manifest from the published audio file.
4. The app launches the worker against that manifest.
5. If the model is valid, transcript artifacts are generated and published.

Current transcript regeneration is single-session only. There is no bulk retry manager yet.

## 12. Power Automate Handoff

The architecture keeps automation file-based.

Expected watcher behavior:

- watch `*.ready` in the transcripts output folder
- resolve sibling files with the same stem

Expected sibling artifacts:

- `<stem>.md`
- `<stem>.json`
- `<stem>.wav`

`.ready` is the only completion signal the app guarantees for successful transcript output.

## 13. Portability and Storage Modes

### GitHub-backed bootstrap install

The release flow can publish stable `Install-LatestFromGitHub.cmd` and `Install-LatestFromGitHub.ps1` assets.

That bootstrap path:

- can consume a colocated packaged `MeetingRecorder-*.zip` when the bootstrap scripts ship beside a locally built installer bundle
- downloads the latest versioned app ZIP from GitHub Releases
- extracts it to a temporary folder
- runs `AppPlatform.Deployment.Cli` from the downloaded bundle
- expects the WPF shell itself to be present as a loose apphost layout with `MeetingRecorder.App.exe`, `MeetingRecorder.App.dll`, `.deps.json`, `.runtimeconfig.json`, and `bundle-layout.json`
- resolves in-app update handoff back to the installed app root by preferring `Environment.ProcessPath` over `AppContext.BaseDirectory`, so helpers stay anchored in `%USERPROFILE%\Documents\MeetingRecorder`
- preserves installed stable apphosts during v2 in-app updates and replaces only mutable DLLs, scripts, assets, and manifests
- requires a one-time MSI or bootstrapper reset for legacy single-file installs that do not have the v2 layout marker
- only clears a same-version pending update when the pending package metadata matches the installed release identity, so a rebuilt release with the same display version still goes through a real install attempt when explicitly launched
- only auto-installs or auto-retries pending updates when the release is newer by semantic version, so republished same-version builds cannot create a self-update loop after an installer relaunch
- validates `bundle-integrity.json` before the managed install root is changed
- persists install provenance under `%LOCALAPPDATA%\MeetingRecorder\install-provenance.json`, including the last installed-at timestamp plus any trusted installed package published-at and asset-size identity used by in-app update comparison, and both MSI post-install provisioning plus app startup now repair a missing provenance file with local install facts so older or partially migrated installs can recover gracefully; if package metadata is still unavailable after that repair, the first successful `UpToDate` GitHub check backfills the installed package publish timestamp and asset size into the same provenance file
- preserves the existing install `data` folder on update installs instead of reimplementing install logic in PowerShell
- promotes staged app files into the managed install root in place during updates instead of renaming the entire `Documents\MeetingRecorder` tree first
- writes a diagnostic log under `%TEMP%\MeetingRecorderInstaller`
- suppresses raw PowerShell transfer progress noise in the user-facing bootstrap scripts
- pauses on error for user-facing console helpers so users can review the failure before the window closes
- avoids cross-process inspection and force-close behavior in installer flows; cooperative shutdown plus normal file-replacement retries are the intended user-safe path
- current releases no longer ship the deprecated thin EXE launcher; MSI plus the script bootstrap assets are the supported release paths

Deprecated thin-launcher responsibilities were intentionally limited to:

- prefer colocated bootstrap assets and a sibling `MeetingRecorder-*.zip` package when they exist beside the launcher
- otherwise resolve the latest release asset set and download `Install-LatestFromGitHub.cmd` plus `Install-LatestFromGitHub.ps1`
- write a small handoff diagnostic log
- launch the CMD bootstrapper with forwarded arguments
- stop there

That deprecated path no longer:

- extracts ZIPs
- copies app files into `Documents\MeetingRecorder`
- mutates the managed install tree directly
- launches the app after install
- creates shortcuts itself

### Per-user MSI install

The WiX package is now authored as a per-user MSI.

That MSI path:

- installs the binaries under `%USERPROFILE%\Documents\MeetingRecorder`
- avoids `Program Files` and per-machine scope
- adds user-scope `.lnk` Start Menu and Desktop shortcuts that target the managed launcher in `Documents\MeetingRecorder`
- keeps writable runtime data outside the installed binaries
- downloads the selected Standard or Higher Accuracy transcription and speaker-labeling assets into `%LOCALAPPDATA%\MeetingRecorder\models`
- shows a first-install-only model-options dialog so the user can keep `Standard` or also request optional `Higher Accuracy` downloads for transcription and speaker labeling
- invokes the installed `AppPlatform.Deployment.Cli provision-models` step after `InstallFinalize` so provisioning and later update repair share one model-management path without depending on pre-commit file visibility
- keeps already-extracted in-place update bundles as immutable repair sources, copies them into a separate staging workspace, validates staging, and only then promotes files into `%USERPROFILE%\Documents\MeetingRecorder`
- treats `MeetingRecorder-v<version>-win-x64.zip` as the only valid in-app update package shape; model binaries, diarization bundles, MSI assets, bootstrap scripts, missing pending files, size mismatches, and corrupt ZIPs are rejected before update apply asks the app process to exit
- preserves user-selected speaker-labeling run mode across in-app updates by stamping the one-time legacy safety migration whenever Settings or Setup saves that preference
- keeps the MSI custom-action handoff on compact CLI aliases and makes the deployment CLI parse those advertised aliases correctly, including `highAccuracy` for Higher Accuracy setup options, so install-time provisioning does not fail on an option-name mismatch or a custom-action target overflow
- avoids binding the Start Menu shortcut icon to the Windows Installer `ProductIcon` cache, so taskbar pins made from that shortcut inherit the installed executable icon instead of a per-MSI cached icon path
- only repairs an existing pinned taskbar shortcut when it is already tied to the current install root or its target is missing, and skips real taskbar repair for temporary install roots used by tests
- keeps the install successful when optional Higher Accuracy downloads fail, records a one-time retry-needed result, and leaves Standard active
- repairs a missing or malformed inherited `windir` process environment variable before WPF window creation, preventing installer/update relaunches from failing inside WPF font initialization
- treats dispatcher UI exceptions as fatal after logging and only acknowledges second-launch activation after a visible main window is restored, so a hidden primary instance cannot strand the app behind the single-instance mutex
- enables verbose Windows Installer logging by default for direct MSI troubleshooting
- replaces raw Windows Installer progress field templates with plain-language status messages for application file copy, registration, and shortcut creation
- schedules `ARPINSTALLLOCATION` through WiX property-setting instead of a raw property literal reference
- suppresses `ICE91` only because the package is intentionally per-user-only and targets user-profile directories
- remains an initial-install convenience channel rather than the long-term version authority after later CLI-driven updates

### Portable mode

If `portable.mode` exists beside the app, runtime data lives under:

- `<AppFolder>\data\config`
- `<AppFolder>\data\logs`
- `<AppFolder>\data\audio`
- `<AppFolder>\data\transcripts`
- `<AppFolder>\data\work`
- `<AppFolder>\data\models`

### Non-portable mode

Without the portable marker, the same structure lives under `%LOCALAPPDATA%\MeetingRecorder`.

This keeps the deployment flexible for local policies that block installer-style locations.

## 14.1 Future Repo Cut Line

The intended future extraction line is:

- move `AppPlatform.Abstractions`
- move `AppPlatform.Configuration`
- move `AppPlatform.Deployment`
- move `AppPlatform.Deployment.Cli`
- move `AppPlatform.Deployment.WpfHost`
- move `AppPlatform.Deployment.Wix`
- move `AppPlatform.Shell.Wpf`
- move `MeetingRecorder.Product` or replace it with a package in the app repo after the platform repo is published

Meeting Recorder would continue to own:

- branding and product metadata
- app-specific shell content for `Home`, `Meetings`, and Settings-hosted setup
- app-specific config schema and defaults
- meeting detection, processing, publishing, and cleanup workflows
- product-specific release guidance and release URLs

## 14. Security and Privacy Posture

- audio and transcript processing are local by default
- the app does not require cloud APIs for the core workflow
- no admin rights are required for normal operation
- model import is supported because downloads may be blocked
- browser extension support is optional and not required for the current workflow
- update downloads are file-based and do not require in-place self-patching while the app is running

## 15. Current Gaps and Planned Hardening

The codebase intentionally does not yet provide:

- transcript editing or review tools
- bulk retry
- rich per-session troubleshooting views inside the app
- automatic cleanup policies for old work folders
- shared or organization-grade speaker identity mapping beyond local user-taught voice profiles

These remain future enhancements, not hidden assumptions in the current design.
