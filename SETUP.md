# Meeting Recorder Setup

This guide explains how to run the current app, set up the Whisper model, validate the pipeline, and recover from failed transcription runs.

Legal notice: You are responsible to comply with all applicable recording, privacy, employment, and consent laws and workplace policies in your location. Tell participants when they are being recorded and obtain consent where required. This app is not legal advice.

## 1. Recommended Deployment

For most Windows installs, the recommended path is now the per-user MSI installer.

Recommended user flow:

- download `MeetingRecorderInstaller.msi`
- run it directly

That installer path:

- installs the app into `%USERPROFILE%\Documents\MeetingRecorder`
- keeps writable runtime data such as config, logs, models, work files, and install provenance under `%LOCALAPPDATA%\MeetingRecorder`
- creates user-scope `.lnk` Start Menu and Desktop shortcuts that point to the safe launcher under `%USERPROFILE%\Documents\MeetingRecorder`
- skips the stock license-agreement page so the install flow stays short
- shows a final completion screen so users can confirm the install succeeded
- offers a `Launch Meeting Recorder` checkbox on that final screen for first launch, checked by default
- uses an installed restart-aware launcher for that finish-screen action, so if Meeting Recorder is already open and idle the MSI can ask the running instance to close and then relaunch the updated app instead of only sending a second-launch activation request
- keeps writable runtime data outside the installed app files
- is built as a per-user MSI and the release pipeline stamps its MSI summary metadata for no-elevation installs, so normal installs should not require a UAC elevation prompt
- enables verbose Windows Installer logging in `%TEMP%` for direct MSI troubleshooting
- is the preferred release asset in the current build and release flow
- keeps shell integration limited to the current user rather than requiring per-machine shortcuts

If you still want the custom bootstrapper path, the EXE remains available:

- `MeetingRecorderInstaller.exe`

That installer EXE:

- prefers a colocated `MeetingRecorder-*.zip` package plus local bootstrap scripts when they ship beside the EXE
- otherwise downloads `Install-LatestFromGitHub.cmd` plus its companion PowerShell script
- launches that command bootstrap path and then steps aside
- does not extract ZIPs, copy files into `Documents\MeetingRecorder`, launch the app, or create shortcuts itself
- explains in the UI that app files land in `%USERPROFILE%\Documents\MeetingRecorder` while writable config, logs, models, and work files live in `%LOCALAPPDATA%\MeetingRecorder`
- uses the same Technical Studio visual system as the main desktop app so setup, status, logs, and fallback actions read consistently
- is now the optional convenience path rather than the preferred MSI install path

If the EXE path is blocked, the fallback path is still available:

- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`

If `Install-LatestFromGitHub.ps1` is not already next to the command file, the command downloads that script from the latest GitHub release automatically. When the script is run from a packaged local installer folder and a sibling `MeetingRecorder-*.zip` is present, it now installs that local package instead of redownloading the remote release.

If you already have a downloaded release ZIP, you can still use the manual path:

- extract `MeetingRecorder-v<version>-win-x64.zip`
- run `Install-MeetingRecorder.cmd`

By default, the installers preserve existing user data on update so recordings, transcripts, logs, models, and config are not wiped. The thin script/bootstrap wrappers now delegate install and update execution to the bundled `AppPlatform.Deployment.Cli` helper instead of mutating app config themselves.
The portable bundle now ships with `bundle-integrity.json`, and the shared deployment CLI validates that manifest before it touches the managed install root.
The shared deployment engine also persists install provenance for diagnostics under `%LOCALAPPDATA%\MeetingRecorder\install-provenance.json`.

User-facing installer and updater rule:

- write a persistent diagnostic log under `%TEMP%\MeetingRecorderInstaller`
- suppress raw PowerShell web-transfer progress noise so the console focuses on installer status
- forward `--pause-on-error` into the deployment CLI so the final child console also stays open on any failed CLI command, not only thrown exceptions
- pause on error so users can read the failure and note the diagnostic log path
- do not wait forever on app shutdown during update apply; if the running app does not exit promptly, the updater should escalate the release sequence and then fail with a clear message instead of hanging indefinitely
- do not enumerate, close, or kill Meeting Recorder processes from the installer EXE path; the installer should only request cooperative shutdown and then rely on normal file replacement or a clear retry message
- the installer EXE should remain a thin launcher only; all actual install and update writes flow through `AppPlatform.Deployment.Cli`
- if bundle validation fails, the shared deployment CLI should abort before touching `Documents\MeetingRecorder` and log the exact missing or mismatched file

For newer managed installs:

- published meeting audio defaults to `Documents\Meetings\Recordings`
- published transcripts default to `Documents\Meetings\Transcripts`
- transcript `.json` and `.ready` sidecars default to `Documents\Meetings\Transcripts\json`
- archived cleanup items default to `Documents\Meetings\Archive`
- config, logs, work artifacts, and models stay outside the installed app files

The app can also migrate older portable data forward on first launch when needed.

If you prefer not to install, the extract-and-run portable folder still works. In that case, run:

- `Run-MeetingRecorder.cmd`

The default portable bundle is `self-contained`, so most laptops do not need a separate .NET Desktop Runtime install.
That default self-contained bundle now publishes the WPF shell as a single-file `MeetingRecorder.App.exe`, while `AppPlatform.Deployment.Cli`, `MeetingRecorder.ProcessingWorker`, scripts, and `MeetingRecorder.product.json` remain as external bundle files.

If you are rebuilding from source locally, note the difference between artifacts and the live installed app:

- `.artifacts\publish\win-x64\MeetingRecorder` is only the freshly published bundle output
- `%USERPROFILE%\Documents\MeetingRecorder` is the canonical managed install root that the Start Menu and live app should use
- after a local source build, run `powershell -ExecutionPolicy Bypass -File .\scripts\Deploy-Local.ps1` to update the live managed install and verify the deployed `MeetingRecorder.App.exe`, `MeetingRecorder.product.json`, and `.lnk` shortcuts match the new bundle

## 2. Data Layout

### Managed installs

For newer managed installs, the default writable layout is split between published outputs and app-managed runtime data.

Stopping a long recording can still take time while the capture devices and chunk writers finish closing, but the app should remain responsive instead of appearing frozen. Auto-started sessions now show a visible `Home` countdown before timeout and switch into `Auto-stopping` immediately when the timeout expires, while attendee enrichment continues in the background queue.

Published outputs:

- `%USERPROFILE%\Documents\Meetings\Recordings`
- `%USERPROFILE%\Documents\Meetings\Transcripts`
- `%USERPROFILE%\Documents\Meetings\Transcripts\json`
- `%USERPROFILE%\Documents\Meetings\Archive`

App-managed runtime data:

- `%LOCALAPPDATA%\MeetingRecorder\config`
- `%LOCALAPPDATA%\MeetingRecorder\logs`
- `%LOCALAPPDATA%\MeetingRecorder\work`
- `%LOCALAPPDATA%\MeetingRecorder\models`

### Portable mode

In portable mode, the app keeps its data under the app folder.

For a raw extract-and-run folder, the layout exists under that extracted app folder:

- `<AppFolder>\data\config`
- `<AppFolder>\data\logs`
- `<AppFolder>\data\audio`
- `<AppFolder>\data\transcripts`
- `<AppFolder>\data\transcripts\json`
- `<AppFolder>\data\work`
- `<AppFolder>\data\models`

The portable behavior is activated by the `portable.mode` marker file shipped with the bundle.
The `bundle-mode.txt` file records whether the bundle is `self-contained` or `framework-dependent`.

The portable bundle also includes:

- `Check-Dependencies.ps1`
- `Install-Dependencies.cmd`
- `Install-Dependencies.ps1`
- `SETUP.md`

## 3. First Launch Checklist

1. Install with `MeetingRecorderInstaller.msi`, `MeetingRecorderInstaller.exe`, `Install-LatestFromGitHub.cmd`, `Install-MeetingRecorder.cmd`, or run `Run-MeetingRecorder.cmd` from the portable folder.
2. If the dependency checker reports a missing runtime, run `Install-Dependencies.cmd`.
3. Open `Settings` from the header and review `Setup`.
4. Make sure the active Whisper model looks correct.
5. If the output folders are not acceptable, review `Settings > Files`.
6. If needed, choose a discovered local model with `Use Selected Model`.
7. Install or import the Whisper model until the app shows `Model status: ready`.
8. Record a short manual test.

The dependency checker currently verifies:

- the app launcher expects `MeetingRecorder.App.exe` to be present in the managed install root
- the transcription worker can launch from either `MeetingRecorder.ProcessingWorker.exe` or `MeetingRecorder.ProcessingWorker.dll`
- the .NET 8 Desktop Runtime is installed when the bundle is framework-dependent
- the .NET 8 Desktop Runtime is also installed when the bundle has to fall back to DLL launches because the apphost executables are missing
- whether a configured local Whisper model is present

## 3.1 In-App Navigation

The app now keeps the main workflow in two primary destinations inside one visible segmented top navigation strip:

- `Home` for the current recording console: separate `Title`, `Client / project`, and `Key attendees` fields, the detected audio source summary, live audio graph, start/stop controls, and quick settings for microphone capture and auto-detection
- `Meetings` for the published meetings workspace, grouped browsing, cleanup review, quick actions, and compact meeting drafts
Capability setup now lives in `Settings > Setup` when you need to make transcription or optional speaker labeling ready.
When you open `Meetings`, the app now shows the current published list first and then fills in cleanup suggestions plus recent Outlook attendee backfill in the background. Repeated opens reuse cached no-match results for unchanged historical meetings so large libraries stay responsive.

Secondary maintenance and support actions live in the header:

- `Settings` for Setup, General, Files, Updates, and Advanced, surfaced through a dedicated section button strip so labels stay readable at the default window size
- `Help` for About details, setup/help links, logs/data folder shortcuts, and release notes entry points

When auto-detection is on, the app now also tries to attribute active Windows render audio to a likely process, meeting window, or Chromium Meet tab. The compact summary appears on `Home`, and `Help` includes the current app/window/tab match plus confidence when attribution is available.
When an auto-started session loses strong meeting signals, `Home` now shows the auto-stop countdown directly in the recording console instead of only writing it to logs.
When attendee enrichment finds fuller Outlook or Teams names, the app now merges those into `Key attendees` with reasonable partial-match promotion so a short typed name like `Pranav` can become `Pranav Sharma` instead of duplicating both.

## 4. Dependency Installer

`Install-Dependencies.cmd` opens:

- this `SETUP.md`
- the official .NET 8 download page
- the official Microsoft Visual C++ x64 redistributable installer

For the default self-contained portable bundle, the .NET download is usually not required, but the helper remains useful for fallback troubleshooting on Windows systems that still need the runtime helpers.

## 4.1 Installer Options

`Install-MeetingRecorder.cmd` and `Install-LatestFromGitHub.cmd` can both forward PowerShell arguments to the thin wrapper script when you need a custom location or behavior.

`MeetingRecorderInstaller.msi` is the preferred install path for current releases. It now ends on an explicit success screen instead of closing silently, and that screen can launch the app immediately after a fresh install. `MeetingRecorderInstaller.exe` remains available as the optional thin launcher path, and the script installers remain the canonical bootstrap path when MSI is blocked or a user wants a more explicit script-based install. The EXE shell shares the same Technical Studio visual language as the main app, including a scrollable shell layout and grouped actions so progress, retry paths, and fallback steps stay readable at the default window size, while the MSI still uses the native Windows Installer dialog style.
The EXE launcher no longer deploys the managed app itself. Its job is to launch the current command bootstrapper, preferring the bundled local package when one is present beside the EXE and otherwise falling back to the GitHub bootstrap path, then let the shared deployment CLI own the actual install/update work.

The MSI welcome flow now skips the license-agreement screen entirely and goes straight from welcome to ready-to-install.
If the MSI launches the app immediately after an install or update while the previous app instance is still open, it now launches through `Launch-MeetingRecorder-AfterInstall.vbs`. That wrapper writes a short-lived relaunch marker under `%LOCALAPPDATA%\MeetingRecorder`, lets the app request a cooperative installer shutdown of the running instance, waits for the primary-instance lock to clear, and then starts the fresh app instance. If the app is still busy recording or processing, the restart request is deferred and the current instance stays alive.

Examples:

- install to a custom folder:
  - `Install-MeetingRecorder.cmd -InstallRoot "D:\Apps\MeetingRecorder"`
- skip auto-launch after install:
  - `Install-MeetingRecorder.cmd -NoLaunch`
- skip Desktop shortcut creation:
  - `Install-MeetingRecorder.cmd -NoDesktopShortcut`

Current limitation:

- the thin wrapper scripts no longer change launch-on-login during install
- if you need launch-on-login, change it from `Settings > General` after the app is installed

## 4.2 GitHub-Based Updates

The same GitHub bootstrap path can be used later for updates.

If Meeting Recorder is already installed and you want to apply the latest GitHub release without manually moving ZIP files around, run:

- `MeetingRecorderInstaller.exe`

or the script fallback:

- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`

