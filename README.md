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

- `Home` for the recording console: editable title, client/project, and key-attendee metadata, a live recording timer, the live audio graph, start/stop controls, quick microphone/auto-detect settings, and a compact capture-status well for the active loopback path
- `Meetings` for the recent-and-published meetings workspace and maintenance actions
- backlog processing now also surfaces through a compact mono queue chip in the header and a denser processing strip at the top of `Meetings`, so you can see whether the queue is `Processing`, `Paused`, `Queued`, or `Idle`, why it is paused, and approximate current-item / overall ETAs without opening extra tools; if the live worker snapshot has not reconnected yet after startup or recovery, the shell now falls back to saved queued/processing meeting rows and shows `ETA unavailable` instead of hiding backlog state entirely
- `Meetings` now also exposes a single persistent `Process ASAP...` lane for one queued or in-progress meeting at a time. You can mark a meeting to run next, optionally let just that rushed item ignore the normal `Responsive` live-recording pause rule, and clear or replace that rush request directly from the focused meeting actions or context menu.
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
- `Settings > Setup` now includes a Teams integration probe that captures the current local detector baseline, checks whether the Teams third-party API candidate exposes anything beyond manual controls on this machine, promotes that path only when readable meeting state is actually available, and otherwise keeps the current local Teams detector as the fallback
- Transcribes recordings offline with local Whisper models
- Publishes transcript artifacts in Markdown, JSON, and ready-marker form for automation
- Makes title, project, key-attendee, and attendee metadata searchable from the Meetings workspace
- Lets you rename meetings, regenerate transcripts, merge meetings, split meetings, and edit diarized speaker names from the Meetings tab
- Flags likely errant meetings with cleanup recommendations such as archive, merge, split, rename, retry-transcript, and add-speaker-label actions
- Offers a one-time historical cleanup review plus ongoing row badges, a lighter cleanup review banner, and compact action drafts that stay tucked away until needed
- On first launch after the updated build, the one-time published-meeting repair pass can now collapse a heavily stop/start-fragmented same-call chain into one merged publish and archive the tiny originals instead of only healing one adjacent pair, and it can also auto-merge a short-gap exact-title split when the preserved work manifests agree on the same specific meeting identity
- Repaired published meetings now derive their displayed duration from the repaired merged artifact payload when that payload no longer matches the original work-manifest timing for the reused stem
- Can ingest dropped audio files such as phone voice memos and queue them for automatic transcription
- Can download or import Whisper models from inside the app
- Guides model setup with a recommended Whisper download path and an optional speaker-labeling checklist
- Checks GitHub releases for updates and can retry a deferred downloaded update after restart when the app becomes idle

## Current Release

The current documented release line is **v0.3**.

## Installation Options

### Recommended for most users

- `MeetingRecorderInstaller.msi`

This is the preferred Windows installer for the current release flow, especially when a per-user MSI is more appropriate than a custom bootstrapper. The MSI installs the binaries under `%USERPROFILE%\Documents\MeetingRecorder`, adds a user-scope Start Menu entry, and leaves writable runtime data outside the installed files. A fresh MSI install now tries the recommended Standard transcription and Standard speaker-labeling downloads first, then offers first-install Advanced options to try Higher Accuracy or skip speaker labeling for now.

### Other supported install paths

- `Install-LatestFromGitHub.cmd`
  - script-based fallback when MSI installation is blocked by local security policy
- `Install-LatestFromGitHub.ps1`
  - direct PowerShell fallback for environments that allow signed scripts more readily than MSI or extracted ZIP installs
- `MeetingRecorder-v0.3-win-x64.zip`
  - manual extract-and-run fallback

Current installer/update authority:

