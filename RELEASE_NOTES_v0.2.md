# Meeting Recorder v0.2

Initial release of Meeting Recorder.

## Highlights

- Local-first Windows desktop recorder for meeting audio
- Manual recording works for Teams, Google Meet, Zoom, Webex, and other conferencing apps that use the normal Windows output and microphone paths
- Assisted auto-detection currently focuses on Teams desktop and Google Meet
- Offline Whisper-based transcription after recording
- Portable install into `Documents\MeetingRecorder`
- Lightweight one-click installer EXE for GitHub Releases
- GitHub-backed script bootstrap install and update fallback
- Meetings tab for browsing recordings, renaming meetings, and re-generating transcripts
- Models tab for downloading, importing, and selecting Whisper models
- Power Automate-ready outputs with `.wav`, `.md`, `.json`, and `.ready`

## Install

- Preferred: download and run `MeetingRecorderInstaller.exe`
- Script fallback: download and run `Install-LatestFromGitHub.cmd`
- Manual fallback: extract `MeetingRecorder-v0.2-win-x64.zip` and run `Install-MeetingRecorder.cmd`

## Notes

- No admin rights are required for normal use
- All writable data stays under `Documents\MeetingRecorder\data`
- A valid local Whisper model is required for transcript generation
- For Zoom, Webex, and other conferencing apps outside the current auto-detection heuristics, use manual recording
- Users are responsible for complying with applicable recording, privacy, employment, and consent laws and policies, including notifying participants and obtaining consent where required
