# Meeting Continuity Decision Contract

## Scope

This contract governs synthetic continuity replay evidence before any live
capture, auto-stop, rollover, recovery, or merge behavior changes. It contains
no transcript, audio, raw title, attendee, window, endpoint, process, or path
data. Tracked fixtures use opaque scenario IDs and public synthetic consent.
Restricted incident material remains outside source control and must be
consented, classified, owned, expiry-bound, and hash-checked locally.

## Decisions

- `SameMeeting`: only compatible strong stable identity can continue a session.
  This does not authorize an automatic merge.
- `DifferentMeeting`: contradictory strong stable identity wins.
- `UnknownGrace`: weak or medium evidence plus recent capture can preserve a
  bounded five-minute tail. It never starts a new capture or auto-merges.
- `ManualReview`: manual stop, missing evidence, expired grace, invalid data,
  or unsupported schema halt automatic action. A user stop always wins.

False merge is highest severity. False split is second. Bounded tail capture is
acceptable only while respecting the five-minute cap and storage/capture
limits. Missing labels never count as correct.

## Corpus and metrics

`ContinuityReplayCorpus` provides five public synthetic fixtures: auto-stop
continuation, crash recovery, generic false start, quiet continuation, and
same-title/different-meeting negative. Fixture integrity is SHA-256 over a
canonical metadata-only schema. The schema rejects unknown platform, bad
version, non-opaque ID, non-public consent, negative timing, missing reason,
and hash mismatch.

`ContinuityReplayMetrics` tracks exact decisions, false merges, false splits,
Unknown/Grace, and Manual Review. All future continuity exceptions require an
opaque fixture plus an adversarial negative case before they can affect live
policy.

## Continuity change gate

Every new continuity exception must be proposed with one opaque public
synthetic regression fixture and one adversarial public synthetic negative
fixture. The change record must name its evidence tier, expected shadow
divergence/metric, privacy classification, and removal condition. A title-only
exception is rejected: title is never enough for automatic merge. Fixtures stay
small; supersede a covered case rather than adding an unbounded variant family.

## Trace and replay boundary

`ContinuityDecisionTrace` is a bounded, metadata-only breadcrumb stream. Each
event holds opaque session/decision/correlation IDs, monotonic sequence/time,
event kind, evidence tier, normalized reason code, state transition, and
bounded numeric timing/count. It rejects raw or non-opaque inputs and drops
the oldest event only after recording a drop count.

`ContinuityDecisionTraceStore` writes the sidecar atomically beside session
work only when a caller explicitly supplies that app-owned path. The allowlist
rejects unknown JSON fields; missing, corrupt, and future sidecars return an
honest status and never alter a session. `ContinuityReplayRunner` is pure: it
uses the S0 contract and trace snapshot only, emits a deterministic SHA-256
digest, and has no UI, audio, window, worker, process, or network dependency.
The explanation formatter maps normalized reason codes to safe support copy.

# Sprint 2 — local identity snapshot contract

Continuity stores only versioned, local keyed tokens for a durable meeting code
or specific title. It does not store a raw title, window tree, app path, audio,
transcript, or attendee identity. Generic shells (for example Teams or sharing
controls) create no merge-capable identity. The local key is private to the
current app data root; key rotation, missing tokens, corrupt data, future
timestamps, expiry, or fingerprint collisions return `Unknown`, never `Same`.

`MeetingContinuityMatcher` is pure and treats runtime/runtime,
runtime/manifest, and manifest/manifest inputs identically. It can return
`SameMeeting` only for compatible, proximate strong evidence. Contradictory
strong evidence returns `DifferentMeeting`; medium and weak evidence remain
`Unknown` for a separate, bounded grace owner.

Older manifests are read without mutation. Their snapshot is derived only from
safe saved metadata and is persisted on the next normal atomic manifest save.
Unsupported or malformed stored snapshots remain readable as legacy data and
are not overwritten by that compatibility path.

## Sprint 3 — shadow comparison contract

Before cutover, `ContinuityShadowEngine` receives the same metadata-only
snapshots and clock as the legacy decision. It emits a bounded receipt with
opaque correlation/revision, boundary, tier, version, verdict/reason, latency,
and divergence taxonomy. It has no I/O, capture, worker, UI, or policy side
effects; overload or invalid input records `Unavailable` and leaves legacy
behavior unchanged.

Local reports group only by boundary/platform/tier/version. Cutover requires
labeled coverage, every protected incident matching expected truth, no
unreviewed potential false merge on protected negatives, and bounded latency
and unavailability. A failure extends shadow observation; it cannot relax the
gate or turn on capture behavior.

