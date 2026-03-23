# Releasing Meeting Recorder

This guide is the repeatable release path for GitHub-based first installs and future updates.

## Goal

Publish the app installer assets plus any separate model assets you want users to download from the Models tab.

- `MeetingRecorderInstaller.msi`
- `MeetingRecorderInstaller.exe`
- `MeetingRecorder-v<version>-win-x64.zip`
- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`
- optional `ggml-*.bin` model assets

The main installer ZIP preserves the manual install path.
The preferred self-service corporate path is now the per-user MSI:

- download `MeetingRecorderInstaller.msi`
- run it directly

The stable bootstrap command remains the backup path:

- download `Install-LatestFromGitHub.cmd`
- run it directly

The custom EXE remains optional:

- download `MeetingRecorderInstaller.exe`
- run it directly if the environment allows custom bootstrap executables

If the companion PowerShell script is not next to it, the command downloads `Install-LatestFromGitHub.ps1` from the latest GitHub release automatically.
That PowerShell bootstrap then downloads the release ZIP and delegates actual install/apply work to the bundled `AppPlatform.Deployment.Cli` executable inside the extracted bundle.
The EXE launcher should only hand off into that same bootstrap path.

That same command can be reused later for updates.

## Before You Build

1. Make sure the version in `Directory.Build.props` is correct.
2. Make sure the branding version in `src\MeetingRecorder.Core\Branding\AppBranding.cs` matches.
3. Put any GitHub-downloadable model assets you want to publish under `assets\models\asr`.

The output names are generated from the current repo version automatically. If the repo is at `0.2`, the main release ZIP becomes `MeetingRecorder-v0.2-win-x64.zip`.

## One-Command Release Build

Command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

Defined in:

- `scripts\Build-Release.ps1`

What it does:

- runs the safe serial build/test flow through `scripts\Test-All.ps1`
- builds the installer assets through `scripts\Build-Installer.ps1`
- publishes the WPF shell as a single-file self-contained `MeetingRecorder.App.exe` when the release is self-contained, while leaving `AppPlatform.Deployment.Cli`, `MeetingRecorder.ProcessingWorker`, scripts, and `MeetingRecorder.product.json` as external bundle assets
- stages `AppPlatform.Deployment.Cli` into the portable bundle so the script wrappers and in-app updater can delegate install/apply work to one canonical executable
- emits `bundle-integrity.json` into the portable bundle so the shared deployment CLI can validate required files before promoting a bundle
- creates:
  - the per-user installer MSI
  - the lightweight installer EXE
  - the main installer ZIP
  - the stable bootstrap command asset
  - the stable bootstrap PowerShell script asset
  - separate `ggml-*.bin` model assets copied from `assets\models\asr`
- fails fast if the main ZIP is too large for GitHub Releases
- stamps the portable bundle with `release-source.json` so the packaged assets record the exact source commit and whether they were built from a dirty worktree

Packaging validation notes:

- the MSI should no longer emit the old `ARPINSTALLLOCATION` WiX warning during normal builds
- `ICE91` is intentionally suppressed for harvested release builds because the MSI is authored as per-user-only and installs into user-profile directories by design
- the MSI now enables Windows Installer logging by default, so direct MSI troubleshooting should leave a verbose log under `%TEMP%`
- the release build should stamp the MSI summary `Word Count` to `10` so the shipped per-user MSI advertises that normal installs do not require UAC elevation
- the MSI now skips the stock license-agreement page and goes straight from welcome to ready-to-install
- the MSI now uses the WiX finish dialog so users see an explicit completion screen instead of a silent exit
- the finish dialog includes a `Launch Meeting Recorder` checkbox for fresh installs, checked by default
- if the finish dialog launches the app during an install or update, it should launch `Launch-MeetingRecorder-AfterInstall.vbs` instead of the app exe directly so the app can consume a relaunch marker from `%LOCALAPPDATA%\MeetingRecorder`, request cooperative shutdown of the existing idle instance, and then start a fresh instance on the updated bits
- automatic updates should never force an installer shutdown while recording or transcript processing is active; that handoff must defer until the app is truly idle
- manual stop for a long-running recording should offload recorder shutdown off the UI thread so the window stays responsive while audio capture drains and finalizes
- app shutdown should tolerate queued processing-worker resolution failures during close instead of surfacing a shutdown error and leaving a headless background process behind
- the final app close path should explicitly close header surfaces and call WPF application shutdown, not rely solely on the main-window close event
- script/bootstrap installers and updater helpers should write diagnostic logs under `%TEMP%\MeetingRecorderInstaller`, suppress raw PowerShell download progress, forward `--pause-on-error` into the deployment CLI, and pause on any failed CLI command path
- the updater apply path should not wait forever for the app to close; it should signal shutdown, escalate install-path release if needed, and then fail clearly if the process still will not exit
- the EXE installer path should avoid cross-process inspection and control APIs on the app process so endpoint tools do not flag it as restricted process-memory access
- the EXE installer should remain a thin launcher over `Install-LatestFromGitHub.cmd/.ps1`; it should not extract ZIPs, copy files into `Documents\MeetingRecorder`, launch the app, or create shortcuts itself
- the EXE installer UI should explain the split between app files in `%USERPROFILE%\Documents\MeetingRecorder` and writable runtime data in `%LOCALAPPDATA%\MeetingRecorder`
- the shared deployment CLI should reject bundles that fail `bundle-integrity.json` validation before it touches the managed install root
- the shared deployment CLI should persist install provenance for diagnostics under `%LOCALAPPDATA%\MeetingRecorder\install-provenance.json`
- the shared deployment CLI should update an existing managed install in place instead of renaming the whole `Documents\MeetingRecorder` root, so locked files under the preserved `data` tree do not block routine updates
- Desktop and Start Menu launchers created by the shared deployment path should be normal `.lnk` shortcuts that target `Run-MeetingRecorder.cmd`, not raw visible `.cmd` files
- `Run-MeetingRecorder.cmd` should wait briefly for `MeetingRecorder.App.exe` to reappear before it shows the missing-apphost error, so short install/update handoff gaps do not look like a permanent broken install
- the EXE shell should keep the backup CMD action enabled even after a handoff, because the command window can still fail after the EXE has already stepped aside
- `Install-LatestFromGitHub.cmd` should preserve the real exit code from the local `Install-LatestFromGitHub.ps1` handoff so a successful install does not print a stale generic failure prompt afterward
- `Run-MeetingRecorder.cmd` and `Check-Dependencies.ps1` should fail clearly and pause if `MeetingRecorder.App.exe` is missing, rather than attempting an invalid WPF DLL fallback
- self-contained WPF packaging should fail fast if the portable app publish regresses back to loose `MeetingRecorder.App.dll`, `.deps.json`, or `.runtimeconfig.json` outputs
- Apps & Features can keep showing the MSI's original version after later CLI-driven updates, so smoke tests should confirm the in-app installed-version display rather than relying only on ARP

If you need a faster packaging-only run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -SkipTests
```

