# Meeting Recorder v0.3

Meeting Recorder v0.3 is the first full release of a Windows-first, local-first meeting capture and transcription app designed for practical day-to-day use.

It records meeting audio locally, transcribes with local Whisper models, optionally applies local speaker labeling, and publishes stable output files that are easy to review, archive, or hand off to downstream automation.

This release should be read as the product definition for Meeting Recorder as it exists today, not just as a list of incremental changes from the prior build.

## What Meeting Recorder Is

Meeting Recorder is built for users who need:

- reliable local meeting capture on Windows
- predictable install and update paths without admin-heavy deployment assumptions
- local transcription without a required cloud dependency
- automation-friendly output files
- a practical workspace for reviewing, fixing, and organizing recorded meetings after capture

The app is intentionally local-first:

- capture runs on the local Windows audio stack
- transcription runs on local Whisper models
- optional speaker labeling runs from a separate local diarization bundle
- runtime data stays under user-writable locations instead of inside the installed app files

## End-to-End Workflow

Meeting Recorder covers the full path from install to usable output.

### 1. Install and setup

The recommended install path is the per-user MSI:

- `MeetingRecorderInstaller.msi`

Current releases also support:

- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`
- `MeetingRecorder-v0.3-win-x64.zip`

The app uses an offline-first setup model:

- a fresh MSI install always seeds the Standard transcription asset and the Standard speaker-labeling asset locally
- the installer can optionally try a Higher Accuracy transcription download and a Higher Accuracy speaker-labeling download
- install still succeeds if those optional downloads fail
- users can retry those optional downloads later from `Settings > Setup`

That makes the app workable on managed Windows machines where large model downloads are unreliable or blocked.

### 2. Record

The `Home` view is the live recording console. It includes:

- required session title
- optional `Client / project`
- optional `Key attendees`
- live audio graph
- live elapsed timer
- start and stop controls
- quick `Microphone capture` and `Automatic detection` toggles

Manual recording works broadly across conferencing apps that use the normal Windows audio path, including:

- Microsoft Teams
- Google Meet
- Zoom
- Webex
- other meeting apps on the standard Windows playback stack

Assisted auto-detection is narrower and currently focuses on:

- Microsoft Teams desktop
- Google Meet in visible Chromium-family browsers

When auto-detection is enabled, the app watches for a supported live meeting and can auto-start and auto-stop supported calls. Manual recordings also continue running with background detection, so a user-started session can still reclassify in place to a detected Teams or Google Meet meeting later.

### 3. Detect and label the meeting

Meeting Recorder uses window evidence plus Windows audio evidence to decide whether a supported call is really active.

Current detection behavior includes:

- Teams desktop detection
- Google Meet browser detection
- Chromium tab-title inspection for Meet
- Windows render-session audio attribution
- continuity handling for quiet patches and temporary shell-state changes
- in-place reclassification when a better meeting identity appears after recording has already started

The goal is not just to start recording, but to keep one meeting as one meeting whenever possible instead of fragmenting it into multiple artifacts.

### 4. Process locally

After stop, the background worker processes the session locally:

- finalizes meeting audio
- runs Whisper transcription
- optionally runs speaker labeling
- publishes stable artifacts

Speaker labeling is optional and best-effort. A meeting can still complete successfully even when diarization is unavailable, skipped, or fails.

### 5. Publish usable artifacts

For a successful transcript run, the app publishes:

- final meeting audio
- Markdown transcript
- JSON transcript
- `.ready` completion marker

For current managed installs, the default publish locations are:

- `Documents\Meetings\Recordings`
- `Documents\Meetings\Transcripts`
- `Documents\Meetings\Transcripts\json`
- `Documents\Meetings\Archive`

The `.ready` marker is written last and acts as the completion signal for downstream tools such as Power Automate.

### 6. Review and maintain

The `Meetings` workspace is the post-recording maintenance surface.

It supports:

- browsing recent and published meetings
- search
- grouping and sorting
- meeting inspection
- rename
- archive
- permanent delete
- transcript retry / regeneration
- merge
- split
- speaker-label maintenance
- project tagging
- cleanup recommendations for likely bad or fragmented recordings

The app is meant to help users recover from real-world messy recordings, not just ideal clean captures.

## Product Capabilities In v0.3

### Recording

- local Windows system-audio capture
- optional microphone capture
- live audio graph during recording
- live elapsed timer
- manual start and stop
- background detection during manual recordings
- in-place meeting reclassification when a better supported meeting identity appears
- auto-stop for meeting-lifecycle-managed recordings when signals disappear

### Detection

- Teams desktop support
- Google Meet browser support
- quiet Teams continuity handling
- Google Meet browser/title continuity handling
- audio-attributed tie-breaking between competing meeting candidates
- bounded detection probes so browser or audio inspection failures do not stall the app indefinitely

### Transcription and speaker labeling

- local Whisper transcription
- curated Standard vs Higher Accuracy setup flow
- downloadable or imported local models
- optional local speaker labeling through a separate diarization bundle
- fallback behavior that prioritizes publishing transcripts even when speaker labeling is unavailable

### Meetings workspace

- published-meeting catalog
- status-aware browsing
- searchable title, project, and attendee metadata
- meeting cleanup and repair workflows
- transcript retry and speaker-label maintenance
- merge and split operations

### Setup and settings

- simplified shell with `Home` and `Meetings` as the primary destinations
- guided `Settings > Setup` for transcription and speaker-labeling readiness
- dedicated `Settings` and `Help` entry points in the header
- read-only diagnostic storage details instead of raw model-path editing as the normal user flow

### Deployment and updates

- per-user MSI installer
- script/bootstrap fallback installers
- ZIP fallback
- in-app update support
- user-data-preserving reinstall and uninstall behavior
- release packaging and upload guards to reduce stale or mismatched published assets

## Installation and Runtime Model

Meeting Recorder supports two practical usage modes.

### Managed per-user install

This is the recommended mode.

- binaries install under `%USERPROFILE%\Documents\MeetingRecorder`
- writable runtime data lives under `%LOCALAPPDATA%\MeetingRecorder`
- published outputs live under `Documents\Meetings\...`
- uninstall removes the managed app files and shortcuts but preserves user data, recordings, transcripts, models, logs, and config

### Portable extract-and-run

This remains available for users who need a non-installed path.

- the app can run from an extracted release folder
- writable data stays beside the portable app folder

## Who This Release Is For

v0.3 is aimed at users who need a practical desktop recorder for real working meetings:

- consultants and project teams capturing calls for notes and follow-up
- users in corporate environments where browser extensions or cloud transcription are not acceptable
- users who need stable files for downstream automation
- users who want a local tool they can install, keep, and trust rather than a one-off prototype

## Current Boundaries

Meeting Recorder v0.3 is intentionally strong in a few areas and explicitly limited in others.

### Strong today

- Windows local capture
- local transcription
- practical install/update paths
- Teams and Google Meet assisted detection
- manual recording fallback for other conferencing apps
- published artifact stability
- post-recording maintenance workflows

### Not the focus of this release

- full cloud meeting-platform integrations across every conferencing product
- rich in-app transcript editing
- a general-purpose media editor
- cross-platform desktop support outside Windows

## Important Notes

- A valid local Whisper model is required for transcript generation.
- Speaker labeling is optional.
- Assisted auto-detection currently focuses on Teams desktop and Google Meet only.
- Manual recording works more broadly than assisted auto-detection.
- Transcript quality depends on the selected model and source audio quality.
- Users are responsible for complying with applicable recording, privacy, employment, and consent laws and workplace policies.

## Bottom Line

Meeting Recorder v0.3 is a complete local meeting workflow for Windows:

- install it without heavy deployment assumptions
- make transcription ready with guided setup
- record manually or with assisted detection
- process locally
- publish stable outputs
- review and repair meetings from one workspace

That is the product shipped in v0.3.