If an installer or updater console fails, copy the diagnostic log path shown in the console window before closing it. Direct MSI installs now also emit Windows Installer logs under `%TEMP%`.

The installer preserves the existing portable `data` folder during updates, so recordings, transcripts, logs, models, and config are kept.
The shared deployment CLI now updates the managed install in place instead of renaming the whole `Documents\MeetingRecorder` root first, which makes updates more tolerant of locked files under the preserved `data` tree.
The actual apply/install work is now delegated to the shipped `AppPlatform.Deployment.Cli` helper so the script layer stays thin and reusable.
That same CLI-first rule also applies after MSI-origin installs, so the app can update through the shared ZIP/CLI path without switching to MSI-based in-app patching.
The in-app `Install Available Update` button now resolves that CLI helper from the installed `MeetingRecorder.App.exe` directory instead of the temporary single-file extraction folder, so the updater can still launch correctly from the packaged managed install.
Same-version pending updates are now only cleared when their published-at and asset-size identity matches the installed build, so a refreshed `0.x` package can still replace an older binary instead of being skipped just because the display version text matches.
Automatic updates are still allowed, but the app now refuses installer shutdown handoff while a recording or background transcript-processing job is active, so an update cannot forcibly close the app in the middle of active work.
The `Run-MeetingRecorder.cmd` launcher now waits briefly for `MeetingRecorder.App.exe` to reappear before it shows the missing-apphost error, which helps during short install or update handoff windows.
The EXE shell keeps the `Try backup CMD installer` action available after a handoff so you can still trigger the fallback path if the command window fails later.
The `Install-LatestFromGitHub.cmd` wrapper now preserves the real PowerShell/bootstrap exit code in its local-script path, so a successful install no longer prints a stale generic failure message afterward.
Normal in-app update installs do not use `Deploy-Local.ps1`; that script remains a repo-only developer utility for source-tree testing.

