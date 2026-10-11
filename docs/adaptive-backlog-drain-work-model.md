# Staged Backlog Work Model

**Status:** Sprints 2A-3 staged queue adoption and overnight acceleration, 2026-09-28.  
**Runtime status:** the app queue creates local staged work for ordinary
background backlog, preserves explicit legacy/manual full-pass work, and passes
the staged identity to the worker.

## Work item

`StagedBacklogWorkItem` is a local, versioned record containing only opaque
identifiers and revision tokens:

| Field group | Contract |
| --- | --- |
| Identity | Work id, session id, and manifest token are required and are not user-facing paths. |
| Currentness | Input and output revision tokens bind a requested pass to the observed artifacts. |
| Request | `Transcript`, `Diarization`, `Summary`, or compatibility `FullPass`; intent is background, manual, or ASAP/Rush. |
| Lifecycle | `Pending`, `Leased`, `Running`, `Succeeded`, `Skipped`, `Blocked`, `Retryable`, `Failed`, `Cancelled`, or `Superseded`. |
| Recovery | Attempt, lease, retry time, creation time, and a bounded reason preserve state without treating a failed/blocked/cancelled item as complete. |

`StagedBacklogWorkStore` uses an atomic local replacement. It preserves future
schema/stage records for repair and rejects corrupt data rather than presenting
an empty queue. The app stores it beside its local configuration. If that file
is unreadable or cannot be updated, it leaves the file untouched, records a
local repair reason, and uses the existing full-pass queue rather than dropping
or inventing staged work.

## Eligibility and barrier

The pure resolver accepts already-known manifest/configuration facts and never
reads paths, processes, power, or providers itself.

1. It excludes invalid, stale, delayed-retry, blocked, terminal, duplicate, or
   active-conflict work with an explicit reason.
2. It selects the earliest nonempty eligible barrier: transcripts, then speaker
   labels, then summaries.
3. Blocked/failed/cancelled/stale earlier work remains counted and visible but
   cannot deadlock an eligible later item from another session.
4. A current transcript is required for label or summary work. Labels need the
   existing labeling capability. Summaries need the existing enabled and
   configured provider/consent boundary; no resolver result enables a provider.
5. Manual or ASAP `FullPass` retains its existing priority. It cannot run beside
   staged work for the same active session. Background legacy `FullPass` stays
   available when no staged barrier is eligible.

The result exposes current-barrier, eligible-by-stage, deferred-later-stage,
blocked, failed, cancelled, stale, and active counts. The live queue applies
the same transcript → labels → summary ordering for ordinary staged entries;
an active earlier stage also prevents a later stage from taking a spare worker
slot. Full queue-status copy is deferred to Sprint 3/Sprint 6.

## Revision and conflict rules

- A duplicate is keyed by session, requested stage, and input revision; only
  one is dispatchable.
- Current leased/running work blocks another pass for the same session.
- A newer transcript revision supersedes only pending/retryable/blocked label
  and summary work for the older revision. Completed historical receipts remain
  readable.
- Fair ordering rotates after the last dispatched work id within a barrier.
- A legacy queued item maps to `FullPass`, preserving existing manual/full-pass
  semantics rather than silently changing it to staged enrichment.

## Queue adoption and worker lease boundary

Sprint 2A makes the contract live without changing the configured worker-count
policy or adding overnight/capacity acceleration:

1. Ordinary enqueue selects the first missing allowed stage. Transcript is
   selected until publication succeeds; then speaker labels when the existing
   label capability is available; then enabled summaries. Existing manual or
   ASAP work remains a full pass.
2. Before launch, the queue assigns a new opaque lease token and persists the
   `Leased` state. It persists `Running` after process start. Interrupted
   `Leased`/`Running` records recover as explicit retryable work on next app
   start.
3. The worker receives `--stage`, `--work-id`, `--work-revision`, and
   `--lease-token` together. It emits a compact JSON receipt with only those
   values and the requested stage; it never prints a title, artifact path, or
   transcript in that receipt. Queue logs redact revision and lease-token.
