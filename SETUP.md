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
- tries the recommended Standard transcription and Standard speaker-labeling downloads into `%LOCALAPPDATA%\MeetingRecorder\models` during install
- shows a first-install `Choose model options` page where users can keep `Standard` or also ask setup to try one optional `Higher Accuracy` download for transcription and speaker labeling
- explains the tradeoff clearly: `Higher Accuracy` can improve transcript or speaker-label quality, but it uses a larger download and may lead to slower processing than the included `Standard` option
- keeps the install successful even if setup downloads are blocked, then tells the user to resume setup from `Settings > Setup`
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

If MSI is blocked, the fallback path is still available:

- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`

If `Install-LatestFromGitHub.ps1` is not already next to the command file, the command downloads that script from the latest GitHub release automatically. When the script is run from a packaged local installer folder and a sibling `MeetingRecorder-*.zip` is present, it now installs that local package instead of redownloading the remote release.

If you already have a downloaded release ZIP, you can still use the manual path:

- extract `MeetingRecorder-v<version>-win-x64.zip`
- run `Install-MeetingRecorder.cmd`

By default, the installers preserve existing user data on update so recordings, transcripts, logs, models, and config are not wiped. The thin script/bootstrap wrappers now delegate install and update execution to the bundled `AppPlatform.Deployment.Cli` helper instead of mutating app config themselves.
The portable bundle now ships with `bundle-integrity.json`, and the shared deployment CLI validates that manifest before it touches the managed install root.
The shared deployment engine also persists install provenance for diagnostics under `%LOCALAPPDATA%\MeetingRecorder\install-provenance.json`, including the last installed-on timestamp plus any trusted installed package published-at and asset size used by the in-app updater. MSI post-install provisioning now recreates that file when it is missing, and the app still keeps the startup repair as a safety net for older managed installs. If a fresh install still reaches the app without package metadata, the first successful `UpToDate` GitHub check now backfills the installed package published-at and asset size into `install-provenance.json` so the Updates tab and same-version comparison logic can recover durably instead of keeping those fields unknown.
The MSI uninstall path also preserves user data by design. Removing `Meeting Recorder` from Windows only removes the managed app files under `%USERPROFILE%\Documents\MeetingRecorder` plus the current-user shortcuts. It does not remove `%LOCALAPPDATA%\MeetingRecorder`, `Documents\Meetings\Recordings`, `Documents\Meetings\Transcripts`, or `Documents\Meetings\Archive`, so a fresh install can pick up your existing settings, models, logs, and published meeting outputs.
After a meeting publishes successfully, current builds keep the merged processing audio but prune the raw capture chunks from `%LOCALAPPDATA%\MeetingRecorder\work`, and startup also reclaims those raw artifacts from older already-published sessions.

User-facing installer and updater rule:

- write a persistent diagnostic log under `%TEMP%\MeetingRecorderInstaller`
- suppress raw PowerShell web-transfer progress noise so the console focuses on installer status
- forward `--pause-on-error` into the deployment CLI so the final child console also stays open on any failed CLI command, not only thrown exceptions
- pause on error so users can read the failure and note the diagnostic log path
- do not wait forever on app shutdown during update apply; if the running app does not exit promptly, the updater should escalate the release sequence and then fail with a clear message instead of hanging indefinitely
- do not enumerate, close, or kill Meeting Recorder processes from installer flows; the installer should only request cooperative shutdown and then rely on normal file replacement or a clear retry message
- all actual install and update writes flow through `AppPlatform.Deployment.Cli`
- the MSI post-finalize `provision-models` handoff now stays on the compact alias form and the deployment CLI now parses those advertised aliases correctly, so first-install provisioning cannot fail on an option-name mismatch or custom-action target overflow
- that MSI handoff now also passes the install root as `INSTALLFOLDER.` instead of a raw quoted trailing-backslash directory, so Windows command-line parsing cannot break the custom-action launch with `0x80070002`
- that MSI handoff now runs after `InstallFinalize` and is best-effort, so a provisioning launch or download failure no longer aborts the installer itself
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
That default self-contained bundle now publishes the WPF shell as a single-file `MeetingRecorder.App.exe`, while `AppPlatform.Deployment.Cli`, the full `MeetingRecorder.ProcessingWorker` publish output, scripts, and `MeetingRecorder.product.json` remain as external bundle files.

If you are rebuilding from source locally, note the difference between artifacts and the live installed app:

- `.artifacts\publish\win-x64\MeetingRecorder` is only the freshly published bundle output
- `%USERPROFILE%\Documents\MeetingRecorder` is the canonical managed install root that the Start Menu and live app should use
- local repo deployments are intentionally disabled for publishing validation; use the MSI install path or release bootstrap scripts when you need to test the managed install root

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

1. Install with `MeetingRecorderInstaller.msi`, `Install-LatestFromGitHub.cmd`, `Install-MeetingRecorder.cmd`, or run `Run-MeetingRecorder.cmd` from the portable folder.
2. If the dependency checker reports a missing runtime, run `Install-Dependencies.cmd`.
3. Open `Settings` from the header and review `Setup`.
4. Confirm `Transcription` shows either `Standard ready` or `Higher Accuracy ready`.
5. If the installer reported that a download did not finish, open `Settings > Setup` after launch. Recording stays blocked until transcription is ready, while speaker labeling can stay optional.
6. If you plan to rely on Teams auto-detection, open `Settings > Setup > Teams integration`, choose the preferred mode, and run the Teams probe so the app can capture the current local detector baseline, test whether the Teams third-party API candidate exposes readable meeting state on this machine, and fall back cleanly when it does not. The Setup card now also saves and shows `Last probe`, `Promotable path`, and `Block reason`.
7. Confirm `Speaker labeling` shows either `Standard ready`, `Higher Accuracy ready`, `Deferred`, or your preferred custom import state if you plan to use diarization.
8. Keep `When to run speaker labeling` on `Deferred` for the safest managed-laptop behavior. If you explicitly want new transcripts to include labels automatically, set it to `Throttled` or `Inline`; choosing or importing a speaker-labeling bundle no longer auto-promotes `Deferred`.
9. If the output folders are not acceptable, review `Settings > Files`.
10. If you need a custom approved model or bundle instead of the curated profiles, use `Import approved file` from `Settings > Setup`.
11. Record a short manual test.

If no Whisper model is installed yet, the app can still launch normally. The packaged launcher now keeps that as an in-app setup reminder instead of printing a startup console warning on every launch.

The dependency checker currently verifies:

- the app launcher expects `MeetingRecorder.App.exe` to be present in the managed install root
- the transcription worker payload includes `MeetingRecorder.ProcessingWorker.exe`, `MeetingRecorder.ProcessingWorker.dll`, `MeetingRecorder.ProcessingWorker.deps.json`, `MeetingRecorder.ProcessingWorker.runtimeconfig.json`, and `MeetingRecorder.Core.dll`
- the .NET 8 Desktop Runtime is installed when the bundle is framework-dependent
- whether a configured local Whisper model is present

## 3.1 In-App Navigation

The app now keeps the main workflow in two primary destinations inside one visible segmented top navigation strip:

- `Home` for the current recording console: separate `Title`, `Client / project`, and `Key attendees` fields, the detected audio source summary, live audio graph, start/stop controls, and quick settings for microphone capture and auto-detection
- `Meetings` for the recent-and-published meetings library, grouped browsing, queue status, cleanup review, artifact shortcuts, and `Open Details` for single-meeting transcript review and maintenance
Capability setup now lives in `Settings > Setup` when you need to make transcription or optional speaker labeling ready. The default setup path is intentionally simpler for non-technical users: pick `Use Standard`, `Use Higher Accuracy`, or `Import approved file` for transcription, and use `Skip for now` when you want to leave optional speaker labeling off. `Speaker labeling` now also includes a direct `When to run speaker labeling` selector so you can move between `Deferred`, `Throttled`, and `Inline` without leaving Setup. Recording and auto-detect stay blocked until transcription is ready.
When you open `Meetings`, the app now shows the current recent-and-published list first and then fills in cleanup suggestions plus recent Outlook attendee backfill in the background. Repeated opens reuse cached no-match results for unchanged historical meetings so large libraries stay responsive.
Recent sessions that have stopped recording but are still finalizing, queued, processing, or failed in the work queue now stay visible in `Meetings` even if their publish artifacts have not landed yet.
When the backlog includes repaired sessions that already finished transcription, startup now resumes those publish-ready items before fresh untouched queue work so the visible queue can shrink faster after a crash backlog.

Secondary maintenance and support actions live in the header:

- `Settings` for Setup, General, Files, Updates, and Advanced, surfaced through a dedicated section button strip so labels stay readable at the default window size
- `Help` for About details, setup/help links, logs/data folder shortcuts, and release notes entry points

`Settings > Advanced` now also exposes two machine-performance controls:

- `Background processing mode`
  - Each dropdown label shows the local thread budget, for example `Fast (8 transcription / 4 labeling)`.
  - `Light` (default): pauses new background queue work while a recording is active, lowers worker priority, and uses the smallest CPU budgets.
  - `Balanced`: keeps background work moving with moderate thread budgets.
  - `Fast`: favors queue throughput over responsiveness and uses normal worker priority.
  - `Maximum`: skips the live-recording pause, uses `AboveNormal` worker priority, and caps local work at up to 12 transcription threads and 6 speaker-labeling threads.
- `Speaker labeling mode`
  - `Deferred` (default): publish audio and transcript first, skip labels in the primary pass, and leave `Add Speaker Labels` for manual follow-up.
  - `Throttled`: run speaker labeling automatically after transcription while the selected background processing mode controls thread budgets.
  - `Inline`: keep speaker labeling in the primary pass for labeled output sooner, with processing speed controlled by the selected background mode.

The responsive defaults are intentional: the app now prioritizes keeping the machine usable during active work over draining the backlog as quickly as possible.
That same responsiveness rule now applies to the shell: supported-call detection runs off the foreground thread, and routine Meetings refreshes can wait until `Meetings` is visible instead of interrupting editing or start/stop flows on `Home`.

For one urgent meeting, use `Process ASAP` from the meeting detail window or `Process This ASAP...` from the context menu. For a whole backlog, use `Rush Backlog...` in the Meetings processing strip. The prompt offers `This backlog only`, `This and future meetings`, and `Cancel`; the future option also saves `Speaker labeling mode` as `Deferred`. Rush Backlog does not interrupt active transcription, but if the current worker is already in speaker labeling it interrupts and requeues that item so the saved transcript snapshot can publish without labels.

When auto-detection is on, the app now also tries to attribute active Windows render audio to a likely process, meeting window, or browser tab when Windows exposes enough metadata. The compact summary appears on `Home`, and `Help` includes the current app/window/tab match plus confidence when attribution is available.
For Google Meet, the detector still tries to prove audio belongs to the Meet tab first, but an explicit active `Meet - ...` browser window can now auto-start from active browser-family audio even when browser session metadata is too weak to name the exact tab.
For Teams, an active auto-started call now stays alive through weak stray Google Meet browser detections during quiet patches, so an unrelated open Meet tab should not keep stopping and restarting the live Teams recording.
When you run the Teams probe in `Settings > Setup`, the app records what the local Teams detector would do right now and whether the Teams third-party API candidate exposed anything beyond control actions. If the third-party path cannot prove readable meeting state, the local Teams detector remains the active fallback. No Microsoft Entra app registration or Graph sign-in is required for this flow.
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

`MeetingRecorderInstaller.msi` is the preferred install path for current releases. It now ends on an explicit success screen instead of closing silently, and that screen can launch the app immediately after a fresh install. Current releases no longer ship the deprecated EXE launcher, and the script installers remain the canonical bootstrap path when MSI is blocked or a user wants a more explicit script-based install.

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

## 4.1.1 MSI Uninstall And Fresh Reinstall

Use this path when you want to remove the installed app but keep your meeting data.

Uninstall with preserved data:

1. Close Meeting Recorder if it is running.
2. Open Windows `Settings > Apps > Installed apps` or `Apps & features`.
3. Find `Meeting Recorder`.
4. Choose `Uninstall`.
5. Let Windows remove the app.
6. Keep `%LOCALAPPDATA%\MeetingRecorder` and your `Documents\Meetings\...` folders in place. Those contain the preserved config, logs, models, transcripts, and recordings that the app can reuse later.

Expected result after uninstall:

- `%USERPROFILE%\Documents\MeetingRecorder` is removed
- current-user Start Menu and Desktop shortcuts are removed
- `%LOCALAPPDATA%\MeetingRecorder` remains
- `Documents\Meetings\Recordings` remains
- `Documents\Meetings\Transcripts` remains
- `Documents\Meetings\Archive` remains

Fresh reinstall:

1. Run `MeetingRecorderInstaller.msi`.
2. On a first install, choose whether to keep `Standard` only or also ask setup to try the optional `Higher Accuracy` downloads.
3. Finish the MSI install.
4. Launch the app.
5. Confirm `Settings > Setup` shows the expected transcription and speaker-labeling readiness state.
6. Confirm your prior meeting outputs and config-backed preferences are still present.

## 4.2 GitHub-Based Updates

The same GitHub bootstrap path can be used later for updates.

If Meeting Recorder is already installed and you want to apply the latest GitHub release without manually moving ZIP files around, run the script fallback:

- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`

