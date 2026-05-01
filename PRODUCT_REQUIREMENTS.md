# Meeting Recorder Product Requirements

This document captures the current product scope and intended behavior of the Meeting Recorder app as implemented in the repository today. Detailed component boundaries and runtime flow live in [ARCHITECTURE.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\ARCHITECTURE.md). Operational setup instructions live in [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md).

## 1. Product Summary

Meeting Recorder is a Windows desktop application that:

- detects likely Microsoft Teams desktop and Google Meet sessions using local OS-visible heuristics
- records system audio locally and can optionally include microphone audio
- transcribes audio locally after the meeting with local Whisper models
- can optionally apply local speaker labeling through a separate diarization bundle
- publishes stable meeting artifacts as audio, Markdown, JSON, and `.ready` marker files
- provides a meetings workspace for follow-up maintenance such as rename, retry, merge, split, archive, project tagging, and speaker-label edits
- supports both managed per-user installs and portable usage on restricted Windows laptops

Manual recording is intentionally conferencing-app agnostic: if audio is flowing through the normal Windows render device and optional microphone path, the recorder can capture it regardless of whether the call is in Teams, Google Meet, Zoom, Webex, or another meeting tool. Assisted auto-detection is narrower and currently focuses on Teams desktop and Google Meet patterns.

The product is intentionally shaped for corporate-restricted and privacy-sensitive laptops:

- standard user permissions
- no required Windows service
- no required browser extension
- no required cloud transcription dependency
- no required GPU
- no required OneDrive integration
- user-writable install and data paths only

## 2. Current User Experience

The current app uses a task-first shell with two primary destinations plus dedicated secondary windows.

- `Home`
  - live recording state and detection state
  - manual `Start Recording` and `Stop Recording` controls
  - current meeting title editor
  - live audio activity graph during active capture
  - model-health and update banners
  - readiness card with quick links for transcription setup, speaker labeling setup, microphone capture, auto-detection, and meeting-file management
  - `Next Best Action` guidance
  - recent activity log
- `Meetings`
  - published meetings workspace derived from current outputs plus retained metadata
  - search, view, sort, direction, and grouped browsing controls
  - grouped browsing by week, month, platform, or status
  - full-width meeting library with a compact selection command strip for `Open Details`, artifacts, containing folder, and cleanup review
  - separate owned meeting detail window with local transcript reading, metadata, attendees, model details, speaker-label state, recommendations, and focused maintenance actions
  - cleanup recommendation review area with safe-fix flows that stays separate from single-meeting details
  - per-meeting and multi-selection maintenance actions such as open artifact, rename, re-transcribe, add speaker labels, archive, and delete permanently
- `Setup`
  - dedicated guided window launched from `Home` and related actions
  - split into `Transcription` and `Speaker labeling`
  - focused on capability readiness rather than everyday settings
- `Settings`
  - dedicated header-level window with `General`, `Files`, `Updates`, and `Advanced`
  - owns recording behavior, file locations, release behavior, and troubleshooting settings
- `Help`
  - dedicated header-level window with About information, setup/help entry points, logs/data shortcuts, runtime diagnostics, release-page links, and legal notice

The product family also includes installer and update surfaces:

- preferred per-user MSI installer
- optional EXE bootstrapper with shared product styling
- script and ZIP/manual fallback paths when MSI or EXE execution is blocked

## 3. Core Goals

### Primary goals

- Make local meeting capture reliable on a restricted Windows laptop.
- Keep recording, transcription, and speaker labeling local by default.
- Produce one stable published audio file per meeting.
- Produce automation-friendly transcript outputs when a valid Whisper model is available.
- Make post-meeting cleanup and retry practical without re-recording.

### Secondary goals

- Reduce friction around Whisper setup and optional speaker-labeling setup.
- Preserve enough durable metadata to recover from failed processing and maintain published meetings later.
- Keep install, update, and data layout predictable for standard-user Windows environments.

## 4. Environment Constraints

- The app must run on Windows 10 or Windows 11.
- The user may not have administrator rights.
- Startup apps, services, browser extensions, or native messaging may be blocked.
- Public model downloads may be blocked or replaced with proxy or error pages.
- The laptop may be CPU-only.
- The app must be usable from user-writable folders only.
- Managed installs must keep writable runtime data outside the installed app files.

## 5. In-Scope Product Surface

### Implemented now

- Manual recording controls for any conferencing app that uses the normal Windows output path
- Assisted auto-detection for Teams desktop and Google Meet
- Auto-stop after qualifying meeting signals disappear for long enough
- Rolling WAV chunk capture and durable per-session work folders
- Optional microphone capture mixed into the final WAV
- Separate worker-based offline transcription
- Optional diarization with post-publish speaker-label editing
- Whisper model download, import, selection, validation, and fallback handling from the guided Setup flow
- Stable published `.wav`, `.md`, `.json`, and `.ready` outputs
- Meetings workspace with search, sort, group, selection command strip, cleanup review, and a separate meeting detail window
- Meeting cleanup recommendations and archive-first safe-fix flows
- Meeting rename, transcript retry, re-transcribe-with-model, merge, split, archive, and permanent delete actions
- Optional project metadata editing
- Best-effort attendee enrichment from Outlook and live Teams roster capture when available
- Optional calendar-based title fallback
- Header-level Settings and Help surfaces
- Managed MSI, EXE bootstrapper, script fallback, and ZIP/manual deployment paths
- GitHub-backed update checks plus in-app manual update controls

