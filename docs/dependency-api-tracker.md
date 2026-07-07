# Dependency And API Tracker

Last checked: 2026-07-04

This tracker is maintained during recurring repo maintenance. Keep it focused on
direct dependencies, local/external contracts, source-of-truth docs, known update
risks, and verification commands. Do not record secrets or local key material.

## Dependency Ecosystems

| Ecosystem | Manifest or source | Check command | Notes |
| --- | --- | --- | --- |
| .NET 8 / NuGet | `MeetingRecorder.sln`, `AppPlatform.sln`, `*.csproj` | `dotnet list .\MeetingRecorder.sln package --outdated` | Primary app and test dependency surface. |
| WiX Toolset | `src\MeetingRecorder.Setup\MeetingRecorder.Setup.wixproj` | Included in `dotnet list .\MeetingRecorder.sln package --outdated` | Installer SDK/extensions are release-sensitive; avoid major upgrades in routine maintenance. |
| Runtime model assets | `assets\models`, `assets\native`, release assets | `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1` | Whisper, diarization, and DirectML runtime assets are shipped product inputs. |
| Local sibling contract | `%USERPROFILE%\OneDrive - Kearney\Documents\Coding Projects\Model Proxy\CONSUMER_INTEGRATION_HANDOFF.md` | Inspect handoff plus Meeting Recorder ModelProxy tests | Model Proxy is consumed over HTTP only, not as an imported library. |

## Direct NuGet Dependencies

| Dependency | Current checked version | Purpose | Source of truth | Status |
| --- | --- | --- | --- | --- |
| `System.IO.Pipelines` | 10.0.9 | Deployment/update stream handling in `AppPlatform.Deployment`. | NuGet package metadata via `dotnet list package --outdated`. | Updated from 10.0.8 on 2026-06-13 with `dotnet add .\src\AppPlatform.Deployment\AppPlatform.Deployment.csproj package System.IO.Pipelines --version 10.0.9`; verified with `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false` and `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`. |
| `NAudio` | 2.2.1 | Windows audio capture and device/session probing. | NuGet package metadata and app audio tests. | 2.3.0 is available; deferred because audio capture is a broad user-facing surface. |
| `Whisper.net` / `Whisper.net.Runtime` | 1.9.0 | Local Whisper transcription bindings/runtime. | NuGet package metadata and transcription tests. | 1.9.1 is available; deferred because transcription runtime updates need focused processing tests plus installer/release asset validation. |
| `org.k2fsa.sherpa.onnx` | 1.12.17 | Optional speaker-labeling/diarization runtime. | NuGet package metadata, DirectML runtime docs, diarization tests. | 1.13.3 is available; deferred because diarization/native runtime behavior needs targeted validation. |
| `WixToolset.Sdk` / `WixToolset.*.wixext` | 5.0.2 | Per-user MSI build. | `MeetingRecorder.Setup.wixproj`, `scripts\Build-Installer.ps1`. | 7.0.0 is available; deferred as a major installer migration. |
| `Microsoft.NET.Test.Sdk` | 17.8.0 | xUnit test host. | NuGet package metadata and test projects. | 18.7.0 is available; deferred as test infrastructure churn. |
| `xunit` / `xunit.runner.visualstudio` | 2.5.3 | Unit and integration tests. | NuGet package metadata and test projects. | xUnit 2.9.3 and runner 3.1.5 are available; deferred to avoid mixed runner migration during this pass. |
| `coverlet.collector` | 6.0.0 | Test coverage collector. | NuGet package metadata and test projects. | 10.0.1 is available; deferred as non-critical tooling churn. |

## Consumed APIs, Services, And Contracts

