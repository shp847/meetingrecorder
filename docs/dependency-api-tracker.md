# Dependency And API Tracker

Last checked: 2026-05-30

This tracker is maintained during recurring repo maintenance. Keep it focused on
direct dependencies, local/external contracts, source-of-truth docs, known update
risks, and verification commands. Do not record secrets or local key material.

## Dependency Ecosystems

| Ecosystem | Manifest or source | Check command | Notes |
| --- | --- | --- | --- |
| .NET 8 / NuGet | `MeetingRecorder.sln`, `AppPlatform.sln`, `*.csproj` | `dotnet list .\MeetingRecorder.sln package --outdated` | Primary app and test dependency surface. |
| WiX Toolset | `src\MeetingRecorder.Setup\MeetingRecorder.Setup.wixproj` | Included in `dotnet list .\MeetingRecorder.sln package --outdated` | Installer SDK/extensions are release-sensitive; avoid major upgrades in routine maintenance. |
| Runtime model assets | `assets\models`, `assets\native`, release assets | `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1` | Whisper, diarization, and DirectML runtime assets are shipped product inputs. |
| Local sibling contract | `..\Model Proxy\CONSUMER_INTEGRATION_HANDOFF.md` | Inspect handoff plus Meeting Recorder ModelProxy tests | Model Proxy is consumed over HTTP only, not as an imported library. |

## Direct NuGet Dependencies

| Dependency | Current checked version | Purpose | Source of truth | Status |
| --- | --- | --- | --- | --- |
| `System.IO.Pipelines` | 10.0.8 | Deployment/update stream handling in `AppPlatform.Deployment`. | NuGet package metadata via `dotnet list package --outdated`. | Updated from 10.0.0 on 2026-05-30 and verified with `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false`. |
| `NAudio` | 2.2.1 | Windows audio capture and device/session probing. | NuGet package metadata and app audio tests. | 2.3.0 is available; deferred because audio capture is a broad user-facing surface. |
| `Whisper.net` / `Whisper.net.Runtime` | 1.9.0 | Local Whisper transcription bindings/runtime. | NuGet package metadata and transcription tests. | Current by `dotnet list` on 2026-05-30. |
| `org.k2fsa.sherpa.onnx` | 1.12.17 | Optional speaker-labeling/diarization runtime. | NuGet package metadata, DirectML runtime docs, diarization tests. | 1.13.2 is available; deferred because diarization/native runtime behavior needs targeted validation. |
| `WixToolset.Sdk` / `WixToolset.*.wixext` | 5.0.2 | Per-user MSI build. | `MeetingRecorder.Setup.wixproj`, `scripts\Build-Installer.ps1`. | 7.0.0 is available; deferred as a major installer migration. |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | xUnit test host. | NuGet package metadata and test projects. | 18.6.0 is available; deferred as test infrastructure churn. |
| `xunit` / `xunit.runner.visualstudio` | 2.5.3 | Unit and integration tests. | NuGet package metadata and test projects. | xUnit 2.9.3 and runner 3.1.5 are available; deferred to avoid mixed runner migration during this pass. |
| `coverlet.collector` | 6.0.0 | Test coverage collector. | NuGet package metadata and test projects. | 10.0.1 is available; deferred as non-critical tooling churn. |

## Consumed APIs, Services, And Contracts

| Contract | Current app usage | Source of truth | Last checked | Notes |
| --- | --- | --- | --- | --- |
| Model Proxy local HTTP API | `GET /v1/models`, `POST /v1/chat/completions`, bearer auth, `X-ModelProxy-Web-Search: false`, safe `X-ModelProxy-*` routing headers, structured `backend_busy` / `cli_timeout` handling. | `..\Model Proxy\CONSUMER_INTEGRATION_HANDOFF.md`; `..\Model Proxy\CONFIG_REFERENCE.md`. | 2026-05-30 | Current local Model Proxy repo had uncommitted handoff changes. Meeting Recorder already uses `gpt-5.4-mini`, `default_model`, no-search requests, routing headers, SSE comment filtering, terminal `event: error`, and structured error handling. No implementation change was made. |
| OpenAI Chat Completions API | Hosted fallback only when the user saves an OpenAI key and provider preference permits fallback. | https://platform.openai.com/docs/api-reference/chat/create-chat-completion | 2026-05-30 | Official docs still list Chat Completions and `POST /v1/chat/completions`. Migration to another OpenAI surface is deferred because Model Proxy compatibility and the existing provider abstraction are Chat Completions based. |
| Microsoft Graph online meetings / cloud communications | Candidate official Teams path in capability probing and meeting-context evaluation. | https://learn.microsoft.com/en-us/graph/api/resources/onlinemeeting?view=graph-rest-1.0 and https://learn.microsoft.com/en-us/graph/api/resources/communications-api-overview?view=graph-rest-1.0 | 2026-05-30 | Keep fallback behavior because tenant policy/consent can block official paths. Live Events Graph API retirement does not change the current local detection fallback. |
| Outlook desktop COM calendar access | Best-effort local attendee/title enrichment, only binding to an already-running Outlook session. | `ARCHITECTURE.md`, `SETUP.md`, source under `src\MeetingRecorder.App\Services\OutlookCalendarMeetingTitleProvider.cs`. | 2026-05-30 | Preserve soft-fail, bounded-timeout behavior; do not add Graph or account requirements unless product scope changes. |
| Windows Core Audio / UI Automation / Win32 shell | Local audio capture, meeting detection, Teams roster capture, shortcuts, activation, installer relaunch. | Windows SDK APIs used directly from app services. | 2026-05-30 | Verify through focused app/core tests and startup smoke when behavior changes. |
| GitHub releases | App update checks, release asset selection, model/diarization downloads, upload helper. | `RELEASING.md`, `scripts\Build-Release.ps1`, `scripts\Upload-ReleaseAssets.cmd`. | 2026-05-30 | Release asset contract remains app ZIP only for in-app updates; installers are release assets, not app-update ZIPs. |

## Deferred Updates And Risks

- Defer audio, transcription, diarization, WiX major, and test-runner upgrades
  unless a focused maintenance task can run the relevant app, packaging, and
  runtime smoke checks.
- Recheck Model Proxy handoff after its current local uncommitted contract docs
  are finalized. If the canonical guidance changes away from forced
  `app-server` for no-search summary validation, update code, tests, README,
  SETUP, ARCHITECTURE, AGENTS, and this tracker together.
- App-owned ignored `.tmp` folders exist in this worktree, but this run did not
  prove they were safe to delete. Runtime cleanup code already targets
  `%TEMP%\MeetingRecorderDiarization`, `%TEMP%\MeetingRecorderTranscription`,
  installer/update workspaces, and generated archive backups.

## Verification Notes

- 2026-05-30: `dotnet list .\MeetingRecorder.sln package --outdated` reached
  NuGet and identified available updates. It returned non-zero because
  `tests\AppPlatform.Tests` had no assets file before restore, but still
  reported the direct package surface for the main solution.
- 2026-05-30: `System.IO.Pipelines` was updated with
  `dotnet add .\src\AppPlatform.Deployment\AppPlatform.Deployment.csproj package System.IO.Pipelines --version 10.0.8`.
- 2026-05-30: `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false`
  restored `tests\AppPlatform.Tests` and passed 7 tests. A follow-up
  `dotnet list .\MeetingRecorder.sln package --outdated` completed successfully;
  `AppPlatform.Deployment` had no remaining updates.
- 2026-05-30: `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`
  passed and rebuilt the ZIP, MSI, and bootstrap assets under
  `.artifacts\installer\win-x64`.
