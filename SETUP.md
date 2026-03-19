# Meeting Recorder Setup

This guide explains how to run the current app, set up the Whisper model, validate the pipeline, and recover from failed transcription runs.

Legal notice: You are responsible to comply with all applicable recording, privacy, employment, and consent laws and workplace policies in your location. Tell participants when they are being recorded and obtain consent where required. This app is not legal advice.

## 1. Recommended Deployment

For a restricted corporate laptop, the recommended path is now the MSI installer.

Recommended user flow:

- download `MeetingRecorderInstaller.msi`
- run it directly

That installer path:

- installs the app in a standard Windows-installed location
- keeps writable runtime data outside the install directory
- is the preferred corporate-friendly release asset in the current build and release flow

If you still want the custom bootstrapper path, the EXE remains available:

- `MeetingRecorderInstaller.exe`

That installer EXE:

- downloads the latest release ZIP from GitHub
- preserves existing data on update
- launches the app directly when it finishes

If the EXE path is blocked, the fallback path is still available:

- `Install-LatestFromGitHub.cmd`

If `Install-LatestFromGitHub.ps1` is not already next to the downloaded command file, the command downloads that script from the latest GitHub release automatically.

If you already have a downloaded release ZIP, you can still use the manual path:

- extract `MeetingRecorder-v<version>-win-x64.zip`
- run `Install-MeetingRecorder.cmd`

By default, the installers preserve existing user data on update so recordings, transcripts, logs, models, and config are not wiped. Fresh installs also enable update checks and can enable launch-on-login so the recorder is available after sign-in.

For newer managed installs, writable data lives in the app's per-user data root instead of the install directory. The app can also migrate older portable data forward on first launch when needed.

If you prefer not to install, the extract-and-run portable folder still works. In that case, run:

- `Run-MeetingRecorder.cmd`

The default portable bundle is `self-contained`, so most laptops do not need a separate .NET Desktop Runtime install.

## 2. Portable Mode Data Layout

In portable mode, the app keeps its data under the app folder.

For a raw extract-and-run folder, the layout exists under that extracted app folder:

- `<AppFolder>\data\config`
- `<AppFolder>\data\logs`
- `<AppFolder>\data\audio`
- `<AppFolder>\data\transcripts`
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
3. Confirm the output folders shown on the Dashboard are acceptable.
4. Open the `Models` tab.
5. Make sure the active Whisper model looks correct.
6. If needed, choose a discovered local model with `Use Selected Model`.
7. Install or import the Whisper model until the app shows `Model status: ready`.
8. Record a short manual test.

The dependency checker currently verifies:

- the app executable is present
- the transcription worker executable is present
- the .NET 8 Desktop Runtime is installed when the bundle is framework-dependent
- whether a configured local Whisper model is present

## 4. Dependency Installer

`Install-Dependencies.cmd` opens:

- this `SETUP.md`
- the official .NET 8 download page
- the official Microsoft Visual C++ x64 redistributable installer

For the default self-contained portable bundle, the .NET download is usually not required, but the helper remains useful for fallback troubleshooting on locked-down laptops.

## 4.1 Installer Options

`Install-MeetingRecorder.cmd` and `Install-LatestFromGitHub.cmd` can both forward PowerShell arguments to the installer script when you need a custom location or behavior.

`MeetingRecorderInstaller.msi` is the preferred install path for current releases. `MeetingRecorderInstaller.exe` remains available as the optional custom bootstrapper path, and the script installers remain the backup path when the EXE is blocked or a user wants a more explicit script-based install.

Examples:

- install to a custom folder:
  - `Install-MeetingRecorder.cmd -InstallRoot "D:\Apps\MeetingRecorder"`
- skip auto-launch after install:
  - `Install-MeetingRecorder.cmd -NoLaunch`
- skip Desktop shortcut creation:
  - `Install-MeetingRecorder.cmd -NoDesktopShortcut`
- disable launch-on-login on first install:
  - `Install-MeetingRecorder.cmd -DisableLaunchOnLogin`
  - `Install-LatestFromGitHub.cmd -DisableLaunchOnLogin`

## 4.2 GitHub-Based Updates

The same GitHub bootstrap path can be used later for updates.

If Meeting Recorder is already installed and you want to apply the latest GitHub release without manually moving ZIP files around, run:

- `MeetingRecorderInstaller.exe`

or the script fallback:

- `Install-LatestFromGitHub.cmd`

The installer preserves the existing portable `data` folder during updates, so recordings, transcripts, logs, models, and config are kept.

## 5. Whisper Model Setup

App releases now keep Whisper models separate from the main installer bundle so installs and updates stay lighter.

The Models tab separates:

- `Downloadable Models From GitHub`
- `Available Local Models`