If an installer or updater console fails, copy the diagnostic log path shown in the console window before closing it. Direct MSI installs now also emit Windows Installer logs under `%TEMP%`.

The installer preserves the existing portable `data` folder during updates, so recordings, transcripts, logs, models, and config are kept.
The shared deployment CLI now updates the managed install in place instead of renaming the whole `Documents\MeetingRecorder` root first, which makes updates more tolerant of locked files under the preserved `data` tree.
In-place updates now use the already-extracted app bundle as the staging source when it is on the same drive as `Documents\MeetingRecorder`, validate that staged source, and only then move current install files aside. That keeps transient executable staging out of random sibling folders under `Documents`, avoids a second unsigned apphost copy under `%TEMP%`, and fails before touching the current install if endpoint protection removes a staged payload.
Managed install repair now also restores the required worker sidecar payload from the source bundle if any of those files are missing from an existing install, so queued sessions do not stay unpublished after restart because the worker cannot load `MeetingRecorder.Core.dll`.
The actual apply/install work is now delegated to the shipped `AppPlatform.Deployment.Cli` helper so the script layer stays thin and reusable.
That same CLI-first rule also applies after MSI-origin installs, so the app can update through the shared ZIP/CLI path without switching to MSI-based in-app patching.
The in-app updater now filters GitHub release assets down to the versioned `MeetingRecorder-v<version>-win-x64.zip` app bundle and ignores the separately published model or speaker-labeling ZIP assets, so `Install Available Update` cannot hand a diarization bundle to `apply-update`.
The in-app `Install Available Update` button now resolves that CLI helper from the installed `MeetingRecorder.App.exe` directory instead of the temporary single-file extraction folder, so the updater can still launch correctly from the packaged managed install.
Same-version pending updates are now only cleared when their published-at and asset-size identity matches the installed build, so a refreshed `0.x` package can still replace an older binary instead of being skipped just because the display version text matches when you install it manually.
The MSI now also allows a refreshed same-version release asset to overwrite the already-installed app binaries on reinstall, so reinstalling an updated `0.x` package does not leave the previous apphost in place.
Automatic updates are still allowed, but the app refuses installer shutdown handoff while a live recording is active and also avoids automatically interrupting background transcript-processing work.
When only background processing is busy, `Settings > Updates` now softens the manual path: the primary action downloads the ZIP immediately as `Queue Install When Idle`, then applies it automatically once the processing queue drains. After that queue is in place, the page exposes `Install Now Anyway` as an explicit override that interrupts background processing and lets it resume on next launch.
Background auto-install and passive pending-update retry are still limited to true version upgrades, so a freshly MSI-relaunched app does not immediately kick off a same-version self-update loop just because the release was republished with a newer timestamp or asset size. An explicitly queued manual same-version install is still allowed to retry once background processing becomes idle.
The `Run-MeetingRecorder.cmd` launcher now waits briefly for `MeetingRecorder.App.exe` to reappear before it shows the missing-apphost error, which helps during short install or update handoff windows.
The EXE shell keeps the `Try backup CMD installer` action available after a handoff so you can still trigger the fallback path if the command window fails later.
The `Install-LatestFromGitHub.cmd` wrapper now preserves the real PowerShell/bootstrap exit code in its local-script path, so a successful install no longer prints a stale generic failure message afterward.
The Home screen now uses a wider full-canvas recording layout inside its dedicated scroll viewer, stretches to the full tab content width at startup, keeps the quick-setting cards underneath the main console, and uses fixed-width `On` / `Off` controls so the setting cards do not resize while you toggle them.
The Home graph now also stays on a lightweight timer only while the Home deck is actually visible and a live recording state is worth drawing, which reduces unnecessary UI churn while you are elsewhere in the app.
Normal in-app update installs do not use `Deploy-Local.ps1`; that old repo-only developer utility is now intentionally disabled so MSI and real upgrade paths stay the only supported validation routes.
Startup now also performs a one-time higher-aggression cleanup of stale unlocked files under `%LOCALAPPDATA%\Temp\MeetingRecorderDiarization` and `%LOCALAPPDATA%\Temp\MeetingRecorderTranscription`, then falls back to recurring cleanup on later starts and before worker launches so orphaned temp data does not keep growing indefinitely.