- `AppPlatform.Deployment.Cli` is the only engine that writes or updates the managed install tree
- `Install-LatestFromGitHub.cmd/.ps1` are the canonical interactive bootstrap/update path
- in-app updates stay CLI-only even when the app was first installed through the MSI
- in-app updates now skip shell-shortcut repair during the silent apply path and relaunch the installed `MeetingRecorder.App.exe` directly instead of shell-launching `Run-MeetingRecorder.cmd`, which reduces Explorer-coupled security prompts on managed Windows laptops
- after the CLI promotes an updated bundle into `%USERPROFILE%\Documents\MeetingRecorder`, it revalidates the installed `bundle-integrity.json` contract and rolls back if required executables disappear before launch
- current releases no longer ship the deprecated EXE launcher; MSI, ZIP, and the command/bootstrap scripts are the supported install assets
- the default self-contained release now ships the WPF shell as a single-file `MeetingRecorder.App.exe`; the deployment CLI, worker, scripts, and product manifest remain external sidecars in the bundle
- the portable/installer bundle now copies the full processing-worker publish output, including `MeetingRecorder.Core.dll` plus the worker `.deps.json` and `.runtimeconfig.json`, so queued sessions can still publish after restart instead of crashing on a missing sidecar dependency

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
- Google Meet detection now relies on explicit browser titles plus Windows render-session metadata, so a Meet call can still be recognized when the visible Edge or Chrome window is a shared Slides or Docs page and Windows exposes Meet-specific session details
- Windows audio-session probing now also uses a short timeout and backoff window, so a hung render-session query cannot stall supported-call detection for minutes before an auto-started Teams meeting is noticed
- Windows audio-session probing now merges both the default multimedia and communications render endpoints, so Teams auto-start can recover when meeting audio moves onto a Bluetooth headset or other communications-only output after the original speaker path drops
- Live recording loopback capture now also prefers the active communications render endpoint when meeting playback is routed there, so recorded meeting audio is less likely to flatten out while only microphone room pickup remains
- Live recording now keeps a readable capture timeline that records which loopback endpoint was selected, when the app swapped endpoints, and whether a loopback fallback or swap failure happened during the session
- While a recording is live, the app keeps reevaluating the preferred render endpoint and can hot-swap loopback capture in place when Windows moves meeting playback to a better device, preserving earlier chunks and continuing the same session without a stop/start restart
- When a live loopback or microphone capture client dies unexpectedly, the recorder now closes the orphaned segment at the actual stop time, reopens capture in place on the best available endpoint, and avoids running the old and new Windows audio clients at the same time during a swap
- Auto-detection now also attributes active Windows render audio to a likely process, meeting window, or browser tab when Windows exposes enough metadata, and uses that source as a strong tie-breaker instead of a single shared audio bonus
- Google Meet auto-start now prefers Meet-specific browser-audio attribution when browser render-session metadata is available, but an explicit active `Meet - ...` browser window can still start when browser-family audio is active and exact tab attribution is unavailable
- Google Meet auto-start can now also begin from a high-confidence explicit Meet window even before render audio becomes active, including `Meet - ...` browser surfaces and `meet.google.com is sharing ...` share surfaces; generic browser windows or tab-only Meet hints still wait for stronger evidence
- Specific quiet Teams desktop meetings can now auto-start after sustained meeting-window evidence, but only when Windows audio attribution still matches a real Teams meeting session, including a quiet matched Teams session that is still present at `peak=0.000`; a stale remembered Teams window title on its own is no longer enough to start a recording
- Teams auto-detection now also recognizes newer in-call window titles that only show the attendee or meeting name without a visible `| Microsoft Teams` suffix, so quiet one-on-one and newer Teams call surfaces can still qualify for sustained quiet-meeting auto-start when Teams render-session evidence is present
- A specific Teams desktop meeting with live Teams audio attribution now also outranks a stale Google Meet browser candidate that no longer has attributed Meet audio, so an old Edge Meet tab cannot steal auto-start after you switch the real call over to Teams
- Auto-started Teams recordings no longer treat a stale same-title quiet window as a forever-positive signal; instead they use a bounded quiet grace period before auto-stop, matching Teams shell/chat surfaces still need recent Teams render activity, and the pinned Teams sharing control bar is treated as a continuation of the already active specific meeting so live screen sharing does not fragment one call into repeated stop/start sessions
- When a validated official Teams path is enabled, stale same-title Teams shells also stop refreshing continuity once the official lookup reports that no current meeting is active, but a current official match can still preserve the bounded quiet grace period during real low-audio patches
- Google Meet continuity now normalizes title variants that share the same Meet code, so browser captions like `...and 1 more page`, `...Work - Microsoft Edge`, and `...Camera and microphone recording...` stay attached to one live call instead of being treated as separate meetings
- Google Meet continuity now also treats browser share-surface titles such as `meet.google.com is sharing a window.` as part of the active call when the session is already pinned to a specific Meet meeting, which prevents screen sharing from prematurely aging an auto-started recording into auto-stop
- When microphone capture is enabled, the publish pipeline now reduces low-level mic bleed and short-lag correlated speaker bleed while strong loopback audio is present, which helps avoid speaker echo when the mic can hear meeting audio from speakers
- When microphone capture is enabled, the app can now also hot-swap to a better default Windows microphone device during a live recording, preserving earlier mic chunks in the same session instead of requiring a manual stop/start restart after a headset or dock microphone change
- If an auto-started recording is first classified from stale Google Meet browser evidence and a stronger Teams in-call window appears afterward, the app now reclassifies that live session in place instead of stopping and starting a second recording
- Manual recordings now keep assisted detection running in the background, so a session you started yourself can still reclassify in place when a stronger Teams or Google Meet meeting window appears later without opting that session into auto-stop
- Manual recordings now also switch in place when a clearly different specific Teams or Google Meet call takes over later, even if the replacement audio attribution is only medium-confidence, and the stale old title draft is reset to the new detected meeting title
- Manual recordings now also adopt a quiet specific Teams meeting when the detector still has matched Teams audio-session evidence at `peak=0.000`, so a live Teams call can replace a stale manual title even before Teams render audio climbs back above the normal auto-start threshold
- If a live manual-to-meeting takeover signal is currently good enough to reclassify but the persisted session still has not switched yet, the `Home` pending stem now reflects the detected meeting platform immediately and the stop/live-metadata paths retry that reclassification before publish so the output does not stay stuck on `manual`
- An active auto-started Teams recording now ignores weak unrelated Google Meet browser candidates during quiet patches, so a stale Meet tab can no longer trigger repeated stop/start churn on the live Teams call
- Auto-stop when meeting signals disappear
- Auto-stop now surfaces a live countdown on `Home` for auto-started sessions and switches immediately into an `Auto-stopping` state when the timeout expires, so controls disable before background finalization finishes
- Once that countdown is visible, weak quiet-continuation signals no longer silently reset it; only a real resumed meeting signal clears the countdown and keeps the recording alive
- A quiet Teams meeting with the same specific title can now also preserve the current session when Teams attribution still points at that meeting and recent microphone activity shows the call is still live, reducing false session splits during one-sided speaking stretches
- Continuity handling for quiet patches, compact-view surfaces, and sharing-control surfaces in Teams
- Teams chat-thread playback surfaces are now deprioritized so recording playback is less likely to auto-start as if it were a live meeting
- Bare Teams shell or navigation titles such as `Chat | Microsoft Teams` and `Activity | Microsoft Teams` are now ignored safely instead of crashing the background detection loop
- Lingering Teams background processes or weak process-only signals no longer keep an auto-started recording alive after the actual meeting window disappears
- Live audio activity graph during active capture

