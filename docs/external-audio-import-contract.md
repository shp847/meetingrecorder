# External Audio Import Contract

Status: Sprints 0–11 intake, normal-processing, startup-reconciliation,
recovery-action, published-origin projection, speaker-stage parity, and Inbox
lifecycle contract, verified 2026-09-27.

This document is the compatibility boundary for external audio intake. It
describes current behavior and the managed Import Inbox boundary. Sprint 1
owns the durable job model and atomic staging transaction; Sprint 2 owns the
opt-in Inbox, its local journal, and archive receipt; Sprint 3 owns the shared
read-only decoder/preparation probe and its retry evidence; Sprint 6 owns the
staged-only processor input boundary and collision-safe imported output stem;
Sprint 7 owns read-only startup reconciliation before worker admission; Sprint
8 owns named recovery-action projection and the first staged-work retry
transaction; Sprint 9 owns the safe published-meeting origin projection;
Sprint 10 proves imported staged audio receives the same speaker-labeling and
voice-sample stage as recordings; Sprint 11 owns bounded Inbox lifecycle
admission while setup is unavailable.

## User promise

Explicit import and legacy watched-folder paths only read an original file.
They copy it into a Meeting Recorder work session. They do not delete, move,
rename, truncate, or overwrite the original source. Removing an unqueued row
removes only that review row. It does not remove a source file, work session,
or published meeting. The optional Import Inbox follows the same retention
default. Its separate Archive option can move only an Inbox-receipted source,
only after its staged work copy and queued manifest commit; an archive failure
keeps both the source and a local `ArchivePending` receipt for retry. A second
opt-in Error policy may move only terminally unreadable Inbox-receipted audio;
transient, duplicate, unsafe-path, and storage blocks always leave the source
in place.

`ImportedSourceAudioInfo.SourceRetained` records this operational fact. It is
not a caller-selected retention policy: `ExternalAudioImportService` always
records `true` after an import copy succeeds.

Full original paths are local work-manifest and local job metadata. Published
meeting audio, Markdown, transcript JSON, summaries, and ready markers are not
written from `ImportedSourceAudioInfo` or `ExternalAudioImportJob`. Local
diagnostic logs can still contain local paths; they are not export-safe support
data.

## Current entry and preflight flow