## 5. Whisper Model Setup

App releases now keep Whisper models separate from the main installer bundle so installs and updates stay lighter.

`Settings > Setup` now opens as a dedicated guided surface with three sections:

- `Transcription`
- `Speaker labeling`
- `Teams integration`

Inside `Transcription`, the app presents guided readiness content first, then keeps alternate/manual paths available when you need a different model source or an existing local file.

Built-in automatic downloads in `Setup` use GitHub release assets only. If GitHub is blocked, use an approved local file instead.
If the packaged `model-catalog.json` file is missing on a given machine, curated `Use Standard` and `Use Higher Accuracy` still fall back to the app's built-in default catalog instead of failing Setup outright.

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

When speaker labeling runs, the local worker estimates anonymous speakers from voice embeddings, then assigns transcript segments to the speaker turn with the strongest time overlap. If the default clustering pass collapses voices into fewer than two supported speakers, the worker retries stricter clustering thresholds before publishing labels.

Speaker names can also improve over time when `Settings > General > Learn speaker names from my corrections` is enabled. The app stores local voice-profile embeddings under `%LOCALAPPDATA%\MeetingRecorder\speaker-profiles\voice-profiles.json` (or the portable app `data\speaker-profiles` folder), compares future anonymous speaker clusters with those user-confirmed profiles, and auto-applies names only when the best match is above the high-confidence threshold and clearly ahead of the next profile. Lower-confidence matches stay anonymous and appear as suggestions in the speaker-name editor.