### Transcription

- Offline Whisper transcription through a separate worker process
- Support for downloadable and imported local ggml Whisper models
- Determinate download progress when the remote asset reports size
- Re-generate transcript action for failed or stale sessions

### Meeting Management

- Meetings workspace with a search/sort/group toolbar, a default grouped view for existing and new users, grouped browsing by week, month, platform, status, client / project, or attendee, and a calmer focused-detail area beneath the list
- Meetings workspace now publishes the current list first, then loads cleanup suggestions and recent attendee backfill in the background so the tab stays responsive on larger histories
- Recent ended sessions that are still queued, processing, finalizing, or failed in the work queue now remain visible in `Meetings` even before publish artifacts land
- Published audio/transcript artifacts now win over stale queued imported-source reprocessing manifests, so already-published meetings stay openable instead of regressing to false `Queued` / `Missing` rows
- Imported-source reprocessing manifests now also collapse back onto the original published-audio stem when they point at the same meeting, so `Meetings` does not show a second near-duplicate row just because a retry manifest used a slightly different title string
- Published meeting list with project tags, status, duration, compact local-time timestamps, compact artifact actions, and recommendation badges
- Grouped browsing now opens the first visible group by default, keeps other groups collapsed initially, and exposes quick `Expand All` and `Collapse All` controls
- Selected-meeting inspector showing attendees, project, recommendation badges, transcript model metadata, speaker-label state, the persisted detected audio source summary, and a capture-diagnostics timeline for the finished meeting
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
- In-app update asset selection now ignores bundled model and diarization ZIP assets on the GitHub release and only downloads the versioned `MeetingRecorder-v<version>-win-x64.zip` app bundle for apply-update handoff
- `Settings > Updates` now separates local install facts from release-package metadata: `Current Installation` shows the actual installed-on timestamp plus the installed package published-at and package size from managed install provenance, while `Latest GitHub Release` continues to show the remote package publish time and installer asset size
- In-app update handoff now resolves `AppPlatform.Deployment.Cli.exe` from the installed app directory via the running process path, so single-file app launches do not accidentally look for the updater helper inside the temporary `.net` extraction folder
- Launch-on-login registration now also resolves `MeetingRecorder.App.exe` from the installed app directory via the running process path, so single-file app launches no longer pin Windows startup to a temporary `.net` extraction folder
- Background publish processing now resolves `MeetingRecorder.ProcessingWorker.exe` from the installed app directory via the running process path, so queued sessions still publish correctly from single-file installs
- Managed install repair now restores the required worker sidecar payload if any of those files are missing from an existing install, instead of only checking for the worker `.exe`
- CLI-driven updates now repair existing Desktop and Start Menu launch surfaces and quarantine both legacy `%LOCALAPPDATA%\Programs\Meeting Recorder` installs and the older `%USERPROFILE%\Documents\Meeting Recorder` root, so stale launchers do not keep pointing at a missing apphost after update
- Same-version pending updates are only cleared when their published-at and asset-size identity matches the installed build, so rebuilt releases can still replace older binaries that report the same semantic version when you install them manually
- In-app update comparison now resolves the installed package published-at and asset-size identity from managed install provenance before falling back to config, so a successful same-version in-app update does not keep advertising the just-installed package as newer
- Background auto-install and pending-update retry are limited to true semantic-version upgrades, so a republished same-version build does not immediately try to update a freshly relaunched app against itself
- If the processing worker crashes inside optional speaker labeling, the queue now stamps that manifest with an internal skip-label override, retries it once without diarization, persists a durable per-session transcript snapshot before speaker labeling begins, repairs older stale post-transcription sessions on startup, and prioritizes those already-transcribed repaired sessions ahead of untouched queue items so audio and transcript publishing can still complete instead of leaving meetings stranded in the backlog
- `Settings > Advanced` now exposes `Background processing mode` (`Responsive`, `Balanced`, `FastestDrain`) and `Speaker labeling mode` (`Deferred`, `Throttled`, `Inline`) so you can trade backlog drain speed against overall machine responsiveness
- The default performance profile is `Responsive` plus `Deferred`, which pauses new background queue work while a live recording is active, launches the worker at reduced OS priority, caps worker thread usage, and lets audio/transcript publish finish before optional speaker labeling
- The shell now exposes queue-state visibility for that throttled backlog: the header chip stays visible whenever backlog exists, and the Meetings processing strip shows pause reasons such as `Paused by live recording`, the active item, current stage, elapsed processing time, and approximate per-item plus overall queue ETAs; when only persisted queued/processing rows are available, the shell still surfaces the backlog and labels the ETA as unavailable until the live worker snapshot reconnects
- When one meeting is marked `ASAP`, that same header chip and Meetings strip now call out the rushed title, whether it is bypassing the live-recording pause rule, and whether another interrupted item was safely requeued behind it. The rushed request is persisted in app config so it survives restart until the meeting finishes or you clear it.
- Startup and pre-worker maintenance now clean up stale unlocked temp files under `%LOCALAPPDATA%\\Temp\\MeetingRecorderDiarization` and `%LOCALAPPDATA%\\Temp\\MeetingRecorderTranscription`, including a one-time more aggressive recovery pass after upgrade so orphaned worker temp files do not keep filling the disk
- Startup now also archives superseded imported-source reprocessing work folders under `%LOCALAPPDATA%\\MeetingRecorder\\maintenance\\archived-imported-source-work` when the original published transcript artifacts already exist, so false backlog rows do not keep reappearing in `Meetings`
- Meeting detection now runs its heavy window/audio scan off the WPF dispatcher, skips overlapping scans instead of stacking them, and bounds both browser-tab inspection and audio-session probing so supported-call detection no longer freezes the shell or wait minutes for one stuck probe to finish
- Non-urgent Meetings refreshes are now coalesced while `Home` is active or a recording is still live, then caught up automatically when `Meetings` becomes visible again, so config saves and stop-time publish no longer interrupt typing or start/stop transitions
- The Home screen now uses a wider full-canvas recording layout that stretches across the available tab width, keeps the quick settings underneath the main console, and uses fixed toggle widths so the setting cards do not resize when states change
- A second launch now waits briefly for a winding-down prior instance before it reports that the app is still running
- MSI, ZIP, curated model downloads, and command bootstrap release assets

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