| Contract | Current app usage | Source of truth | Last checked | Notes |
| --- | --- | --- | --- | --- |
| Model Proxy local HTTP API | `GET /v1/models`, `POST /v1/responses`, bearer auth, `X-ModelProxy-Web-Search: false`, optional `X-ModelProxy-Cloud: deny` for local-only requests, safe `X-ModelProxy-*` routing headers, structured `backend_busy` / `cli_timeout` / `config_error` handling. | `%USERPROFILE%\OneDrive - Kearney\Documents\Coding Projects\Model Proxy\CONSUMER_INTEGRATION_HANDOFF.md`; `%USERPROFILE%\OneDrive - Kearney\Documents\Coding Projects\Model Proxy\CONFIG_REFERENCE.md`. | 2026-07-04 | Meeting Recorder consumes the portable Responses surface only, treats `/v1/models` as an OpenAI-shaped model list, defaults to `gpt-5.4-mini` unless app settings choose another model, and no longer reads `default_model`, `default_codex_model`, `backend`, `default`, or `default_backend_model`. Private transcript enrichment keeps `X-ModelProxy-Web-Search: false`; local-only requests also send `X-ModelProxy-Cloud: deny` and may receive `config_error` until a proven local backend exists. Remote audio remains parked unless `/v1/models` advertises `gpt-4o-transcribe` or `gpt-4o-transcribe-diarize`; local Whisper plus local diarization remain the primary fallback on `audio_disabled`, `unsupported_model`, `backend_unavailable`, `backend_busy`, `timeout`, `quota`, `config_error`, or protocol mismatch. The local Model Proxy handoff is still dirty and transitional: its Meeting Recorder section documents Responses, while older generic examples still mention Chat Completions and the default `sk-modelproxy` key. |
| OpenAI Responses API | Hosted fallback only when the user saves an OpenAI key and provider preference permits fallback. | https://developers.openai.com/api/reference/responses/overview/ and https://developers.openai.com/api/docs/guides/migrate-to-responses | 2026-07-04 | Official OpenAI docs still document the Responses API and migration from Chat Completions to `POST /v1/responses`; Meeting Recorder uses `POST /v1/responses` for hosted fallback too, keeping the provider abstraction portable across local ModelProxy and hosted OpenAI. |
| Microsoft Graph online meetings / cloud communications | Candidate official Teams path in capability probing and meeting-context evaluation. | https://learn.microsoft.com/en-us/graph/api/resources/onlinemeeting?view=graph-rest-1.0, https://learn.microsoft.com/en-us/graph/api/onlinemeeting-get?view=graph-rest-1.0, https://devblogs.microsoft.com/microsoft365dev/deprecation-notice-teams-live-events-meeting-creation-via-microsoft-graph/, and https://learn.microsoft.com/en-us/graph/api/resources/communications-api-overview?view=graph-rest-1.0 | 2026-07-04 | Keep fallback behavior because tenant policy/consent can block official paths. Current Microsoft docs are mixed for Teams live events: the `onlineMeeting` resource page still carries the September 30, 2024 live-event deprecation caveat, the `Get onlineMeeting` page says Teams live events were not removed on that date, and Microsoft 365 Developer guidance removed `isBroadcast` live-event creation support from Graph v1.0 on June 30, 2026. This does not change the current local detection fallback. |
| Outlook desktop COM calendar access | Best-effort local attendee/title enrichment, only binding to an already-running Outlook session. | `ARCHITECTURE.md`, `SETUP.md`, source under `src\MeetingRecorder.App\Services\OutlookCalendarMeetingTitleProvider.cs`. | 2026-05-30 | Preserve soft-fail, bounded-timeout behavior; do not add Graph or account requirements unless product scope changes. |
| Windows Core Audio / UI Automation / Win32 shell | Local audio capture, meeting detection, Teams roster capture, shortcuts, activation, installer relaunch. | Windows SDK APIs used directly from app services. | 2026-05-30 | Verify through focused app/core tests and startup smoke when behavior changes. |
| GitHub releases | App update checks, release asset selection, model/diarization downloads, upload helper. | `RELEASING.md`, `scripts\Build-Release.ps1`, `scripts\Upload-ReleaseAssets.cmd`. | 2026-05-30 | Release asset contract remains app ZIP only for in-app updates; installers are release assets, not app-update ZIPs. |

## Deferred Updates And Risks

- Defer audio, transcription, diarization, WiX major, and test-runner upgrades
  unless a focused maintenance task can run the relevant app, packaging, and
  runtime smoke checks.
- Recheck Model Proxy handoff after its current local uncommitted contract docs
  are finalized. If the canonical guidance changes away from Meeting Recorder's
  forced `app-server` no-search summary and validation path, update code, tests,
  README, SETUP, ARCHITECTURE, AGENTS, and this tracker together.
- Ignored repo `.tmp` folders and app-owned `%TEMP%\MeetingRecorder*`
  directories remain cleanup candidates only when ownership and inactivity are
  proven. This run left them in place because several are recent build, test,
  installer, transcription, or diarization locations. Runtime cleanup code
  already targets `%TEMP%\MeetingRecorderDiarization`,
  `%TEMP%\MeetingRecorderTranscription`, installer/update workspaces, and
  generated archive backups.

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
- 2026-06-06: `dotnet list .\MeetingRecorder.sln package --outdated` reached
  NuGet and identified available updates for NAudio 2.3.0, Whisper.net 1.9.1,
  Whisper.net.Runtime 1.9.1, org.k2fsa.sherpa.onnx 1.13.2, WiX 7.0.0, and test
  packages. No package updates were applied because the available updates touch
  audio, transcription, diarization/native runtime, installer, or test
  infrastructure surfaces.
- 2026-06-06: `dotnet list .\AppPlatform.sln package --outdated` reached NuGet;
  only `tests\AppPlatform.Tests` test packages had available updates.
- 2026-06-06: OpenAI official docs still document
  `POST /v1/chat/completions` and recommend Responses for new projects.
- 2026-06-06: Microsoft Learn still documents Graph `onlineMeeting` and cloud
  communications APIs; Teams live-event-specific Graph APIs remain deprecated
  and outside the current local fallback path.