## 5. Whisper Model Setup

App releases now keep Whisper models separate from the main installer bundle so installs and updates stay lighter.

`Settings > Setup` now opens as a dedicated guided surface with two sections:

- `Transcription`
- `Speaker labeling`

Inside `Transcription`, the app presents guided readiness content first, then keeps alternate/manual paths available when you need a different model source or an existing local file.

Built-in automatic downloads in `Setup` use GitHub release assets only. If GitHub is blocked, use an approved local file instead.

### Option A: Download from the current GitHub release

Use this on first install when the laptop can reach GitHub Releases.

1. Open `Settings`.
2. Open `Setup`.
3. Review `Transcription`.
4. Click `Download Recommended Model`.
5. Wait for the status message.
6. Confirm the `Transcription` section shows transcription as `Ready`.

Recommended order:

- `ggml-base.en-q8_0.bin` for most laptops
- `ggml-small.en-q8_0.bin` if you want more accuracy and can tolerate a larger/slower model
- `ggml-tiny.en-q8_0.bin` when you want the smallest possible download

### Option B: Use an already-installed local model

Use this when the model is already present under:

- `<AppFolder>\data\models\asr`

In `Settings > Setup`:

1. Review `Transcription`.
2. Select the model you want.
3. Click `Use Selected Model`.

