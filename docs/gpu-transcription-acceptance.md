# GPU Transcription Acceptance And No-Go Contract

Status: Sprint 0 policy baseline, 2026-09-27. No GPU transcription runtime,
model, asset, setting, download, telemetry channel, or product execution path
is authorized by this document.

## Product promise

CPU transcription is the product. A GPU candidate is optional, local, and
revocable per job/device/runtime version. It may only accelerate eligible
background backlog work after an approved feasibility result. It must never
block recording, model setup, publish, retry, update, rollback, or CPU
transcription. Normal users receive no driver, administrator, CUDA, Python,
model-conversion, download, or hardware-management task.

## Policy state contract

`GpuTranscriptionPolicy` is pure and has no hardware, process, network, or
asset access. Its reason codes are stable and contain no device name, source
path, audio, transcript, model hash, or exception text.

| Condition | State | CPU behavior | GPU action |
| --- | --- | --- | --- |
| No candidate requested | `NotRequested` | Required. | None. |
| Any no-go reason | `Unavailable` | Required. | No probe or execution. |
| Active recording or open circuit breaker | `Suppressed` | Required. | No probe or execution. |
| CPU baseline, asset contract, or capability unproven | `Unavailable` | Required. | No probe or execution. |
| Every gate passes | `Probing` | Required while probing. | A later isolated spike may probe only. |
| Future feasibility-authorized run | `Eligible`, `Accelerating`, `CPUFallback`, or `Failed` | Must remain available and preserve current publish semantics. | Not implemented in Sprint 0. |

## Hard no-go reasons

Reject a candidate before product integration if it requires administrator
rights, driver installation, a user-managed runtime, an unapproved download,
or has an unsupported license/supply chain. Also reject it for CPU reliability
regression, material output divergence, privacy or endpoint-security risk,
battery/thermal harm, or unsupported package/update rollback. A technically
working candidate does not override a no-go result.

## Feasibility benchmark protocol

Sprint 1 must use a pinned runtime/model/source-hash tuple and a local,
synthetic-or-consented corpus. For each supported device/power mode and each
short, long, and sparse fixture, collect at least ten matched cold CPU/candidate
runs and ten matched warm runs. Record median and p95 elapsed time, throughput,
peak working-set estimate, battery/AC mode, and bounded error category. Report
the sample count and uncertainty; do not claim acceleration from a smaller or
unmatched sample.

Compare normalized transcript schema, segment order, timestamps, language,
cancellation, empty/sparse output, and failure behavior against CPU. Reports
contain only fixture ids, capability class, aggregated metrics, package file
list, hashes, and safe reason codes—never input audio, transcript text, local
paths, voice data, keys, or auth headers.

## Evidence and stop review

The feasibility owner must record candidate runtime/version, model format and
provenance, license/signing, supported Windows/device class, package size,
CPU-fallback result, quality comparison, and rollback path in
`docs/dependency-api-tracker.md` only after evidence exists. No candidate has
passed this gate yet.

Focused verification: `GpuTranscriptionPolicyTests` and
`SettingsPresetServiceTests` (14/14 passed on 2026-09-27).