| Entry | Current behavior | Source ownership | Contract owner |
| --- | --- | --- | --- |
| `Meetings > Add Audio Files` | `MainWindow.AddAudioFilesButton_OnClick` permits multi-select existing files, then creates `FilePicker` candidates. | User-owned. | `MainWindow.xaml.cs`, `ExternalAudioImportService` |
| Drop on Meetings | `MeetingsWorkspaceGrid_OnDrop` accepts files and directories. A dropped directory expands only its top-level files; it is not recursive. | User-owned. | `MainWindow.xaml.cs` |
| Legacy watch | Startup and background timer call `RunExternalAudioImportCycleAsync`; `ScanWatchedAudioFolderAsync` enumerates only configured `AudioOutputDir` top-level files. | User-owned. | `MainWindow.xaml.cs`, `ExternalAudioImportService` |
| Import Inbox | `Settings > Files & Updates` offers an explicit opt-in Inbox. Startup/background scans reconcile only its top-level supported audio under a durable local lease, then queue one source through the normal staged-import path. If transcription setup is unavailable, cadence-bound discovery still writes local Inbox receipts but never queues work; Setup recovery can therefore see arrivals without a hidden worker start. | Inbox-owned only after a receipt; original retained unless the separate Archive option is enabled. | `ImportInboxLifecyclePolicy`, `ImportInboxReconciliationService`, `ImportInboxIntakeService`, `ImportInboxJournalStore` |
| Candidate identity | `Path.GetFullPath` plus observed length and UTC last-write identify a batch or existing imported source. Friendly title, project, and filename alone do not identify a duplicate. | Read-only observation. | `ExternalAudioImportService` |
| Accepted containers | `.wav`, `.mp3`, `.m4a`, `.aac`, and `.mp4` can be reviewed, but extension is never a codec guarantee. A source queues only when the local preparation stack decodes and normalizes it. | No mutation. | `ExternalAudioMediaProbe`, `ExternalAudioImportService` |
| Watch stability | Watched sources need a 15-second quiet period. Explicit picker/drop candidates have no quiet-period gate. | No mutation. | `ExternalAudioImportService` |
| Preflight | Rejects missing/offline/duplicate/unsafe sources before decoding. `ExternalAudioMediaProbe` snapshots length and UTC write before and after the same local transcription preparation used by queued work, uses restricted read sharing, a two-minute cancellation limit, two concurrent probes per volume, a 2 GiB source limit, and deletes only randomized app-owned temporary probe files. Stable results distinguish ready, waiting/lock, changed, unsupported codec, no/empty/short audio, decode failure, and resource limit. | Read-only source; app-owned temporary probe only. | `ExternalAudioMediaProbe`, `ExternalAudioImportService` |
| Setup gate | A missing, invalid, or unverified transcription model produces an opaque readiness snapshot. Queue Valid may still stage an already verified source, but saves it as `BlockedBySetup`; no worker admission occurs. `Resume Blocked Imports` is explicit after Setup is ready. | No source mutation; staged copy only after normal admission. | `ExternalAudioImportReadinessResolver`, `ExternalAudioImportReadinessCoordinator`, `MainWindow.xaml.cs` |
| Queue admission | `QueueImportAsync` re-stats length and UTC write time before making a work session, rejects an unchanged source that already has an imported work session, and rechecks app-owned work/recordings/transcript storage with a conservative copy/normalization estimate. A source changed after review must be reviewed again. | No mutation. | `ExternalAudioImportService`, `ImportInboxPathPolicy` |
| Staging and manifest | Service copies to a unique app-owned `.import-staging` file, verifies the observed size and source observation again, atomically promotes it to `processing/imported-source.<ext>`, then repeats the decoder/preparation probe on that exact staged observation before saving manifest and job. `import-job.json` receives a metadata-only receipt: opaque source/staged observations, source revision, ready code, decoder version, normalized duration/rate/channels, and completion time. | Staging file, work copy, manifest, job, and receipt are app-owned; original remains user-owned. | `ExternalAudioImportService`, `ExternalAudioMediaProbe`, `ExternalAudioImportJobStore`, `SessionManifestStore` |
| Processing handoff | `SessionProcessingInputResolver` reads only a queued/processing job whose receipt, session id, readiness snapshot, staged identity, and current staged observation all verify. The normal processor receives that staged path, never the original locator. It marks the import job processing, then published or failed where the processor reaches those terminal stages. | Original is not read. | Staged work copy, job checkpoint, normal processor artifacts. | `SessionProcessingInputResolver`, `SessionProcessor` |
| Startup reconciliation | Before queue admission, `ExternalAudioImportStartupReconciliationService` classifies manifest/job/staged/output facts without mutating them. Only admissible queued or interrupted work is returned to the worker queue. Complete published artifacts win over a stale processing checkpoint; changed or unavailable staged work remains out of backlog for recovery. A safely migrated pre-receipt legacy work copy stays compatible, but still never reads the original source. | Original is not read. | Existing manifest, job, staged input, and output artifacts. | `ExternalAudioImportRecoveryClassifier`, `ExternalAudioImportStartupReconciliationService`, `ProcessingQueueService` |
| Recovery-action contract | A path-free resolver names one safe next action and its consequence. A failed import prefers `Retry from work copy` only when its current staged observation still matches the persisted receipt; otherwise it offers a current-original re-observation or replacement-source route. A setup block routes only to Setup; published/active imports expose no import mutation. The first executor action serializes same-process clicks, rechecks the expected job revision and staged receipt, preserves user metadata, and writes a bounded metadata-only result receipt while queueing or retaining a setup block. | Retry reads only app-owned staged input. Original/open-source/replace controls require separate current path-policy validation and are not wired by this slice. | Job checkpoint and up to ten opaque recovery-action receipts. | `ExternalAudioImportRecoveryActionResolver`, `ExternalAudioImportRecoveryActionService` |
| Published origin projection | Catalog rows with a current imported manifest expose immutable `Imported audio` provenance, an intake-method label, opaque session id, and retained-at-import state. It never serializes the external locator, staged path, size, hash, profile, or source contents. A missing or legacy work manifest safely has no origin projection instead of inventing import facts. | No source access. | Existing work manifest only. | `MeetingOriginResolver`, `MeetingOutputCatalogService` |

## Review privacy, focus, and semantics

The review surface is a local, bounded decision point. It shows a source file
name, method, editable meeting metadata, retained-source statement, status, and
next step. It never binds or announces the full source locator. If an untrusted
preflight message carries a drive or UNC locator, review replaces it with a
safe status derived from the stable preflight code. The same allowlist applies
to every preflight message, including messages without a locator, so decoder
diagnostics and source-derived content cannot become default UI text.

