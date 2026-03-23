Place optional diarization model-bundle assets for GitHub release publishing here.

Recommended asset naming:
- `meeting-recorder-diarization-bundle-win-x64.zip`
  - preferred bundle that contains:
  - `meeting-recorder-diarization-bundle.json`
  - `model.int8.onnx`
  - `nemo_en_titanet_small.onnx`
  - any required license or supporting metadata files
- standalone supporting files such as:
  - `*diarization*.onnx`
  - `*diarization*.json`
  - `*diarization*.yaml`
  - `*diarization*.yml`

The bundle manifest should include:
- `bundleVersion`
- `segmentationModelFileName`
- `embeddingModelFileName`

`Build-Release.ps1` will copy these files into `.artifacts/installer/win-x64` as separate GitHub release assets without bundling them into the main app ZIP.