### Option A: Download from the current GitHub release

Use this on first install when the laptop can reach GitHub Releases.

1. Open `Models`.
2. Review `Downloadable Models From GitHub`.
3. Pick a model.
4. Click `Download Selected Model`.
5. Wait for the status message.
6. Confirm the tab shows `Model status: ready`.

Recommended order:

- `ggml-base.en-q8_0.bin` for most laptops
- `ggml-small.en-q8_0.bin` if you want more accuracy and can tolerate a larger/slower model
- `ggml-tiny.en-q8_0.bin` when you want the smallest possible download

### Option B: Use an already-installed local model

Use this when the model is already present under:

- `<AppFolder>\data\models\asr`

In the `Models` tab:

1. Review `Available Local Models`.
2. Select the model you want.
3. Click `Use Selected Model`.

### Option C: Import an existing file

Use this when corporate policy blocks the GitHub download or you have a model file from another approved source.

1. Acquire a valid ggml `.bin` file.
2. Open `Models`.
3. Click `Import Existing File`.
4. Select the `.bin` file.
5. Wait for validation to complete.
6. Confirm the tab shows `Model status: ready`.

### Manual fallback

If needed, you can place the model file directly at the configured path.

Default portable path:

- `<AppFolder>\data\models\asr\ggml-base.bin`

The app treats tiny files as invalid and will not accept an HTML or proxy error page saved with a `.bin` extension.

## 6. Config Setup

Open the `Config` tab to review or change:

- audio output folder
- transcript output folder
- work folder
- model cache folder
- Whisper model path
- diarization asset path
- microphone capture toggle
- launch-on-login toggle
- auto-detection toggle
- update-check toggle
- update feed URL
- audio threshold
- meeting stop timeout

The UI shows whether each setting applies:

- immediately
- on the next recording
- on the next processing run

## 7. Recording Validation

After the model is ready:

1. Start a short meeting or test call.
2. If the call is in Teams or Google Meet, you can let the app auto-detect it or click `Start Recording` manually.
3. If the call is in Zoom, Webex, or another conferencing app, click `Start Recording` manually.
4. Optionally type a better meeting title in the Dashboard while recording.
5. Stop the recording, or let auto-stop trigger.
6. Confirm the final title is reflected in the published filename stem.

Expected outputs:

- `<stem>.wav` in the audio folder
- `<stem>.md` in the transcripts folder
- `<stem>.json` in the transcripts folder
- `<stem>.ready` in the transcripts folder

If transcription fails, you should still see the final `.wav`.

## 8. Retrying Failed Sessions

If a recording produced audio but no transcript because the Whisper model was missing or invalid:

1. Fix the model in the `Models` tab until it shows `Model status: ready`.
2. Open the `Meetings` tab.
3. Select the failed meeting.
4. Confirm the row status shows `Failed`.
5. Click `Re-Generate Transcript`.

Transcript regeneration works when:

- the session work folder and `manifest.json` still exist, or
- the app can synthesize a new work manifest from an existing published audio file

## 9. Power Automate Setup

Point your downstream flow at the transcripts folder and watch:

- `*.ready`

Use the shared stem to find:

- `<stem>.md`
- `<stem>.json`
- `<stem>.wav`

`.ready` is created last and is the only supported completion signal for successful transcript output.

## 10. Troubleshooting

### No transcript was created

Check the `Models` tab first.

Common causes:

- Whisper model missing
- Whisper model invalid or too small
- corporate network replaced the download with an HTML or proxy page

Then inspect:

- `<AppFolder>\data\logs\app.log`
- `<AppFolder>\data\work\<session-id>\logs\processing.log`

### The GitHub bootstrap installer failed before install started

Common causes:

- GitHub downloads are filtered by corporate network policy
- PowerShell web requests were blocked or the connection was closed early
- Windows certificate revocation checks against GitHub were blocked by the network

What to do:

- retry `MeetingRecorderInstaller.msi` if that path is allowed on the laptop
- if you prefer the bootstrapper, retry `MeetingRecorderInstaller.exe`
- if the EXE path is blocked, retry `Install-LatestFromGitHub.cmd`
- if that still fails, download the full `MeetingRecorder-v<version>-win-x64.zip` asset and run `Install-MeetingRecorder.cmd`

### The meeting recorded speaker audio but not the mic

Make sure `Enable microphone capture` is turned on in `Config` before starting the next recording. That setting applies on the next recording, not mid-session.

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

## 11. Corporate-Laptop Notes

- No admin rights are required for normal use.
- No browser extension is required.
- No OneDrive dependency is required.
- CPU-only machines are supported.
- Model import exists specifically because some corporate networks block public model downloads.
