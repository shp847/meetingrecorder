# Meeting Recorder

Meeting Recorder is a Windows-first, local-first desktop app for capturing meeting audio, generating transcripts with local Whisper models, and publishing structured outputs for downstream automation.

It is designed for people who need practical local meeting capture with predictable install and update paths:

- local audio capture
- local transcription
- optional microphone capture
- optional local speaker diarization
- lightweight install and update paths
- automation-friendly output artifacts

Legal notice: You are responsible for complying with applicable recording, privacy, employment, and consent laws and workplace policies in your location. Tell participants when they are being recorded and obtain consent where required. This app is not legal advice.

## App Shell

The app now uses a task-first shell with two main destinations in one visible segmented navigation strip:

- `Home` for the recording console: editable title, client/project, and key-attendee metadata, plus the live audio graph, start/stop controls, and quick microphone/auto-detect settings
- `Meetings` for the published meetings workspace and maintenance actions
Capability setup is still first-class, but it now lives inside `Settings > Setup` instead of the primary navigation.

`Key attendees` is intentionally stored separately from the full raw attendee list. That keeps it useful as a searchable, user-curated field in `Meetings`, while Outlook and Teams enrichment can still upgrade short partial names to fuller matches when better attendee names are discovered.

Startup now prioritizes an interactive `Home` shell first, keeps the heavy Meetings enrichment off the launch-critical path, and waits to queue the full Meetings analysis until you actually open `Meetings`.
When an auto-started meeting ends, attendee enrichment now happens in the background processing queue instead of blocking the foreground stop action.

Maintenance and support actions live in the header instead of the main navigation:

- `Settings` for Setup, General, Files, Updates, and Advanced, with a dedicated full-width section button strip instead of the older clipped tab headers
- `Help` for About details, setup/help links, logs/data shortcuts, and release entry points

## What It Does

- Records system audio and optional microphone audio on Windows
- Supports manual recording for Teams, Google Meet, Zoom, Webex, and other conferencing apps that use the normal Windows audio stack
- Uses assisted auto-detection for Microsoft Teams desktop and Google Meet browser meetings when you turn it on
- Transcribes recordings offline with local Whisper models
- Publishes transcript artifacts in Markdown, JSON, and ready-marker form for automation
- Makes title, project, key-attendee, and attendee metadata searchable from the Meetings workspace
- Lets you rename meetings, regenerate transcripts, merge meetings, split meetings, and edit diarized speaker names from the Meetings tab
- Flags likely errant meetings with cleanup recommendations such as archive, merge, split, rename, retry-transcript, and add-speaker-label actions
- Offers a one-time historical cleanup review plus ongoing row badges, a lighter cleanup review banner, and compact action drafts that stay tucked away until needed
- Can ingest dropped audio files such as phone voice memos and queue them for automatic transcription
- Can download or import Whisper models from inside the app
- Guides model setup with a recommended Whisper download path and an optional speaker-labeling checklist
- Checks GitHub releases for updates and can retry a deferred downloaded update after restart when the app becomes idle

## Current Release

The current documented release line is **v0.3**.

## Installation Options

### Recommended for most users

- `MeetingRecorderInstaller.msi`

This is the preferred Windows installer for the current release flow, especially when a per-user MSI is more appropriate than a custom bootstrapper. The MSI installs the binaries under `%USERPROFILE%\Documents\MeetingRecorder`, adds a user-scope Start Menu entry, and leaves writable runtime data outside the installed files.

### Other supported install paths

- `Install-LatestFromGitHub.cmd`
  - script-based fallback when MSI or EXE installation is blocked by local security policy
- `Install-LatestFromGitHub.ps1`
  - direct PowerShell fallback for environments that allow signed scripts more readily than custom executables
- `MeetingRecorderInstaller.exe`
  - optional thin launcher if you still want a one-click EXE path
  - prefers the colocated release ZIP and bootstrap scripts when they ship beside the EXE
  - otherwise downloads the command bootstrap assets, launches `Install-LatestFromGitHub.cmd`, and then steps aside
