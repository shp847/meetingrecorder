# Meeting Recorder Product Requirements

This document defines the product and MVP requirements for the app. Implementation and deployment details live in [ARCHITECTURE.md](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\ARCHITECTURE.md).

## 1. Purpose

Build a Windows-first application that:

- detects when the user is in a Teams or Google Meet meeting,
- records meeting audio locally,
- transcribes the meeting locally after it ends,
- groups transcript segments by speaker using local diarization,
- writes final audio and transcript outputs to predictable folders, and
- hands off completed transcripts to Power Automate through a file-based workflow.

The design must assume a corporate-restricted Windows laptop with standard user permissions, limited install flexibility, and possible restrictions on browser extensions, background services, and outbound downloads.

## 2. Product Goals

### Primary goals

- Capture meeting audio with minimal user interaction.
- Keep recording and transcription local to the laptop.
- Produce one final merged audio file per meeting.
- Produce one final transcript per meeting with timestamps and speaker labels.
- Make transcript completion easy to detect by Power Automate.

### Secondary goals

- Support both Teams desktop meetings and Google Meet browser meetings.
- Keep the architecture resilient when parts of the environment are restricted.
- Allow an optional path to better meeting detection without making it a hard dependency.

## 3. Constraints and Assumptions

### Environment constraints

- The app runs on Windows 10 or Windows 11.
- The user may not have local administrator rights.
- The laptop may block system services, startup apps, browser extensions, or native messaging.
- The laptop may restrict downloads from public model repositories.
- OneDrive and Power Automate may be managed by corporate policy.

### Product assumptions

- The user is comfortable launching the app manually.
- Batch transcription after the meeting is acceptable and preferred.
- A transcript review UI is not required.
- Speaker correction tools are not required.
- Power Automate will consume transcript files after they are written.
- Audio files may remain local even if transcripts are copied to a synced folder later.

## 4. Scope Boundaries

### In scope for MVP

- Native Windows desktop application launched by the user
- Heuristic meeting detection
- Manual start/stop fallback
- Local audio capture
- Temporary per-meeting working folders
- Final shared `Audio` and `Transcripts` folders
- Batch transcription after recording ends
- Local speaker diarization when models are available locally
- Atomic transcript publishing for Power Automate pickup
- `.ready` marker creation as the only Power Automate completion trigger
- Basic logging and failure handling

### Explicitly out of scope for MVP

- Auto-start on login
- Tray-only background behavior
- Live transcription during meetings
- Transcript review UI
- Speaker correction tools
- Named speaker identification
- Calendar integration
- Automatic summarization inside this app
- Browser extension as a required dependency
- OneDrive as a required dependency
- GPU as a required dependency
- Administrator rights as a required dependency

### Candidate post-MVP items

- Optional browser extension for better meeting detection
- Optional OneDrive publish target for transcript inbox
- Rich settings UI
- GPU acceleration tuning
- Retention automation
- Health dashboard

## 5. User Workflow

1. User launches the application manually.
2. App watches for meeting signals or allows the user to arm/start recording manually.
3. App records meeting audio to a temporary per-meeting work folder.
4. When the meeting ends or the user stops recording, the app finalizes the audio.
5. App runs local transcription and diarization.
6. App writes final output files into stable destination folders.
7. App publishes the transcript atomically so Power Automate can react only to complete files.

## 6. Functional Requirements by Feature

## 6.1 Meeting Detection

### Goal

Start and stop recordings with minimal user action while remaining usable if corporate policies block deeper integrations.

### Requirements

- `MVP` The app must detect likely Teams desktop meetings using Windows-visible signals such as process name, window title, and meeting-state keywords.
- `MVP` The app must detect likely Google Meet sessions using browser window titles and URL-derived indicators when available through accessible OS signals.
- `MVP` The app must require a configurable confidence threshold before auto-starting a recording.
- `MVP` The app must support manual `Start Recording` and `Stop Recording` controls even if auto-detection fails.
- `MVP` Manual start and stop are an accepted fallback path for MVP success.
- `MVP` The app must record why a meeting was considered detected or not detected in logs.
- `MVP` The app must avoid starting multiple overlapping recordings for the same detected session.
- `MVP` The app must support a configurable idle timeout or end-of-meeting timeout to stop recordings automatically when meeting signals disappear.
- `Post-MVP` The app may accept meeting-detection events from a browser extension if such an extension can be installed.