Voice profiles are local-only user data. The app stores embeddings/centroids rather than raw audio, does not upload them, and lets you disable, delete selected profiles, or delete all profiles from Settings. Voice-profile data can still be sensitive because it is derived from a person's voice; review your local policies before teaching names for other people. Background reference: [sherpa-onnx speaker identification](https://k2-fsa.github.io/sherpa/onnx/speaker-identification/index.html) and [FTC biometric information warning](https://www.ftc.gov/news-events/news/press-releases/2023/05/ftc-warns-about-misuses-biometric-information-harm-consumers).

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
6. Confirm the `Speaker labeling` section now shows speaker labeling as `Ready` or `Deferred`.
7. Keep `When to run speaker labeling` on `Deferred` for the safest managed-laptop behavior. `Deferred` publishes audio and transcripts first, then leaves `Add Speaker Labels` for later. If you explicitly want newly published transcripts to include labels automatically, set the mode to `Throttled` or `Inline`; choosing or importing a speaker-labeling bundle no longer auto-promotes `Deferred`.

If GitHub is blocked or no recommended bundle is loaded:

1. Open `Settings`.
2. Open `Setup`.
3. In `Speaker labeling`, click `Open local setup guide`.
4. Review `Alternate public download locations`.
5. If the list says `No vetted public mirror configured yet.`, use the local guide plus `Import Existing File` after you obtain an approved local diarization model bundle or matching supporting assets.
6. Use `Open Asset Folder` if you need to inspect or manage the installed files manually.

