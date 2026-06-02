# Agent Instructions

These rules add Meeting Recorder-specific guidance on top of the global agent
instructions.

## Design Guidance

- You must follow `DESIGN.md` as the local source of truth for UI, UX, shell, installer,
  layout, typography, spacing, color, and interaction-polish work.

## Repository Workflows And Commands

- This is a Windows/.NET 8 WPF app with xUnit tests. `MeetingRecorder.sln` is
  the full product solution; `AppPlatform.sln` scopes the reusable platform
  projects and `tests\AppPlatform.Tests`.
- Use PowerShell commands from the repo root. `rg` may be unavailable on this
  Windows environment; if it fails, fall back to PowerShell search/enumeration.
- Main verification command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`. Defined in
  `scripts\Test-All.ps1`; it shuts down dotnet build servers, builds
  core/app/worker/installer/test projects serially with
  `RestoreIgnoreFailedSources=true` and `NuGetAudit=false`, then runs
  `MeetingRecorder.Core.Tests` and `MeetingRecorder.IntegrationTests`.
- AppPlatform verification gap: `tests\AppPlatform.Tests` exists in both
  solutions but is not invoked by `scripts\Test-All.ps1`. AppPlatform, shared
  deployment/update, or extraction-workflow changes use
  `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false`.
- Installer asset rebuild command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
  Defined in `scripts\Build-Installer.ps1`; it runs `Publish-Portable.ps1`,
  generates WiX authoring, builds `MeetingRecorderInstaller.msi`, and writes
  ZIP/MSI/bootstrap assets under `.artifacts\installer\win-x64`.
- Smaller framework-dependent packaging command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1 -FrameworkDependent`.
  Defined in `scripts\Build-Installer.ps1`; use only when target machines
  already have the .NET 8 Desktop Runtime installed.
- Release build command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1`.
  Defined in `scripts\Build-Release.ps1`; it runs `Test-All.ps1` unless
  `-SkipTests` is passed, then runs `Build-Installer.ps1`, copies model assets,
  writes release metadata, and can optionally upload to GitHub with
  `-UploadToGitHubLatestRelease` or dry-run with `-DryRunGitHubUpload`.
- Packaging-only release command for explicitly scoped fast rebuilds:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -SkipTests`.
  Defined in `scripts\Build-Release.ps1`.
- Packaged startup smoke test:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.
  Defined in `scripts\Smoke-Test-Release.ps1`; it requires no running
  `MeetingRecorder.App` process and checks built bundle/MSI-installed startup
  plus relevant Windows crash events.
- Portable publish command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1`.
  Defined in `scripts\Publish-Portable.ps1`; it publishes app/CLI/worker
  payloads, writes `release-source.json`, `bundle-mode.txt`,
  `bundle-layout.json`, and `bundle-integrity.json`, and fails if required loose
  apphost files are missing.
- Portable startup/dependency behavior lives in `scripts\Run-MeetingRecorder.cmd`
  and `scripts\Check-Dependencies.ps1`; installed shortcuts should target the
  launcher, but in-app update relaunches should start `MeetingRecorder.App.exe`
  directly when possible, and both launcher/dependency checks should fail
  clearly if the apphost is missing.
- `scripts\Deploy-Local.ps1` and `scripts\Deploy-Local.cmd` are developer-only
  local deployment shortcuts; they can refresh the managed install root by
  delegating to `AppPlatform.Deployment.Cli install-bundle`, but release
  validation must still go through MSI, bootstrapper, or in-app update paths.
- Release upload helper: `scripts\Upload-ReleaseAssets.cmd` is documented in
  `RELEASING.md`; it validates `release-source.json`, rebuilds stale installer
  assets only when the current source state is clean, skips EXE/MSI uploads
  unless `-Installers` is passed, supports `-DryRun` and `-MaxParallelUploads`,
  and must keep real tokens only in ignored local files or environment
  variables.
- In-app update package validation is intentionally narrow: only
  `MeetingRecorder-v<version>-win-x64.zip` is an app update asset. Model
  binaries, diarization bundles, MSI files, bootstrap scripts, missing pending
  files, size mismatches, and corrupt ZIPs must be rejected before update apply
  asks the running app to shut down.
- V2 in-app updates preserve stable apphosts (`MeetingRecorder.App.exe`,
  `AppPlatform.Deployment.Cli.exe`, and `MeetingRecorder.ProcessingWorker.exe`)
  and replace mutable DLLs, scripts, assets, and manifests around them;
  `Publish-Portable.ps1` must keep `bundle-layout.json`,
  `bundle-integrity.json`, and loose app files (`MeetingRecorder.App.dll`,
  `.deps.json`, `.runtimeconfig.json`) aligned.
- Startup/relaunch changes must preserve headless-failure recovery: repair
  missing or malformed process `windir` before WPF window creation, treat
  dispatcher UI exceptions as fatal after logging, and only acknowledge
  second-launch activation after a visible main window is available.
- ModelProxy summary validation command:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Test-ModelProxy.ps1`.
  Defined in `scripts\Test-ModelProxy.ps1`; it posts a synthetic
  `summary-provider-ok` prompt to `http://127.0.0.1:8645/v1/chat/completions`,
  uses `MODELPROXY_MEETING_RECORDER_API_KEY` or local fallback `sk-modelproxy`,
  forces the no-search app-server path with `X-ModelProxy-Backend: app-server`
  and `X-ModelProxy-Web-Search: false`, and prints only safe routing metadata.
- Speaker-name learning stores local voice-profile embeddings under
  `%LOCALAPPDATA%\MeetingRecorder\speaker-profiles\voice-profiles.json` or
  portable `data\speaker-profiles`; changes in this area should preserve
  local-only behavior and cover `VoiceProfileStoreTests`,
  `VoiceProfileMatcherTests`, `SpeakerNameLearningServiceTests`,
  `TranscriptSchemaTests`, and relevant `SessionProcessorTests`.
- DirectML speaker-labeling changes should keep
  `MeetingRecorder.ProcessingWorker --probe-directml` manifest-free, attempt
  DirectML only when acceleration is `Auto`, fall back safely to CPU, and cover
  `OptionalSidecarDiarizationProviderSourceTests`,
  `DiarizationClusterSelectionServiceTests`, and related config/UI tests.
- Maintain `docs/dependency-api-tracker.md` during recurring maintenance runs.
  Keep it limited to dependency ecosystems, key local/external contracts,
  latest checked versions or revisions, deferred updates, and the verification
  command used; do not duplicate the tracker contents in this file.

## Release Hygiene

- Treat installer assets as part of the shipped product, not an optional
  follow-up.
- When the user asks to `deploy` or requests a deployment, interpret that as:
  run the required verification/build flow, commit the intended source changes,
  push the commit, rebuild release artifacts from the clean pushed commit when
  needed, then upload GitHub release assets including installers. Do not treat
  `deploy` as a local install or local file-copy deployment.
- For every app, installer, script, or runtime behavior change, rebuild the
  installer assets before considering the task complete.
- After every build-backed change set that is intended to ship, commit and push
  the work, then upload the release assets including installers.
- For every app, installer, script, or runtime behavior change, update the
  relevant documentation in the same task so the written guidance stays in sync
  with the shipped behavior.
- Relevant documentation may include `README.md`, `SETUP.md`, `RELEASING.md`,
  `ARCHITECTURE.md`, installer guidance, or other task-specific docs.
- If a task is docs-only, state that no installer rebuild was required.