4. A success without the exact receipt remains retryable. A stale lease cannot
   complete a newer owner. Worker failure/preemption stays retryable, and the
   existing manifest/recovery paths still own the user-visible failure state.
5. The app re-evaluates a successfully completed staged manifest and queues
   only its next stage. It does not rerun the same stage merely because an old
   fake/legacy process left its manifest status unchanged.

## Evidence

`StagedBacklogWorkModelTests` covers barrier order, excluded earlier work,
summary setup/currentness, manual full-pass priority, active conflict,
deduplication/fairness, future/corrupt local persistence, migration,
revision supersession, and lease compare-and-swap/restart recovery. Focused
model/worker-parser/queue tests pass 69 cases on 2026-09-28.

## Overnight acceleration

Sprint 3 resolves the local window through one pure policy boundary. Valid
configured-stage windows permit at most three transcript workers, then one
speaker-label or summary worker; the live staged barrier remains the authority
for that order. `TranscriptFirst` remains an explicit one-worker emergency
strategy and never raises enrichment concurrency.

Invalid, empty, outside-window, or DST-ambiguous local time falls back to the
normal conservative queue policy. A live recording always stops a *new*
overnight-accelerated launch; it never kills a worker already running. The
queue snapshot reports the active focus (`Transcripts`, `Speaker labels`, or
`Summaries`) and its conservative fallback reason. Existing provider consent,
asset, currentness, manual, and legacy full-pass boundaries still apply.

Legacy `OvernightDrain` config migration now maps once to configured staged
acceleration. Existing explicit `TranscriptFirst` selections remain
transcript-only.

Focused policy/config/UI checks (222 cases) and direct staged cap, recording
gate, and barrier-order queue tests passed on 2026-09-28. The package gate is
currently external to this contract: the local Windows security control removes
the generated `MeetingRecorder.App.exe` immediately after creation, so the
installer cannot be rebuilt or smoke-tested until that host condition is
recovered.

## Stage worker passes

Sprint 2 adds an opt-in worker `--stage` argument. Omission preserves the
existing full-pass behavior. Only `transcript`, `diarization`, `summary`, and
`full-pass` are valid values; malformed or compound values fail before the
processor starts.

| Requested stage | Preconditions | Provider calls | Result |
| --- | --- | --- | --- |
| `transcript` | Existing full-pass source/preparation contract | Transcription only | Publishes audio/transcript, records optional labels and summaries as skipped for that pass. |
| `diarization` | Current published audio, JSON, Markdown, ready marker, and transcript | Speaker labeling only | Republishes transcript sidecars with labels. Any previous summary becomes explicitly stale and is not regenerated. |
| `summary` | Current published audio, JSON, Markdown, ready marker, transcript, and enabled summary configuration | Summary only | Republishes transcript sidecars with the resulting summary. Disabled summary configuration returns a blocked stage result without altering prior readable artifacts. |
| `full-pass` | Existing full-pass contract | Existing ordered providers | Unchanged compatibility behavior. |

Before an enrichment write, the worker captures and rechecks an in-process
SHA-256 fingerprint of the retained audio and current sidecars. A changed or
missing artifact fails the requested commit rather than applying enrichment to
a different transcript. The fingerprint is never logged, surfaced, or stored.

Enrichment publication requires the current audio, Markdown, JSON, and ready
marker. It stages replacement sidecars, preserves backups, rolls back the pair
if promotion fails, and leaves the existing ready marker in place—there is no
new completion signal for an enrichment-only pass.

Focused `SessionProcessor`, stage-parser, worker-source, artifact-fingerprint,
transcript-reader, and publish tests passed 43 cases on 2026-09-28. The full
suite then passed 1,475 core and 8 integration tests, and the installer bundle
and MSI rebuilt successfully. Packaged startup smoke remains intentionally
unrun because a user-owned installed app instance is active.
