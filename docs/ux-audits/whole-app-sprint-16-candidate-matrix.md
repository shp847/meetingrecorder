# Whole-App Sprint 16 Candidate Evidence Matrix

This is the deterministic candidate-evidence ledger for Whole-App UX
Simplification Sprints 0–16. It records source, test, rendered, package, and
installed evidence separately. It does not authorize signing, publication, or
release promotion. Sprint 17 owns manual Windows OS-DPI, High Contrast, and
Narrator observation.

| Sprint | User boundary | Authoritative automated evidence | Rendered/package evidence | Candidate result |
| --- | --- | --- | --- | --- |
| S0 | Control inventory and redacted journey baseline | `docs/ux-audits/whole-app-sprint-0-controls.json`, `whole-app-sprint-0-friction.md`, source validators | `whole-app-sprint-0-rendered-evidence.md` | Existing evidence; recheck link integrity in S16. |
| S1 | UX safety, visible consequences, and no hidden mutations | `AutomationCatalogSnapshotTests`, `UserActionCopyResolverTests`, `UserFacingCopyTests` | S15 synthetic shell evidence | Full regression passed 2026-10-06. |
| S2 | Preset inference and atomic setting patching | `SettingsPresetServiceTests`, `MainWindowInteractionLogicTests` | S15 Settings fixture evidence | Full regression passed 2026-10-06. |
| S3 | Settings sections and deep-link focus | `SettingsInformationArchitectureTests`, `AccessibilityContractTests`, `MainWindowInteractionLogicTests` | S15 Settings routing/focus capture | Full regression passed 2026-10-06. |
| S4 | Safe recording and setup choices | `RecordingReadinessServiceTests`, `RecordingSessionCoordinatorTests`, `MainWindowInteractionLogicTests` | Synthetic ready/blocked shell states | Full regression passed 2026-10-06. |
| S5 | Home command-center next action | `HomeCommandCenterServiceTests`, `MainWindowInteractionLogicTests` | S15 Home shell capture | Full regression passed 2026-10-06. |
| S6 | Meetings presets and resilient selection | `MeetingViewPresetResolverTests`, `MeetingsExperienceResolverTests`, `MeetingSearchResolverTests` | S15 Meetings shell capture | Full regression passed 2026-10-06. |
| S7 | Recommendation-first triage | `MeetingRecommendationResolverTests`, `MeetingRecommendationPresentationTests`, `MeetingAttentionInboxResolverTests` | Synthetic Meetings fixture | Full regression passed 2026-10-06. |
| S8 | Grouped, eligible meeting actions | `MeetingActionCatalogTests`, `MeetingCapabilityInventoryTests`, `MeetingArchivePreflightTests` | S15 destructive-action fixture | Full regression passed 2026-10-06. |
| S9 | Detail read/maintain route and return focus | `MeetingDetailTaskCenterTests`, `MainWindowInteractionLogicTests`, `AccessibilityContractTests` | S15 detail-open/close capture | Full regression passed 2026-10-06. |
| S10 | Backlog/recovery status and safe action | `BacklogExperienceResolverTests`, `MeetingOutputStatusResolverTests`, `MeetingCleanup*Tests` | Synthetic processing/recovery fixture | Full regression passed 2026-10-06. |
| S10A | ASAP priority continues through speaker labeling | `AsapLifecycleResolverTests`, `SpeakerExperienceResolverTests` | Synthetic status fixture | Full regression passed 2026-10-06. |
| S11 | Safe automation with bounded receipts | `AutomationCatalogSnapshotTests`, `MeetingCleanupAutoApply*Tests`, `MeetingCleanupWorkLedgerServiceTests` | Synthetic automation-state fixture | Full regression passed 2026-10-06. |
| S12 | Local/hosted summary consent and recovery | `SummaryExperienceResolverTests`, `SummarySecretStoreTests`, `MeetingSummarizationProviderTests` | S15 hosted-consent cancel capture | Full regression passed 2026-10-06. |
| S13 | Speaker labels, profiles, and privacy-safe deletion | `SpeakerReviewSnapshotResolverTests`, `SpeakerNameLearningServiceTests`, `SpeakerExperienceResolverTests` | S15 profile-delete-cancel capture | Full regression passed 2026-10-06. |
| S14 | Clear blocked state and destructive-copy scope | `UserActionCopyResolverTests`, `UserFacingCopyTests`, `AccessibilityContractTests` | S15 confirmation fixtures | Full regression passed 2026-10-06. |
| S15 | Deterministic WPF, keyboard, semantic contracts | `WpfRenderedShellHarnessTests`, `AccessibilityContractTests`, `MainWindowInteractionLogicTests` | `whole-app-sprint-15-rendered-evidence.md`; installed integrity/startup smoke | Complete 2026-10-04. |
| S16 | Candidate regression, installer, smoke, docs, provenance | `scripts/Test-All.ps1`; `tests/AppPlatform.Tests`; installer and smoke scripts | Current source/package/installed evidence below | Deterministic and package gates passed 2026-10-06. |

## Required candidate gates

| Gate | Command or review | Result |
| --- | --- | --- |
| Core and integration regression | `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1` | Passed 2026-10-06: core 1,676/1,676; integration 8/8. |
| Shared platform regression | `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false` | Passed 2026-10-06: 7/7. |
| Installer payload | `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1` | Passed 2026-10-06: portable ZIP and MSI built. |
| Installed startup | `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64` | Passed 2026-10-06: portable, MSI install/integrity, and MSI-installed app. |
| Artifact provenance | ZIP/MSI SHA-256, bundle integrity, source revision, dirty-tree disclosure | Recorded below. |
| Documentation | Command/link review of release-relevant guidance and this matrix | Updated 2026-10-06. |
| External accessibility | Sprint 17 manual test-profile checklist | Not part of S16; release promotion blocked until complete. |

## Candidate interpretation

- Passing source tests, synthetic WPF rendering, package construction, and
  startup smoke demonstrate different boundaries. None substitutes for another.
- Existing generated binaries, local artifacts, personal profiles, audio,
  transcripts, credentials, and hosted-provider data are excluded from evidence.
- A failed, skipped, stale, or dirty-source gate is recorded as such. It cannot
  be summarized as a candidate pass.

## Provenance — 2026-10-06

- Source base: `34d842a`; this candidate is a scoped working-tree snapshot.
  Unrelated generated binaries, local artifacts, and test result directories
  were preserved and not included as source evidence.
- Bundle: `MeetingRecorder-v0.3-win-x64.zip`, SHA-256
  `640c3261ae46496b03ca3877ada383d7e11bacf26f8b385b0e04ae7be209686d`.
- Installer: `MeetingRecorderInstaller.msi`, SHA-256
  `8cb15d1b7e2d113f921e7b83b4d732cfd80a66b5bb4a8d7ccb8177ac7cd140cb`.
- `bundle-integrity.json` covers stable apphosts, loose app/worker DLLs,
  manifests, runtime configuration, model catalog, and launcher assets; the
  installed-integrity smoke validated it after MSI installation.
- This evidence does not authorize signing, publication, or release promotion.
  Sprint 17 owns live Windows DPI, High Contrast, and Narrator observation.
