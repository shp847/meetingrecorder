# Meeting Recorder Architecture

This document describes the current implementation architecture for the Meeting Recorder app as it exists in the repository today. Product behavior and scope live in [PRODUCT_REQUIREMENTS.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\PRODUCT_REQUIREMENTS.md). Deployment and first-run guidance live in [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md).

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
  - also provides the Technical Studio brushes, lines, typography, and action styles reused by the EXE installer shell
- `MeetingRecorder.Product`
  - product adapter that owns the manifest, shell registrations, about/support content, and default install/data layout

For the shipped MSI flow, the managed install root in `MeetingRecorder.Product` and the bundled `MeetingRecorder.product.json` are expected to stay aligned with `%USERPROFILE%\Documents\MeetingRecorder`. Writable runtime data remains outside the install root under `%LOCALAPPDATA%\MeetingRecorder`.
The MSI finish-launch path is intentionally not a raw second launch of `MeetingRecorder.App.exe`; it uses an installed relaunch wrapper plus a short-lived marker under `%LOCALAPPDATA%\MeetingRecorder` so the app can distinguish installer relaunches from normal user activations and coordinate a clean close-and-reopen of an idle existing instance.

### Meeting Recorder-specific runtime projects

- `MeetingRecorder.App`
  - WPF desktop UI
  - recording orchestration
  - local meeting detection for Teams and Google Meet, including Chromium browser tab-title inspection for visible Google Meet windows and Windows render-session audio attribution for process/window/tab tie-breaking
  - guarded Google Meet browser-audio attribution that refuses generic Chromium playback when the render-session metadata does not point back to Meet
  - in-place reclassification of an auto-started live session from stale Google Meet browser evidence to a stronger Teams meeting window, so the recording is preserved as one session
  - config editing and hot reload
  - meetings library, rename, and retry actions
  - Whisper model setup UI
- `MeetingRecorder.Core`
  - shared domain models
  - path and filename rules
  - work-manifest persistence
  - output catalog logic
  - WAV merge and mix logic
  - transcript rendering
  - publish helpers
  - Whisper model inspection, download, and import logic
- `MeetingRecorder.ProcessingWorker`
  - separate background process for post-recording work
  - final audio preparation
  - Whisper transcription
  - optional diarization sidecar integration
  - transcript and ready-marker publishing

The worker is launched as a separate process so transcription or diarization failures do not destabilize the desktop UI.
For self-contained release bundles, `MeetingRecorder.App` is now published as a single-file executable while `AppPlatform.Deployment.Cli`, `MeetingRecorder.ProcessingWorker`, scripts, and `MeetingRecorder.product.json` stay external so bootstrap, install, and update flows can keep invoking those sidecars directly.

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
Auto-stop now follows the same responsiveness rule: the Home console shows a visible countdown for auto-started sessions, flips into an immediate `Auto-stopping` transition when the timeout expires, and pushes attendee enrichment into the background queue so stop finalization no longer waits on Outlook lookups.

### Home

Responsibilities:

- show the current shell status through the header-level status capsule
- keep the editable session metadata visible
- render the live audio graph inside a single recording console
- expose manual start and stop controls
- expose immediate-save quick settings for microphone capture and auto-detection
- clarify that manual recording works beyond Teams and Google Meet, while auto-detection is narrower
- allow editing the current meeting title, client/project, and key attendees while recording
- keep setup, update, and warning actions out of the Home body so the recording deck stays visually simple

### Meetings

Responsibilities:

- list published meetings from output artifacts
- provide a toolbar-driven workspace with search, explicit sorting, and a default grouped view that older configs are migrated into once
- support grouped browsing by week, month, platform, or status without changing the underlying meeting source
- initialize grouped browsing with only the first visible group expanded, keep the rest collapsed, and offer explicit `Expand All` and `Collapse All` controls when grouping is active
- publish the baseline meeting list immediately, then defer cleanup suggestion analysis and attendee enrichment so opening `Meetings` does not block the shell
- surface title, project, local started time, duration, platform, status, and compact audio/transcript actions
- let search match title, project, key attendees, and captured attendee names
- surface cleanup recommendation badges independently from publish status
- host the one-time historical cleanup review banner and ongoing recommendation block
- host the selected-meeting inspector, quick-action launchers, and Meetings-tab context menu so manual maintenance actions reuse the same underlying meeting services
- keep the lower Meetings area focused on compact recommendation review plus the small set of action drafts that still need extra user input
- rename published meetings and keep stems aligned
- expose transcript re-generation for failed sessions and audio-only sessions
- allow project metadata edits for one meeting or a multi-selection without leaving the Meetings workspace

