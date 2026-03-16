# Meeting Recorder Product Requirements

This document captures the current product scope and intended behavior of the Meeting Recorder app as implemented in the repository today. Detailed component boundaries and runtime flow live in [ARCHITECTURE.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\ARCHITECTURE.md). Operational setup instructions live in [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md).

## 1. Product Summary

Meeting Recorder is a Windows desktop application that:

- detects likely Microsoft Teams and Google Meet sessions using local OS-visible heuristics
- records meeting audio locally
- optionally records microphone audio and mixes it into the final WAV
- transcribes audio locally after the meeting
- optionally applies speaker labels using a local diarization sidecar
- writes final artifacts into stable `Audio` and `Transcripts` folders
- creates a `.ready` marker so Power Automate or another watcher can react to completed transcript output

The product is intentionally shaped for corporate-restricted laptops:

- standard user permissions
- no required Windows service
- no required browser extension
- no required OneDrive integration
- no required GPU
- no cloud transcription dependency

## 2. Current User Experience

The current app exposes four main tabs:

- `Dashboard`
  - live status
  - manual start and stop
  - current meeting title editor
  - recent activity log
  - links to output and config paths
- `Meetings`
  - published meeting list
  - rename published meeting
  - retry failed processing when a matching work-session manifest still exists
- `Models`
  - Whisper model status
  - Whisper model download
  - Whisper model import from an existing file
  - open model path and folder
- `Config`
  - editable settings
  - hot reload for supported values
  - per-setting notes about when changes apply

## 3. Core Goals

### Primary goals

- Make local meeting capture reliable on a restricted Windows laptop.
- Keep audio and transcript processing local by default.
- Produce one final audio file per meeting.
- Produce machine-readable and human-readable transcript outputs when a valid Whisper model is available.
- Make transcript completion easy for Power Automate to detect.

### Secondary goals

- Reduce friction around Whisper model setup.
- Preserve enough state to retry failed post-processing without re-recording.
- Keep meeting naming, filenames, and retry state aligned.

## 4. Environment Constraints

- The app must run on Windows 10 or Windows 11.
- The user may not have administrator rights.
- Startup apps, services, browser extensions, or native messaging may be blocked.
- Public model downloads may be blocked or replaced with proxy or error pages.
- The laptop may be CPU-only.
- The app must be usable from user-writable folders only.

## 5. In-Scope Product Surface

### Implemented now

- Manual recording controls
- Heuristic auto-detection for Teams desktop and Google Meet windows
- Auto-stop after a configurable timeout
- Rolling WAV chunk capture
- Optional microphone capture
- Mixed final WAV output
- Separate worker-based offline transcription
- Optional diarization sidecar
- Shared `Audio` and `Transcripts` output folders
- `.md`, `.json`, and `.ready` publish contract
- Hot-reloaded config
- Whisper model status, download, and import UI
- Published meeting rename
- Retry for failed processing
- Portable deployment

### Explicitly out of scope

- Live transcription during the meeting
- Transcript review and correction UI
- Named speaker identification
- Calendar integration
- Required browser extension integration
- Required cloud services
- Bulk retry management
- Transcript editing
- In-app summarization

## 6. Functional Requirements

## 6.1 Meeting Detection

- The app must detect likely Teams desktop meetings from process names, window titles, and meeting-like keywords.
- The app must detect likely Google Meet sessions from browser window titles and other OS-visible hints.
- The app must require both meeting-like signals and active output audio before auto-starting recording.
- The app must suppress obvious Teams chat or navigation windows.
- The app must expose manual start and stop even when detection is wrong or unavailable.
- The app must log detection candidates, confidence, and reasons.
- The app must stop auto-started sessions after the configured timeout if qualifying meeting signals disappear.

## 6.2 Recording

- The app must capture system output audio locally using Windows-supported capture APIs.
- The app must optionally capture microphone audio on a separate input path.
- The app must write rolling chunk files during recording.
- The app must preserve chunk files in the session work folder for diagnosis and retry.
- The app must merge loopback and microphone recordings into one final WAV for downstream transcription and archival.
- The app must remain usable if microphone capture is disabled.

## 6.3 Naming and Session Identity