Advanced details:

- `Advanced` shows the configured asset folder path, CPU-only acceleration status, and the raw readiness details
- the recommended bundle is preferred because it installs the segmentation model, embedding model, and bundle manifest together
- diarization labels begin as anonymous voice clusters; the app only predicts real names later from local profiles that you taught through speaker-name corrections
- the app looks for the bundled local `SETUP.md` first and only falls back to GitHub help when the local guide cannot be found
- `Alternate public download locations` can legitimately show `No vetted public mirror configured yet.` when no curated mirror is configured
- the alternative asset picker is mainly for cases where you already know you need a specific supporting file instead of the bundle

GPU acceleration:

- `Settings` shows the speaker-labeling GPU acceleration control as unavailable in managed builds
- CPU-only speaker labeling is enforced, and existing `Auto` configs are normalized to CPU-only to avoid endpoint-protection memory-access prompts from DirectML initialization
- the processing worker no longer packages the DirectML ONNX Runtime; diarization stays on CPU
- the diarization `Advanced` panel records the last effective provider and any fallback message reported by the worker

## 6. Settings

Open `Settings` from the header to review or change settings through these grouped sections:

- `Setup`
- `General`
- `Files`
- `Updates`
- `Advanced`

Use `Settings > Setup` when you need to make transcription or speaker labeling work. Use the other sections for recording behavior, meeting-file locations, updates, and troubleshooting.
Use `Settings > Setup > Teams integration` when you want the app to test a more stable Teams path before falling back to local heuristics. The probe can promote `Third-party API usable`, report `Third-party API available but control-only`, or stay at `Fallback only` while preserving any Teams policy block reason it can detect.