### Acceptance criteria

- A Teams desktop meeting with a recognizable meeting window can trigger a candidate recording session.
- A Google Meet browser session can be detected without requiring an extension.
- If detection is blocked or unreliable, the user can still capture the meeting manually.

## 6.2 Recording Pipeline

### Goal

Capture usable local meeting audio reliably on a managed Windows laptop.

### Requirements

- `MVP` The app must capture system output audio using a Windows-supported local recording method.
- `MVP` The app must support optional microphone recording as a separate track.
- `MVP` The app must write audio in rolling chunks during the session to reduce total data loss if the app crashes.
- `MVP` The app must create one logical recording session per meeting.
- `MVP` The app must store audio using a format suitable for downstream transcription and merging.
- `MVP` The app must allow the user to disable microphone capture if policy or preference requires it.
- `MVP` The app must safely stop and finalize recordings when the app closes unexpectedly, the device sleeps, or the user ends the session.
- `Post-MVP` The app may support automatic source-device selection and richer device diagnostics.

### Acceptance criteria

- A completed meeting produces one or more raw chunk files in the session work folder.
- System audio can be captured without requiring a virtual audio cable.
- The app can finalize a valid audio file even after a partial interruption.

## 6.3 Temporary Working Storage

### Goal

Keep in-progress artifacts isolated and recoverable before final publishing.

### Requirements

- `MVP` The app must create a unique temporary working folder for each meeting session.
- `MVP` The work folder must contain raw chunks, merged intermediates, processing logs, and temporary transcript outputs.
- `MVP` The app must store work folders in a user-writable path such as `%LOCALAPPDATA%`.
- `MVP` The app must clean up temporary files after a successful publish, unless retention is explicitly enabled.
- `MVP` The app must preserve failed-session work folders for retry and diagnosis.

### Acceptance criteria

- Work from one meeting cannot overwrite work from another.
- Failed transcription runs can be retried using files preserved in the work folder.

## 6.4 Post-Meeting Audio Finalization

### Goal

Produce one stable audio output per meeting for archival and downstream use.

### Requirements

- `MVP` The app must merge raw session chunks into one final audio file after recording ends.
- `MVP` The app must publish the final merged audio file into a shared `Audio` destination folder.
- `MVP` The file naming convention must be deterministic and sortable by date.
- `MVP` The filename should include date/time, source platform, and a sanitized session identifier when possible.
- `MVP` The app must prevent partially written final audio files from appearing under their final names.
- `Post-MVP` The app may support multiple output audio formats.

### Acceptance criteria

- Each completed session has exactly one final merged audio file in the `Audio` folder.
- Final audio filenames sort correctly in file explorer by meeting time.

## 6.5 Local Transcription

### Goal

Convert meeting audio to text locally after the meeting is over.

### Requirements

- `MVP` The app must run transcription only after recording is complete.
- `MVP` The transcription engine must be runnable fully locally with no cloud API dependency.
- `MVP` The app must load transcription models from a local model directory and must not depend on runtime model downloads.
- `MVP` The app must support CPU execution.
- `MVP` The app may support GPU acceleration if available, but GPU availability must not be required.
- `MVP` The app must emit timestamps for transcript segments.
- `MVP` The app must preserve the transcription job status as `queued`, `running`, `succeeded`, or `failed`.
- `MVP` The app must keep enough metadata to retry failed transcription without re-recording.

### Acceptance criteria

- A finished recording can be transcribed with network access disabled after models are installed locally.
- The app can process a transcript in the background after the meeting ends.

## 6.6 Speaker Diarization

### Goal

Label transcript segments by anonymous speaker to improve readability and handoff quality.

### Requirements