- 2026-06-13: `dotnet list .\MeetingRecorder.sln package --outdated` and
  `dotnet list .\AppPlatform.sln package --outdated` reached NuGet and found
  `System.IO.Pipelines` 10.0.9 available for `AppPlatform.Deployment`; both
  commands returned non-zero because `tests\AppPlatform.Tests` did not yet have
  an assets file. Broader NAudio, Whisper.net, sherpa.onnx, WiX, and test
  package updates remained available and deferred for the same user-facing or
  infrastructure-risk reasons recorded above.
- 2026-06-13: `System.IO.Pipelines` was updated with
  `dotnet add .\src\AppPlatform.Deployment\AppPlatform.Deployment.csproj package System.IO.Pipelines --version 10.0.9`.
- 2026-06-13: `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false`
  passed 7 tests after restore.
- 2026-06-13: Follow-up `dotnet list .\MeetingRecorder.sln package --outdated`
  and `dotnet list .\AppPlatform.sln package --outdated` completed
  successfully; `AppPlatform.Deployment` had no remaining updates, while the
  deferred audio, transcription, diarization, WiX, and test package updates
  remained available.
- 2026-06-13: `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`
  passed and rebuilt the ZIP, MSI, and bootstrap assets under
  `.artifacts\installer\win-x64`.
- 2026-06-13: The local Model Proxy handoff was rechecked from the sibling repo;
  it was still dirty and still compatible with Meeting Recorder's explicit
  no-search app-server summary policy.
- 2026-06-13: OpenAI official docs still document
  `POST /v1/chat/completions`, recommend Responses for new text-generation
  apps, and recommend migrating Chat Completions flows over time.
- 2026-06-13: Microsoft Learn still documents Graph `onlineMeeting` and cloud
  communications APIs, including the live-event Graph API deprecation and cloud
  communications media-persistence restriction.
- 2026-06-21: Meeting Recorder summary transport moved to `POST /v1/responses`,
  the local default ModelProxy key changed to
  `sk-modelproxy-meeting-recorder`, `/v1/models` parsing was tightened to
  OpenAI-shaped model objects only, and local-only requests now send
  `X-ModelProxy-Cloud: deny`.
- 2026-06-21: Focused validation passed with
  `dotnet test .\tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj -p:NuGetAudit=false --filter "FullyQualifiedName~ModelProxyClientTests|FullyQualifiedName~MeetingSummarizationProviderTests|FullyQualifiedName~InstallerScriptTests"`.
- 2026-06-27: `dotnet list .\MeetingRecorder.sln package --outdated`
  reached NuGet and found the same deferred NAudio, Whisper.net, WiX, xUnit,
  runner, and coverlet updates; `org.k2fsa.sherpa.onnx` latest is now 1.13.3
  and `Microsoft.NET.Test.Sdk` latest is now 18.7.0. The command returned
  non-zero because `tests\AppPlatform.Tests` still had no assets file.
- 2026-06-27: `dotnet list .\AppPlatform.sln package --outdated` reached NuGet;
  AppPlatform projects had no package updates. The command returned non-zero
  because `tests\AppPlatform.Tests` still had no assets file.
- 2026-06-27: Model Proxy sibling docs were rechecked. The sibling worktree is
  still dirty; Meeting Recorder source already follows the dirty handoff's
  Responses/offload section, so no implementation change was made in this
  maintenance pass.
- 2026-06-27: App-owned temp candidates under `%TEMP%\MeetingRecorder*` and
  `%LOCALAPPDATA%\MeetingRecorder\work` were rechecked; they were recent
  test/build/runtime roots, so nothing was deleted.
- 2026-07-04: `dotnet list .\MeetingRecorder.sln package --outdated`
  reached NuGet and found the same deferred NAudio, Whisper.net,
  org.k2fsa.sherpa.onnx, WiX, xUnit, runner, and coverlet updates recorded
  above. The command returned non-zero because `tests\AppPlatform.Tests` still
  had no assets file.
- 2026-07-04: `dotnet list .\AppPlatform.sln package --outdated` reached NuGet;
  AppPlatform projects had no package updates. The command returned non-zero
  because `tests\AppPlatform.Tests` still had no assets file.
- 2026-07-04: Model Proxy handoff docs were rechecked at the local OneDrive
  source path. The Model Proxy worktree is still dirty and transitional;
  Meeting Recorder source already follows the handoff's Responses/offload
  section, so no implementation change was made.
- 2026-07-04: OpenAI Responses docs and Microsoft Graph online meeting/live
  event docs were rechecked. The only tracker change needed was documenting
  the current Teams live-event Graph deprecation nuance; Meeting Recorder's
  local detection fallback remains unchanged.
- 2026-07-04: App-owned temp candidates under `%TEMP%\MeetingRecorder*` and
  `%LOCALAPPDATA%\MeetingRecorder\work` were rechecked. Recent installer,
  transcription, test, and work roots were present, so nothing was deleted.