If you need a smaller ZIP for environments that already have the .NET 8 Desktop Runtime installed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -FrameworkDependent
```

Size notes:

- the default release build is `self-contained`, so it bundles the .NET desktop runtime and now ships the WPF shell as a single-file `MeetingRecorder.App.exe` for easier first-time installs and safer startup behavior
- `-FrameworkDependent` produces a smaller app bundle, but target machines must already have the .NET 8 Desktop Runtime
- the portable publish step merges worker runtime files into the main `runtimes` folder so the bundle does not grow a duplicate `runtimes\runtimes` tree

If you want the build script to sync the generated assets straight to the current latest GitHub release, set `GITHUB_TOKEN` or `GH_TOKEN` first and then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -UploadToGitHubLatestRelease
```

The GitHub upload path now refuses to publish if:

- the repo worktree is dirty
- the packaged assets were built from a dirty worktree
- the packaged assets were built from a different commit than the current repo `HEAD`

If you want to preview the upload decisions without changing GitHub, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -DryRunGitHubUpload
```

## Upload Helper

If you already have fresh assets in `.artifacts\installer\win-x64`, the lighter-weight upload helper is:

```powershell
scripts\Upload-ReleaseAssets.cmd -DryRun
```

The upload helper now queues changed GitHub assets in parallel and prints coarse `Upload progress:` snapshots while larger files are in flight, instead of the very noisy per-chunk PowerShell web progress output.

It also validates `release-source.json` before upload, so it will fail fast instead of publishing stale `.artifacts` from an older build.

The command wrapper now normalizes its working directory before invoking `cmd.exe`/PowerShell, which avoids the Codex app `\\?\C:\...` current-directory issue.

If you want the helper to provide the GitHub token automatically, keep the token in a local ignored companion file instead of the tracked wrapper:

1. Copy `scripts\Upload-ReleaseAssets.local.cmd.example` to `scripts\Upload-ReleaseAssets.local.cmd`.
2. Replace the placeholder PAT inside the local copy.
3. Run `scripts\Upload-ReleaseAssets.cmd`.

Important:

- `scripts\Upload-ReleaseAssets.local.cmd` is ignored by git.
- the installer/release packaging flow does not ship `Upload-ReleaseAssets.cmd` or `Upload-ReleaseAssets.local.cmd`, so the local token bootstrap stays outside packaged artifacts.
- do not commit a real PAT into any tracked file.

By default it uploads only:

- `MeetingRecorder-v<version>-win-x64.zip`
- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`