### Option C: Import an existing file

Use this when local policy blocks the GitHub download or you have a model file from another approved source.

1. Acquire a valid ggml `.bin` file.
2. Open `Settings`.
3. Open `Setup`.
4. Review `Transcription`.
5. Click `Import Existing File`.
6. Select the `.bin` file.
7. Wait for validation to complete.
8. Confirm the `Transcription` section shows transcription as `Ready`.

### Manual fallback

If needed, you can place the model file directly at the configured path.

Default portable path:

- `<AppFolder>\data\models\asr\ggml-base.bin`

Default managed path:

- `%USERPROFILE%\Documents\MeetingRecorder\data\models\asr\ggml-base.bin`

The app treats tiny files as invalid and will not accept an HTML or proxy error page saved with a `.bin` extension.

## 5.1 Speaker Labeling (Optional)

Speaker labeling is the optional diarization model-bundle path. It can group transcript text under speaker labels such as `Speaker 1` and `Speaker 2`, but normal transcription still works without it.

Built-in automatic speaker-labeling downloads use GitHub release assets only. Alternate public download locations, when shown, are curated links and may currently be unavailable.

`Settings > Setup` presents this as a guided checklist:

1. `Get the diarization model bundle`
2. `Install or import it`
3. `Confirm speaker labeling is ready`

Use `Local Help First` when you need the bundled instructions for an approved local bundle or matching supporting files.

Recommended path:

1. Open `Settings`.
2. Open `Setup`.
3. Open `Speaker labeling`.
4. In `Recommended model bundle`, click `Download Recommended Bundle`.
5. Wait for the status update.
6. Confirm the `Speaker labeling` section now shows speaker labeling as `Ready`.

If GitHub is blocked or no recommended bundle is loaded:

1. Open `Settings`.
2. Open `Setup`.
3. In `Speaker labeling`, click `Open local setup guide`.
4. Review `Alternate public download locations`.
5. If the list says `No vetted public mirror configured yet.`, use the local guide plus `Import Existing File` after you obtain an approved local diarization model bundle or matching supporting assets.
6. Use `Open Asset Folder` if you need to inspect or manage the installed files manually.

Advanced details:

- `Advanced` shows the configured asset folder path, the last GPU fallback details, and the raw readiness details
- the recommended bundle is preferred because it installs the segmentation model, embedding model, and bundle manifest together
- the app looks for the bundled local `SETUP.md` first and only falls back to GitHub help when the local guide cannot be found
- `Alternate public download locations` can legitimately show `No vetted public mirror configured yet.` when no curated mirror is configured
- the alternative asset picker is mainly for cases where you already know you need a specific supporting file instead of the bundle

GPU acceleration:

- `Settings` now includes `Use GPU acceleration for speaker labeling`
- when enabled, the worker tries DirectML on compatible Windows GPUs and falls back to CPU automatically
- when disabled, diarization always stays on CPU
- the diarization `Advanced` panel records the last effective provider and any fallback message reported by the worker

## 6. Settings

Open `Settings` from the header to review or change settings through these grouped sections:

- `Setup`
- `General`
- `Files`
- `Updates`
- `Advanced`

Use `Settings > Setup` when you need to make transcription or speaker labeling work. Use the other sections for recording behavior, meeting-file locations, updates, and troubleshooting.

The simplified `Home` surface keeps only the title editor, live audio graph, start/stop controls, and quick toggles for microphone capture and auto-detection. Setup and maintenance flows now live in the header surfaces.