The active app records the committed legacy result for continuation,
reclassification/rollover, and successful recent-auto-stop recovery into an
in-memory bounded meter. Startup currently seals manifests and publish repair
currently has no continuity candidate, so neither is given a synthetic shadow
hook. Adding one requires a real decision boundary and the same receipt
contract.

## Sprint 5 — current-work healing boundary

`OngoingMeetingAutoHealEnabled` is disabled by default. When explicitly
enabled, a successful publish runs one bounded pass over only the preceding
24 hours of published work. It considers one chronologically adjacent pair at
a time and requires complete WAV, Markdown, JSON, and ready-marker artifacts,
compatible strong manifest identities, a five-minute-or-less measured gap,
monotonic output order, matching project/attendee metadata, and no existing
summary or speaker-label result to reconcile. Missing,
different, or weak identity, incomplete artifacts, active work, unsafe order,
or metadata conflict leaves both meetings unchanged.

The pass uses the same cleanup merger as deliberate cleanup, an exclusive
local lease, and an atomic receipt keyed by source session IDs. Original
published artifacts move to the local `ongoing-heal` archive; raw source audio
is not deleted. The receipt records the reason, time, and archive location and
suppresses repeat passes. Historical repair is deliberately outside this
automation boundary.

## Sprint 6 — callback and crash-evidence boundary

`CallbackIntentDispatcher` is a local, bounded metadata-only handoff for
callback work. Entries contain only normalized key, correlation, allowed edge,
revision, sequence, and outcome; title, transcript, audio, window, path,
exception payload, and credentials are rejected. At most 64 requests and 128
trace entries are retained. Reentrant matching correlation/edge requests are
declined, queued duplicate keys coalesce, and overload is recorded without
recursion.

Meeting refresh, manual and automatic start/stop/rollover, startup warmup,
deferred startup maintenance, and post-repair resume each acquire an intent
before mutating transition state. Recording transitions flush the trace sidecar
both before and after their risky boundary. The sidecar is atomically replaced
under the app root; missing, corrupt, oversized, or non-normalized input is
treated as absent diagnostic data, never as recovery authority.

A restart may inspect this metadata-only diagnostic sidecar, but it must still
use manifest checkpoints and normal interrupted-session recovery. A trace never
starts capture, overrides manual stop, assigns `SameMeeting`, merges meetings,
or retries a failed callback recursively. The ongoing healer remains a later,
explicit post-publish pass governed by its own receipt and eligibility checks.

## Sprint 7 — rollout, retirement, and rollback boundary

`ContinuityEngineRolloutMode` is local and versioned. New configurations use
`Matcher`; migrated configurations retain their prior local continuity choice;
malformed or future values fail closed to `Legacy`. In `Matcher` mode,
`ContinuityCutoverPolicy` is the sole verdict authority for active continuation,
recent auto-stop recovery, reclassification, and rollover. Only its
`DifferentMeeting` result can pass into the legacy mechanics that choose the
safe transition shape. The retained `AutoRecordingContinuityPolicy` branches
are evidence and safety extractors for the rollback-only `Legacy` mode; they
must not create a matcher-mode verdict.

| Retained branch | Matcher-mode role | Deletion condition |
| --- | --- | --- |
| `ShouldRefreshLastPositiveSignal` | Supplies capture/activity evidence only; matcher selects Continue or bounded grace. | Remove after equivalent normalized activity evidence is owned by the matcher adapter. |
| `ShouldRecoverFromRecentAutoStop` | Legacy fallback only; matcher compares persisted/observed identity when context exists. | Remove after recovery corpus and installed synthetic journey prove matcher-only recovery. |
| `GetEligibleActiveSessionTransition` and rollover/reclassify helpers | Choose transition mechanics only after matcher returns `DifferentMeeting`. | Remove or reduce after matcher owns transition selection without altering manual-stop protections. |
| `OngoingMeetingHealEligibilityResolver` | Uses the shared matcher directly for manifest-to-manifest eligibility. | Retain as the post-publish admission adapter; it contains no title-only verdict. |

`OngoingMeetingHealRolloutMode` is also local and versioned: `Off`,
`ReviewOnly`, or `Live`. Invalid values and corrupt migration state resolve to
`Off`; a rollback changes only future decision snapshots and never edits an
active capture or an existing transaction. `ReviewOnly` writes one expiring,
metadata-only candidate recommendation keyed by opaque session IDs. It writes
no audio, transcript, archive, lease, or transaction receipt and grants no
merge authority. Support can clear stale review evidence only by waiting for
its 24-hour expiry or removing the local `.ongoing-heal/review` entry while the
app is stopped; changing `Live` to `ReviewOnly` is the immediate circuit-breaker
response to an unexpected potential merge.