To include the heavier Windows installer assets too, opt in explicitly:

```powershell
scripts\Upload-ReleaseAssets.cmd -Installers
```

If you want to tune concurrency for slower or more restricted networks:

```powershell
scripts\Upload-ReleaseAssets.cmd -Installers -MaxParallelUploads 2
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

For the broader set of secure-corporate-environment deployment lessons that future apps should inherit from this stack, see `CORPORATE_ENVIRONMENT_LESSONS.md`.

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
- `MeetingRecorderInstaller.msi`
- `MeetingRecorder-v<version>-win-x64.zip`
- `Install-LatestFromGitHub.cmd`
- `Install-LatestFromGitHub.ps1`
- optional `ggml-*.bin`

The portable ZIP now also carries:

- `MeetingRecorder.App.exe`
- `AppPlatform.Deployment.Cli.exe`
- `MeetingRecorder.product.json`
- `bundle-integrity.json`

The WPF app host no longer requires these loose published files in the shipped self-contained bundle:

- `MeetingRecorder.App.dll`
- `MeetingRecorder.App.deps.json`
- `MeetingRecorder.App.runtimeconfig.json`

Important local verification note:

- `scripts\Publish-Portable.ps1`, `scripts\Build-Installer.ps1`, and `scripts\Build-Release.ps1` create fresh artifacts under `.artifacts`, but they do not replace the live app already installed under `%USERPROFILE%\Documents\MeetingRecorder`
- if you want to run the freshly built app from the canonical managed install root, deploy the published bundle explicitly with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Deploy-Local.ps1
```

- add `-BuildFirst` when you want the script to publish the portable bundle before installing it locally
- `Deploy-Local.ps1` installs the bundle through `AppPlatform.Deployment.Cli`, verifies `MeetingRecorder.App.exe` and `MeetingRecorder.product.json` hashes, and confirms the `.cmd` shell launchers now point at `%USERPROFILE%\Documents\MeetingRecorder`
- `Smoke-Test-Release.ps1` launches the built bundle and the MSI-installed app copy, waits 30 seconds for each, and fails on new `.NET Runtime`, `Application Error`, or `Windows Error Reporting` events that mention `MeetingRecorder.App.exe`

If you want the packaged startup smoke test before publishing, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64
```

## Publish To GitHub

1. Go to the repo releases page:
   - `https://github.com/shp847/meetingrecorder/releases`
2. Draft a new release.
3. Create or select the tag `v<version>`.
4. Upload:
   - `MeetingRecorderInstaller.msi`
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
- `Upload-ReleaseAssets.cmd` is the lighter-weight helper when you want to update the latest release without rebuilding first
- `Upload-ReleaseAssets.cmd` skips the EXE and MSI by default unless you pass `-Installers`

## Smoke Test After Publishing

1. Confirm the latest feed returns release JSON instead of `404`:
   - `https://api.github.com/repos/shp847/meetingrecorder/releases/latest`
2. Download `MeetingRecorderInstaller.msi`
3. Run `MeetingRecorderInstaller.msi`
4. Confirm the app installs into `%USERPROFILE%\Documents\MeetingRecorder`
5. Confirm the MSI skips the license agreement page and shows a success/completion screen
6. Confirm the completion screen offers `Launch Meeting Recorder`
7. Confirm `Meeting Recorder.lnk` shortcuts appear for the current user in Start Menu and on Desktop, and that they launch successfully
8. Confirm published outputs default to `Documents\Meetings\Recordings` and `Documents\Meetings\Transcripts`
9. Confirm the app launches and the `Setup` page is usable
10. Confirm the app does not immediately offer the same GitHub release again on first launch after the MSI install
10. Confirm the `Setup` page lists the uploaded `ggml-*.bin` assets under the GitHub-backed transcription download path
11. Download `Install-LatestFromGitHub.cmd` and confirm the fallback path still works when the MSI is not used

## User-Facing Install Story

Once a release is published, the recommended user path is:

1. Download `MeetingRecorderInstaller.msi` from GitHub Releases
2. Run it directly
3. On the MSI completion screen, leave `Launch Meeting Recorder` checked if you want to open the app immediately

Backup path:

1. Download `Install-LatestFromGitHub.cmd`
2. Run it directly

Optional EXE path:

1. Download `MeetingRecorderInstaller.exe`
2. Run it directly if the endpoint policy allows custom bootstrap executables
3. Confirm it launches the command bootstrap window and then gets out of the way instead of performing deployment itself

Later updates can reuse the MSI, EXE, or script bootstrap paths, with the script bootstrap still available as the lowest-friction fallback.
Existing installs keep their saved config values on update.