Manual recording works more broadly than assisted auto-detection. Auto-detection currently focuses on Teams desktop and Google Meet browser heuristics driven by visible browser titles plus Windows render-session metadata.

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
- a persisted loopback capture timeline describing the chosen endpoint, endpoint swaps, fallback events, and stop-time capture summary
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
- The shipped model catalog now defines two curated profiles for each capability: `Standard` and `Higher Accuracy`
- `Standard` transcription (`ggml-base.en-q8_0.bin`) and `Standard` speaker labeling (`meeting-recorder-diarization-bundle-standard-win-x64.zip`) are bundled into the main app payload for offline-first installs
- `Higher Accuracy` transcription (`ggml-small.en-q8_0.bin`) and `Higher Accuracy` speaker labeling (`meeting-recorder-diarization-bundle-accurate-win-x64.zip`) remain separate GitHub release downloads
- The MSI can offer Higher Accuracy downloads during first install, but install still completes if downloads are blocked and `Settings > Setup` resumes transcription setup at first launch when needed
- `Settings > Setup` separates `Transcription` and `Speaker labeling` into dedicated sections with curated `Use Standard`, `Use Higher Accuracy`, `Skip for now` for optional speaker labeling, `Import approved file`, and open-folder diagnostics actions for lay users
- `Settings > Setup` now also exposes `When to run speaker labeling`, so an existing install can switch directly between `Deferred`, `Throttled`, and `Inline` without leaving Setup; choosing or importing a speaker-labeling bundle from Setup auto-promotes `Deferred` to `Throttled` unless you later switch it back
- `Settings > Setup` now also includes `Teams integration`, where you can choose `Auto`, `Fallback only`, or `Third-party API`, then run a probe that saves the last probe time, strongest promotable path, and any block reason alongside the current Teams capability snapshot
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
- curated transcription and speaker-labeling profile selection
- diarization GPU acceleration preference
- microphone capture
- launch on login
- auto-detection behavior
- update checks and install behavior
- calendar-based title fallback
- attendee enrichment from Outlook and Teams when available
- audio threshold and meeting stop timeout

