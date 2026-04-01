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
- Specific quiet Teams desktop meetings now auto-start after sustained meeting-window evidence when Windows can still attribute the quiet session to Teams, so muted or listen-heavy calls no longer wait for a later audio spike before recording begins while stale Teams titles alone no longer trigger false starts
- Auto-started Teams recordings now stop after a bounded quiet grace period when only a stale same-title Teams window remains, and matching Teams shell/chat/share surfaces only prolong the session when recent capture activity still exists, instead of letting stale post-call UI keep the live-call timer refreshed forever after the meeting has ended
- Google Meet continuity now treats browser-title variants that share the same Meet code as the same live call, which prevents a single meeting from being split when Edge or Chrome changes the tab caption mid-call
- Chromium browser tab inspection now uses a short timeout and cooldown, so a stuck browser automation query cannot stall later Teams or Google Meet detection scans for minutes
- Windows audio-session probing now uses a short timeout and cooldown too, so a hung render-session query cannot delay Teams auto-start by several minutes before the detector notices the live call
- Replaces launch-time `Process` enumeration with top-level window discovery for meeting detection and existing-instance activation, and keeps live Teams roster capture disabled by default so extra permission prompts are less likely
- Auto-stops recordings when meeting signals disappear
- Auto-stop now shows a visible `Home` countdown for auto-started sessions and flips into `Auto-stopping` immediately when timeout expires instead of looking idle while finalization runs
- Microphone capture can now be turned on or off during an active recording, with the change applying from that moment forward while also updating the default for later recordings
- Manual recordings now keep meeting detection alive in the background so a session you started yourself can still switch in place to a stronger detected Teams or Google Meet meeting identity later, without inheriting auto-stop
- Manual recordings and already-managed live sessions now also switch in place when a clearly different specific Teams or Google Meet call takes over later on the same platform, and the stale old title draft is reset to the new meeting title instead of staying pinned to the prior call
- Shows a simplified Technical Studio Home console with separate `Title`, `Client / project`, and `Key attendees` fields, plus the live audio graph, start/stop controls, and quick microphone/auto-detect settings
- Shows a live elapsed recording timer on `Home` during active capture so users can immediately see how long the current session has been running
- Loads the Home shell first and finishes heavier Meetings enrichment in the background so the app becomes interactive sooner after launch
- Shows the main shell before transcript sidecar migration and published-meeting repair run, so startup and installer relaunch are much less likely to strand a headless primary process before the window appears
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
- Meetings grouping now supports client / project and attendee in addition to week, month, platform, and status
- Exposes quick actions for opening audio and transcript artifacts
- Lets users rename meetings while keeping published artifacts aligned
- Supports transcript regeneration for retryable failed or incomplete sessions
- Supports merge, split, archive, and permanent delete maintenance actions
- Surfaces cleanup recommendations and safe-fix flows for likely false starts, duplicates, or repairable meetings
- On first launch after the updated build, the one-time published cleanup repair can now collapse longer same-title split chains from repeated stop/start fragmentation into one merged publish
- Supports project tagging and post-publish speaker-name maintenance for diarized transcripts

### Setup, settings, and help