- `MeetingRecorder-v0.3-win-x64.zip`
  - manual extract-and-run fallback

Current installer/update authority:

- `AppPlatform.Deployment.Cli` is the only engine that writes or updates the managed install tree
- `Install-LatestFromGitHub.cmd/.ps1` are the canonical interactive bootstrap/update path
- in-app updates stay CLI-only even when the app was first installed through the MSI
- `MeetingRecorderInstaller.exe` is a branded launcher over that same bootstrap path, not a peer deployment engine
- the default self-contained release now ships the WPF shell as a single-file `MeetingRecorder.App.exe`; the deployment CLI, worker, scripts, and product manifest remain external sidecars in the bundle

## Runtime Model

Meeting Recorder supports two storage modes:

- Portable mode
  - writable data lives beside the extracted app bundle
- Managed per-user mode
  - the install is managed separately from writable runtime data
  - published outputs default to `Documents\Meetings\Recordings` and `Documents\Meetings\Transcripts`
  - config, logs, work artifacts, and models stay outside the installed app files
  - current managed runtime data lives under `%LOCALAPPDATA%\MeetingRecorder`

For newer managed installs, the app can also migrate prior portable data forward on first launch when appropriate, so an install update does not appear to lose earlier recordings or transcripts.

## Core Features

### Recording

- Manual Start and Stop controls
- Auto-detection for Teams desktop and Google Meet, off by default on new installs and reset off once for older configs to avoid extra permission prompts on some systems
- Google Meet detection now also inspects visible Chromium tab titles, so a Meet tab can still be recognized when the main browser title is a shared Slides or Docs page
- Auto-detection now also attributes active Windows render audio to a likely process, meeting window, or Chromium Meet tab and uses that source as a strong tie-breaker instead of a single shared audio bonus
- Google Meet auto-start now requires Meet-specific browser-audio attribution when Chromium render sessions are available, so unrelated browser playback such as music or video does not get credited to an open Meet tab
- If an auto-started recording is first classified from stale Google Meet browser evidence and a stronger Teams in-call window appears afterward, the app now reclassifies that live session in place instead of stopping and starting a second recording
- Auto-stop when meeting signals disappear
- Auto-stop now surfaces a live countdown on `Home` for auto-started sessions and switches immediately into an `Auto-stopping` state when the timeout expires, so controls disable before background finalization finishes
- Continuity handling for quiet patches, compact-view surfaces, and sharing-control surfaces in Teams
- Teams chat-thread playback surfaces are now deprioritized so recording playback is less likely to auto-start as if it were a live meeting
- Lingering Teams background processes or weak process-only signals no longer keep an auto-started recording alive after the actual meeting window disappears
- Live audio activity graph during active capture

### Transcription

- Offline Whisper transcription through a separate worker process
- Support for downloadable and imported local ggml Whisper models
- Determinate download progress when the remote asset reports size
- Re-generate transcript action for failed or stale sessions

### Meeting Management

- Meetings workspace with a search/sort/group toolbar, a default grouped view for existing and new users, grouped browsing by week, month, platform, or status, and a calmer focused-detail area beneath the list
- Meetings workspace now publishes the current list first, then loads cleanup suggestions and recent attendee backfill in the background so the tab stays responsive on larger histories
- Published meeting list with project tags, status, duration, compact local-time timestamps, compact artifact actions, and recommendation badges
- Grouped browsing now opens the first visible group by default, keeps other groups collapsed initially, and exposes quick `Expand All` and `Collapse All` controls
- Selected-meeting inspector showing attendees, project, recommendation badges, transcript model metadata, speaker-label state, and the persisted detected audio source summary
- Recommendation badges for likely cleanup actions directly in the meeting list
- Dedicated cleanup recommendation review area for bulk apply, dismiss, and open-related flows without leaving the Meetings workspace
- Per-meeting context menu for transcript, audio, archive, delete, rename, retry, split, merge, and speaker-label maintenance actions
- Rename published meetings while keeping artifacts aligned
- Merge multiple meetings into one queued transcript job
- Split one meeting into two queued transcript jobs
- Apply or clear a simple free-text project value for one meeting or a multi-selection from the focused Meetings tools
- Speaker-name editing for diarized transcripts
- Manual context-menu actions for archive, permanent delete, reprocessing, split, merge, and related meeting maintenance
- Suggests adding missing speaker labels when the diarization model bundle is ready and the transcript can be upgraded safely
- One-time historical review flow with `Review Suggestions` and `Apply Safe Fixes`
- Unified Meetings workflow where quick actions sit next to the focused details, the context menu handles most maintenance actions, and the lower drafts stay collapsed until a task needs extra input