### Explicitly out of scope

- Live transcription during the meeting
- Full transcript text editing and correction UI
- Automatic named speaker identity beyond generic labels and user-edited display names
- Full calendar-native scheduling or calendar-management workflow
- Required browser extension integration
- Required cloud services
- Generated AI summaries or meeting note generation; the detail window may reserve inactive space for future AI summaries
- Cross-device sync or shared-account collaboration features

## 6. Functional Requirements

## 6.1 Meeting Detection

- The app must detect likely Teams desktop meetings from process names, window titles, audio activity, and meeting-like keywords.
- The app must detect likely Google Meet sessions from browser window titles and other OS-visible hints.
- The app is not required to auto-detect Zoom, Webex, or every other conferencing platform.
- The app must require both meeting-like signals and active output audio before auto-starting a recording.
- The app must suppress obvious non-meeting Teams surfaces such as chat, navigation, or playback-style windows when possible.
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
- The Home surface must expose the current recording status, detection status, and live capture feedback.

## 6.3 Naming, Metadata, and Session Identity

- Each session must have one canonical human title stored in the manifest.
- The current meeting title entered on `Home` must be applied automatically when recording stops.
- Published audio and transcript filenames must be derived from the canonical session title using the shared filename stem format.
- Renaming a published meeting must rename all matching published artifacts together.
- When a matching work-session manifest exists, the rename must update that manifest title too.
- A meeting may also carry optional project metadata.
- When available, attendee metadata from the session, Outlook matching, or live Teams roster capture should be persisted into durable meeting metadata.
- Published transcript JSON sidecars should retain meeting metadata needed for later browsing and repair, including attendees, optional project value, transcription model file name, and whether speaker labels were present.

## 6.4 Work Folders, Persistence, and Recovery

- Each session must have a dedicated work folder containing raw chunks, processing artifacts, logs, and `manifest.json`.
- Failed sessions must retain enough local state for retry.
- The app must be able to locate the manifest that corresponds to a published meeting row when the work folder still exists.
- Retry must reset the manifest to `Queued` and launch the worker again.
- If the original work folder is missing but the final audio file exists, the app should be able to synthesize a new queued manifest and regenerate the transcript from that audio.
- Archive-style cleanup actions must move artifacts into the Meetings archive rather than deleting them permanently.
- Permanent delete must remain a separate manual action guarded by explicit confirmation.

## 6.5 Local Transcription and Speaker Labeling

- Transcription must run after recording is complete.
- Transcription must run in a separate worker process so failures do not destabilize the desktop UI.
- Transcription must use a local Whisper model file.
- The app must validate the configured model file and reject obviously invalid tiny files.
- The user must be able to recover from missing or invalid models through the guided Setup flow.
- Transcription must remain CPU-capable and must not require cloud APIs.
- Speaker labeling is optional and best-effort.
- When available, speaker labeling must produce generic transcript labels such as `Speaker 1`, `Speaker 2`, and so on.
- Diarization failure or missing diarization assets must not block transcript output.
- The diarization runtime must allow a user-controlled GPU acceleration preference and fall back safely when acceleration is unavailable.

## 6.6 Model and Asset Management

- The `Setup` window must show whether the current Whisper model is `Missing`, `Invalid`, or `Ready`.
- The app must allow a user to download the recommended Whisper model from GitHub.
- The app must allow a user to import an existing valid local Whisper model file.
- The app must list discovered local models from the managed model folder and the currently configured path.
- The app must allow the user to switch the active model without editing config by hand.
- If the configured model path is missing or invalid, the app should prefer another valid managed model when one is available.
- Download and import flows must validate the model before replacing the configured file.
- The app must not silently keep a tiny invalid proxy or error file as if it were a real model.
- Speaker-labeling assets must be managed separately from the Whisper model and remain optional.
- GitHub must remain the only built-in automatic download source for managed setup flows.
- The app should open bundled local setup guidance before falling back to remote help links.

## 6.7 Publish Contract

- Final published audio must use the shared filename stem:
  - `YYYY-MM-DD_HHMMSS_<platform>_<session-slug>`
- When transcription succeeds, the app must publish:
  - `<stem>.wav`
  - `<stem>.md`
  - `<stem>.json`
  - `<stem>.ready`
- `.ready` must be created last.
- Power Automate and similar downstream tools should watch `.ready`, not raw transcript creation.
- If transcription fails, the app must still publish the final audio file.
- If transcription fails, transcript artifacts and `.ready` must not be created.
- For managed installs, published outputs should default to the stable Meetings folder layout under the user's Documents folder.