- Each session must have one canonical human title stored in the manifest.
- The current meeting title entered in the Dashboard must be applied automatically when recording stops.
- Published audio and transcript filenames must be derived from the canonical session title using the shared filename stem format.
- Renaming a published meeting must rename all matching published artifacts together.
- When a matching work-session manifest exists, the rename must update that manifest title too.

## 6.4 Work Folders and Recovery

- Each session must have a dedicated work folder containing raw chunks, processing artifacts, logs, and `manifest.json`.
- Failed sessions must retain enough local state for retry.
- The app must be able to locate the manifest that corresponds to a published meeting row when the work folder still exists.
- Retry must reset the manifest to `Queued` and launch the worker again.

## 6.5 Local Transcription

- Transcription must run after recording is complete.
- Transcription must use a local Whisper model file.
- The worker may attempt a first-run download when the model file is missing.
- The app must validate the configured model file and reject obviously invalid tiny files.
- The user must be able to recover from missing or invalid models through the Models tab.
- Transcription must remain CPU-capable and not require cloud APIs.

## 6.6 Model Management

- The app must show the configured Whisper model path.
- The app must show whether the model is `Missing`, `Invalid`, or `Ready`.
- The app must allow a user to attempt downloading the Whisper base model.
- The app must allow a user to import an existing valid `ggml-base.bin`.
- Download and import flows must validate the model before replacing the configured file.
- The app must not silently keep a tiny invalid proxy or error file as if it were a real model.

## 6.7 Diarization

- Diarization is optional and best-effort.
- When available, diarization must label transcript segments as `Speaker 1`, `Speaker 2`, and so on.
- Diarization failure must not block transcript output.
- Missing diarization assets must not block transcript output.

## 6.8 Publish Contract

- Final published audio must use the shared filename stem:
  - `YYYY-MM-DD_HHMMSS_<platform>_<session-slug>`
- When transcription succeeds, the app must publish:
  - `<stem>.wav`
  - `<stem>.md`
  - `<stem>.json`
  - `<stem>.ready`
- `.ready` must be created last.
- Power Automate should watch `.ready`, not raw transcript creation.
- If transcription fails, the app must still publish the final audio file.
- If transcription fails, transcript artifacts and `.ready` must not be created.

## 6.9 Meetings Library

- The Meetings tab must list published meetings derived from output files.
- The meeting list should prefer exact titles from the work manifest when available.
- The meeting list must show enough status to distinguish complete sessions from failed or incomplete ones.
- The app must expose `Retry Processing` for sessions that have a retryable failed manifest.
- Retry is not required for sessions whose work folders were removed.

## 6.10 Configuration

The app must allow configuration of:

- audio output folder
- transcript output folder
- work folder
- model cache folder
- Whisper model path
- diarization asset path
- microphone capture on or off
- auto-detection on or off
- auto-detect audio threshold
- meeting stop timeout

Configuration requirements:

- config must be stored in a user-writable location
- supported settings must hot reload without restart
- the UI must indicate whether a setting applies immediately, on the next recording, or on the next processing run

## 6.11 Logging and Troubleshooting

- The app must maintain an activity log in the UI.
- The app must write per-session processing logs under the session work folder.
- Logs must include detection decisions, recording start and stop, worker launch, and model errors.
- The app must make it clear when transcript generation failed because the Whisper model is missing or invalid.

## 7. Non-Goals and Deferred Work

- Transcript review or editing UI
- Manual speaker correction
- Bulk retry
- Calendar-aware meeting naming
- Required browser-extension handshake
- Automatic summarization inside the app
- Cross-device sync

## 8. Acceptance Criteria for the Current MVP

The current MVP is considered successful when:

- a user can launch the app without admin rights
- the app can record a meeting locally
- the app can publish one final WAV per session
- a valid local Whisper model can be installed through the app or imported into place
- once a valid model exists, the app can generate `.md`, `.json`, and `.ready` outputs
- a failed transcription caused by a missing or invalid model can be retried from the Meetings tab after model setup is fixed
- Power Automate can watch `.ready` and find sibling artifacts by stem

## 9. Related Documents

- [README.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\README.md)
- [ARCHITECTURE.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\ARCHITECTURE.md)
- [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md)
