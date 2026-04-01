# Bundled ASR Models

Place the curated Whisper `ggml` binaries referenced by `src\MeetingRecorder.Core\Assets\model-catalog.json` in this folder.

Current expected files:

- `ggml-base.en-q8_0.bin`
  - bundled into the main portable/MSI payload as the offline-first `Standard` transcription option
- `ggml-small.en-q8_0.bin`
  - published as the separate `Higher Accuracy` transcription download asset

When you run `scripts\Publish-Portable.ps1`, the `Standard` asset is copied into:

- `MeetingRecorder\model-seed\transcription\`

inside the portable output bundle and is then seeded into the managed model cache during install or repair.

When you run `scripts\Build-Installer.ps1`, the `Higher Accuracy` asset is copied into:

- `.artifacts\installer\win-x64\ggml-small.en-q8_0.bin`

Notes:

- The app still uses a single active transcription model at a time, but user-facing setup now defaults to curated `Standard` vs `Higher Accuracy` profile choices instead of raw path editing.
- If you host this repo on GitHub, large model files may require Git LFS.