- Simplifies the primary shell to `Home` and `Meetings`
- Uses one visible `Home` / `Meetings` strip backed by the built-in WPF tab host so `Home` paints sooner and the primary shell stops relying on brittle custom tab-content routing
- Replaces the clipped settings section tabs with a dedicated section button strip so labels stay readable at the default window size
- Simplifies the Home quick-setting wells by removing the oversized secondary headings and highlighting the active `On` / `Off` state directly in the selected button
- Gives the Home recording console a wider fixed-width floor at the default window size and keeps the title editor single-line so the panel does not expand while you type
- Moves the Home quick settings back underneath the main recording console, widens the default app window, centers the primary tab labels more cleanly, and keeps the shell status footprint stable when a mic-off warning appears
- Tightens shared control alignment so the primary `Home` / `Meetings` tab labels stay centered and the Meetings dropdowns plus project picker keep their selection text vertically centered inside the control chrome
- Marks `Client / project` and `Key attendees` as optional metadata so only the session title reads as required on Home
- Removes the redundant required/optional helper line under the Home metadata fields, stretches the recording console across the full tab canvas, and keeps the Home quick-setting toggles at a stable fixed width while you switch states
- Lets `Key attendees` accept comma- or semicolon-delimited names so they remain separate searchable attendees later in `Meetings` and can still reconcile to fuller Outlook names when enrichment finds a better match
- `Client / project` and `Key attendees` on `Home` now save into the active recording shortly after you edit them, so manual recordings no longer wait until stop before those optional fields persist
- A manual recording that is later strongly reclassified to an active Teams or Google Meet call now adopts the existing meeting-end auto-stop path, so closing the meeting no longer leaves the recording open to capture unrelated browser audio afterward
- Outlook attendee enrichment and backfill now require a reasonable meeting-identity match before they merge attendee names, which prevents a 1:1 recording from inheriting people from an unrelated overlapping calendar invite
- Outlook calendar lookups now reuse a per-day cached appointment snapshot and temporarily back off after Outlook failures, which reduces repeated COM automation pressure from live title fallback plus attendee backfill and helps prevent the recurring "Outlook has exhausted all shared resources" warning
- The packaged launcher no longer prints a recurring startup warning just because no Whisper model is installed yet; missing models remain an in-app setup task, while real missing runtime or worker-payload failures still block launch
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
- The MSI now allows refreshed same-version release assets to overwrite the installed app binaries on reinstall, fixing cases where `release-source.json` updated but `MeetingRecorder.App.exe` and `MeetingRecorder.ProcessingWorker.exe` stayed on the previous `0.3` build
- Background publish processing now resolves `MeetingRecorder.ProcessingWorker.exe` from the installed app directory instead of the transient single-file extraction folder, fixing queued sessions that could otherwise stay unpublished after recording stops
- Portable and installer bundles now ship the full worker sidecar payload, including `MeetingRecorder.Core.dll` plus the worker `.deps.json` and `.runtimeconfig.json`, and managed-install repair restores those files if they go missing so queued same-day sessions can publish after restart without manual bundle repair
- If the worker crashes inside optional speaker labeling, the queue now stamps that session with an internal skip-label override, saves a durable per-session transcript snapshot before diarization begins, retries it once without diarization, repairs older stale post-transcription queue items on startup, and resumes those already-transcribed repaired items ahead of untouched queue work so transcript/audio publish can still complete instead of leaving meetings stuck in `Processing` or repeatedly retranscribing the same session
- `Settings > Advanced` now exposes `Background processing mode` and `Speaker labeling mode`, with defaults of `Responsive` plus `Deferred` so the app reduces worker priority, caps CPU usage, pauses new backlog work during active recordings, and prioritizes publish-first behavior over inline speaker labeling
- The shell now surfaces backlog status through a compact header queue chip plus a Meetings processing strip, including `Processing` / `Paused` / `Queued` state, pause reason, remaining queue size, active stage, elapsed processing time, and approximate current-item plus overall ETA so users can understand long backlogs without guessing
- Startup and pre-worker maintenance now clean stale unlocked files from the diarization and transcription temp roots, including a one-time post-upgrade recovery pass so orphaned temp files stop consuming large amounts of disk space
- Supported-call detection now runs its heavy scan off the WPF dispatcher, skips overlapping scans instead of stacking them, bounds both browser-tab inspection and audio-session probing, and defers non-urgent Meetings refreshes while `Home` is active or a recording is still live so typing, call starts, and stop flows stay responsive under load
- Meetings now prefer real published audio/transcript artifacts over stale queued imported-source reprocessing manifests, and startup archives those superseded imported work folders into maintenance storage so false `Queued` / `Missing` rows drop out of the visible backlog instead of lingering indefinitely
- Startup now ignores early `TabControl` selection-change events until the Home shell finishes initializing, fixing a `NullReferenceException` that could otherwise surface as the generic “unexpected UI error” dialog immediately after launch
- The shared deployment CLI bundle now explicitly carries `System.IO.Pipelines`, fixing the old local-deploy / `install-bundle` failures that could crash late while saving install provenance after the new app files were already staged
- `Deploy-Local.ps1` used to install by default without auto-launching the app again, which avoided local-redeploy races that could leave duplicate background `MeetingRecorder.App` processes and break existing-instance activation; that repo-only local deploy path is now intentionally disabled in favor of MSI and in-app upgrade validation
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