### External Audio Import

- Drop supported audio files into the watched audio folder
- Automatic import into the processing pipeline
- Automatic transcript generation for newly discovered audio without transcripts
- Retry suppression for unchanged failed imports so the app does not loop forever on a bad file

### Updates and Deployment

- GitHub-backed release checks
- Manual update check and install controls in the app
- Idle auto-install support
- Automatic GitHub update checks on app startup and app shutdown when update checks are enabled
- Pending downloaded update retry after restart when the app could not install immediately
- CLI-only update apply flow for MSI, EXE, script, and ZIP-origin installs
- In-app update handoff now resolves `AppPlatform.Deployment.Cli.exe` from the installed app directory via the running process path, so single-file app launches do not accidentally look for the updater helper inside the temporary `.net` extraction folder
- Background publish processing now resolves `MeetingRecorder.ProcessingWorker.exe` from the installed app directory via the running process path, so queued sessions still publish correctly from single-file installs
- Same-version pending update metadata is reconciled on restart so the app does not loop on an already installed build
- A second launch now waits briefly for a winding-down prior instance before it reports that the app is still running
- MSI, EXE, ZIP, and command bootstrap release assets

## Reusable Platform Layers

The repo now has a reusable app-platform slice that future desktop apps can consume before it is moved to its own repository:

- `AppPlatform.Abstractions`
  - neutral app manifest, release-channel, install-layout, shell-integration, settings, and about/support definitions
- `AppPlatform.Configuration`
  - generic config-store and live-config contracts for JSON-backed desktop settings
- `AppPlatform.Deployment`
  - reusable install/update orchestration, release-feed download logic, shortcut repair, bundle integrity validation, install provenance persistence, and bundle apply helpers
- `AppPlatform.Deployment.Cli`
  - canonical install/update command runner used by script wrappers and in-app update handoff
- `AppPlatform.Deployment.WpfHost`
  - shared WPF host layer reserved for branded installer/update shells
- `AppPlatform.Deployment.Wix`
  - reusable WiX packaging assets and per-user install conventions
- `AppPlatform.Shell.Wpf`
  - reusable shell resources plus shared Settings and Help host windows
- `MeetingRecorder.Product`
  - Meeting Recorder product adapter: manifest, shell registrations, about/support content, and install-layout ownership

Meeting Recorder still owns:

- product branding
- Home, Meetings, and Settings-hosted setup workflows
- app-specific config schema and defaults
- meeting-processing logic
- product-specific installer/release URLs and guidance

## Supported Inputs

### Conferencing apps

- Microsoft Teams
- Google Meet
- Zoom
- Webex
- Other conferencing apps that use standard Windows playback and microphone devices

Manual recording works more broadly than assisted auto-detection. Auto-detection currently focuses on Teams desktop and Google Meet browser heuristics, including Meet tabs inside visible Chromium browser windows.

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

For newer managed installs, the default published locations are:

- `Documents\Meetings\Recordings` for final audio
- `Documents\Meetings\Transcripts` for Markdown transcripts
- `Documents\Meetings\Transcripts\json` for transcript `.json` and `.ready` sidecars

When available, the published transcript JSON now also carries durable meeting metadata such as:

- attendee names gathered from persisted meeting data, including Outlook calendar attendee metadata when a meeting can be matched to an appointment
- attendee names gathered live from the Teams roster UI during an active Teams recording when that roster is exposed through Windows UI Automation
- cached Outlook no-match results for unchanged historical meetings so repeated Meetings-tab opens do not rescan the full backlog every time
- an optional user-edited project name that is also persisted in the meeting manifest and Markdown transcript
- the compact detected audio source summary that won the meeting classification, including the app plus the matched meeting window or browser tab when available
- the transcription model file name used for the transcript run
- whether speaker labels were present in the transcript