The app keeps only `Home` and `Meetings` in the primary navigation. `Settings > Setup` is where you make transcription, optional speaker labeling, and the Teams integration probe ready. The normal setup flow now stays on curated `Standard` vs `Higher Accuracy` choices plus approved-file import, while raw storage paths are read-only diagnostics in `Advanced`. `Settings > General`, `Files`, `Updates`, and `Advanced` handle recording behavior, storage, release behavior, and troubleshooting.

The `Home` console is intentionally minimal. It uses a wider full-width recording canvas, keeps `Microphone capture` and `Automatic detection` underneath the main panel in a compact side-by-side quick-settings row, treats `Client / project` and `Key attendees` as optional metadata next to the required title, keeps all three metadata fields editable during an active recording including manual starts, shows a live elapsed recording timer while capture is active, and keeps setup or update actions in the shell header status capsule when needed. `Key attendees` accepts comma- or semicolon-delimited names, stores them as separate attendee entries, and later reconciles them against richer Outlook or Teams attendee names when those are discovered. Microphone capture can also be turned on or off during an active recording, with the change applying from that moment forward while also updating the saved default.
While a recording is active, edits to `Client / project` and `Key attendees` are now saved back into the live session shortly after you type instead of waiting until stop.
The live capture-status well under `Detected audio source` keeps the active loopback endpoint visible in plain language, shows whether the app is in `Loopback live`, `Loopback + mic live`, `Swapping loopback`, or `Fallback capture active` mode, and lists the most recent capture events so users can tell when Windows moved the meeting onto a different playback device.
If a manual recording is later strongly reclassified to a supported Teams or Google Meet call, that session now also adopts meeting-end auto-stop behavior so it does not keep recording unrelated system audio after the call closes. If you manually stop a supported meeting while its tab or window is still open, the app now suppresses auto-restart for that same meeting until detection proves a different meeting context has taken over. If a clearly different specific supported call takes over later on the same platform while an already managed session is live, the app now closes the first session and starts a fresh recording for the new meeting instead of silently renaming the old one in place.
When automatic detection is turned off, the header status now switches into an explicit manual-mode state instead of continuing to imply that auto-detection is available.
The packaged startup launcher now stays quiet on normal success. If no Whisper model is installed yet, that remains an in-app setup state instead of printing a recurring startup console warning every time you launch the app.
Approximate queue ETA now also works for imported audio sessions that have not populated `EndedAtUtc` yet, as long as the merged WAV is available for duration inspection.

