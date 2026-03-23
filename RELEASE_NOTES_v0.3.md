# Meeting Recorder v0.3

Meeting Recorder v0.3 is the first full release of the app: a Windows-first, local-first desktop workflow for recording meeting audio, transcribing it with local Whisper models, and publishing stable output files for follow-up review or automation.

This release turns Meeting Recorder into a complete day-to-day product for practical Windows meeting capture. It combines guided setup, reliable local recording, offline transcription, optional speaker labeling, a full meetings workspace, and multiple install paths that do not require administrator rights.

## Release Highlights

- Local-first meeting capture with offline transcription and automation-ready outputs
- Manual recording support across Teams, Google Meet, Zoom, Webex, and other conferencing apps that use the normal Windows audio stack
- Assisted auto-detection for Microsoft Teams desktop and Google Meet, with new installs defaulting it off and older configs reset off once to avoid extra permission prompts on some systems
- Guided in-app setup for Whisper transcription and optional speaker labeling
- A full Meetings workspace for browsing, cleanup, retry, rename, merge, split, and speaker-label maintenance
- Flexible install options including a per-user MSI, EXE bootstrapper, script fallback, and ZIP/manual path
- Stable published artifacts in Markdown, JSON, audio, and `.ready` marker formats for downstream tooling

## What Ships In This Release

### Recording and detection

- Records system audio locally on Windows
- Optionally records microphone audio and mixes it into the final meeting audio
- Supports manual start and stop for any conferencing app that uses the normal Windows playback path
- Uses assisted auto-detection for Teams desktop and Google Meet when you turn it on
- Replaces launch-time `Process` enumeration with top-level window discovery for meeting detection and existing-instance activation, and keeps live Teams roster capture disabled by default so extra permission prompts are less likely
- Auto-stops recordings when meeting signals disappear
- Auto-stop now shows a visible `Home` countdown for auto-started sessions and flips into `Auto-stopping` immediately when timeout expires instead of looking idle while finalization runs
- Shows a simplified Technical Studio Home console with separate `Title`, `Client / project`, and `Key attendees` fields, plus the live audio graph, start/stop controls, and quick microphone/auto-detect settings
- Loads the Home shell first and finishes heavier Meetings enrichment in the background so the app becomes interactive sooner after launch
- Moves attendee enrichment off the foreground stop path and into the background processing queue so auto-stop feels much less frozen on longer sessions

### Transcription and speaker labeling

- Runs Whisper transcription locally after the meeting completes
- Supports downloadable or imported local ggml Whisper models
- Validates models before use so broken or obviously invalid files are rejected
- Publishes transcripts as Markdown and JSON
- Supports optional local speaker labeling through a separate diarization model bundle
- Keeps speaker labeling best-effort so transcript generation can still succeed without it

### Meetings workspace

- Lists published meetings in a dedicated Meetings view
- Keeps the meeting list in a primary split-pane region so it remains visible and scrollable at the default window size while inspector and cleanup tools stay in a separate side pane
- Supports grouped browsing, search, sorting, and a focused meeting inspector
- Meetings search now matches title, project, key attendees, and captured attendee names
- Exposes quick actions for opening audio and transcript artifacts
- Lets users rename meetings while keeping published artifacts aligned
- Supports transcript regeneration for retryable failed or incomplete sessions
- Supports merge, split, archive, and permanent delete maintenance actions
- Surfaces cleanup recommendations and safe-fix flows for likely false starts, duplicates, or repairable meetings
- Supports project tagging and post-publish speaker-name maintenance for diarized transcripts

### Setup, settings, and help

- Simplifies the primary shell to `Home` and `Meetings`
- Uses one visible `Home` / `Meetings` strip backed by the built-in WPF tab host so `Home` paints sooner and the primary shell stops relying on brittle custom tab-content routing
- Replaces the clipped settings section tabs with a dedicated section button strip so labels stay readable at the default window size
- Simplifies the Home quick-setting wells by removing the oversized secondary headings and highlighting the active `On` / `Off` state directly in the selected button
- Gives the Home recording console a wider fixed-width floor at the default window size and keeps the title editor single-line so the panel does not expand while you type
- Stamps release bundles with `release-source.json` and now blocks GitHub release uploads when installer assets do not match the current clean repo commit, preventing stale `.artifacts` from being published as a fresh release
- Moves guided transcription and speaker-labeling setup into `Settings > Setup`
- Moves maintenance and support into header-level `Settings` and `Help`
- Keeps setup discoverable through the header status capsule when the model or optional speaker-labeling assets need attention
- Keeps file locations, update behavior, troubleshooting paths, and advanced settings in one dedicated settings surface
- Provides local setup help, logs/data shortcuts, and release-note entry points from Help

### Deployment and updates

- Preferred installer: `MeetingRecorderInstaller.msi`
- Optional thin bootstrapper: `MeetingRecorderInstaller.exe`
- EXE bootstrapper now prefers a sibling locally built release ZIP when one ships beside the installer, instead of always falling back to the current GitHub release
- Self-contained releases now ship the WPF shell as a single-file `MeetingRecorder.App.exe`, which avoids the loose-file `WindowsBase` startup failure that could surface as a downstream `WerFault.exe` restricted-access popup on launch
- EXE bootstrapper now uses the shared app shell styling for status, progress, fallback guidance, and actions, with a scrollable layout and cleaner grouped buttons at the default window size
- In-app `Install Available Update` handoff now resolves the updater CLI from the installed app directory instead of the transient single-file extraction folder, fixing failed update launches that could report the helper missing
- Same-version pending updates now compare published-at and asset-size identity before they are treated as already installed, so refreshed `0.3` builds do not get skipped just because the semantic version text stayed the same
- Background publish processing now resolves `MeetingRecorder.ProcessingWorker.exe` from the installed app directory instead of the transient single-file extraction folder, fixing queued sessions that could otherwise stay unpublished after recording stops
- Script fallback: `Install-LatestFromGitHub.cmd` or `Install-LatestFromGitHub.ps1`
- Manual fallback: `MeetingRecorder-v0.3-win-x64.zip` with `Install-MeetingRecorder.cmd`
- Supports managed per-user installs and portable extract-and-run usage
- Preserves user data on update
- Checks GitHub releases for updates and supports deferred install retry when the app becomes idle

## Published Outputs

For a successful transcript run, Meeting Recorder publishes:

- final meeting audio as `.wav`
- transcript Markdown as `.md`
- transcript JSON as `.json`
- completion marker as `.ready`

For current managed installs, the default locations are:

- `Documents\Meetings\Recordings`
- `Documents\Meetings\Transcripts`
- `Documents\Meetings\Transcripts\json`
- `Documents\Meetings\Archive`

The `.ready` file is created last and is intended to be the completion signal for downstream tools such as Power Automate.

## Designed For Real-World Windows Use

- No admin rights required for normal use
- No browser extension required
- No cloud transcription dependency required
- CPU-only local transcription supported
- Portable usage supported
- Local model import supported when direct downloads are blocked
- Writable runtime data stays outside the installed app files for managed installs

## Important Notes

- A valid local Whisper model is required for transcript generation
- Speaker labeling is optional and remains best-effort
- Assisted auto-detection currently focuses on Teams desktop and Google Meet only
- Manual recording works more broadly than assisted auto-detection
- Transcript quality depends on the chosen model and source audio quality
- There is no full transcript text-editing workflow in the app yet

## Compliance Notice

Users are responsible for complying with applicable recording, privacy, employment, and consent laws and workplace policies. Tell participants when they are being recorded and obtain consent where required.