The simplified `Home` surface keeps only the title/project/attendee editor, live audio graph, start/stop controls, and quick toggles for microphone capture and auto-detection. Setup and maintenance flows now live in the header surfaces.
If you turn automatic detection off, the header status now switches into an explicit manual-mode state so it is obvious that supported meetings will not auto-start until you turn it back on.

Everyday recording and file settings stay visible first, while infrastructure and troubleshooting paths stay hidden until you open `Advanced`.

Installer and update flows now refuse to replace the app while an active recording or processing session is still running. Stop the live session first, then retry the install or update.

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
- speaker-labeling CPU-only acceleration policy
- update-check toggle
- auto-install toggle
- update feed URL

Use `Help` from the header when you need About details, the bundled setup guide, logs/data folder shortcuts, or the release page.

On `Home`, the active session title, `Client / project`, and `Key attendees` fields all stay editable during a live recording, including a recording you started manually.
If startup resolves or auto-switches the configured Whisper model just after the window opens, the manual `START` button now refreshes as soon as transcription readiness becomes healthy instead of staying stuck until you relaunch or navigate away and back.

For auto-started Teams recordings, the app can briefly tolerate quiet patches and reduced meeting surfaces such as compact view, sharing controls, or a matching chat/navigation surface. If the actual meeting window disappears entirely, a lingering `ms-teams` background process by itself is no longer treated as enough evidence to keep the recording running.
If the active Teams title stays on screen after the call ends, microphone-only activity is no longer treated as a forever-positive keep-alive. The session now needs recent Teams render activity to keep refreshing the quiet-call grace window, and an official Teams path can also veto stale shell continuity when it reports that no current meeting is active, so recordings stop instead of drifting into long post-call silence.
The pinned Teams `Sharing control bar` surface is now treated differently once a specific Teams meeting is already active: it counts as a continuation of that same live call even during a quiet screen-sharing stretch, which prevents one presentation from being chopped into multiple short fragments.

The detector also now treats a plain Teams content window more cautiously when a matching `Chat | ... | Microsoft Teams` surface is present at the same time, or when no recent Teams-attributed render audio is visible. That helps avoid auto-starting on Teams recording playback or chat-thread media that looks meeting-like but is not actually a live call.
Bare Teams shell titles such as `Chat | Microsoft Teams` or `Activity | Microsoft Teams` are also ignored safely now, so those navigation surfaces no longer crash the detection loop when automatic detection is enabled.

## 7. Recording Validation

After the model is ready:

1. Start a short meeting or test call.
2. If the call is in Teams or Google Meet, you can leave auto-detection on and let the app watch for it automatically, or click `Start Recording` manually. New installs now keep auto-detection on by default, and microphone capture is on by default too; older configs are still reset off once after the security-prompt migration so existing users can opt back in deliberately. Google Meet detection can now recover Meet identity from Windows render-session metadata even when the visible browser window is a shared Slides or Docs page, and the detector now uses Windows render-session ownership as a tie-breaker so a real Teams call can beat stale Google Meet browser titles. When browser render-session metadata is available, Google Meet auto-start now requires that metadata to look Meet-specific before browser audio is credited to the meeting, which helps prevent music or video playback in the same browser from triggering a false Meet recording. Bare Teams shell titles such as `Chat | Microsoft Teams` are now ignored safely instead of crashing the detection loop. When microphone capture is on, the published audio path now ducks both low-level mic bleed and delayed `80-400 ms` speaker pickup under strong loopback audio so speaker playback is less likely to create echo. If an auto-started recording is first labeled from stale Google Meet browser evidence and a stronger Teams in-call window plus audio source shows up afterward, the app still reclassifies that live session in place when it is the same underlying call. If you manually stop the current supported meeting while its tab or window is still open, the app now suppresses auto-restart for that same meeting until detection proves a different meeting has taken over. If a clearly different supported call replaces an already managed live session, the app now rolls over to a fresh recording instead of just renaming the earlier one.
3. If the call is in Zoom, Webex, or another conferencing app, click `Start Recording` manually.
4. Optionally type a better meeting title, client / project, and comma- or semicolon-delimited key attendees on Home while recording.
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
If source-audio preparation fails before a `.wav` can be built, the meeting is marked `Failed` instead of staying in the queue. Sessions with usable loopback audio can still publish by falling back to loopback-only audio when microphone merge data is unreadable.

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
- on first launch after the updated build, the versioned `published-meeting-repair-v6` pass can now merge longer same-title split chains from repeated auto-stop / auto-start churn, auto-merge a short-gap exact-title split when the matching work manifests still point at the same specific meeting window/title evidence, and republish repairable historical microphone sessions from `%LOCALAPPDATA%\MeetingRecorder\work` while archiving the replaced published and processing WAVs plus an `echo-repair-report.txt` summary
- successful publishes now also normalize the work manifest onto the retained merged audio and prune raw capture chunks from the session `raw` folder, and startup reclaims the same raw artifacts from older already-published sessions so `%LOCALAPPDATA%\MeetingRecorder\work` does not keep growing without bound
- repaired merged publishes now show duration from the repaired artifact itself when the preserved original stem would otherwise point back to stale manifest timing
- dismissed recommendations stay hidden until the underlying meeting data changes enough to produce a new recommendation fingerprint

Archive output is user-recoverable. The app moves artifacts into timestamped folders under `Documents\Meetings\Archive` instead of permanently deleting them. Current builds also treat older top-level folders such as `ArchivedRepairs`, `ArchivedFalseStarts`, and `ArchivedGenericCleanup` as legacy inputs that can be consolidated under the single `Archive` root. On Windows, archived artifacts are marked unpinned after they are written so OneDrive Files On-Demand can keep the recovery copy online without forcing the WAV backup to remain allocated on the local disk. Generated repair backup folders such as `published-meeting-repair-v*` and timestamped `*-echo-repair-*` archives are automatically removed after 14 days; manual meeting archive folders remain user-managed.

## 8.2 Meetings Library, Detail Window, and Context Menu

The Meetings tab is now a library and work-queue surface. It keeps queue status, folder links, filtering, grouped browsing, a full-width meeting list, and a compact selection command strip on the tab. Single-meeting reading and maintenance open in one owned detail window, similar to transcript-first meeting tools.

Meetings workspace behavior:

- grouped view is now the default for current releases, including a one-time migration for older configs
- grouped browsing can now pivot by client / project and attendee as well as time, platform, and status
- grouped browsing opens only the first visible group by default and keeps the rest collapsed until you expand them
- `Expand All` and `Collapse All` appear when grouped browsing is active
- meeting timestamps in the list and detail window use the local system time zone with the current short date/time format
- one selected meeting enables `Open Details`, `Open Transcript`, `Open Audio`, and `Open Folder`
- multiple selected meetings keep bulk-safe actions in the context menu
- cleanup recommendations stay in the library-level cleanup review panel instead of mixing with selected-meeting controls

