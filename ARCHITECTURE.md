# Meeting Recorder Architecture

This document describes the current implementation architecture for the Meeting Recorder app as it exists in the repository today. Product behavior and scope live in [PRODUCT_REQUIREMENTS.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\PRODUCT_REQUIREMENTS.md). Deployment and first-run guidance live in [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md).

## 1. Architectural Intent

The architecture is optimized for a corporate-restricted Windows laptop:

- standard user permissions
- local-first recording and processing
- portable deployment support
- no Windows service requirement
- resilient batch transcription after the meeting
- graceful behavior when model download, diarization, or browser integration are unavailable

## 2. Runtime Shape

The app is split into three runtime projects:

- `MeetingRecorder.App`
  - WPF desktop UI
  - recording orchestration
  - local meeting detection for Teams and Google Meet
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

## 3. Enterprise Constraints

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

The current WPF surface is split across four tabs.

### Dashboard

Responsibilities:

- show current app and detection status
- expose manual start and stop controls
- clarify that manual recording works beyond Teams and Google Meet, while auto-detection is narrower
- allow editing the current meeting title while recording
- show the publish-stem preview for the active title
- show a model-health banner when the current Whisper model is missing or invalid
- show an update banner when the configured release feed reports a newer version
- show recent activity
- link to output and config paths

### Meetings

Responsibilities:

- list published meetings from output artifacts
- surface title, time, duration, platform, status, audio file, and transcript file
- rename published meetings and keep stems aligned
- expose transcript re-generation for failed sessions and audio-only sessions

### Models

Responsibilities:

- show the configured Whisper model path
- validate the current model file
- show `Missing`, `Invalid`, or `Ready` status
- list discovered local model files
- allow choosing which local model is active
- allow `Download Base Model`
- allow `Import Existing File`
- provide browser links for manual model download
- provide quick access to the model folder

### Config

Responsibilities:

- edit the app configuration file
- hot reload supported settings into the running app
- control launch-on-login registration
- control update-check behavior and update feed URL
- show which settings apply immediately versus only on the next recording or processing run

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
4. The UI launches the worker with the manifest path and config path.
5. The worker loads the manifest, merges audio, and publishes the final WAV.
6. If transcription succeeds, transcript artifacts are rendered and published.
7. If transcription fails, the manifest becomes `Failed` and the final WAV remains available.
8. A retry action can move a failed manifest back to `Queued` and relaunch the worker.

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
- detection evidence
- raw chunk paths
- microphone chunk paths
- processing state
- error summary

The Meetings tab uses the shared filename stem to reconnect published output files back to their work manifests when those manifests still exist.
If the original work manifest is missing but the published audio file still exists, the app can synthesize a new queued manifest in the work folder to support transcript regeneration.

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

- the Dashboard title editor changes the future publish stem
- published rename updates all sibling artifacts together
- when possible, published rename also updates the underlying work manifest title
- retry relies on the stem to locate the correct manifest

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

- the `Models` tab in the desktop app
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

- downloads the latest versioned app ZIP from GitHub Releases
- extracts it to a temporary folder
- runs the bundled per-user installer
- preserves the existing `data` folder and sticky config on update installs

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

This keeps the deployment flexible for corporate policies that block installer-style locations.

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