- `MVP` The app must attempt local speaker diarization when the required local assets are available.
- `MVP` The app must label speakers as generic identifiers such as `Speaker 1`, `Speaker 2`, and so on.
- `MVP` The app must align speaker labels to transcript segments or timestamps.
- `MVP` The app must not fail the entire pipeline if diarization is unavailable or errors out.
- `MVP` If diarization fails, the app must still output a transcript and mark diarization status in metadata.
- `Post-MVP` The app may support speaker re-identification or named-speaker mapping.

### Acceptance criteria

- A successful diarization run adds speaker labels to transcript segments.
- A diarization failure still results in a usable transcript output.

## 6.7 Transcript Output

### Goal

Create clean, machine-readable, and automation-friendly transcript outputs.

### Requirements

- `MVP` The app must write a final transcript to a shared `Transcripts` folder.
- `MVP` The app must support at least one human-readable format and one machine-readable format.
- `MVP` Recommended formats are Markdown for readability and JSON for automation.
- `MVP` Transcript filenames must align with the final audio filenames.
- `MVP` The transcript must include timestamps and, when available, speaker labels.
- `MVP` The transcript must include enough metadata to identify source platform and processing outcome.
- `MVP` The app must publish transcripts atomically by writing to a temporary name and renaming only when complete.
- `MVP` The app may optionally emit a `.ready` or manifest file after transcript publish completes.

### Acceptance criteria

- Power Automate or another watcher never sees a half-written transcript file under the final filename.
- A transcript can be matched to its corresponding audio file using the filename stem.

## 6.8 Power Automate Handoff

### Goal

Allow downstream automation to react to transcript completion with minimal coupling.

### Requirements

- `MVP` The handoff contract must be file-based, not API-based.
- `MVP` The app must only publish the final transcript after processing is complete.
- `MVP` The app must support a configurable transcript destination path so the user can point it to a folder monitored by Power Automate.
- `MVP` The app should support an optional sidecar metadata file for downstream flows.
- `MVP` The app must create a `.ready` marker file after all required transcript artifacts are fully published.
- `MVP` The app must treat `.ready` marker creation as the only supported Power Automate completion trigger for MVP.
- `MVP` The app should document that Power Automate must watch `.ready` marker creation rather than transcript file creation.
- `Post-MVP` The app may support a second publish target such as a OneDrive-synced inbox folder.

### Acceptance criteria

- A downstream file watcher can reliably trigger exactly once per completed transcript.
- The app can publish to a local folder or a synced folder without code changes.

## 6.9 Configuration

### Goal

Allow basic setup without requiring admin rights or deep technical intervention.

### Requirements

- `MVP` The app must allow configuration of:
  - audio output folder
  - transcript output folder
  - temp work folder
  - transcription model path
  - diarization model path or asset path
  - source-language preference if needed
  - microphone capture on or off
  - auto-detection on or off
  - meeting-stop timeout
- `MVP` Configuration must be stored in a user-writable location.
- `MVP` The app must start with sensible defaults when only a subset of settings is provided.
- `MVP` The app must remain functional without OneDrive, browser extensions, or GPU acceleration.
- `Post-MVP` The app may provide a richer graphical settings experience.

### Acceptance criteria

- A standard user can configure the app without editing protected system locations.
- The app can run with defaults after a simple first-time setup.

## 6.10 Logging and Failure Handling

### Goal

Make the pipeline diagnosable on a locked-down corporate laptop.

### Requirements

- `MVP` The app must write per-session logs.
- `MVP` The app must log detection decisions, recording state transitions, transcription state transitions, diarization state, and publish state.
- `MVP` The app must distinguish between recoverable and non-recoverable errors.
- `MVP` The app must leave enough local artifacts behind to retry a failed post-processing run.
- `MVP` The app must expose a simple status summary to the user at runtime or at completion.
- `Post-MVP` The app may add richer diagnostics and health dashboards.

### Acceptance criteria

- A failed run can be investigated without needing external telemetry.
- The user can determine whether failure happened in detection, recording, transcription, diarization, or publishing.

## 6.11 Privacy, Security, and Compliance

### Goal

Operate conservatively in a corporate environment.

### Requirements