Everyday recording and file settings stay visible first, while infrastructure and troubleshooting paths stay hidden until you open `Advanced`.

Installer, update, and local deploy flows now refuse to replace the app while an active recording or processing session is still running. Stop the live session first, then retry the install or update.

Settings available there include:

- microphone capture toggle
- auto-detection toggle
- audio threshold
- meeting stop timeout
- launch-on-login toggle
- calendar-based fallback title toggle
- attendee enrichment from Outlook and Teams when available
- audio output folder
- transcript output folder
- work folder
- model cache folder
- Whisper model path
- diarization asset path
- speaker-labeling GPU acceleration toggle
- update-check toggle
- auto-install toggle
- update feed URL

Use `Help` from the header when you need About details, the bundled setup guide, logs/data folder shortcuts, or the release page.

For auto-started Teams recordings, the app can briefly tolerate quiet patches and reduced meeting surfaces such as compact view, sharing controls, or a matching chat/navigation surface. If the actual meeting window disappears entirely, a lingering `ms-teams` background process by itself is no longer treated as enough evidence to keep the recording running.

The detector also now treats a plain Teams content window more cautiously when a matching `Chat | ... | Microsoft Teams` surface is present at the same time. That helps avoid auto-starting on Teams recording playback or chat-thread media that looks meeting-like but is not actually a live call.

## 7. Recording Validation

After the model is ready:

1. Start a short meeting or test call.
2. If the call is in Teams or Google Meet, you can turn on auto-detection and let the app watch for it automatically, or click `Start Recording` manually. New installs keep auto-detection off by default, and older configs are reset off once after this update, to avoid extra permission prompts on some systems. Google Meet detection can now follow Meet tabs inside a visible Chromium browser window even when the main window title is a shared Slides or Docs page, and the detector now uses Windows render-session ownership as a tie-breaker so a real Teams call can beat stale Google Meet browser titles. When Chromium render-session metadata is available, Google Meet auto-start now requires that metadata to look Meet-specific before browser audio is credited to the meeting, which helps prevent music or video playback in the same browser from triggering a false Meet recording. If an auto-started recording is first labeled from stale Google Meet browser evidence and a stronger Teams in-call window plus audio source shows up afterward, the app now reclassifies the live session in place instead of stopping and starting a second recording.
3. If the call is in Zoom, Webex, or another conferencing app, click `Start Recording` manually.
4. Optionally type a better meeting title on Home while recording.
5. Stop the recording, or let auto-stop trigger.
6. Confirm the final title is reflected in the published filename stem.

Expected outputs for managed installs:

- `<stem>.wav` in `Documents\Meetings\Recordings`
- `<stem>.md` in `Documents\Meetings\Transcripts`
- `<stem>.json` in `Documents\Meetings\Transcripts\json`
- `<stem>.ready` in `Documents\Meetings\Transcripts\json`

Expected outputs for portable installs:

- `<stem>.wav` in the audio folder
- `<stem>.md` in the transcripts folder
- `<stem>.json` in the transcripts `json` subfolder
- `<stem>.ready` in the transcripts `json` subfolder

If transcription fails, you should still see the final `.wav`.

## 8. Retrying Failed Sessions

If a recording produced audio but no transcript because the Whisper model was missing or invalid:

1. Fix the model in `Settings > Setup` until it shows `Model status: ready`.
2. Open the `Meetings` tab.
3. Select the failed meeting.
4. Confirm the row status shows `Failed`.
5. Click `Re-Generate Transcript`.

Transcript regeneration works when:

- the session work folder and `manifest.json` still exist, or
- the app can synthesize a new work manifest from an existing published audio file

## 8.1 Cleanup Recommendations

The Meetings tab now includes an ongoing cleanup recommendation system.

What it does:

- shows row-level recommendation badges such as `Archive`, `Merge`, `Rename`, `Split`, `Retry Transcript`, or `Add Speaker Labels`
- shows a lighter cleanup review tray for the current selection or the whole library
- offers a one-time historical review prompt with `Review Suggestions` and `Apply Safe Fixes`
- includes `Add Speaker Labels` as a safe fix only when the diarization model bundle is installed and the meeting has usable published audio plus a transcript that currently lacks speaker labels