What the detail window shows for the focused meeting:

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
- detected audio source and collapsed capture diagnostics
- transcript segments read from the local JSON sidecar, with Markdown fallback for app-rendered transcripts
- an inactive AI-summary placeholder reserved for a later update

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

The lower Meetings area is now intentionally library-level:

- the cleanup review area is for bulk review, apply, dismiss, and open-related actions
- the selected-meeting command strip handles the most common artifact and detail-entry flows without making the page longer
- meeting-specific metadata, transcript reading, project edits, rename, retry, split, speaker labels, archive, and delete live in the detail window
- generated AI summaries are not part of this release; transcript reading remains local-file based

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
- if MSI is blocked, retry `Install-LatestFromGitHub.cmd`
- if that still fails, download the full `MeetingRecorder-v<version>-win-x64.zip` asset and run `Install-MeetingRecorder.cmd`

### The app says it is already running, but the existing instance did not respond

This usually means the previous instance is still winding down after a close or update handoff.

What the current app does:

- a new launch now waits briefly for the prior instance to finish exiting before it shows the warning
- if the existing instance owns the single-instance lock but does not answer the newer activation handshake yet, a new launch now also tries to find that already-running Meeting Recorder window and bring it to the foreground before it warns
- if the prior instance is still shutting down after an install or update handoff, a new launch now gives that shutdown a longer final grace window before it shows the warning
- if a recording or background transcript-processing job is active, the app now defers installer shutdown instead of letting an update forcibly close the current session
- if only background processing is active, `Settings > Updates` can now queue the download for automatic idle install or let you explicitly override and install immediately; a live recording is still a hard stop
- if queued transcript processing faults while the app is closing, shutdown now logs and ignores that queued-worker failure instead of leaving a headless background process behind
- if older queue items were stranded in `Processing` after transcription had already finished, the next startup now repairs those stale manifests automatically, keeps the saved transcript snapshot, and requeues them once without speaker labels so the backlog can drain without manual manifest editing or repeated full retranscription
- when shutdown cleanup finishes, the app now explicitly tears down any header surfaces and requests full WPF application shutdown instead of relying only on the main window close path
- automatic update handoff now requests a full application shutdown instead of only closing the main window, so modeless header surfaces cannot keep the process alive after the window disappears
- app exit no longer blocks the UI thread waiting on the activation and installer-shutdown monitor tasks, which prevents a closed window from turning into a stuck headless background process
- same-version pending updates are only promoted to the installed state when their release identity matches the installed build, so refreshed packages with the same version still get a real install attempt when you launch them manually
- background auto-install and passive pending-update retry only run for true version upgrades, while an explicitly queued manual same-version update can still install once background processing becomes idle
- launch-on-login registration now resolves the installed `MeetingRecorder.App.exe` path from the running process instead of persisting a temporary `%TEMP%\\.net\\...` extraction path
- update repair now rewrites existing Desktop and Start Menu launchers to the canonical `Documents\\MeetingRecorder` bundle and quarantines older `Documents\\Meeting Recorder` or `%LOCALAPPDATA%\\Programs\\Meeting Recorder` roots if they are still present

If you still see the warning:

- wait a few seconds and try again once
- check Task Manager for a lingering `MeetingRecorder.App.exe` process
- if it is clearly stuck, end that lingering process and relaunch the app

### Outlook calendar fallback did not add attendees

The Outlook calendar lookup is best-effort.

What the current app does:

- it can use a matching Outlook appointment as a fallback title source
- it can also persist attendee names from that appointment into the session manifest and published transcript JSON when the meeting can be matched successfully, including post-publish backfill for meetings that were listed without attendee metadata
- it only binds to an already-running Outlook session and keeps the lookup best-effort, so the app no longer tries to spin up extra hidden Outlook automation sessions on its own
- it runs that Outlook read on a short-lived STA-bound lookup with per-day caching, concurrent-read coalescing, a restricted time-window query, cancellation for timed-out background reads, and temporary backoff after timeouts or other Outlook failures, so queue processing and live detection can keep moving if Outlook is unhealthy
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

Make sure `Enable microphone capture` is turned on in `Settings > General` before starting the next recording. You can also change it during an active recording from `Home` or `Settings`; the change applies from that moment forward and also becomes the new default for future recordings.

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
