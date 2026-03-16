# Meeting Recorder Setup

This guide explains how to run the current app, set up the Whisper model, validate the pipeline, and recover from failed transcription runs.

## 1. Recommended Deployment

For a restricted corporate laptop, the recommended path is the portable extract-and-run bundle.

Copy this folder to the target location:

- `C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\.artifacts\publish\win-x64\MeetingRecorder`

Then run:

- `Run-MeetingRecorder.cmd`

No install step is required for the normal portable flow.

## 2. Portable Mode Data Layout

In portable mode, the app keeps its data under:

- `<AppFolder>\data\config`
- `<AppFolder>\data\logs`
- `<AppFolder>\data\audio`
- `<AppFolder>\data\transcripts`
- `<AppFolder>\data\work`
- `<AppFolder>\data\models`

The portable behavior is activated by the `portable.mode` marker file shipped with the bundle.

## 3. First Launch Checklist

1. Launch the app once.
2. Confirm the output folders shown on the Dashboard are acceptable.
3. Open the `Models` tab.
4. Make sure the configured Whisper model path looks correct.
5. Install or import the Whisper model until the app shows `Model status: ready`.
6. Record a short manual test.

## 4. Whisper Model Setup

The current app supports two setup paths.

### Option A: Download from the Models tab

Use this when the laptop can reach the model host.

1. Open `Models`.
2. Click `Refresh Status`.
3. If the model is missing or invalid, click `Download Base Model`.
4. Wait for the status message.
5. Confirm the tab shows `Model status: ready`.

### Option B: Import an existing file

Use this when corporate policy blocks the download.

1. Acquire a valid `ggml-base.bin` through an approved internal or manual process.
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

## 5. Config Setup

Open the `Config` tab to review or change:

- audio output folder
- transcript output folder
- work folder
- model cache folder
- Whisper model path
- diarization asset path
- microphone capture toggle
- auto-detection toggle
- audio threshold
- meeting stop timeout

The UI shows whether each setting applies:

- immediately
- on the next recording
- on the next processing run

## 6. Recording Validation

After the model is ready:

1. Start a short meeting or test call.
2. Let the app auto-detect it, or click `Start Recording` manually.
3. Optionally type a better meeting title in the Dashboard while recording.
4. Stop the recording, or let auto-stop trigger.
5. Confirm the final title is reflected in the published filename stem.

Expected outputs:

- `<stem>.wav` in the audio folder
- `<stem>.md` in the transcripts folder
- `<stem>.json` in the transcripts folder
- `<stem>.ready` in the transcripts folder

If transcription fails, you should still see the final `.wav`.

## 7. Retrying Failed Sessions

If a recording produced audio but no transcript because the Whisper model was missing or invalid:

1. Fix the model in the `Models` tab until it shows `Model status: ready`.
2. Open the `Meetings` tab.
3. Select the failed meeting.
4. Confirm the row status shows `Failed`.
5. Click `Retry Processing`.

Retry works only when the session work folder and `manifest.json` still exist.

## 8. Power Automate Setup

Point your downstream flow at the transcripts folder and watch:

- `*.ready`

Use the shared stem to find:

- `<stem>.md`
- `<stem>.json`
- `<stem>.wav`

`.ready` is created last and is the only supported completion signal for successful transcript output.

## 9. Troubleshooting

### No transcript was created

Check the `Models` tab first.

Common causes:

- Whisper model missing
- Whisper model invalid or too small
- corporate network replaced the download with an HTML or proxy page

Then inspect:

- `<AppFolder>\data\logs\app.log`
- `<AppFolder>\data\work\<session-id>\logs\processing.log`

### The meeting recorded speaker audio but not the mic

Make sure `Enable microphone capture` is turned on in `Config` before starting the next recording. That setting applies on the next recording, not mid-session.

### Retry is disabled

Retry will be unavailable when:

- the selected meeting has no matching work manifest
- the work folder was removed
- the session is already complete instead of failed

## 10. Corporate-Laptop Notes

- No admin rights are required for normal use.
- No browser extension is required.
- No OneDrive dependency is required.
- CPU-only machines are supported.
- Model import exists specifically because some corporate networks block public model downloads.
