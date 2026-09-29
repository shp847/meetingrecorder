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
