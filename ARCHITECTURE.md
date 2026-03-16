# Meeting Recorder Architecture

## 1. Purpose

This document describes the implementation architecture for a Windows-first meeting recorder that can run on a corporate-restricted laptop. It complements [PRODUCT_REQUIREMENTS.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\PRODUCT_REQUIREMENTS.md), which defines the product and MVP requirements.

The architecture is optimized for:

- standard-user Windows laptops,
- local-first recording and processing,
- resilient batch transcription after meetings,
- optional speaker diarization, and
- file-based handoff to Power Automate.

## 2. Enterprise Constraints

The app must assume the following constraints by default:

- no administrator rights for install or normal use,
- no Windows service installation,
- no dependency on startup tasks or tray-only background behavior,
- possible restrictions on browser extensions or native messaging,
- possible restrictions on model downloads and external internet access,
- CPU-only execution as the baseline,
- local operation without requiring OneDrive or cloud APIs.

## 3. Deployment Model

The app is a per-user Windows desktop application built on `.NET 8` and `WPF`.

### Runtime shape

- `MeetingRecorder.App` is the desktop UI and orchestration process.
- `MeetingRecorder.ProcessingWorker` is a separate worker process invoked by the app for merge, transcription, diarization, and publish work.
- `MeetingRecorder.Core` contains shared contracts, path rules, persistence helpers, state transitions, and common services.

### Deployment characteristics

- install must work in a user-writable location,
- app data must stay under the user profile,
- the app must not require elevation to launch, record, or process,
- model assets are cached locally and reused after provisioning.

## 4. Solution Layout

- `src/MeetingRecorder.App`
  - WPF UI
  - session orchestration
  - manual controls
  - detection engine
  - recording engine
- `src/MeetingRecorder.Core`
  - domain models
  - configuration loading
  - session manifest persistence
  - path and filename rules
  - logging abstractions
  - publish contract helpers
- `src/MeetingRecorder.ProcessingWorker`
  - background processing runner
  - audio merge
  - transcription adapter
  - diarization adapter
  - transcript rendering
  - artifact publishing
- `tests/MeetingRecorder.Core.Tests`
  - unit tests for deterministic domain behavior
- `tests/MeetingRecorder.IntegrationTests`
  - pipeline and publish-contract tests

## 5. Component Responsibilities

## 5.1 App Host

Responsibilities:

- launch the desktop app,
- load configuration,
- display current state,
- allow manual start and stop,
- show consent reminder text,
- manage the active recording session,
- queue completed sessions for worker processing.

The app host must remain usable even if detection, transcription, or diarization are unavailable.

## 5.2 Detection Engine

Responsibilities:

- inspect local Windows-visible signals for Teams and Google Meet,
- assign confidence to candidate meeting sessions,
- suppress duplicate or overlapping detection events,
- emit detection evidence for logging and manifests,
- support a configurable timeout for meeting end.

Detection remains advisory in MVP. Manual start and stop must always be available.

## 5.3 Recording Engine

Responsibilities:

- capture system audio using Windows loopback recording,
- optionally capture microphone audio,
- write rolling chunk files in the current session work folder,
- finalize a recording cleanly on stop or interruption,
- keep raw data recoverable for retry or diagnostics.

The recording engine must not depend on transcription or diarization success.

## 5.4 Session Store

Responsibilities:

- create a unique session ID,
- persist `MeetingSessionManifest` to disk,
- track session state transitions,
- expose enough information for retry on restart,
- preserve failed-session context.

The work folder is the system of record until final publishing succeeds.

## 5.5 Processing Worker

Responsibilities:

- merge audio chunks into one final file,
- provision or locate local models,
- run offline transcription,
- run optional diarization,
- render final transcript artifacts,
- publish files atomically into their final locations.

Worker failures must not crash the UI process.

## 5.6 Publish Service

Responsibilities:

- write final artifacts using temporary filenames,
- rename only after file writes are complete,
- create `.ready` only after all mandatory sibling artifacts exist,
- avoid duplicate publish events,
- leave retryable state behind when publish fails.