The `Settings` surface opens as a dedicated dialog from the header, keeps capability setup first under `Setup`, keeps everyday defaults under `General`, groups output paths under `Files`, keeps release behavior in `Updates`, and tucks infrastructure and troubleshooting paths under `Advanced`.

The header-level `Help` surface now opens as a dedicated dialog and owns About details, setup/help entry points, logs/data folder shortcuts, and release-page links.

Calendar title fallback is optional and soft-fail by design. Separately, attendee enrichment now defaults on and can merge Outlook appointment attendees plus best-effort live Teams roster names into meeting metadata when those sources are available. Outlook attendee enrichment now requires a reasonable meeting-identity match before it stamps names into the meeting, so a 1:1 recording does not inherit attendees from an unrelated overlapping calendar item. The Outlook lookup path now reuses a per-day in-memory appointment cache, coalesces concurrent same-day reads, only binds to an already-running Outlook session, performs the COM read on an STA thread, narrows the Outlook `Items` query to the relevant meeting window, cancels timed-out background reads instead of letting them continue, and enters a temporary backoff after short lookup timeouts or other Outlook failures. That keeps generic-title detection and attendee backfill from repeatedly reopening calendar automation work, reduces the chance of Outlook shared-resource warnings, and lets queue processing continue when Outlook is unhealthy. If Outlook access or Teams automation is unavailable, recording and detection continue normally.

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