### Settings-hosted setup

Responsibilities:

- provide the first Settings section for transcription and optional speaker labeling readiness
- split setup into dedicated `Transcription` and `Speaker labeling` sections
- stay focused on capability readiness only, while pointing behavior, storage, updates, and troubleshooting back to `Settings`
- keep transcription in one consolidated setup section instead of split recommendation panels
- show the configured Whisper model path and validate the current model file
- show `Missing`, `Invalid`, or `Ready` status
- list downloadable GitHub model assets and discovered local model files
- allow choosing which local model is active
- allow `Download Recommended Model` and `Import Existing File`
- keep speaker labeling optional and separate from core transcription readiness
- use GitHub as the only built-in automatic download source for Whisper models and speaker-labeling assets
- open bundled local setup help first for speaker labeling, then fall back to GitHub guidance if the local guide is unavailable
- show curated alternate public speaker-labeling download locations when configured, or an explicit empty state when none are available
- provide quick access to model and diarization asset folders plus advanced raw-path details

### Settings and Help

Responsibilities:

- edit the app configuration file from the header-level `Settings` dialog
- hot reload supported settings into the running app
- use a dedicated section-button strip instead of a custom clipped tab header template
- group capability readiness under `Setup`, everyday defaults under `General` and `Files`, and release/troubleshooting paths under `Updates` and `Advanced`
- make the distinction from `Setup` explicit in copy: `Setup` is for readiness, while the other Settings sections are for behavior, storage, updates, and troubleshooting
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
   For single-file app installs, worker discovery prefers the installed app directory derived from `Environment.ProcessPath` instead of the transient `.net` extraction directory behind `AppContext.BaseDirectory`.
6. The worker loads the manifest, merges audio, and publishes the final WAV.
7. If transcription succeeds, transcript artifacts are rendered and published.
8. If transcription fails, the manifest becomes `Failed` and the final WAV remains available.
9. A retry action can move a failed manifest back to `Queued` and relaunch the worker.

## 6. Work Folders and Persistence

Each session has a dedicated work folder:

- `<workDir>\<session-id>\manifest.json`
- `<workDir>\<session-id>\raw\`
- `<workDir>\<session-id>\processing\`
- `<workDir>\<session-id>\logs\`

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
- microphone chunk paths
- processing overrides
- processing metadata such as the transcription model file name used and whether speaker labels were present
- processing state
- error summary

The Meetings tab uses the shared filename stem to reconnect published output files back to their work manifests when those manifests still exist.
If the original work manifest is missing but the published audio file still exists, the app can synthesize a new queued manifest in the work folder to support transcript regeneration.

Published transcript JSON sidecars now also persist attendee, project, key-attendee, detected-audio-source, and processing metadata so meeting rows can still recover those details even when the original work manifest is gone.

When Outlook appointment matching succeeds, the queue can stamp calendar attendee metadata into the manifest before publish so both the raw attendee list and the curated key-attendee list survive into the published transcript JSON sidecar as durable fallbacks without slowing down the foreground stop action.

The Meetings workspace can also perform a best-effort Outlook attendee backfill pass for listed meetings that still lack attendee metadata but retain enough timing and platform context to match a calendar item. That merged attendee metadata is persisted back into the meeting manifest and transcript sidecars when possible.
That backfill now runs recent-first in background batches, keeps the list visible while enrichment is in flight, and stores a managed-data no-match cache under `%LOCALAPPDATA%\MeetingRecorder\cache` so unchanged historical misses are not retried on every Meetings refresh.

During active Teams recordings, the app can also attempt best-effort live attendee capture through Windows UI Automation. That capture runs off the UI thread on a bounded polling cadence, merges discovered names into the active manifest, and soft-fails without affecting recording stability when Teams does not expose a usable roster tree.

Outlook attendee backfill and live Teams roster capture are both gated by a default-on config flag so users can disable attendee enrichment without turning off calendar title fallback.

## 6.1 Meeting Cleanup Recommendation System

The current Meetings architecture now has a recommendation-driven maintenance layer.

Core pieces:

- `MeetingCleanupRecommendationEngine`
  - inspects published meetings using artifact presence, manifest evidence, detection history, transcript state, and narrow audio-identity checks
- `MeetingCleanupExecutionService`
  - executes archive, merge, rename, and other recommended cleanup actions
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
- strong split candidates derived from manifest title-history evidence

The recommendation layer is intentionally separate from meeting publish status. `Status` continues to describe artifact state, while recommendation badges and the recommendation block describe cleanup suggestions.

Historical behavior:

- the old silent repair path has been replaced with a one-time historical review prompt in the Meetings experience
- users can review suggestions first or apply only the high-confidence safe fixes
- safe fixes are never permanent deletes; archive-style cleanup moves artifacts into the Meetings archive
- current archive-style flows use a single `Documents\Meetings\Archive` root, and any older parallel legacy archive roots are treated as migration inputs rather than ongoing destinations
- permanent delete is a separate manual Meetings-tab context-menu action guarded by typed `DELETE` confirmation and implemented outside the cleanup recommendation pipeline

## 7. Audio Pipeline

### Capture

The app records:

- system output via Windows loopback capture
- optional microphone input via a separate capture path

Because capture is based on the Windows audio stack rather than a product-specific conferencing SDK, manual recording works for any meeting app whose audio is present on the normal Windows render path. The current platform-specific logic is only in auto-detection and platform labeling, not in the audio capture pipeline itself.

### Chunking

Audio is written as rolling WAV chunks during recording to reduce data loss from crashes or abrupt shutdowns.

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

If transcription fails:

- the final `.wav` is still published
- transcript artifacts are not published
- `.ready` is not created
- the manifest remains retryable

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
- expects the WPF shell itself to be present as a single-file `MeetingRecorder.App.exe` rather than a loose `MeetingRecorder.App.dll/.deps.json/.runtimeconfig.json` trio
- resolves in-app update handoff back to the installed app root by preferring `Environment.ProcessPath` over `AppContext.BaseDirectory`, so a single-file app launch does not look for `AppPlatform.Deployment.Cli.exe` under the transient `.net` extraction directory
- validates `bundle-integrity.json` before the managed install root is changed
- persists install provenance under `%LOCALAPPDATA%\MeetingRecorder\install-provenance.json`
- preserves the existing install `data` folder on update installs instead of reimplementing install logic in PowerShell
- promotes staged app files into the managed install root in place during updates instead of renaming the entire `Documents\MeetingRecorder` tree first
- writes a diagnostic log under `%TEMP%\MeetingRecorderInstaller`
- suppresses raw PowerShell transfer progress noise in the user-facing bootstrap scripts
- pauses on error for user-facing console helpers so users can review the failure before the window closes
- avoids cross-process inspection and force-close behavior in the installer EXE flow; cooperative shutdown plus normal file-replacement retries are the intended user-safe path

### Thin EXE launcher

`MeetingRecorderInstaller.exe` is no longer treated as a peer deployment engine.

Current EXE responsibilities:

- prefer colocated bootstrap assets and a sibling `MeetingRecorder-*.zip` package when they exist beside the EXE
- otherwise resolve the latest release asset set and download `Install-LatestFromGitHub.cmd` plus `Install-LatestFromGitHub.ps1`
- write a small handoff diagnostic log
- launch the CMD bootstrapper with forwarded arguments
- stop there

It no longer:

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
- enables verbose Windows Installer logging by default for direct MSI troubleshooting
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
- production-grade speaker identity mapping

These remain future enhancements, not hidden assumptions in the current design.