| State | Screen-reader/status contract | Keyboard focus |
| --- | --- | --- |
| Add or drop accepted files | A concise local-copy, source-retention, and recording-consent notice remains present; a polite count/status updates without a full path. | The selected row receives focus after candidates enter review. |
| Ready or setup-blocked row | The row names source filename, method, status, source retention, and next step; queue/setup controls have explicit names. | Tab navigation remains inside the review surface until the user moves on. |
| Retry, duplicate, or remove | The status explains that the review row changed, never the original file. | The action retains normal control focus; the next selected row remains available for editing. |
| Published catalog origin | Only the import kind, intake method, opaque session id, and retained-at-import state are projected. | No original locator is exposed on published rows. |

`Path.GetFullPath` is lexical canonicalization only. UNC sources, paths longer
than 240 characters, and any source path with a reparse-point ancestor are
rejected before metadata/probe work. A source file marked offline is also
blocked as unavailable. This is an admission boundary, not a claim that
arbitrary linked or long-path storage is supported.

## State and ownership model

This is the required common vocabulary for follow-on sprints. “Current” says
whether the state is already durable and user-visible.

| State | Current implementation | Original source | App-owned state | Retry/cancel rule |
| --- | --- | --- | --- | --- |
| `Selected` | Explicit picker/drop selection exists in memory. | Retained. | None. | User can cancel selection. |
| `Expanding` | Synchronous top-level folder expansion exists for drop only; no durable state. | Retained. | None. | Invalid paths are skipped. |
| `Preflighting` | Async candidate build exists; no durable progress record. | Read only. | Temporary probe WAV only. | Cancel stops operation and cleans probe file. |
| `Ready` | Candidate preflight is ready; explicit review row can be edited. | Retained. | In-memory review row only. | Queue or remove review row. |
| `Duplicate` | Batch/existing-import, watched transcript, or normal app-meeting collision blocks queue. | Retained. | None. | Needs changed observation or separate future choice. |
| `Unsupported` | Unsupported extension blocks queue. | Retained. | None. | Choose supported media. |
| `Unreadable` | Missing, offline, unsupported codec, no/empty/short audio, decode failure, or resource preflight blocks queue. | Retained. | Probe temporary file is removed. | Restore/download/replace then review again. |
| `Changing` | Probe and queue admission detect changed length/write time before or during decode/copy. | Retained. | No queued session after a failed admission. | Review source again. |
| `RetryPending` | Inbox only: retryable missing/offline/lock/change/storage-like preflight result is stored against its observation with exponential 15-second to five-minute backoff. | Retained. | Local Inbox journal, no source move. | Reconciliation or later intake reclaims only after next-check time. |
| `TerminalFailure` | Inbox only: terminal result is stored against its exact opaque observation and suppressed from startup/timer retry. A changed source gets a new observation. | Retained unless separately moved by opted-in Error policy. | Local Inbox journal, no source move by default. | Replace/change source or use a future explicit retry control. |
| `BlockedSetup` | Verified staged import has user queue intent but transcription setup is missing, invalid, or temporarily unverified. | Retained; source is no longer needed when staged copy remains valid. | Work manifest, staged input, job, opaque readiness snapshot. | Complete Setup, then explicitly choose Resume Blocked Imports; startup never auto-resumes it. |
| `Queued` | Work manifest is queued and enqueue call is made. | Retained; later user removal does not stop staged processing. | Work manifest and copied `processing` input. | Normal queue resume applies. |
| `Processing` | Normal processing queue owns a receipt-verified staged input and advances the job from `Queued` to `Processing`. | Not read. | Work input, manifest, job checkpoint, logs, generated work artifacts. | A stale receipt/work copy returns a recoverable import status instead of reading the original. |
| `Published` | Normal pipeline owns published artifacts and advances a current import job to `Published`. A new imported session uses its edited date/title plus a stable session suffix for a collision-safe output stem. | Retained. | Published artifacts, ready marker, work state, output-stem provenance. | Normal meeting actions apply. |
| `Failed` | Normal pipeline owns terminal error state. The S8 recovery resolver now explains one safe action. `Retry from work copy` is implemented only after the persisted staged observation still matches its receipt, expected revision still matches, and setup is rechecked; it appends an opaque bounded receipt. | Retained and never mutated. | Work manifest, staged input, job, and opaque recovery receipt. | Retry from verified work queues or remains setup-blocked; missing work directs replacement. Current-original retry, replacement, bulk, and cleanup actions remain later work. |
| `ArchivePending` | Durable local Inbox receipt holds/retries archive after queued work commits. | Retained until a verified Inbox-only archive move succeeds. | Local Inbox journal. | Retry is bounded; no external picker/drop source is eligible. |
| `ArchivedFromInbox` | Optional post-queue same-Inbox move records a relative archive receipt. | Moved only from the configured Inbox into its managed `Archive` child. | Local Inbox journal plus queued work session. | Queue work continues independently; no deletion is performed. |
| `ErrorPending` / `Errored` | Optional terminal-readability action records a relative Error receipt. | Moves only a receipt-backed unreadable Inbox source into its managed `Error` child; failed moves leave it in place. | Local Inbox journal. | Retry is bounded; setup/storage/duplicate blocks never move sources. |
| `SourceMissingAfterQueue` | No durable state. Processing uses copied `MergedAudioPath`; original disappearance does not normally block it. | User-controlled and may be absent. | Work copy/manifest remain. | Continue from work copy when valid. |

