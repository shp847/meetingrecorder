Place optional diarization sidecar assets for GitHub release publishing here.

Recommended asset naming:
- `MeetingRecorder.Diarization.Sidecar-win-x64.zip`
  - preferred bundle that contains `MeetingRecorder.Diarization.Sidecar.exe` plus any required model/config files
- `MeetingRecorder.Diarization.Sidecar.exe`
  - executable-only fallback if you distribute model files separately
- supporting files such as:
  - `*diarization*.onnx`
  - `*diarization*.bin`
  - `*diarization*.json`
  - `*diarization*.yaml`
  - `*diarization*.yml`

`Build-Release.ps1` will copy these files into `.artifacts/installer/win-x64` as separate GitHub release assets without bundling them into the main app ZIP.