## 6. Session Lifecycle

The session lifecycle uses durable states:

- `Idle`
- `Recording`
- `Finalizing`
- `Queued`
- `Processing`
- `Published`
- `Failed`

### Lifecycle rules

1. App enters `Recording` only when a manual or detected start is accepted.
2. Recording writes raw chunks and updates the manifest in place.
3. On stop, the session enters `Finalizing`, then `Queued`.
4. The worker loads queued sessions and moves them to `Processing`.
5. A successful publish moves the session to `Published`.
6. A recoverable failure moves the session to `Failed` while preserving artifacts for retry.

## 7. Configuration Model

Configuration is stored in a user-writable path and loaded at app startup.

### Core configuration fields

- `audioOutputDir`
- `transcriptOutputDir`
- `workDir`
- `modelCacheDir`
- `transcriptionModelPath`
- `diarizationAssetPath`
- `micCaptureEnabled`
- `autoDetectEnabled`
- `meetingStopTimeoutSeconds`

### Configuration principles

- provide sensible defaults,
- never require editing protected locations,
- allow the app to start even if optional AI assets are missing,
- let the user override output and model locations.

## 8. Folder Layout

### Config

- `%LOCALAPPDATA%\MeetingRecorder\config\appsettings.json`

### Working data

- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\raw\`
- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\processing\`
- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\logs\`
- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\manifest.json`

### Model cache

- `%LOCALAPPDATA%\MeetingRecorder\models\asr\`
- `%LOCALAPPDATA%\MeetingRecorder\models\diarization\`

### Final outputs

- `<ConfiguredAudioFolder>\`
- `<ConfiguredTranscriptFolder>\`

## 9. File Publish Contract

Each successful session shares a common filename stem:

- `YYYY-MM-DD_HHMMSS_<platform>_<session-slug>`

### Required published artifacts

- `<stem>.wav`
- `<stem>.md`
- `<stem>.json`
- `<stem>.ready`

### Publish ordering

1. write `.wav`, `.md`, and `.json` using temporary filenames,
2. atomically rename each file into place,
3. verify all required siblings exist,
4. create `.ready` last.

`.ready` is the only supported Power Automate completion trigger in MVP.

## 10. Model Provisioning

The app uses a local-first provisioning model.

### ASR

- primary runtime is `whisper.cpp` through `Whisper.net`,
- first use attempts a model download into the local ASR cache,
- if download fails, the app must support offline model import,
- once present locally, transcription must not require network access.

### Diarization

- diarization assets live in the local diarization cache,
- diarization is optional in MVP,
- missing diarization assets must not block transcript publishing.

## 11. Failure Handling

### Detection failures

- detection failure must not block manual recording.

### Recording failures

- preserve existing chunks,
- attempt orderly finalization where possible,
- mark the session as failed if capture cannot continue.

### Transcription failures

- preserve the work folder,
- mark transcription status as failed,
- do not create `.ready`.

### Diarization failures

- publish transcript artifacts without speaker labels when necessary,
- mark diarization status explicitly,
- still allow `.ready` creation if all required artifacts are present.

### Publish failures

- keep the work folder and manifest,
- leave no partial final files under final filenames,
- do not create `.ready`.

## 12. Security Posture

- no cloud API dependency in core runtime,
- no external telemetry by default,
- no secrets required for MVP,
- no admin privileges required for normal usage,
- no browser extension required for MVP,
- all data stored in user-writable directories,
- recording remains subject to company policy and local law.

## 13. Milestone Map

### Milestone 1

- solution scaffold
- config and session manifests
- structured logging

### Milestone 2

- manual recording
- chunk persistence
- merged final audio

### Milestone 3

- durable processing queue
- restart-safe session recovery

### Milestone 4

- local transcription
- transcript rendering

### Milestone 5

- atomic publish contract
- `.ready` handoff

### Milestone 6

- assisted Teams and Google Meet detection

### Milestone 7

- optional diarization sidecar integration

### Milestone 8

- packaging and restricted-laptop validation