Each newly queued import now has a versioned local `import-job.json` companion
record with opaque job/source-observation ids, revision, state, source method,
relative staged-work identity, and (after successful queue admission) a
metadata-only staged-preparation receipt. Legacy manifests still have no companion
record; `ExternalAudioImportJobStore.LoadOrProjectLegacyAsync` can project one
read-only into the compatible job model without writing a companion file.
During queue startup, `ExternalAudioImportJobRecoveryService` can safely
migrate a missing companion only when the imported manifest and staged file
are both present under that session's app-owned `processing` directory. No
lease or durable transition log has shipped yet.

Any future published/import-summary projection must use
`ExternalAudioImportJobPublicProjection`. It contains only opaque job id,
state/reason, display name, method, and source-retention status. It excludes
the original locator, staging identity, and optional content hash.

## Downstream compatibility map

| Downstream contract | Current behavior | Tests |
| --- | --- | --- |
| Startup resume and queue | `ProcessingQueueService.ResumePendingSessionsAsync` first repairs a missing companion job only for a safe imported manifest plus staged input, then passes all manifests through the read-only reconciliation classifier. A durable `BlockedBySetup`, failed, unreadable, changed-stage, or invalid-published state is not silently queued. Complete published artifacts suppress stale processing checkpoints. A safely migrated legacy app-owned staged copy remains compatible. Imported sessions use normal queue duration estimation from merged work audio. `SessionProcessingInputResolver` is the final worker-side receipt check before any imported path is read. | `ExternalAudioImportRecoveryClassifierTests`; `ExternalAudioImportStartupReconciliationServiceTests`; `ProcessingQueueServiceTests` imported-source resume and ETA cases; `SessionProcessingInputResolverTests` |
| Superseded imported reprocessing | If original source still exists and transcript artifacts already exist for its stem, startup excludes and archives only that app-owned session root under `maintenance/archived-imported-source-work`. It never archives the original source. | `ResumePendingSessionsAsync_Archives_Superseded_Imported_Source_Queued_Work_And_Does_Not_Enqueue_It`; related exclusion/missing-artifact tests |
| Catalog precedence | New imported processing persists its selected collision-safe output stem locally; catalog uses it. Older manifests without that local value retain original-source-stem projection, so published-artifact and reprocess precedence remain compatible. | `MeetingOutputCatalogServiceTests` published-artifact, primary-manifest, and original-stem precedence cases; `SessionProcessingInputResolverTests` |
| Legacy manifest compatibility | Missing display name/method/retention fields normalize to filename, watched-folder method, and retained source. | `SessionManifestStoreTests.LoadAsync_Normalizes_Legacy_Imported_Source_Metadata` |
| Explicit review UI | Rows project display-safe source method, duration, status, retention promise, and recovery; user can edit title/time/project, Queue Valid, retry a current retryable row, skip only a duplicate, remove only a row, or route to Inbox/Setup. Each row automation name repeats method/state/retention/recovery without a local path. | `ExternalAudioImportReviewProjectionTests`; `MainWindowXamlTests.Meetings_Workspace_Exposes_Explicit_Audio_Import_Entry_Points_And_Review_Surface` |

## Compatibility fence

Until Import Inbox ships, legacy scanning stays a secondary compatibility path:
it scans only configured `AudioOutputDir`, only non-recursively, and only when
the application runs its startup or background import cycle. It must remain
non-destructive and must produce the same source metadata, copied work input,
and queued manifest provenance as explicit intake.

