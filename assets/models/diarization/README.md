Place the curated speaker-labeling bundle assets referenced by `src\MeetingRecorder.Core\Assets\model-catalog.json` here.

Recommended asset naming:
- `meeting-recorder-diarization-bundle-standard-win-x64.zip`
  - bundled into the main portable/MSI payload as the offline-first `Standard` speaker-labeling option
- `meeting-recorder-diarization-bundle-accurate-win-x64.zip`
  - published as the separate `Higher Accuracy` speaker-labeling download asset
- each bundle should contain:
  - `meeting-recorder-diarization-bundle.json`
  - `model.int8.onnx`
  - `nemo_en_titanet_small.onnx`
  - any required license or supporting metadata files

The bundle manifest should include:
- `bundleVersion`
- `segmentationModelFileName`
- `embeddingModelFileName`

`Publish-Portable.ps1` copies the `Standard` bundle into `MeetingRecorder\model-seed\speaker-labeling\` inside the portable/MSI payload.

`Build-Installer.ps1` copies the `Higher Accuracy` bundle into `.artifacts\installer\win-x64` as a separate GitHub release asset without bundling it into the main app ZIP.
