# Adaptive Backlog Drain Release Matrix

This matrix records only evidence for the staged-drain profiles. It does not authorize a release, change a provider, or override recording protection.

| Surface | Required proof | Current evidence | Result |
| --- | --- | --- | --- |
| Profile migration | New config is `Normal`; missing legacy profile preserves combined behavior once. | `BacklogAccelerationProfileResolverTests` | Focused pass |
| Transcript-only | Future staged work uses transcript-first strategy and optional stages remain deferred. | `BacklogAccelerationProfileResolverTests`, queue dispatch tests | Focused pass |
| Overnight | Window, DST ambiguity, and live-recording gates retain conservative single-worker fallback. | Overnight queue and background-policy tests | Focused pass |
| Idle CPU | AC/recording/sample gates and caps cannot preempt an active worker. | `IdleCpuCapacityPolicyTests`, queue capacity dispatch coverage | Focused pass |
| Optional GPU | Missing or invalid counters retain CPU-safe behavior; only one Auto/DirectML-capable lane exists. | `GpuCapacityPolicyTests`, diarization queue lane test | Focused pass |
| Queue status | Profile, truthful reason, current cap, and active count expose no device/process identity. | `ProcessingQueueServiceTests` and profile queue status projection | Focused pass |
| Settings accessibility | Profile control has an automation name and help text; native ComboBox supplies keyboard focus/selection. | `MainWindowXamlTests.Advanced_Settings_Expose_Background_Processing_And_Speaker_Labeling_Mode_Selectors` | Focused source contract pass; desktop visual review remains an operational follow-up |
| Full product tests | Core and integration regressions are clear. | `scripts\Test-All.ps1`, 2026-09-29 | 1511/1512 pass; one unrelated diarization fixture replay fails |
| Package | Portable layout/integrity, MSI install, and startup work with no active app. | `Build-Installer.ps1`, `Smoke-Test-Release.ps1` | Passed 2026-09-29 |
| Installed profile journey | Clean install preserves safe defaults; legacy migration and CPU/GPU fallback remain testable. | Profile/config/queue tests plus MSI-installed startup smoke | Passed within local synthetic/test boundary |

## Guardrails

- Profiles affect the next queue admission only; existing leases, worker priority, recording protection, provider consent, and published artifacts are unchanged.
- `Normal` is conservative for new installs. A legacy profile is migrated once to preserve the already-installed combined policy.
- Capacity samples are local, bounded, identity-free, non-telemetry inputs. Unknown data means no added launch.
- Package and installed evidence must be recorded separately from source tests. No release/upload action is implied by this document.
