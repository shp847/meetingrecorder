# External Audio Import Acceptance Matrix

Date: 2026-09-27. This is a synthetic-evidence ledger: it contains no source
paths, audio, transcripts, voice profiles, credentials, or user recordings.
`Partial` means the source/test slice exists but one or more rendered or
installed-package gates still need a controlled test environment.

| Sprint | Contract and source owner | Synthetic test / static evidence | Rendered, package, and documentation gate | Current evidence / open gate |
| --- | --- | --- | --- | --- |
| S0 — audit | Import boundary and user journey | `docs/external-audio-import-contract.md` | Review and recovery states at supported sizes | Partial: static journey exists; native capture still needed. |
| S1 — source safety | `ExternalAudioImportService`, source ownership policy | `ExternalAudioImportJobContractTests`, `ExternalAudioImportServiceTests` | Explicit picker/drop retention copy | Partial: synthetic source-mutation coverage; installed journey needed. |
| S2 — work storage and Inbox | Inbox path, journal, archive/error services | Inbox path, journal, reconciliation, and intake tests | Inbox opt-in/lock/offline/OneDrive states | Partial: no native Inbox state capture. |
| S3 — media probe | `ExternalAudioMediaProbe`, import service | Probe and service tests for decode/readiness taxonomy | Synthetic WAV plus licensed/generated non-WAV | Partial: installed non-WAV path not run. |
| S4 — add-files review | `MainWindow` review projection and commands | Review projection, XAML, and accessibility contract tests | Picker/drop, bulk, focus, long strings | Partial: source guard and focus proof; no WPF capture. |
| S5 — setup queueing | readiness resolver/coordinator | readiness, queue, and processing tests | setup-blocked then explicit resume | Partial: no installed setup journey. |
| S6 — normal processing | session input resolver/processor | staged-only resolver and session processor tests | publish and `.ready` ordering | Partial: package journey outstanding. |
| S7 — restart/backlog | recovery classifier, reconciliation, queue | classifier, reconciliation, and queue resume tests | restart mixed-state journey | Partial: installed restart proof outstanding. |
| S8 — recovery controls | recovery action resolver/service | resolver/service revision and receipt tests | retry/repair control states | Partial: native recovery controls remain unwired or uncaptured. |
| S9 — Meetings origin | origin resolver and output catalog | origin resolver/catalog public-projection tests | native catalog provenance row | Partial: rendered provenance row outstanding. |
| S10 — speaker parity | session processor speaker-label stage | imported staged-audio label parity test | imported speaker review state | Partial: synthetic processor proof; rendered result outstanding. |
| S11 — Inbox lifecycle | lifecycle policy and app import cycle | lifecycle, journal, reconciliation, and intake tests | pause/rescan/failed-only controls | Partial: durable lifecycle controls/capture outstanding. |
| S12 — privacy/accessibility | review projection and `MainWindow` semantics | 31 focused projection, public-output, and accessibility tests | ready/blocked/retry/duplicate/Inbox-disabled at DPI/high contrast | Partial: no exposed native test window for screenshot capture. |

## Cross-cutting release gates

| Gate | Evidence | Status |
| --- | --- | --- |
| Full core gate | `scripts/Test-All.ps1`, 2026-09-27: 1,427 passed / 1,434 total. Seven pre-existing failures remain: six installer executable-payload cases and one metadata-only diarization fixture-script case. | Recorded; not green. |
| Integration | `MeetingRecorder.IntegrationTests`, 2026-09-27: 8/8 passed. | Pass. |
| Installer layout | `scripts/Build-Installer.ps1`, 2026-09-27: ZIP 90,911,162 bytes; MSI 76,095,488 bytes. | Pass. |
| Clean-build provenance | Intended commit, toolchain/hash ledger, stable apphost/bundle-integrity inspection. | Open: shared working tree is dirty; do not treat current artifacts as clean-commit release evidence. |
| Packaged startup | `Smoke-Test-Release.ps1 -Runtime win-x64`. | Open: an installed Meeting Recorder process is live; do not interrupt user work. |
| Installed synthetic journey | Generated WAV plus legally distributable/generated non-WAV: Add/Drop, setup block/resume, retention, publish/ready, restart, retry, mixed result. | Open: needs a clean packaged build and isolated non-user install. |
| Rendered/accessibility | 1280x800 and 1440x900; 100/125%; high contrast; keyboard/screen reader. | Open: no native WPF window exposed to capture. |
