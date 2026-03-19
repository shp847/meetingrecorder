# Meeting Recorder

Meeting Recorder is a Windows-first, local-first desktop app for capturing meeting audio, generating transcripts with local Whisper models, and publishing structured outputs for downstream automation.

It is designed for people who need practical meeting capture on locked-down or privacy-sensitive laptops:

- local audio capture
- local transcription
- optional microphone capture
- optional speaker diarization sidecar
- lightweight install and update paths
- automation-friendly output artifacts

Legal notice: You are responsible for complying with applicable recording, privacy, employment, and consent laws and workplace policies in your location. Tell participants when they are being recorded and obtain consent where required. This app is not legal advice.

## What It Does

- Records system audio and optional microphone audio on Windows
- Supports manual recording for Teams, Google Meet, Zoom, Webex, and other conferencing apps that use the normal Windows audio stack
- Uses assisted auto-detection for Microsoft Teams desktop and Google Meet browser meetings
- Transcribes recordings offline with local Whisper models
- Publishes transcript artifacts in Markdown, JSON, and ready-marker form for automation
- Lets you rename meetings, regenerate transcripts, merge meetings, split meetings, and edit diarized speaker names from the Meetings tab
- Can ingest dropped audio files such as phone voice memos and queue them for automatic transcription
- Can download or import Whisper models from inside the app
- Checks GitHub releases for updates and can retry a deferred downloaded update after restart when the app becomes idle

## Current Release

The current documented release line is **v0.2**.

## Installation Options

### Recommended for most users

- `MeetingRecorderInstaller.msi`

This is the preferred Windows installer for the current release flow, especially on corporate laptops where a plain MSI is more likely to be acceptable than a custom bootstrapper.

### Other supported install paths

- `MeetingRecorderInstaller.exe`
  - optional custom bootstrapper if you still want the one-click EXE path
- `Install-LatestFromGitHub.cmd`
  - script-based fallback when the EXE path is blocked
- `MeetingRecorder-v0.2-win-x64.zip`
  - manual extract-and-run fallback

## Runtime Model

Meeting Recorder supports two storage modes:

- Portable mode
  - writable data lives beside the extracted app bundle
- Managed per-user mode
  - writable data lives in the user app-data location instead of the install directory

For newer managed installs, the app can also migrate prior portable data forward on first launch when appropriate, so an install update does not appear to lose earlier recordings or transcripts.

## Core Features

### Recording

- Manual Start and Stop controls
- Auto-detection for Teams desktop and Google Meet
- Auto-stop when meeting signals disappear
- Continuity handling for quiet patches, compact-view surfaces, and sharing-control surfaces in Teams
- Live audio activity graph during active capture

### Transcription

- Offline Whisper transcription through a separate worker process
- Support for downloadable and imported local ggml Whisper models
- Determinate download progress when the remote asset reports size
- Re-generate transcript action for failed or stale sessions

### Meeting Management

- Published meeting list with status, duration, and artifact links
- Rename published meetings while keeping artifacts aligned
- Merge multiple meetings into one queued transcript job
- Split one meeting into two queued transcript jobs
- Speaker-name editing for diarized transcripts

### External Audio Import

- Drop supported audio files into the watched audio folder
- Automatic import into the processing pipeline
- Automatic transcript generation for newly discovered audio without transcripts
- Retry suppression for unchanged failed imports so the app does not loop forever on a bad file

### Updates and Deployment

- GitHub-backed release checks
- Manual update check and install controls in the app
- Idle auto-install support
- Pending downloaded update retry after restart when the app could not install immediately
- MSI, EXE, ZIP, and command bootstrap release assets

## Supported Inputs

### Conferencing apps

- Microsoft Teams
- Google Meet
- Zoom
- Webex
- Other conferencing apps that use standard Windows playback and microphone devices

Manual recording works more broadly than assisted auto-detection. Auto-detection currently focuses on Teams desktop and Google Meet browser heuristics.

### Imported audio

The external audio import flow accepts these source formats:

- `.wav`
- `.mp3`
- `.m4a`
- `.aac`
- `.mp4`

## Outputs

For a successful transcript run, the app publishes:

- final meeting audio
- Markdown transcript
- JSON transcript
- ready-marker file for automation

The ready-marker is the completion signal intended for downstream tools such as Power Automate.

## Model and Diarization Notes

- A valid local Whisper model is required for transcript generation
- Diarization is optional and remains best-effort
- Speaker-label editing works even after publication when diarized labels exist
- The app includes diarization asset plumbing, but the sidecar itself remains a separate optional dependency

## Configuration Highlights

The app exposes settings for:

- output locations
- work folder
- model and diarization asset selection
- microphone capture
- launch on login
- auto-detection behavior
- update checks and install behavior
- calendar-based title fallback
- audio threshold and meeting stop timeout

Calendar title fallback is optional and soft-fail by design. If Outlook access is unavailable or broken, recording and detection continue normally.

## Designed for Restricted Laptops

- No browser extension required
- No cloud transcription dependency required
- CPU-only local transcription supported
- Portable usage supported
- Script-based fallback install path supported
- External model import supported when direct downloads are blocked

## Known Limitations

- Assisted auto-detection currently targets Teams desktop and Google Meet only
- Transcript quality depends on the chosen local Whisper model and source audio quality
- Diarization is optional and not guaranteed for every workflow
- There is still no full transcript text editing interface
- Corporate endpoint controls may still block unsigned installers or downloaded executables

## Documentation

For more detail, see the other docs in the repository root:

- `SETUP.md`
- `ARCHITECTURE.md`
- `PRODUCT_REQUIREMENTS.md`
- `RELEASING.md`