Important behavior:

- cleanup recommendations and safe automatic fixes never permanently delete meetings
- safe automatic fixes only apply high-confidence archive, merge, and retry-transcript actions
- split and lower-confidence actions stay manual
- dismissed recommendations stay hidden until the underlying meeting data changes enough to produce a new recommendation fingerprint

Archive output is user-recoverable. The app moves artifacts into timestamped folders under `Documents\Meetings\Archive` instead of permanently deleting them. Current builds also treat older top-level folders such as `ArchivedRepairs`, `ArchivedFalseStarts`, and `ArchivedGenericCleanup` as legacy inputs that can be consolidated under the single `Archive` root.

## 8.2 Selected Meeting Inspector and Context Menu

The Meetings tab now keeps a focused meeting inspector and quick actions visible below the main list.

Meetings workspace behavior:

- grouped view is now the default for current releases, including a one-time migration for older configs
- grouped browsing opens only the first visible group by default and keeps the rest collapsed until you expand them
- `Expand All` and `Collapse All` appear when grouped browsing is active
- meeting timestamps in the list and inspector use the local system time zone with the current short date/time format
- a `Project` field can be applied or cleared for one meeting or multiple selected meetings from the focused tools area

What it shows for the focused meeting:

- title
- project
- started time
- duration
- platform
- attendee list
- publish status
- cleanup recommendation badges
- transcript model metadata
- speaker-label state

You can also right-click a meeting to open the context menu for common maintenance actions.

Single-meeting actions include:

- open transcript
- open audio
- open the containing folder
- copy transcript or audio paths
- apply the top recommended action
- rename and suggest title workflows
- re-generate transcript
- add speaker labels
- split
- archive
- delete permanently with typed confirmation

When multiple meetings are selected, the same context menu also exposes bulk actions such as:

- apply recommendations
- merge selected
- add speaker labels to selected
- archive selected
- delete selected permanently

The lower Meetings area is now intentionally simpler and more contextual:

- the cleanup review area is for bulk review, apply, dismiss, and open-related actions
- the selected-meeting inspector is for understanding the focused record
- the quick actions row handles the most common artifact and maintenance flows without making the page longer
- the compact meeting drafts only show the action cards that fit the current selection, such as title editing for one meeting or merge confirmation for multiple meetings

Separately, the Meetings tab context menu now includes a manual `Delete Permanently` action. That path is not part of cleanup recommendations, requires typing `DELETE` exactly to confirm, and irreversibly removes the published audio, transcript markdown, transcript JSON, ready marker, and linked session work folder when one still exists.

## 9. Power Automate Setup

Point your downstream flow at the transcripts sidecar folder and watch:

- `json\*.ready`

Use the shared stem to find:

- `<stem>.md`
- `json\<stem>.json`

The transcript JSON sidecar can also include persisted attendee names, including Outlook calendar attendees when appointment matching succeeds and best-effort Teams live roster names when the roster UI is exposed, the transcription model file name, whether speaker labels were present when the transcript was generated, and the compact detected audio source summary that won the meeting classification.
- `<stem>.wav`

`.ready` is created last and is the only supported completion signal for successful transcript output.

## 10. Troubleshooting

### No transcript was created

Check `Settings > Setup` first.

Common causes:

- Whisper model missing
- Whisper model invalid or too small
- network filtering replaced the download with an HTML or proxy page

Then inspect:

- managed install:
  - `%LOCALAPPDATA%\MeetingRecorder\logs\app.log`
  - `%LOCALAPPDATA%\MeetingRecorder\work\<session-id>\logs\processing.log`
- portable install:
  - `<AppFolder>\data\logs\app.log`
  - `<AppFolder>\data\work\<session-id>\logs\processing.log`

### The GitHub bootstrap installer failed before install started

