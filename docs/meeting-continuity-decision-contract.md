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