- `MVP` Audio and transcript processing must remain local by default.
- `MVP` The app must not send meeting audio or transcript text to external services unless explicitly configured later.
- `MVP` The app must avoid requiring administrator privileges for normal use.
- `MVP` The app must use user-writable directories only.
- `MVP` The app must allow the user to disable microphone capture.
- `MVP` The app must remain usable when browser extensions are unavailable or blocked.
- `MVP` The app must remain usable when OneDrive is unavailable or disabled.
- `MVP` The app must remain usable on CPU-only machines.
- `MVP` The app should display a reminder that recording may be subject to company policy and local law.

### Acceptance criteria

- Core recording and transcription work without cloud services.
- The app can be used entirely within the user's profile directories.

## 7. Non-Functional Requirements

### Reliability

- `MVP` The app should tolerate transient detection failures without crashing.
- `MVP` The app should preserve in-progress recording data during unexpected termination as much as possible.
- `MVP` The app should fail gracefully when diarization assets are missing.
- `MVP` Diarization is best-effort and must never block transcript delivery.

### Performance

- `MVP` The app must remain usable on a CPU-only laptop.
- `MVP` Recording must not noticeably degrade meeting audio playback quality.
- `MVP` Post-meeting processing may take time, but it must run without blocking the recording flow for future meetings.

### Portability

- `MVP` The app must install and run in a per-user context when possible.
- `MVP` The architecture must not require Windows service installation.

### Maintainability

- `MVP` The app should separate detection, recording, processing, and publishing into independent components.
- `MVP` The app should treat transcription and diarization engines as replaceable adapters.

## 8. Recommended Folder Layout

### Working folders

- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\raw\`
- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\processing\`
- `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\logs\`

### Final output folders

- `<ConfiguredAudioFolder>\`
- `<ConfiguredTranscriptFolder>\`

### Optional synced handoff folder

- `<OneDriveMonitoredTranscriptInbox>\`

## 9. MVP Definition

The MVP is complete when all of the following are true:

- The user can launch the app manually and capture a meeting without admin rights.
- The app can detect likely Teams and Google Meet sessions well enough to assist the user, while manual start/stop remains available.
- The app records local system audio and optionally microphone audio.
- The app stores temporary meeting artifacts safely and merges them into one final audio file.
- The app transcribes locally after the meeting ends.
- The app adds anonymous speaker labels when local diarization assets are available.
- The app still succeeds with transcript output when diarization is unavailable.
- The app publishes one final transcript per meeting into a stable folder using an atomic publish step and a final `.ready` marker.
- A Power Automate flow or equivalent watcher can reliably trigger on transcript completion.
- Logs are sufficient to diagnose common failures.

## 10. Post-MVP Backlog

### High priority

- Optional browser extension for higher-confidence Google Meet detection
- Optional OneDrive transcript publish target
- Retry UI for failed sessions
- Better device diagnostics

### Medium priority

- Additional transcript export formats
- Retention rules for old audio and temp folders
- GPU tuning and model selection profiles

### Low priority

- Named speaker identification
- Transcript review UI
- Live transcription
- Calendar integration

## 11. Risks to Track

- Browser extension installation may be blocked by enterprise policy.
- Native messaging between an extension and local app may require admin-level installation.
- Some laptops may restrict background app behavior or audio device access.
- Local diarization setup may be harder than transcription because of model packaging and dependencies.
- Power Automate pickup may depend on whether the destination folder is local, synced, or connector-accessible in the user's tenant.

## 12. Implementation Guidance

### Architecture guidance

- Prefer a user-mode desktop application over a Windows service.
- Treat browser extension support as optional.
- Do not make internet access part of runtime processing.
- Package models separately or document an offline model install step.
- Keep the publish boundary narrow: processing happens in a work folder, then final assets are moved into destination folders only when complete.

### Suggested release slices

#### Slice 1

- Manual record/stop
- Local audio capture
- Temp folders
- Final audio publish

#### Slice 2

- Heuristic meeting detection
- Batch transcription
- Logs and retry metadata

#### Slice 3

- Diarization
- Transcript JSON and Markdown outputs
- Power Automate-ready publish contract

#### Slice 4

- Optional extension-assisted detection
- Optional synced transcript inbox
