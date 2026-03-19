# Bundled ASR Models

Place any Whisper `ggml` model binaries that should ship with the portable build in this folder.

Examples:

- `ggml-base.bin`
- `ggml-small.bin`
- `ggml-small.en.bin`

When you run `scripts\Publish-Portable.ps1`, any `.bin` files in this folder are copied into:

- `MeetingRecorder\data\models\asr\`

inside the portable output bundle.

Notes:

- The app still uses a single active model at a time, controlled by `transcriptionModelPath` in `appsettings.json`.
- If you host this repo on GitHub, large model files may require Git LFS.