The ready-marker is the completion signal intended for downstream tools such as Power Automate.

Automatic cleanup recommendations and safe fixes remain archive-first. The app moves suspicious or superseded meeting artifacts into the Meetings archive so they can be recovered later if needed. Current builds use a single `Documents\Meetings\Archive` root for archive-style actions and one-time repair flows, and older parallel legacy roots such as `ArchivedRepairs` or `ArchivedFalseStarts` can be consolidated under that same `Archive` stem.

The Meetings tab also exposes a separate manual `Delete Permanently` action from the meeting context menu. That path is irreversible, requires typing `DELETE` to confirm, removes the published audio and transcript artifacts, and also removes the linked session work folder when one still exists.

## Model and Diarization Notes

- A valid local Whisper model is required for transcript generation
- Diarization is optional and remains best-effort
- Speaker diarization now runs inside the local worker through `sherpa-onnx` using an optional diarization model bundle
- GPU acceleration for diarization is user-controlled and defaults to `Auto`, which tries DirectML on compatible Windows GPUs and falls back to CPU automatically
- `Settings > Setup` separates `Transcription` and `Speaker labeling` into dedicated sections while keeping the same compact readiness guidance and `Local Help First` fallback behavior
- Built-in automatic model and speaker-labeling downloads use GitHub release assets only
- Alternate public speaker-labeling download locations are curated links and may currently be unavailable
- The speaker-labeling help path opens the bundled local `SETUP.md` first and falls back to GitHub only when the local guide cannot be found
- Speaker-label editing works even after publication when diarized labels exist
- The diarization bundle is a separate optional dependency from the main app installer and should include the segmentation model, embedding model, and bundle manifest together

## Configuration Highlights

The app exposes settings in the header-level `Settings` surface for:

- setup
- output locations
- work folder
- model and diarization asset selection
- diarization GPU acceleration preference
- microphone capture
- launch on login
- auto-detection behavior
- update checks and install behavior
- calendar-based title fallback
- attendee enrichment from Outlook and Teams when available
- audio threshold and meeting stop timeout

The app keeps only `Home` and `Meetings` in the primary navigation. `Settings > Setup` is where you make transcription or optional speaker labeling ready. `Settings > General`, `Files`, `Updates`, and `Advanced` handle recording behavior, storage, release behavior, and troubleshooting.

The `Home` console is intentionally minimal. It keeps only the meeting title editor, detected audio source summary, live audio graph, start/stop controls, and quick `Microphone capture` / `Automatic detection` controls, while the shell header status capsule surfaces setup or update actions when needed.

The `Settings` surface opens as a dedicated dialog from the header, keeps capability setup first under `Setup`, keeps everyday defaults under `General`, groups output paths under `Files`, keeps release behavior in `Updates`, and tucks infrastructure and troubleshooting paths under `Advanced`.

The header-level `Help` surface now opens as a dedicated dialog and owns About details, setup/help entry points, logs/data folder shortcuts, and release-page links.

Calendar title fallback is optional and soft-fail by design. Separately, attendee enrichment now defaults on and can merge Outlook appointment attendees plus best-effort live Teams roster names into meeting metadata when those sources are available. If Outlook access or Teams automation is unavailable, recording and detection continue normally.

## Designed for Practical Windows Use

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
- Local security controls may still block unsigned installers or downloaded executables
- Apps & Features can continue to show the MSI's original version even after a later CLI-driven update installs a newer app build

## Documentation

For more detail, see the other docs in the repository root:

- `SETUP.md`
- `ARCHITECTURE.md`
- `PLATFORM_DEPLOYMENT_CHECKLIST_TEMPLATE.md`
- `PLATFORM_DEPLOYMENT_SNIPPETS.md`
- `PRODUCT_REQUIREMENTS.md`
- `RELEASING.md`

