# Meeting Recorder Setup

## What to install

For a restricted corporate laptop, use the packaged installer bundle produced by:

- `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`

If you want a fully self-contained bundle and your build machine has the needed runtime packs available, use:

- `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -SelfContained`

That generates:

- `.artifacts\installer\win-x64\MeetingRecorder-win-x64.zip`
- or a timestamped variant such as `.artifacts\installer\win-x64\MeetingRecorder-win-x64-YYYYMMDD-HHMMSS.zip`

The ZIP contains:

- `MeetingRecorder\` with the app and worker binaries
- `Run-MeetingRecorder.cmd` at the ZIP root for direct launch after extraction
- `Install-MeetingRecorder.cmd`
- `Install-MeetingRecorder.ps1`
- this setup guide

By default, the bundle is framework-dependent to keep the packaging flow reliable in restricted environments. If the target laptop does not already have the required `.NET 8 Desktop Runtime`, rebuild the bundle with `-SelfContained` on a machine that can satisfy that publish mode.

## Easiest portable run flow

1. Copy the newest `MeetingRecorder-win-x64*.zip` bundle to the target machine.
2. Extract the ZIP to any user-writable folder that is not blocked by policy.
3. Double-click `Run-MeetingRecorder.cmd` from the extracted ZIP root.
4. Keep the extracted folder where you want the app to live, because the app writes its config, logs, models, and outputs under that same folder.

In portable mode, the app runs directly from the extracted folder and keeps its app data in:

- `<ExtractedFolder>\MeetingRecorder\data`

The bundle ships with a `portable.mode` marker so this behavior is automatic.

No install step is required for this portable flow.

## Direct-copy deployment folder

If you do not want to use the ZIP at all, copy this folder directly to the target location:

- `.artifacts\publish\win-x64\MeetingRecorder`

That folder already contains the portable app files, `portable.mode`, and `Run-MeetingRecorder.cmd`.

If you want the same layout as the ZIP contents before compression, use:

- `.artifacts\installer\win-x64\staging`

## Optional install flow

If you still want a copied local install plus a desktop shortcut:

1. Extract the ZIP.
2. Double-click `Install-MeetingRecorder.cmd`.
3. If PowerShell prompts, allow the script to run for this install.
4. The installer copies the app to:
   - `%LOCALAPPDATA%\MeetingRecorder\app`
5. The installer creates a desktop shortcut named `Meeting Recorder`.

If PowerShell script execution is blocked, you can still run the app manually:

1. Open the extracted `MeetingRecorder\` folder.
2. Run `MeetingRecorder.App.exe` or `Run-MeetingRecorder.cmd` directly.

## First launch behavior

In portable mode, on first launch the app creates folders under:

- `<ExtractedFolder>\MeetingRecorder\data\config`
- `<ExtractedFolder>\MeetingRecorder\data\logs`
- `<ExtractedFolder>\MeetingRecorder\data\audio`
- `<ExtractedFolder>\MeetingRecorder\data\transcripts`
- `<ExtractedFolder>\MeetingRecorder\data\work`
- `<ExtractedFolder>\MeetingRecorder\data\models`

The config file is created at:

- `<ExtractedFolder>\MeetingRecorder\data\config\appsettings.json`

## Initial setup checklist

1. Launch the app once so it creates the config file and default folders.
2. Confirm the `Audio` and `Transcripts` folders are acceptable for your environment.
3. Record a short manual test session with `Start Recording` and `Stop Recording`.
4. Confirm a session folder appears under the extracted bundle's `data\work` folder.
5. Confirm a merged `.wav` file appears in the audio output folder after processing.
6. Confirm a `.md`, `.json`, and `.ready` file appear in the transcripts output folder.

## Whisper model setup

The app tries to download the Whisper model on first transcription attempt.

If corporate policy blocks the download:

1. Place a compatible Whisper model file at the configured path in `appsettings.json`.
2. By default, that path is:
   - `<ExtractedFolder>\MeetingRecorder\data\models\asr\ggml-base.bin`
3. Relaunch the app and retry processing.

## Diarization sidecar

Speaker diarization is optional and best-effort.

If you later build or receive a compatible diarization sidecar, place it at:

- `<ExtractedFolder>\MeetingRecorder\data\models\diarization\MeetingRecorder.Diarization.Sidecar.exe`

If the sidecar is missing, transcript publishing still completes without speaker labels.

## Power Automate setup

Configure Power Automate to watch the transcripts output folder for:

- `*.ready`

Use the shared filename stem to find sibling files:

- `<stem>.md`
- `<stem>.json`
- `<stem>.wav`

The `.ready` file is created last and is the only supported completion signal.

## Corporate-laptop notes

- No admin rights are required for normal use.
- No browser extension is required.
- The build is local-first and works without OneDrive.
- CPU-only machines are supported.
- If microphone capture is restricted, leave `micCaptureEnabled` set to `false` in the config file.
