# Releasing Meeting Recorder

This guide is the repeatable release path for GitHub-based first installs and future updates.

## Goal

Publish the app installer assets plus any separate model assets you want users to download from the Models tab.

- `MeetingRecorderInstaller.exe`
- `MeetingRecorder-v<version>-win-x64.zip`
- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`
- optional `ggml-*.bin` model assets

The main installer ZIP preserves the manual install path.
The stable installer EXE gives first-time users and upgraders the preferred one-click entry point:

- download `MeetingRecorderInstaller.exe`
- run it directly

The stable bootstrap command remains the backup path:

- download `Install-LatestFromGitHub.cmd`
- run it directly

If the companion PowerShell script is not next to it, the command downloads `Install-LatestFromGitHub.ps1` from the latest GitHub release automatically.

That same command can be reused later for updates.

## Before You Build

1. Make sure the version in [Directory.Build.props](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\Directory.Build.props) is correct.
2. Make sure the branding version in [AppBranding.cs](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\src\MeetingRecorder.Core\Branding\AppBranding.cs) matches.
3. Put any GitHub-downloadable model assets you want to publish under `assets\models\asr`.

The output names are generated from the current repo version automatically. If the repo is at `0.2`, the main release ZIP becomes `MeetingRecorder-v0.2-win-x64.zip`.

## One-Command Release Build

Command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

Defined in:

- [Build-Release.ps1](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\scripts\Build-Release.ps1)

What it does:

- runs the safe serial build/test flow through [Test-All.ps1](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\scripts\Test-All.ps1)
- builds the installer assets through [Build-Installer.ps1](C:\Users\psharm04\OneDrive - Kearney\Documents\Coding Projects\Meeting Recorder\scripts\Build-Installer.ps1)
- creates:
  - the lightweight installer EXE
  - the main installer ZIP
  - the stable bootstrap command asset
  - the stable bootstrap PowerShell script asset
  - separate `ggml-*.bin` model assets copied from `assets\models\asr`
- fails fast if the main ZIP is too large for GitHub Releases

If you need a faster packaging-only run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -SkipTests
```

If you want the build script to sync the generated assets straight to the current latest GitHub release, set `GITHUB_TOKEN` or `GH_TOKEN` first and then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -UploadToGitHubLatestRelease
```

If you want to preview the upload decisions without changing GitHub, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -DryRunGitHubUpload
```

## Optional Code Signing

For corporate environments, signing the installer EXE and PowerShell fallback scripts is strongly recommended.

Why it helps:

- improves SmartScreen trust prompts
- gives IT a publisher identity they can allow-list in AppLocker or WDAC
- helps signed PowerShell scripts run more cleanly under stricter execution policy

Important:

- signing helps, but it does not guarantee bypass of corporate controls
- some environments still require the publisher certificate to be explicitly trusted or allow-listed

The release scripts support signing with a certificate already installed in the Windows certificate store.

Supported inputs:

- `MEETINGRECORDER_SIGNING_CERT_THUMBPRINT`
- `MEETINGRECORDER_SIGNING_CERT_STORE_PATH`
- `MEETINGRECORDER_SIGNING_TIMESTAMP_URL`

Defaults:

- store path defaults to `Cert:\CurrentUser\My`
- timestamp URL is optional but recommended

Example:

```powershell
$env:MEETINGRECORDER_SIGNING_CERT_THUMBPRINT = "YOUR_CERT_THUMBPRINT"
$env:MEETINGRECORDER_SIGNING_CERT_STORE_PATH = "Cert:\CurrentUser\My"
$env:MEETINGRECORDER_SIGNING_TIMESTAMP_URL = "http://timestamp.digicert.com"
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

You can also pass the same values as explicit parameters:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 `
  -CodeSigningCertificateThumbprint "YOUR_CERT_THUMBPRINT" `
  -CodeSigningCertificateStorePath "Cert:\CurrentUser\My" `
  -CodeSigningTimestampUrl "http://timestamp.digicert.com"
```

## Output Location

The release assets are written under:

- `.\.artifacts\installer\win-x64`

That output folder is cleaned on every build so only the current uploadable assets remain.
Root-level `ggml-*.bin` model assets are preserved between builds and only recopied locally when their size or timestamp changes.

Expected outputs:

- `MeetingRecorderInstaller.exe`
- `MeetingRecorder-v<version>-win-x64.zip`
- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`
- optional `ggml-*.bin`

## Publish To GitHub

1. Go to the repo releases page:
   - `https://github.com/shp847/meetingrecorder/releases`
2. Draft a new release.
3. Create or select the tag `v<version>`.
4. Upload:
   - `MeetingRecorderInstaller.exe`
   - `MeetingRecorder-v<version>-win-x64.zip`
   - `Install-LatestFromGitHub.cmd`
   - `Install-LatestFromGitHub.ps1`
   - any `ggml-*.bin` model assets you want the app to offer in the Models tab
5. Publish the release.

Important:

- upload the built installer ZIP asset, not only GitHub's automatic source ZIP
- the bootstrap flow depends on the GitHub release having a real app ZIP asset
- the Models tab only shows downloadable GitHub models that are uploaded as release assets
- `Build-Release.ps1 -UploadToGitHubLatestRelease` can automate the upload step when a GitHub token is available
- remote assets are skipped when their size and stored source timestamp metadata still match the local file
- large model assets also fall back to size-only skipping when an older GitHub asset exists without stored source timestamp metadata

## Smoke Test After Publishing

1. Confirm the latest feed returns release JSON instead of `404`:
   - `https://api.github.com/repos/shp847/meetingrecorder/releases/latest`
2. Download `MeetingRecorderInstaller.exe`
3. Run `MeetingRecorderInstaller.exe`
4. Confirm the app installs into `Documents\MeetingRecorder`
5. Confirm the app launches and the `Models` tab is usable
6. Confirm the `Models` tab lists the uploaded `ggml-*.bin` assets under `Downloadable Models From GitHub`

## User-Facing Install Story

Once a release is published, the recommended user path is:

1. Download `MeetingRecorderInstaller.exe` from GitHub Releases
2. Run it directly

Backup path:

1. Download `Install-LatestFromGitHub.cmd`
2. Run it directly

Later updates can reuse the same installer EXE, with the script bootstrap still available as backup.
Existing installs keep their saved config values on update.