## 6.8 Meetings Workspace

- The Meetings workspace must list published meetings derived from output files plus retained meeting metadata.
- The meeting list should prefer exact titles from the work manifest when available.
- The meeting list should show meeting duration when it can be resolved from the manifest or audio file.
- The meeting list must show enough status to distinguish complete sessions from failed or incomplete ones.
- The workspace must expose direct links to published audio and transcript artifacts.
- The workspace must support search, explicit sorting, and grouped browsing.
- The workspace should support grouping by week, month, platform, or status without changing the underlying meeting source.
- Single-meeting reading and maintenance must happen in an owned detail window opened from the Meetings library, not in a long mixed right rail.
- The detail window must show the key metadata needed for follow-up work, including timing, status, project value, attendee data when available, transcript model, speaker-label state, recommendations, detected audio source, and capture diagnostics.
- The detail window must render local transcript segments from structured JSON sidecars and fall back to the app-owned Markdown transcript format when possible.
- The detail window must show an inactive AI-summary placeholder and must not generate summaries in the current scope.
- Cleanup recommendations must remain separate from publish status and may suggest actions such as archive, rename, retry transcript, merge, split, or add speaker labels.
- Historical cleanup review and ongoing recommendation flows must remain archive-first and reversible where possible.
- The workspace must expose `Re-Generate Transcript` for sessions that can be retried from an existing or synthesized work manifest.
- The workspace and detail window together must support rename, merge, split, archive, permanent delete, project editing, and speaker-label maintenance without requiring external file editing.
- The workspace must support multi-selection for the maintenance actions that are safe and meaningful in bulk.

## 6.9 Configuration and Operational Settings

The app must allow configuration of:

- audio output folder
- transcript output folder
- work folder
- model cache folder
- Whisper model path
- diarization asset folder
- microphone capture on or off
- launch on login on or off
- auto-detection on or off
- update checks on or off
- idle auto-install behavior for downloaded updates
- update feed URL
- auto-detect audio threshold
- meeting stop timeout
- calendar-based title fallback on or off
- attendee enrichment on or off
- diarization GPU acceleration preference

Configuration requirements:

- config must be stored in a user-writable location
- supported settings must hot reload without restart
- the UI must indicate whether a setting applies immediately, on the next recording, or on the next processing run
- `Setup` must remain focused on capability readiness, while `Settings` owns behavior, storage, updates, and troubleshooting

## 6.10 Updates and Deployment

- The preferred installation path for current releases must be a per-user MSI installer.
- The product must also support an EXE bootstrapper, PowerShell or CMD fallback scripts, and a ZIP/manual install path.
- The app must remain usable in portable mode as well as managed per-user mode.
- Update checks must use GitHub release metadata.
- The app must expose manual update check and install controls inside the product.
- Updates must preserve existing user data, including config, logs, models, and meeting outputs.
- Managed installs must keep writable runtime data outside the installed app files.
- If a downloaded update cannot be installed immediately, the app should be able to retry later when the app becomes idle or after restart.

## 6.11 Logging and Troubleshooting

- The app must maintain an activity log in the UI.
- The app must write per-session processing logs under the session work folder.
- Logs must include detection decisions, recording start and stop, worker launch, and model errors.
- The app must make it clear when transcript generation failed because the Whisper model is missing or invalid.
- The Help surface must provide quick access to setup guidance, logs, data folders, runtime diagnostics, and release information.

## 7. Non-Goals and Deferred Work

- Live transcription during the meeting
- Full transcript editing and review workflows
- Automatic named speaker identity resolution
- Deep calendar-first meeting management
- Required browser-extension or native-messaging integration
- Required cloud transcription, cloud storage, or cloud account workflows
- In-app summarization, action-item extraction, or meeting-note generation
- Cross-device sync and shared multi-user collaboration

## 8. Acceptance Criteria for the Current Release Line

The current release line is considered successful when:

- a user can install or run the app without admin rights using the supported managed or portable paths
- a user can launch the app, see readiness state on `Home`, and manually record a meeting locally
- assisted auto-detection can start and stop recordings for supported Teams desktop and Google Meet scenarios without blocking manual recording for other apps
- the app can publish one final WAV per session
- a valid local Whisper model can be installed or imported through the guided Setup flow
- once a valid model exists, the app can generate `.md`, `.json`, and `.ready` outputs
- optional speaker-labeling setup can be completed separately and does not block core transcription
- a failed transcription caused by a missing or invalid model can be retried from the Meetings workspace after setup is fixed
- the Meetings workspace can inspect published meetings, open artifacts, and perform core maintenance actions such as rename, retry, archive, merge, split, and permanent delete
- downstream automation can watch `.ready` and find sibling artifacts by stem
- a later update preserves the user's existing config values and published data

## 9. Related Documents

- [README.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\README.md)
- [ARCHITECTURE.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\ARCHITECTURE.md)
- [SETUP.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\SETUP.md)