Common causes:

- GitHub downloads are filtered by local network policy
- PowerShell web requests were blocked or the connection was closed early
- Windows certificate revocation checks against GitHub were blocked by the network

What to do:

- retry `MeetingRecorderInstaller.msi` if that path is allowed on the laptop
- if you prefer the bootstrapper, retry `MeetingRecorderInstaller.exe`
- if the EXE path is blocked, retry `Install-LatestFromGitHub.cmd`
- if that still fails, download the full `MeetingRecorder-v<version>-win-x64.zip` asset and run `Install-MeetingRecorder.cmd`

### The app says it is already running, but the existing instance did not respond

This usually means the previous instance is still winding down after a close or update handoff.

What the current app does:

- a new launch now waits briefly for the prior instance to finish exiting before it shows the warning
- if the existing instance owns the single-instance lock but does not answer the newer activation handshake yet, a new launch now also tries to find that already-running Meeting Recorder window and bring it to the foreground before it warns
- if the prior instance is still shutting down after an install or update handoff, a new launch now gives that shutdown a longer final grace window before it shows the warning
- if a recording or background transcript-processing job is active, the app now defers installer shutdown instead of letting an update forcibly close the current session
- if queued transcript processing faults while the app is closing, shutdown now logs and ignores that queued-worker failure instead of leaving a headless background process behind
- when shutdown cleanup finishes, the app now explicitly tears down any header surfaces and requests full WPF application shutdown instead of relying only on the main window close path
- same-version pending updates are only promoted to the installed state when their release identity matches the installed build, so refreshed packages with the same version still get a real install attempt

If you still see the warning:

- wait a few seconds and try again once
- check Task Manager for a lingering `MeetingRecorder.App.exe` process
- if it is clearly stuck, end that lingering process and relaunch the app

### Outlook calendar fallback did not add attendees

The Outlook calendar lookup is best-effort.

What the current app does:

- it can use a matching Outlook appointment as a fallback title source
- it can also persist attendee names from that appointment into the session manifest and published transcript JSON when the meeting can be matched successfully, including post-publish backfill for meetings that were listed without attendee metadata
- if Outlook is unavailable or the appointment cannot be matched confidently, recording and processing continue without attendee metadata

### Teams live attendee capture did not add attendees

The Teams live attendee capture path is also best-effort.

What the current app does:

- it only attempts live attendee capture while a Teams meeting is actively being recorded
- it uses Windows UI Automation against the visible Teams roster surface
- it merges discovered names into the active session manifest over time so they flow into the published transcript JSON later
- the behavior is controlled by the `Capture attendees from Outlook and Teams when available` setting in `Settings`

Common reasons no live attendees are captured:

- the roster/participants pane was never opened in Teams
- Teams did not expose the relevant participant elements through UI Automation on that machine
- endpoint controls or Teams updates changed the accessible UI structure

If that happens, the recording and transcript still complete normally; you just may not get attendee metadata from the live Teams roster for that session.

### The meeting recorded speaker audio but not the mic

Make sure `Enable microphone capture` is turned on in `Settings > General` before starting the next recording. That setting applies on the next recording, not mid-session.

### Will this work with Zoom, Webex, or another conferencing app?

Usually yes for manual recording.

- The app captures system output audio and optional microphone audio through the normal Windows audio stack.
- That means manual recording is not limited to Teams or Google Meet.
- Assisted auto-detection is currently implemented only for Teams desktop and Google Meet, so other conferencing apps should be started manually for now.

### Retry is disabled

Transcript regeneration will be unavailable when:

- the selected meeting has no matching work manifest and no usable published audio file
- the published audio file is missing or unreadable
- the session is already complete instead of failed

## 11. Managed-Device Notes

- No admin rights are required for normal use.
- No browser extension is required.
- No OneDrive dependency is required.
- CPU-only machines are supported.
- Model import exists specifically because some managed networks block public model downloads.
- Apps & Features can keep showing the MSI's original version even after a later CLI-driven update installs a newer app build.