The current implementation does not yet provide a setting to opt into legacy
scanning separately from the configured audio-output location. Sprint 2 must
add that policy without changing existing users' behavior silently.

Collision rule today: canonical lexical path + observed size + UTC last-write.
Within one candidate batch and across imported manifests, an exact match is a
duplicate. Queue admission repeats that check before creating a session. A
changed source is rejected at queue admission and must be preflighted again.
Content hash, physical file identity, durable lease, and potential-duplicate
choices remain S1 work.

## Characterization evidence

All evidence uses temporary synthetic audio. No real title, path, transcript,
or audio content is required.

| Journey | Evidence |
| --- | --- |
| Phone memo via picker | Ready candidate, editable metadata, source copy/manifest, and source byte/timestamp retention tests in `ExternalAudioImportServiceTests` |
| Downloaded export via drop | `DragDrop` provenance metadata and unchanged-source test in `ExternalAudioImportServiceTests`; review controls in `MainWindowXamlTests` |
| Folder drop | `ExpandExternalAudioImportSelection` defines top-level-only expansion; no recursive intake claim is made |
| Setup block/recovery | `ExternalAudioImportReadinessTests`, `QueueImportAsync_Stages_And_Persists_A_Setup_Block_Without_Queueing_Work`, and review-control source test |
| Staged-only processing and terminal checkpoints | `SessionProcessingInputResolverTests` verifies missing-original processing, receipt/tamper rejection, imported job `Queued → Processing → Published`, collision-safe output stem, and audio-only transcription failure without `.ready` |
| Startup truth | `ExternalAudioImportRecoveryClassifierTests` covers all durable state classes and crash-after-publish precedence; `ExternalAudioImportStartupReconciliationServiceTests` proves receipt-valid resume, changed-stage exclusion, published-artifact precedence, and legacy recovery compatibility |
| Explicit staged-work retry | `ExternalAudioImportRecoveryActionResolverTests` covers state/action/copy semantics; `ExternalAudioImportRecoveryActionServiceTests` covers receipt revalidation, revision conflict, repeated-click serialization, setup block, metadata-only receipts, and source-retention sentinels |
| Duplicate and changed source | Existing duplicate/failed-import suppression cases plus `QueueImportAsync_Rejects_A_Source_Changed_After_Preflight_Without_Creating_A_Work_Session` and `QueueImportAsync_Rejects_An_Unchanged_Source_That_Already_Has_An_Imported_Work_Session` |
| Retry/restart/published collision | Queue archive/exclusion and catalog-precedence tests named above |
| Removal | `RemoveExternalAudioImportButton_OnClick` removes only an in-memory review row; no source mutation operation exists |

Focused regression coverage also verifies source bytes, path, length, and
last-write timestamp after a successful queue, a locked-source failure, and a
canceled request. The imported source copy or failed session cleanup is the
only mutable storage covered by this contract.

## Known gaps and next owners

- The staged-file rename and each job-file write are atomic, but
  `SessionManifestStore.SaveAsync` and `import-job.json` are not one atomic
  cross-file commit. Startup safely repairs the manifest-without-job half when
staged input exists; an admission transaction receipt and other crash windows
remain later hardening work.
- Current source validation rejects UNC/remote paths, work-root overlap,
  reparse ancestry, and paths over 240 characters. Publish-root overlap and a
  durable platform file-identity boundary remain Sprint 1 work.
- Import Inbox records keep relative paths, opaque observations, queue/lease,
  and archive-receipt state locally. They are bounded, top-level only, and
  never recurse or make an external picker/drop source archive-eligible.
- Sprint 4 provides explicit review-row retry; richer Inbox-specific review
  state remains later work. Terminal suppression stays locally actionable
  without exposing a local path.
- No rendered import-state journey was captured because native WPF capture is
  unavailable in this task environment and an existing live app session must
  not be interrupted. Sprint 2 owns synthetic rendered accessibility evidence
  after durable inbox states exist.
- Startup classification is deliberately read-only. Lease heartbeats, atomic
  per-stage publish receipts, stale-checkpoint repair writes, retry from the
  current original, source replacement lineage, bulk results, and bounded
  cleanup remain later recovery work; startup never turns a classifier decision
  into an implicit retry. The shipped retry receipt currently covers only the
  explicitly invoked, receipt-verified staged-work retry transaction.
