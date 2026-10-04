# Meeting Recorder Feature Roadmaps

This file tracks implementation-ready sprint plans for major Meeting Recorder
feature work. Each roadmap is scoped independently unless it explicitly names a
dependency on another section.

# Sprint Delivery Tracker

**Baseline audited:** 2026-09-27. **Total sprints:** 123. **Done:** 24.
**Ready:** 77. **Partial:** 20. **Blocked:** 2. **Planned:** 0. This is a source-and-test-evidence baseline,
not release approval. A feature-shaped class, XAML control, or unverified local
change can justify `Partial`; only complete acceptance evidence can justify
`Done`.

## Status Rules

- `Planned`: no implementation evidence for the sprint's user contract.
- `Ready`: pressure-tested implementation plan with bounded slices, acceptance
  evidence, dependencies, and first action. Work may start when its named
  dependencies are satisfied; this does not claim shipped behavior.
- `Partial`: some contract or enabling work exists, but one or more acceptance
  criteria, tests, rendered checks, documentation, packaging, or release gates
  remain open. It reports implementation evidence, not planning quality: its
  Implementation Record must still give a ready-to-start residual slice and
  exact first gap.
- `Blocked`: implementation cannot proceed because a named dependency or
  required external decision is unavailable. Include blocker and owner.
- `Done`: every acceptance criterion has dated evidence, focused tests, and any
  required documentation, packaging, and release checks. Do not infer this from
  code presence alone.
- `Superseded`: a later, explicitly named roadmap replaces this sprint. Link the
  replacement and retain the historical row.

## Updating And Expanding A Sprint

When work starts, add this record immediately below that sprint heading, then
update its ledger row in the same change. Keep completed evidence rather than
rewriting history.

```markdown
### Implementation Record

- Status: `Planned` | `Ready` | `Partial` | `Blocked` | `Done` | `Superseded`
- User outcome:
- Scope / non-goals:
- Dependencies and decisions:
- Authoritative files and contracts:
- Implementation slices:
  1. First independently testable slice.
  2. Next independently testable slice.
- Tests and rendered checks:
- Documentation / installer / release work:
- Evidence and date:
- Remaining gap or next action:
```

For a `Ready` transition, record the pressure-test findings and an implementable
first slice. For a `Done` transition, append dated evidence and keep prior
status history. For `Partial`, name first missing user-visible contract; do not
use vague notes such as "mostly complete." Implementation slices must be small,
independently testable, and preserve roadmap ordering unless a ledger row
records an approved dependency change.

## Status Ledger

### Whole-App UX Simplification And Control Balance

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Friction Audit And Baseline Evidence | `Done` | 2026-09-30: refreshed 271-control audit and its rendered evidence pass current validation. |
| 1 — UX Rules And Safety Policy | `Done` | 2026-09-30: refreshed 271-control policy inventory and validation pass. |
| 2 — Settings Preset Engine | `Done` | 2026-09-27: pure core projection/patch service, exact ownership maps, blocked hosted modes, `Custom` reasons, editor-only atomic patches, mapping documentation, and 10 focused passing tests. |
| 3 — Settings Information Architecture | `Done` | 2026-09-27: six intent sections, transient target/routing contract, legacy deep-link aliases, one-owner/timing map, reparented existing controls, focused source/routing tests, and settings documentation. |
| 4 — Recording And Setup Simplification | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 5 — Home As Command Center | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 6 — Meetings View Presets | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 7 — Recommendation-First Meetings | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 8 — Meetings Action Grouping | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 9 — Meeting Detail Task Center | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 10 — Backlog And Recovery Simplification | `Done` | 2026-09-27: implementation record and focused evidence below. |
| 10A — ASAP Priority Carries Through Speaker Labeling | `Done` | 2026-09-27: lifecycle, priority release, status parity, docs, and focused test evidence below. |
| 11 — Safe Automation Layer | `Done` | 2026-09-27: full-scan provenance, durable receipts, bounded dispatch, safe revalidation, docs, and focused evidence below. |
| 12 — Summary And Hosted AI Trust Flow | `Done` | 2026-09-27: consent-gated route projection, save boundary, provider enforcement, docs, and focused evidence below. |
| 13 — Speaker Labels And Speaker Names UX | `Done` | 2026-09-27: state resolver, revision-safe artifact transaction, local profile controls, distinct UI terms, docs, and focused tests completed; live render/package smoke remains a recording-time boundary. |
| 14 — Copy, Trust, And Blocked-State Polish | `Done` | 2026-09-27: typed safe-copy authority, exhaustive blocked taxonomy, normal-surface error migration, source inventory guard, docs, and focused test evidence below; package/render smoke remains a recording-time boundary. |
| 15 — Accessibility And Rendered UX QA | `Partial` | 2026-09-27: redacted journey matrix, semantic accessibility contracts, and source design checks exist; live synthetic capture remains blocked by the active production-profile instance and unavailable native capture surface. |
| 16 — Tests, Documentation, Installer, And Release | `Blocked` | 2026-09-27: requires Sprint 15 rendered/accessibility evidence; active production-profile instance and unavailable native capture prevent that prerequisite. |

### Meetings Management UX Simplification

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Meetings Friction Audit | `Done` | 2026-09-29: inventory, disposition, validator, and focused evidence completed. |
| 1 — Meetings Experience Contract | `Done` | 2026-09-29: shared state/action ownership contract and focused evidence completed. |
| 2 — Meeting State Model | `Done` | 2026-09-29: metadata-only state precedence and focused evidence completed. |
| 3 — Recommendation Engine | `Done` | 2026-09-29: deterministic metadata-only ranking, bounded dismissal, and focused evidence below. |
| 4 — View Presets | `Done` | 2026-09-29: existing preset resolver, migration, toolbar contract, and focused evidence verified below. |
| 5 — Needs Attention Inbox | `Done` | 2026-09-29: metadata-only triage resolver and focused evidence below. |
| 6 — Processing View | `Done` | 2026-09-29: existing backlog projection, ASAP lifecycle, UI wiring, and focused evidence verified below. |
| 7 — Selection Strip Redesign | `Done` | 2026-09-29: existing shared catalog/selection contract and focused evidence verified below. |
| 8 — Action Grouping | `Done` | 2026-09-29: canonical family catalog, context/detail bindings, and focused evidence verified below. |
| 9 — Cleanup Consolidation | `Done` | 2026-09-29: shared recommendation/review/ledger paths and focused evidence verified below. |
| 10 — Meeting Detail Task Center | `Done` | 2026-09-29: read-first task center, revision-safe detail binding, and focused evidence verified below. |
| 11 — Transcript And Summary Reading | `Done` | 2026-09-29: pure transcript-first reader state and focused evidence below. |
| 12 — Speaker Workflow Clarity | `Done` | 2026-09-29: shared speaker state/routing and focused evidence verified below. |
| 13 — Bulk Operations | `Done` | 2026-09-29: immutable bulk preview/result contract and focused evidence below. |
| 14 — Archive, Delete, And Recovery Trust | `Partial` | 2026-09-29: shared archive/recovery/delete preflight complete; receipt-backed restore execution remains. |
| 15 — Search And Metadata Simplification | `Done` | 2026-09-29: metadata-only query projection and focused evidence below. |
| 16 — Safe Background Refresh | `Done` | 2026-09-29: pure coalesced refresh state and focused evidence below. |
| 17 — Imported Meeting Parity | `Done` | 2026-09-29: display-safe provenance projection and focused evidence verified below. |
| 18 — Rendered UX Polish | `Ready` | Apply `DESIGN.md` component rules with measured multi-viewport visual acceptance. |
| 19 — Accessibility And Keyboard QA | `Ready` | Define a focus graph, semantic-control contract, and state-based assistive QA. |
| 20 — Tests, Docs, Release | `Ready` | Gate shipped Meetings changes on traceable contract tests, package smoke, docs, and authorized release evidence. |

### End-To-End Speaker Diarization Experience

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 1 — Speaker Identity Contract | `Done` | 2026-09-29: revisioned safe review snapshot, precedence, migration-safe source state, and privacy tests verified below. |
| 2 — Speaker Review Surface Foundation | `Partial` | 2026-09-29: snapshot-backed discoverable review well, draft/pending state, and separate repair route implemented; suggestion-choice sources and rendered fixture sweep remain. |
| 3 — Contextual Transcript Evidence | `Ready` | Define deterministic, bounded local evidence selection with truthful weak-evidence states. |
| 4 — Inline Audio Clip Playback | `Ready` | Define bounded local clip derivation, cache lifecycle, recording safety, and one-session playback. |
| 5 — Transcript-Label Click Editing | `Ready` | Define accessible label-to-review focus, current-revision global rename, and additive override schema reservation. |
| 6 — Segment-Level Attribution Overrides | `Ready` | Define additive per-segment attribution, effective-label precedence, revision safety, and no-learning boundary. |
| 7 — Merge Duplicate Speakers | `Ready` | Define user-confirmed canonical speaker mapping, conflict gates, artifact consistency, and repair boundary. |
| 8 — Corrections, Rejections, Undo, And Local Learning | `Partial` | 2026-09-29: identity-keyed correction decision contract and safety matrix implemented; additive receipt persistence remains. |
| 9 — Automatic Future Naming | `Partial` | 2026-09-29: conservative local eligibility matrix implemented; post-processing dispatch and receipt persistence remain. |
| 10 — Rematch Past Meetings | `Partial` | 2026-09-29: metadata-only eligibility planner and exclusion tests implemented; dispatch, receipts, and bulk UX remain. |
| 11 — Bad Diarization Repair Guidance | `Partial` | 2026-09-29: structural quality diagnosis and action-route matrix implemented; UI and repair preflight lifecycle remain. |
| 12 — Summary And Derived Output Consistency | `Partial` | 2026-09-29: attribution fingerprint and readable historic-summary state implemented; persistence and regeneration wiring remain. |
| 13 — Profile Management And Privacy | `Partial` | 2026-09-29: lifecycle consequence/preflight contract implemented; store serialization, unavailable UX, and exclusion audit remain. |
| 14 — Calibration And Experience Harness | `Partial` | 2026-09-29: protected false-attribution promotion gate implemented; corpus manifest, metrics report, and experience fixtures remain. |
| 15 — UI Polish, Accessibility, And Rendered QA | `Partial` | 2026-09-29: speaker review/profile focus and semantic acceptance contract documented; fixture render matrix remains. |
| 16 — Documentation, Installer, And Release Smoke | `Partial` | 2026-09-29: S1–S15 acceptance matrix added; package gates remain open and upstream Partial evidence is explicit. |

### Speaker Name Recognition Revised Plan

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 1 — Stabilize The Current Feature Baseline | `Partial` | 2026-09-29: authoritative review-state projection and focused baseline checks added; live receipt/rejection wiring remains. |
| 2 — Build Fixture Evidence And Metrics | `Partial` | 2026-09-29: redacted count/recognition metric arithmetic added; private catalog/truth governance and runner integration remain. |
| 3 — Calibrate Recognition And Diarization Thresholds | `Partial` | 2026-09-29: coverage/regression/manual-promotion comparison gate added; pinned corpus/holdout receipts remain. |
| 4 — Repair, Undo, And Release Readiness | `Partial` | 2026-09-29: revision-safe undo preflight added; receipt persistence, release evidence, and package smoke remain. |

### External Audio Import Seamless Experience

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — User Journey And Compatibility Audit | `Partial` | 2026-09-27: source-to-terminal contract, source-retention guard, and focused characterization suite complete; native rendered import-review evidence remains unavailable. |
| 1 — Import Domain Model And Source Safety | `Done` | 2026-09-27: versioned job/source observation, durable app-owned staging, local atomic job store, safe startup missing-job recovery, repeat-source guard, path fence, legal transitions, schema compatibility, and legacy read projection complete; future content/lease/publish refinements are explicitly owned by S2/S6. |
| 2 — Dedicated Intake Storage And Import Inbox | `Partial` | 2026-09-27: opt-in Inbox config/settings, bounded scheduled reconciliation, durable discovery/lease/queue/archive/error receipts, storage admission/recheck, source-retention defaults, and focused tests are implemented; native rendered journey and packaged smoke remain blocked by the active user app. |
| 3 — Media Probe And Preflight | `Partial` | 2026-09-27: shared source/staged decoder probe, opaque receipt, Inbox retry/suppression, focused/integration evidence, and installer rebuild complete; rendered review and installed smoke await safe access to close user-owned app. |
| 4 — First-Class Add-Files UX | `Partial` | 2026-09-27: privacy-safe review projection, immutable retry/queue action model, accessibility contract, focused/full/integration evidence, and installer rebuild complete; rendered journey and installed smoke await safe access to close user-owned app. |
| 5 — Setup-Aware Queueing | `Partial` | 2026-09-27: opaque transcription-readiness snapshot, durable user-intent block, verified staged-copy resume fence, explicit Meetings resume action, focused/full/integration evidence, and installer rebuild complete; rendered and installed-smoke checks await safe access to close user-owned app. |
| 6 — Normal Processing Parity | `Partial` | 2026-09-27: receipt-verified staged-only processor handoff, terminal import-job checkpoints, collision-safe persisted import stems, legacy catalog compatibility, focused/full/integration evidence, and installer rebuild complete; per-stage idempotent receipt/restart/queue/render/installed-smoke proof remains. |
| 7 — Restart, Resume, And Backlog Truth | `Partial` | 2026-09-27: pure recovery classifier, startup reconciliation admission, stale-published precedence, legacy staged compatibility, focused/full/integration/package evidence implemented; leases/atomic receipts/UI projection/restart matrix remain. |
| 8 — Recovery Controls And Import Maintenance | `Partial` | 2026-09-27: safe recovery-action projection plus receipt-verified staged-work retry, same-process repeated-click serialization, and focused/full/integration/package evidence implemented; original-source retry/replacement, bulk, cleanup, UI, and rendered recovery proof remain. |
| 9 — Native Meetings Library Behavior | `Partial` | 2026-09-27: immutable, path-free published `MeetingOrigin` projection is catalog-wired with focused evidence; action capabilities, lineage, UI, consumer parity, and rendered proof remain. |
| 10 — Speaker Label And Identity Parity | `Partial` | 2026-09-27: imported staged-audio processing now has direct shared speaker-label/sample parity proof; restart/mode/input-boundary/UI/privacy matrix remains. |
| 11 — Automation Intake And Completion Signals | `Partial` | 2026-09-27: cadence-bound discovery now remains safe and visible while setup blocks queue admission; durable lifecycle receipts, pause/recovery controls, completion and standard-user matrix remain. |
| 12 — Privacy, Consent, Accessibility, And Polish | `Partial` | 2026-09-27: path-redaction sentinel, accessible review semantics, focus return, docs, focused/full/integration/package evidence complete; synthetic native rendered capture remains open. |
| 13 — Verification, Documentation, And Release | `Partial` | 2026-09-27: dated S0–S12 acceptance matrix records source/test owners, privacy boundaries, current package evidence, and remaining clean-build/install/render gates. |

### GPU Transcription Seamless Acceleration

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Product Promise And No-Go Gates | `Partial` | 2026-09-27: pure CPU-first policy gate, redacted reason contract, no-go matrix, benchmark protocol, architecture/tracker evidence, and focused tests implemented; isolated candidate feasibility remains S1. |
| 1 — Backend Feasibility Spike | `Partial` | 2026-09-27: redacted CPU/candidate output fingerprint and primary-source candidate scorecard implemented; no runtime/model candidate is selected or downloaded, so feasibility remains open. |
| 2 — Hidden Asset Contract | `Blocked` | 2026-09-27: requires the S1 selected candidate's signed/licensed runtime/model tuple and compatibility evidence; no asset schema can be truthfully defined before that decision. |
| 3 — Metadata And Snapshot Compatibility | `Ready` | Additive execution-attempt schema, migration/redaction policy, snapshot idempotency, and consumer compatibility plan defined below. |
| 4 — Provider Factory And Shared Quality Policy | `Ready` | CPU-preserving provider boundary, provider-neutral quality gate, selection/fallback precedence, and migration proof defined below. |
| 5 — Probe And Readiness System | `Ready` | Isolated synthetic probe, eligibility/backoff scheduler, versioned cache, manual-safe control, and non-disruption policy defined below. |
| 6 — GPU Provider MVP | `Ready` | Gated provider execution, final-result quality arbitration, exactly-once CPU fallback, cancellation, and provenance plan defined below. |
| 7 — Worker-Crash Recovery | `Ready` | Crash-attribution receipt, one-time CPU retry, tuple-scoped circuit breaker, and artifact-safe recovery plan defined below. |
| 8 — Performance, Battery, And Queue UX | `Ready` | Conservative power/workload policy, local timing estimator, confidence-aware ETA, and non-disruptive status plan defined below. |
| 9 — Advanced UX | `Ready` | Advanced-only preference/probe controls, freshness-aware status vocabulary, accessibility/focus behavior, and Technical Studio render contract defined below. |
| 10 — Benchmark And Quality Harness | `Ready` | Governed corpus, reproducible CPU/GPU comparison, quality/no-go metrics, and promotion evidence plan defined below. |
| 11 — Packaging And Release Validation | `Ready` | Intentional asset supply chain, V2 bundle/update integrity, CPU/GPU installed matrix, and release evidence plan defined below. |
| 12 — Update, Rollback, And Field Suppression | `Ready` | Durable local CPU safety policy, precedence/migration rules, tuple-aware rollback, and update asset fence defined below. |
| 13 — Documentation And Support Readiness | `Ready` | Evidence-linked user/admin docs, safe diagnostic/support playbook, release-copy accuracy gate, and no-setup boundary defined below. |

### Adaptive Backlog Drain Acceleration

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Current Behavior Audit And Safety Baseline | `Done` | 2026-09-27: policy/migration/window/worker/stage ledger, redacted executable fixture corpus, and legacy/current transcript-first limitation evidence are recorded; no production behavior changed. |
| 1 — Staged Drain Work Model | `Done` | 2026-09-27: versioned local work store, stage/barrier resolver, coalescing/currentness/supersession rules, full-pass compatibility, and 10 focused tests completed without queue dispatch changes. |
| 2 — Stage-Specific Worker Passes | `Done` | 2026-09-28: validated worker stage contract, isolated transcript/label/summary passes, current-artifact fence, ready-marker-preserving enrichment publication, and focused provider-spy checks completed. |
| 2A — Staged Queue Adoption And Lease Wiring | `Done` | Durable work-store adoption, barrier-aware dispatch, worker stage/lease contract, restart recovery, and receipt fencing implemented and focused-tested 2026-09-28. |
| 3 — Overnight Acceleration Semantics | `Done` | 2026-09-28: verified timezone-safe staged policy, caps, recording gate, config migration, and queue/settings copy; hash-verified apphost fallback package, clean MSI install, installed integrity, and startup smoke all pass. |
| 4 — Idle CPU Capacity Acceleration | `Done` | 2026-09-28: local aggregate CPU/AC sampler, hysteresis, conservative staged cap, status projection, 51 focused queue/policy tests, 214 UI tests, installer guard tests, and final portable/clean-MSI smoke pass. |
| 5 — Best-Effort GPU Capacity Acceleration | `Done` | 2026-09-28: bounded opaque GPU sampling, readiness-gated one-GPU/one-CPU diarization lanes, focused queue/policy proof, and package gates completed. |
| 6 — UI, Documentation, Packaging, And Release | `Done` | 2026-09-29: versioned backlog-profile migration, Processing settings/status accessibility, release matrix/docs, focused queue policy tests, portable/clean-MSI smoke, and hash-verified package gates completed; one unrelated fixture replay remains in Test-All. |

### Meeting Continuity And Split-Healing Reliability

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Failure Corpus And Decision Contract | `Done` | 2026-09-29: opaque public synthetic corpus, hash/consent/schema validation, deterministic tri-state decision contract, severity metrics, and governance documentation completed; no live policy changed. |
| 1 — Observability And Decision Trace Infrastructure | `Done` | 2026-09-29: bounded metadata-only trace/sidecar, strict allowlist and recovery states, pure digest replay, and synthetic explanation coverage completed without changing decisions. |
| 2 — Shared Meeting Identity And Evidence Ladder | `Done` | 2026-09-29: local identity snapshot, centralized tri-state matcher, evidence/conflict ladder, privacy boundary, and lazy compatibility evidence complete below. |
| 3 — Shadow Continuity Engine | `Done` | 2026-09-29: version-pinned side-effect-free parallel evaluator, divergence taxonomy/review, bounded evidence retention, and explicit cutover gate evidence complete below. |
| 4 — Live Continuity Cutover | `Done` | 2026-09-29: guarded matcher cutover, bounded/idempotent grace, identity-scoped auto-stop recovery, and rollback/negative-case evidence complete below. |
| 5 — Recovery, Publish-Time Stitching, And Ongoing Auto-Heal | `Done` | 2026-09-29: isolated ongoing-heal trigger, strict eligibility/transaction/reversal/lineage rules, shared merge execution, package, and smoke evidence complete below. |
| 6 — Crash Root Cause And Callback Topology Hardening | `Done` | 2026-09-29: bounded metadata-only callback dispatcher now covers refresh, automatic/manual transitions, startup/recovery maintenance, and abrupt-loss trace proof; package and smoke gates pass. |
| 7 — Heuristic Retirement And Rollout Controls | `Partial` | 2026-09-29: versioned local matcher/heal rollout settings preserve legacy opt-ins and fail invalid values closed; direct-authority retirement, review-only healer, and contribution gate remain. |
| 8 — Packaging, Validation, And Support Readiness | `Ready` | Continuity acceptance matrix, clean package/recovery smoke, decision-log/support evidence, and authorized release boundary defined below. |

### Production Capture, Transcription, And Cleanup Remediation

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Contain Damage and Establish Authority | `Ready` | Conservative stop conditions, source-only evidence inventory, privacy-safe corpus governance, and durable contract plan defined below. |
| 1 — Capture Admission and Continuity | `Ready` | Consent-safe provisional admission, bounded frame/evidence proof, suppression/failover/end-state contracts, and model-independent capture plan defined below. |
| 2 — Approved Transcription and Source Quality | `Ready` | Approved profile/corpus governance, source-health provenance, quality/failure semantics, and protected retention/storage policy defined below. |
| 3 — Durable Cleanup and Atomic Publishing | `Ready` | Lease/retry state machine, journaled atomic promotion/rollback, storage preflight, and conservative derived-merge plan defined below. |
| 4 — Pilot and Rebuild History | `Ready` | Conservative source inventory/classification, approval-gated pilot, journaled resumable rebuild, and no-loss lineage/validation plan defined below. |
| 5 — Verification and Release Gates | `Ready` | Layered deterministic/package/authorized-live canary matrix, explicit no-go/rollback criteria, and evidence-segregated release plan defined below. |

### Unified Local Meeting Intelligence

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 1 — Meeting Moment And Capture Confidence | `Ready` | Consent-safe meeting-context projection, local raw-note/capture state model, global controller, and recovery plan defined below. |
| 2 — Evidence-Aware Notes And Reviewed Actions | `Ready` | Local editable note/action provenance model, explicit review/publish boundary, conflict safety, and grounded-output plan defined below. |
| 3 — Local Recall And Action Inbox | `Ready` | Rebuildable local index, permission/provenance-aware recall, explicit grounded-Q&A boundary, and action-inbox state plan defined below. |
| 4 — Focused Recipes And Follow-Up Workbench | `Ready` | Local recipe/workbench inputs, provenance/review/output boundary, explicit export-copy safety, and recovery plan defined below. |
| 5 — Preparation And Relationship Memory | `Ready` | Local consent-scoped people/project memory, provenance/freshness/conflict model, vocabulary-feedback safety, and brief-generation plan defined below. |
| 6 — Retention, Transparency, And Explicit Export | `Ready` | Data-class retention policy, user transparency/provenance surface, explicit export/delete transactions, and verification/release plan defined below. |

### Self-Serve Release Remediation

| Sprint | Status | Evidence or first expansion focus |
| --- | --- | --- |
| 0 — Clean-Machine Evidence And Retirement Ledger | `Ready` | Clean-machine journey protocol, public-control inventory/retirement ledger, evidence segregation, and no-regression baseline defined below. |
| 1 — Microsoft Store Package And Channel Contract | `Ready` | Store/MSIX capability/package identity, worker/data/update boundary, channel migration/rollback, and clean-install validation plan defined below. |
| 2 — Automatic Capability Preparation | `Ready` | Signed/hashed capability catalog, resumable atomic preparation, truthful readiness/recovery, and clean-machine verification plan defined below. |
| 3 — Single First-Run Journey | `Ready` | Consent-led resumable onboarding, safe capture/transcript boundaries, direct recovery, and accessibility plan defined below. |
| 4 — Settings Reduction And Feature Retirement | `Ready` | Retirement-led settings ownership, backward-compatible migration, advanced/support routing, and accessibility plan defined below. |
| 5 — Distribution, Trust, And Public Documentation | `Ready` | Evidence-gated Store-first presentation, honest MSI/portable boundaries, provenance, and support-documentation plan defined below. |
| 6 — Release Qualification | `Ready` | Traceable clean-machine/channel/accessibility/usability qualification matrix, no-go criteria, and authorized release-evidence plan defined below. |

# Whole-App UX Simplification And Control Balance Plan

## Summary

Goal: simplify the full Meeting Recorder user experience without removing the
richness that makes the app useful. The current Settings and Meetings surfaces
are too cumbersome because too many controls, diagnostics, recovery actions, and
advanced tuning choices compete at the same level. The target experience is a
three-layer model:

- A default assistant layer that safely chooses defaults, refreshes status,
  ranks next actions, and explains what the app is doing.
- A guided control layer where normal users choose intent-level modes instead
  of scattered technical knobs.
- A power control layer where every existing rich control remains available,
  discoverable, and linked from the workflow where it matters.

This is not a visual hiding exercise. It is a product simplification program:
automate reversible work, combine related settings into transparent outcome
modes, make one next action obvious, and keep explicit user control for privacy,
cost, destructive changes, reprocessing, microphone capture, hosted AI, and
active-work interruption.

## Experience Contract

- Automate safely: refresh readiness, meeting catalog, cleanup
  recommendations, attendee backfill, provider status, update state, and
  metadata-only diagnostics.
- Ask explicitly: microphone capture, hosted AI fallback, speaker-name
  learning, voice-profile deletion, archive/delete, permanent delete,
  reprocessing, update interruption, and anything that may send transcript text
  off-machine.
- Combine controls where settings naturally move together; show what each mode
  controls so consolidation is transparent rather than magical.
- Expose richness contextually: advanced controls stay available, but they
  should appear where the user is solving that kind of problem.
- Make one next action obvious on Home, meeting rows, meeting detail, and setup
  recovery states.
- Preserve `DESIGN.md`: dense professional WPF, opaque technical surfaces,
  1px structure lines, 4px radii, no drop-shadow or decorative hiding, and no
  card-sprawl redesign.

## Interface Additions

Add intent-level UI/config concepts while preserving the existing concrete
settings and runtime service contracts:

- `RecordingAssistanceMode`: `Recommended`, `ManualOnly`, `Custom`.
- `ProcessingExperienceMode`: `Responsive`, `TranscriptFirst`,
  `FasterBacklog`, `Custom`.
- `SummaryExperienceMode`: `Off`, `LocalOnly`,
  `LocalWithHostedFallback`, `HostedOnly`, `Custom`.
- `UpdateExperienceMode`: `AutomaticWhenIdle`, `NotifyOnly`,
  `ManualOnly`, `Custom`.
- `MeetingsViewPreset`: `Recent`, `NeedsAttention`, `Processing`,
  `Archived`, `Custom`.

These modes are interpretation and editing layers. Runtime services should
continue consuming concrete settings such as `AutoDetectEnabled`,
`MeetingStopTimeoutSeconds`, `AutoDetectAudioPeakThreshold`,
`BackgroundProcessingMode`, `BackgroundSpeakerLabelingMode`,
`SummaryGenerationMode`, `SummaryProviderPreference`, update settings, and
folder paths. Existing artifact formats, transcript JSON sidecars, `.ready`
completion semantics, publish paths, and meeting maintenance contracts remain
unchanged.

## Sprint 0: Friction Audit And Baseline Evidence

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 source-audit completion); `Partial`
  (2026-09-27 validator rerun: control-to-source coverage stale); `Partial`
  (2026-09-29 source inventory and control policy refreshed and validated; the
  rendered-state matrix remains incomplete); `Partial` (2026-09-30 isolated
  five-state 100%/125% rendered evidence captured; manual journey replay
  remains open); `Done` (2026-09-30 rendered-state acceptance record complete;
  broader accessibility replay is owned by Sprint 15).
- User outcome: every current interactive control has a traceable future home,
  so simplification removes no power or safety choice by accident.
- Scope / non-goals: audit current WPF behavior and document evidence only. Do
  not change production layout, config, detection, recording, providers,
  artifacts, or installation behavior. Do not present this structural audit as
  user research.
- Dependencies and decisions: complete before Sprint 1. Use `DESIGN.md` as the
  visual baseline, and label each finding as source evidence, rendered evidence,
  or future-design hypothesis. Run against synthetic/redacted data only; never
  capture a real transcript, audio path, attendee, profile, API key, or log.
- Authoritative files and contracts: `DESIGN.md`,
  `src/MeetingRecorder.App/MainWindow.xaml`, `MainWindow.xaml.cs`,
  `MeetingDetailWindow.xaml`, `MeetingDetailWindow.xaml.cs`, `SetupWindow.xaml`,
  `SetupWindow.xaml.cs`, `SettingsWindowSection.cs`, app configuration, and
  current focused UI/source tests. Include any child dialog, command, or
  programmatically created action reachable from those roots.
- Implementation slices:
  1. Define `docs/ux-audits/whole-app-sprint-0-controls.schema.json` and create
     `whole-app-sprint-0-controls.json`. One row per interactive route: stable
     id, surface, visible copy, control type, source anchor, command/handler,
     visibility/enabled condition, action classification, duplicate aliases,
     current user purpose, target layer, target surface, future owner sprint,
     safety rationale, and disposition. Enumerate Buttons, MenuItems,
     Hyperlinks, editable inputs, selectors, tabs, expanders, context-menu and
     keyboard-only commands, plus child dialogs. Exclude purely presentational
     elements, but record dynamic gating that can hide or disable an action.
  2. Add `docs/ux-audits/whole-app-sprint-0-journeys.md` for all nine named
     journeys. For each, record preconditions, exact synthetic fixture state,
     ordered user activations, surface switches, waits, technical concepts the
     user must understand, recovery uncertainty, destructive/privacy/cost
     decisions, outcome, and failure/blocked branch. Count only explicit user
     activations; waits and automatic refreshes are reported separately.
  3. Add `docs/ux-audits/whole-app-sprint-0-friction.md`. Rank Settings and
     Meetings findings with `impact (1-5) × frequency (1-5) × comprehension
     burden (1-3)`; publish top three from each surface, ties included. Every
     finding links to control ids and a journey step, separates fact from
     hypothesis, and names the later sprint that owns remediation.
  4. Capture Settings and Meetings baseline screenshots at 1280×800 and 125%
     DPI from a redacted synthetic profile. Keep image files under ignored
     `.artifacts/ux-audits/whole-app-sprint-0/`; commit only
     `docs/ux-audits/whole-app-sprint-0-rendered-evidence.md` with capture
     environment, paths relative to that artifact root, SHA-256 hashes, visual
     observations, and no private payload. Capture states: empty/healthy,
     setup-blocked, processing, selection-active, and cleanup-recommendation.
  5. Add `docs/ux-audits/whole-app-sprint-0-disposition.md`. Every control id
     maps to `retain`, `combine`, `automate`, `move-to-advanced`,
     `move-contextually`, or `retire`; retired controls require a replacement
     contract. Each row names user-control layer (assistant, guided, power),
     explicit-consent boundary, target surface, and owning sprint. No row may
     silently remove a destructive, privacy, cost, microphone, hosted-AI, or
     processing-interruption choice.
  6. Add `scripts/Validate-UxAudit.ps1`. It validates schema/version, unique
     control ids, required dispositions, valid future-sprint references,
     control-to-disposition coverage, all nine journey ids, required rendered
     states, valid scoring inputs, and absence of prohibited private-path or
     transcript payload fields. It must fail with actionable missing-id output.
- Tests and rendered checks: run
  `powershell -ExecutionPolicy Bypass -File .\scripts\Validate-UxAudit.ps1`;
  manually replay every named journey from a synthetic profile; inspect the ten
  required screenshots against `DESIGN.md`; run focused UI/source tests affected
  by audit-only support code. Full `Test-All.ps1` is not required unless the
  sprint changes product code.
- Documentation / installer / release work: add only the audit artifacts and
  validator. No installer rebuild or release is required for this documentation
  and validation sprint.
- Evidence and date: 2026-09-27: committed
  `whole-app-sprint-0-controls.schema.json`,
  `whole-app-sprint-0-controls.json` (244 controls from three authoritative
  WPF roots), nine synthetic/redacted journey traces, six scored findings, and
  full disposition map. `Validate-UxAudit.ps1 -RefreshInventory` passed:
  `UX audit valid: 244 controls, nine journeys, five rendered states, and 6
  scored findings.` The rendered-evidence record initially named the safe
  synthetic capture blocker: a live installed profile owns
  `Local\\MeetingRecorder.PrimaryInstance`, so this sprint did not open or
  capture user data.
- Evidence and date: 2026-09-29: refreshed the committed inventory to 271
  controls and reviewed its changes: 56 are newly named controls and 29 are
  anonymous-control line-number renames; no capability was removed by the
  refresh. The regenerated 271-row ownership/consent policy includes the new
  destructive import-archive setting as `explicit-per-action`; both
  `Validate-UxAudit.ps1` and `Validate-UxControlPolicy.ps1` pass. A test-only
  STA WPF harness renders from an isolated temp profile without starting the
  production entry point, acquiring the mutex, or accessing the installed
  profile. It now builds all required synthetic states (`empty/healthy`,
  `setup-blocked`, `processing`, `selection-active`, and
  `cleanup-recommendation`) through real configuration, catalog, manifest, and
  contained-cleanup contracts, and exposes a shared 125% raster path. Focused
  harness project builds pass with `UseAppHost=false`; this avoids an unrelated
  shared-temp apphost access denial. Direct VSTest runs in the current executor
  are forcibly cut off at 30 seconds before a result is reported. On 2026-09-30,
  the same test-only harness was run through `MeetingRecorder.WpfRenderProbe`,
  which returned directly without acquiring the production mutex. All five
  redacted states were captured and visually reviewed at a 1280x800 logical
  viewport at 100% and 125%; hashes, artifact-relative paths, automation,
  keyboard evidence, and limits are recorded in
  `docs/ux-audits/whole-app-sprint-0-rendered-evidence.md`. The render revealed
  and fixed the 800px-height minimum and bounded queue-card overflow. Focused
  probe build passes with `UseAppHost=false`.
- Remaining gap or next action: none for Sprint 0. Sprint 15 owns the broader
  synthetic keyboard/accessibility journey matrix; do not use the live
  installed profile or change production instance behavior for that work.

Goal: prove where overwhelm comes from before changing the experience.

Workstream 1 - Control inventory:

- Inventory every Home, Settings, Setup, Meetings, meeting detail, Help,
  update, queue, and processing control.
- Classify each control as safe default, explicit choice, destructive action,
  privacy/cost decision, recovery action, advanced tuning, diagnostic-only, or
  duplicate/contextual alias.
- Identify controls currently shown in more than one place, such as setup
  readiness, speaker-labeling mode, summaries, updates, and processing actions.

Workstream 2 - Journey baseline:

- Map current journeys for:
  - first setup and first useful recording,
  - manual recording,
  - assisted auto-detected recording,
  - recovering a failed transcript,
  - managing a processing backlog,
  - configuring summaries,
  - fixing speaker labels,
  - applying speaker-name suggestions,
  - cleaning up old meetings.
- Capture current interaction counts and current rendered screenshots for the
  Settings and Meetings flows.
- Record which steps force the user to understand implementation details such
  as thresholds, manifests, worker modes, provider internals, or artifact
  locations.

Workstream 3 - Control disposition map:

- Decide which controls will be combined into modes, which will be automated,
  which will move to advanced/power surfaces, and which must remain explicit.
- Keep every existing capability assigned to a future home; do not remove
  functionality by omission.

Sprint 0 acceptance criteria:

- Every current user-visible control and action has a target disposition.
- The audit identifies at least the top Settings and Meetings friction points
  with evidence from the current app structure.
- The future design has no orphaned capability.
- `Validate-UxAudit.ps1` passes; all 9 journeys and 5 rendered states are
  present, every interactive route has one disposition, and all screenshots are
  redacted synthetic captures with hash-backed evidence.

## Sprint 1: UX Rules And Safety Policy

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 policy completion); `Partial`
  (2026-09-27 upstream control inventory stale); `Done` (2026-09-30 refreshed
  271-control inventory and policy validation).
- User outcome: users encounter one clear owner for each outcome, understand
  what can happen automatically, and must explicitly choose privacy-sensitive,
  destructive, costly, or interrupting work.
- Scope / non-goals: establish product policy and its validation data. Do not
  consolidate controls, change defaults, remove aliases, alter consent already
  stored in configuration, or ship an automation behavior in this sprint.
- Dependencies and decisions: require completed Sprint 0 control inventory and
  disposition map. This policy is authoritative for later UX roadmaps; a later
  sprint may add a stricter rule but cannot weaken these boundaries without a
  dated `PRODUCT_REQUIREMENTS.md` decision and regression coverage.
- Authoritative files and contracts: Sprint 0 control inventory; `DESIGN.md`;
  `PRODUCT_REQUIREMENTS.md`; `AppConfig` and its persisted settings; current
  confirmation dialogs, update/install policies, queue controls, summary
  provider paths, microphone/auto-detect controls, and archive/delete handlers.
- Implementation slices:
  1. Add a `Control Ownership And Safety Policy` section to
     `PRODUCT_REQUIREMENTS.md` and a machine-readable
     `docs/ux-audits/whole-app-sprint-1-policy.json`. For each Sprint 0 control
     id, record primary owner surface, layer (`assistant`, `guided`, `power`),
     alias behavior (`primary`, `navigate`, `contextual-duplicate`), default
     behavior, user-visible status location, and exact later-sprint owner.
  2. Define automation/consent matrix with four dispositions: `automatic`,
     `automatic-after-persisted-opt-in`, `recommend-only`, and
     `explicit-per-action`. Metadata-only refresh and recommendation generation
     may be automatic only when local, bounded, non-overlapping, and shown on
     the owning surface. A persisted opt-in must state scope, data boundary,
     reversal path, and applies only to its named capability; it never permits
     an unrelated provider, microphone path, or destructive action.
  3. Make permanent delete, archive, cleanup apply, reprocessing, worker
     interruption, microphone enablement, hosted transcript transfer, hosted
     provider fallback, and update installation explicit-per-action or explicit
     persisted opt-in as appropriate. Bulk choices show eligible count, affected
     artifacts, irreversible impact, and cancellation behavior. Hosted fallback
     requires a clear local-versus-hosted boundary before first use and when its
     configured provider or data scope changes.
  4. Define one-primary-owner rule: aliases either invoke that same action with
     identical safety semantics or navigate to the owner; they cannot make an
     action easier, broader, or less explicit. Disabled/blocked actions state
     reason, prerequisite, and next safe action in the current viewport. No
     background mutation may rely only on an activity log or transient toast.
  5. Add approved copy patterns for recommended, unavailable, local-only,
     hosted-boundary, destructive, interruption, and background states. Each
     pattern contains outcome, concise reason, scope, and next action; names
     internal thresholds, manifests, workers, or provider details only in
     Advanced/Help. Preserve technical status text where a power user explicitly
     opened diagnostics.
  6. Add `scripts/Validate-UxControlPolicy.ps1`. It joins Sprint 0 control ids
     with policy rows; fails on missing/duplicate ownership, unknown layer or
     disposition, invalid alias target, automatic high-risk action, missing
     current-viewport status, unowned future sprint, or hosted/microphone/
     destructive/interrupting action without its required consent rule. Include
     compact fixture cases for all four dispositions and alias conflict.
- Tests and rendered checks: run Sprint 0 validator, then
  `powershell -ExecutionPolicy Bypass -File .\scripts\Validate-UxControlPolicy.ps1`.
  Manually review policy examples for local summary, hosted fallback, mic
  enablement, archive, permanent delete, cleanup apply, re-transcription,
  queue interruption, and idle update. Use synthetic data; verify a blocked
  state is visible without opening Help or logs.
- Documentation / installer / release work: update `PRODUCT_REQUIREMENTS.md`
  and audit artifacts only. No installer rebuild or release is required unless
  product behavior changes while implementing the policy.
- Evidence and date: 2026-09-27: added `PRODUCT_REQUIREMENTS.md` control
  ownership, alias, automation/consent, blocked-state, and copy contract;
  `whole-app-sprint-1-policy.schema.json`; and
  `whole-app-sprint-1-policy.json`. The policy maps all 244 Sprint 0 controls
  to exactly one ownership row (237 primary owners, 7 aliases), with 13 scoped
  persisted opt-ins, 227 explicit-per-action controls, and 4 recommendation-only
  controls. It classifies 46 high-risk controls (microphone, hosted transfer,
  destructive work, safe cleanup, and interruption) with a required consent
  rule. `Validate-UxControlPolicy.ps1` passed: `UX control policy valid: 244
  ownership rows, 5 fixtures, and all high-risk actions consent-gated.` A
  2026-09-27 validator rerun still passes, but initially could not restore Done
  status while its Sprint 0 inventory dependency reported stale control-to-source
  coverage.
- Evidence and date: 2026-09-30: reviewed the refreshed 271-row policy after
  the Sprint 0 inventory refresh. It has 260 primary owners and 11 aliases,
  with 13 capability-scoped persisted opt-ins, 254 explicit-per-action controls,
  and four recommendation-only controls. It identifies 47 high-risk actions
  (including the new destructive/interrupting import-archive setting); none is
  automatic or recommendation-only. Both UX validators pass against the same
  current inventory.
- Remaining gap or next action: none for this policy sprint. Later UX work must
  retain alias equivalence, capability-scoped persisted opt-ins, and
  non-automatic destructive/hosted/microphone paths.

Goal: create the decision rules that prevent future control sprawl.

Workstream 1 - Three-layer product model:

- Define the assistant-default layer, guided-control layer, and power-control
  layer in `PRODUCT_REQUIREMENTS.md`.
- State that normal users should see outcome choices first, while advanced
  users can still inspect and edit the underlying details.
- Require each new feature to decide which layer owns its default control,
  guided mode, and advanced configuration.

Workstream 2 - Automation boundaries:

- Allow automatic metadata-only refreshes and reversible recommendation
  generation.
- Prohibit silent hosted-provider use, microphone enablement, permanent delete,
  destructive cleanup, archive actions, reprocessing, or active-work
  interruption.
- Require visible status when background automation is running or recently
  changed what the user sees.

Workstream 3 - Copy and blocked-state rules:

- Define plain-language patterns for recommended actions, unavailable states,
  provider setup, local-only features, hosted transcript boundaries, and
  destructive operations.
- Prefer outcome labels such as `Publish transcript first` over internal
  mechanism labels when the user does not need the implementation detail.

Sprint 1 acceptance criteria:

- Product docs define "assume safely" versus "ask explicitly".
- Future work has a written rule for where controls should appear.
- Privacy, hosted AI, destructive actions, and active-work interruption remain
  explicit user decisions.
- Policy validator passes against the completed Sprint 0 inventory; every
  action has exactly one owner/layer, every alias preserves safety semantics,
  and no high-risk action is classified as automatic.

## Sprint 2: Settings Preset Engine

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27).
- User outcome: users choose a small outcome-level mode without losing, hiding,
  silently changing, or misrepresenting their exact configuration.
- Scope / non-goals: add a projection and editing layer over existing concrete
  `AppConfig` fields. Do not persist new mode fields, rewrite config on load,
  infer credentials, change model paths, enable microphone capture, authorize a
  hosted provider, or claim later backlog/GPU behavior already exists.
- Dependencies and decisions: implement after Sprint 1 policy. UI placement is
  Sprint 3; this sprint delivers core contracts, mappings, editor behavior, and
  focused tests. `Custom` is an inference result, not a selectable preset and
  not a normalization target.
- Authoritative files and contracts: `AppConfig`, `AppConfigStore`, live-config
  save/reload behavior, `MainWindowInteractionLogic` pending-editor comparison,
  summary-secret store, current update and processing policies, and Sprint 1
  control/safety policy.
- Implementation slices:
  1. Add immutable mode enums and `SettingsPresetService` in core. The service
     takes an `AppConfig` snapshot and returns four independent projections:
     recording, processing, summaries, and updates. It must be pure, have no
     file/UI access, and return mode, owned-field summary, setup/blocked state,
     and a field-level reason for `Custom`.
  2. Publish one mapping table in code/docs. Recording owns only detection and
     auto-stop/title/attendee behavior; it never changes microphone capture.
     Processing owns only current background-processing, speaker-labeling, and
     transcript-first settings. Summary owns enablement/provider preference, not
     keys, provider URLs, model names, or request tuning. Updates own check and
     install-when-idle preferences, never an immediate install. Every concrete
     field has at most one mode owner; unowned fields survive all preset changes.
  3. Implement `ApplyPreset(editorConfig, preset)` as an atomic, owned-field
     patch against the unsaved editor snapshot. Selecting a mode updates only
     its owned fields and waits for normal Save Changes. Selecting `Custom` is
     impossible. Changing one owned advanced field recalculates that mode to
     `Custom`; changing an unowned field does not. No automatic save or config
     rewrite occurs during inference, startup, or hot reload.
  4. Define unavailable-mode behavior. `Recommended` never enables microphone
     capture. A hosted summary mode remains `Needs provider setup` until explicit
     provider consent and credentials already exist; it must not fall back or
     transmit text. `FasterBacklog` only maps presently supported safe settings;
     unavailable future acceleration is disclosed and does not fabricate a GPU
     or capacity promise. Manual-only leaves manual recording usable.
  5. Add compact mode summaries and a `What this controls` data model for Sprint
     3 UI. It names exact changed behaviors, excludes secrets/technical tuning,
     exposes Advanced route, and shows whether changes apply now, next job, or
     next recording. Preserve unsaved-edit semantics on hot reload: do not
     overwrite editor input; show reload/conflict state and recompute modes from
     the currently displayed snapshot only after user resolution.
  6. Add `SettingsPresetServiceTests` and focused pending-editor tests. Include
     canonical inference, every owned-field deviation to `Custom`, unrelated
     fields preserved, legacy/unknown normalized config safe loading, atomic
     patch, no secret/path mutation, no microphone/hosted implicit enablement,
     no auto-save, reload with unsaved edits, and each mode's applied-state copy.
- Tests and rendered checks: run focused preset and config-store tests, then
  inspect a synthetic configuration for each canonical mode, `Custom`, provider
  not ready, and unsaved reload conflict. Confirm no test or implementation
  writes secrets into `AppConfig`.
- Documentation / installer / release work: update `PRODUCT_REQUIREMENTS.md`
  mapping/policy reference and the future Settings copy. No installer rebuild or
  release is required until a later sprint exposes the feature in shipped UI.
- Evidence and date: 2026-09-27: added immutable intent-mode contracts and a
  pure `SettingsPresetService` with per-category ownership maps, exact
  projection, field-level `Custom` reasons, provider-readiness blocking, and
  editor-only owned-field patches. Added `SettingsPresetServiceTests` (10 pass)
  covering canonical/unknown inference, hosted consent and credential gates,
  no microphone/path/model mutation, ownership, atomic patches, and unsaved
  editor preservation. `PRODUCT_REQUIREMENTS.md` now carries the published
  mapping and timing table. No installer rebuild is required: Sprint 2 has no
  shipped UI or runtime packaging change.
- Remaining gap or next action: Sprint 3 must bind this core contract to the
  Settings information architecture while retaining the normal Save Changes and
  unsaved-reload conflict semantics.

Goal: combine related settings into meaningful outcome modes while preserving
exact control.

Workstream 1 - Preset inference:

- Infer each intent-level mode from existing config on load.
- Show `Custom` when concrete config values do not match a known bundle.
- Preserve hot reload, pending-change detection, config save status, and
  backward compatibility.

Workstream 2 - Preset mapping:

- `RecordingAssistanceMode.Recommended` should manage auto-detect, title
  fallback, attendee enrichment, stop timeout, and detection threshold using
  safe defaults.
- `RecordingAssistanceMode.ManualOnly` should keep recording usable without
  assisted detection or auto-stop semantics.
- `ProcessingExperienceMode.Responsive` should protect live recording and
  defer speaker labels when appropriate.
- `ProcessingExperienceMode.TranscriptFirst` should prioritize publishing
  transcripts before optional speaker labels.
- `ProcessingExperienceMode.FasterBacklog` should drain queued work more
  aggressively while preserving app safety.
- `SummaryExperienceMode` should combine summary enablement and provider
  preference while keeping hosted-provider credentials explicit.
- `UpdateExperienceMode` should combine update checks and idle auto-install
  behavior while preserving manual update controls.

Workstream 3 - Transparent control:

- Add a "what this controls" summary for each mode.
- Changing an individual underlying value should move the visible mode to
  `Custom`.
- Keep raw advanced fields editable in Advanced or expanded details.

Sprint 2 acceptance criteria:

- Existing configs load safely and infer a mode or `Custom`.
- Users can choose high-level modes without losing exact-field control.
- Focused tests cover preset-to-config mapping, config-to-preset inference,
  pending-change detection, and `Custom` transitions.
- Inference/save/hot reload never persist modes or mutate concrete config; all
  mode patches are owned-field-only, atomic, pending until explicit save, and
  uphold Sprint 1 microphone, hosted-provider, update, and interruption rules.

## Sprint 3: Settings Information Architecture

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27).
- User outcome: Settings starts with the task a person is trying to complete;
  exact and diagnostic controls remain reachable without forcing normal users
  through implementation details.
- Scope / non-goals: reorganize existing settings surfaces and routes. Do not
  remove a configuration field, change its runtime semantics, persist UI
  navigation, relocate secrets into `AppConfig`, or merge unrelated Setup and
  provider behavior.
- Dependencies and decisions: implement after Sprint 0 disposition, Sprint 1
  policy, and Sprint 2 projection contracts. The shared `AppPlatform.Shell.Wpf`
  settings host remains owner of window lifecycle; this sprint changes content
  routing only.
- Authoritative files and contracts: `SettingsWindowSection`, settings-host
  open/focus/deep-link path, `MainWindow.xaml` setting panels, configuration
  editor snapshot/save behavior, Sprint 0 control inventory, Sprint 1 control
  ownership policy, and `DESIGN.md`.
- Implementation slices:
  1. Replace the current content model with `Setup`, `Recording`, `Processing`,
     `Summaries`, `FilesAndUpdates`, and `Advanced`. Maintain a control-to-
     section manifest derived from Sprint 0; each interactive setting has one
     primary section and optional navigation-only aliases. Setup contains only
     readiness/provisioning/probe actions, never ordinary recording or provider
     tuning.
  2. Add a transient `SettingsNavigationTarget(section, controlId?)` contract.
     It opens the shared host, selects the section, waits for layout, focuses the
     exact visible control, and supplies a section heading for assistive tech.
     Existing `general`, `files`, and `updates` deep-link ids remain valid:
     `general` routes to Recording, `files` to FilesAndUpdates, and `updates`
     to FilesAndUpdates plus the update anchor. Unknown links land on Recording
     with no state mutation and a diagnostic-only log.
  3. Move controls by user intent. Recording owns recording assistance/mic/
     detection/title/attendee/startup. Processing owns transcript and backlog
     behavior, speaker-label timing, learning, profiles, and supported
     acceleration truth. Summaries owns mode, readiness, local provider status,
     hosted boundary, and retry readiness. FilesAndUpdates owns output folders,
     release state, checks, and install controls. Advanced holds raw thresholds,
     custom-provider details, URLs, model/request tuning, troubleshooting, and
     performance controls. No field becomes hidden-only; Advanced has a visible
     navigation entry and an exact deep link.
  4. Add an `ApplyTiming` map for every configurable control: `Immediate`,
     `NextRecording`, `NextProcessingRun`, `NextAppStart`, or
     `ExternalActionRequired`. Display timing next to changed mode summaries
     and affected fields. Derive it from actual dependency/lifecycle behavior;
     where timing is uncertain, show conservative `NextAppStart` rather than
     claiming immediate application.
  5. Preserve editor integrity while navigating. Changing section, using a
     deep link, or reopening the shared host must preserve unsaved values and
     pending-change state; it cannot reload, save, or normalize config. If an
     external reload conflicts with unsaved edits, use Sprint 2 conflict state
     before changing selection/focus. Keep source order and keyboard tab order
     aligned with visual order; use dense technical wells and no-shadow rules.
  6. Add mapping and routing tests: every Sprint 0 settings control has one
     valid section/timing record; each old/new route resolves; deep links focus
     visible controls; Advanced remains navigable; unsaved changes survive
     navigation; unknown routes are safe; and section movement preserves the
     exact config snapshot. Add source/XAML guards only for routing and
     accessibility names, not arbitrary layout trivia.
- Tests and rendered checks: run preset/config tests plus focused routing tests.
  Manually check direct links from Home, Meetings, meeting detail, Help, update,
  setup-blocked, and provider-blocked states at 1280×800 and 125% DPI. Verify
  screen-reader section name, focus target, keyboard order, persistent Save
  Changes state, and Advanced discovery.
- Documentation / installer / release work: update settings navigation and
  timing guidance in `README.md`/`SETUP.md` only after UI ships. Rebuild the
  installer when shipped WPF changes are made; no rebuild is needed for this
  planning record alone.
- Evidence and date: 2026-09-27: replaced the five-section host registration
  with `Setup`, `Recording`, `Processing`, `Summaries`, `Files & Updates`, and
  `Advanced`; added a pure route/control/timing catalog and transient target;
  routed old `general`, `files`, and `updates` links safely (with the update
  anchor); and moved existing live controls between section panels without
  recreating or loading editor state. The source shows conservative apply timing
  in each intent panel. `MainWindowXamlTests`,
  `SettingsInformationArchitectureTests`, `SettingsPresetServiceTests`,
  `AppConfigStoreTests`, and `LiveAppConfigTests` passed (82 focused tests).
  README and SETUP navigation wording now matches the host. Canonical installer
  rebuild and live rendering were not run: the shared worktree contains
  unrelated uncommitted release inputs and the existing app owns the global
  recorder mutex, so rebuilding/package-testing that state would overwrite or
  package user work.
- Remaining gap or next action: when the intended source set is isolated,
  rebuild the installer and run the 1280×800/125% DPI rendered journey checks;
  Sprint 4 can now consume the Recording and Setup routes without route drift.

Goal: make Settings navigable by user intent instead of implementation area.

Workstream 1 - Section model:

- Reorganize Settings into:
  - `Setup`: readiness for transcription, speaker-labeling assets, and Teams
    probe.
  - `Recording`: recording assistance, microphone capture, auto-detection,
    titles, attendees, and recording startup behavior.
  - `Processing`: transcript/backlog behavior, speaker labeling timing,
    speaker-name learning, and voice-profile management.
  - `Summaries`: summary mode, provider readiness, local ModelProxy status,
    hosted OpenAI controls, and summary retry readiness.
  - `Files & Updates`: meeting folders, update mode, release status, and
    update install controls.
  - `Advanced`: raw thresholds, custom provider internals, update feed URL,
    diagnostics, performance tuning, GPU/acceleration truth, and advanced
    troubleshooting.

Workstream 2 - Routing:

- Update `SettingsWindowSection`, section definitions, navigation buttons,
  focus targets, and `ShellStatusTarget`/deep-link routing.
- Add direct links from Home, Meetings, meeting detail, and Help to exact
  Settings sections.
- Preserve the existing shared settings host ownership from
  `AppPlatform.Shell.Wpf`.

Workstream 3 - Apply timing:

- Show whether a section's changes apply immediately, on the next recording,
  on the next processing run, or after update/restart.
- Reuse existing config dependency logic where possible.

Sprint 3 acceptance criteria:

- Everyday settings are not mixed with thresholds, raw provider internals, or
  diagnostics.
- Setup is readiness-only.
- Advanced controls remain discoverable and real, not removed.
- Every existing settings control resolves to exactly one intent section and
  application-timing record; legacy deep links, unsaved edits, keyboard focus,
  and shared-host ownership remain compatible.

## Sprint 4: Recording And Setup Simplification

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27).
- User outcome: a person reaches local standard transcription and manual
  recording through one plain path, while optional accuracy, speaker labeling,
  microphone capture, integrations, and repair remain clear choices.
- Scope / non-goals: simplify setup/readiness presentation and routing. Do not
  lower model validation, bypass hashes/path checks, enable microphone or hosted
  AI, begin recording, download optional assets, alter detection thresholds, or
  change Teams probing without an explicit user action.
- Dependencies and decisions: implement after Sprints 1–3. `Ready to record`
  means a valid local transcription runtime/model is selected and writable
  output paths are available. Speaker labeling, Teams integration, imported
  models, higher accuracy, microphone capture, auto-detection, summaries, and
  updates are not readiness prerequisites. Existing manual Start/Stop remains
  separate from detector state.
- Authoritative files and contracts: setup host/window, setup guidance/model
  provisioning, model and diarization asset catalogs, config store, recording
  start gate, microphone consent path, Teams probe result, Sprint 1 policy, and
  `DESIGN.md`.
- Implementation slices:
  1. Define `RecordingReadinessSnapshot` with independently visible fields for
     local transcription, output path, recording permission, optional speaker
     labeling, microphone capture, auto-detection, Teams integration, and
     advanced provider state. It returns one primary blocking reason, zero or
     more non-blocking notices, exact remediation target, and no raw paths or
     private probe payload in normal copy.
  2. Implement `Use recommended` as an explicit user-triggered setup action:
     select the supported local Standard transcription path, validate available
     disk/asset destination, and start any required approved download with
     source/size/progress/cancel/error visible. It must never enable mic capture,
     hosted summaries, external providers, auto-detection, or speaker labeling.
     On download failure/cancel, preserve existing valid selection and provide
     Retry, Import approved file, and diagnostic route; do not claim readiness.
  3. Keep Higher Accuracy, Import approved file, and Skip speaker labeling as
     sibling, explicit choices. Higher Accuracy states disk/time impact before
     download. Import keeps file picker validation and makes copied/managed
     location clear without exposing it in normal status. Skipping optional
     speaker labeling publishes transcripts normally and leaves later setup
     discoverable; it does not write a permanent refusal unless the user chooses
     an explicit persisted preference.
  4. Place recording assistance controls under Recording. Keep microphone as a
     separate consent-bearing control with clear captured-audio scope; place
     threshold and stop-timeout tuning under `Recording assistance > Custom`.
     Manual Start/Stop remains visible and operable whenever transcription is
     ready, even when auto detection is disabled, blocked, or uncertain.
  5. Map Teams probe to compact capability state: `Local detector active`,
     `Official integration available`, `Limited`, or `Blocked`. State signal
     scope and next action, not unsupported certainty about meeting capture.
     Detailed baseline, failure reason, and metadata stay in expandable
     Advanced/Help diagnostics; normal UI never shows sensitive process data.
  6. Add focused readiness/provisioning tests for valid Standard, missing or
     corrupt asset, no writable output, canceled/failed download, valid import,
     skipped optional diarization, manual-only detection, mic disabled, and each
     Teams state. Add source/routing tests that all primary fixes reach Setup or
     Recording and that no recommended action mutates protected settings.
- Tests and rendered checks: run focused model catalog/provisioning, setup,
  recording-gate, config, and Teams-probe tests. Render fresh install, Standard
  ready, download in progress, download failure, custom import, optional
  speaker-label skip, mic disabled, auto-detection blocked, and Teams blocked.
  Verify primary action, retry/cancel, keyboard order, accessible names, and
  Advanced access at 1280×800 and 125% DPI.
- Documentation / installer / release work: document post-ship Standard versus
  Higher Accuracy, local-only default, optional speaker labeling, and repair
  routes in `README.md` and `SETUP.md`. Rebuild installer assets and run package
  smoke once setup/runtime behavior changes ship.
- Evidence and date: 2026-09-27: added pure `RecordingReadinessSnapshot` with
  one ordered blocker, safe notices, compact Teams capability mapping, and
  remediation targets. Setup renders that summary and routes its primary fix;
  recording assistance now owns auto-detection and its `Custom` threshold/
  timeout disclosure. `Use recommended` validates free space, reports
  approved-download size/progress, supports cancel, preserves a valid model on
  failure, and leaves speaker labeling and all other protected settings
  untouched. Teams normal copy is compact; probe detail is expandable. Focused
  provisioning/readiness/routing/config/recording/Teams tests passed (249).
  README and SETUP now describe local-only recommended setup, its alternatives,
  and Advanced Teams diagnostics. Canonical installer rebuild/package smoke and
  live rendering were not run because the shared worktree has unrelated
  uncommitted release inputs and the installed app holds the global recorder
  mutex; either action could overwrite/package user work.
- Remaining gap or next action: when the intended source set is isolated,
  rebuild installer assets and manually render the Sprint 4 journey states at
  1280×800/125% DPI. Sprint 5 can consume `RecordingReadinessSnapshot`.

Goal: make the first useful recording easier without weakening recording
control.

Workstream 1 - First-run and setup path:

- Make `Use recommended` the default setup path for standard transcription and
  safe optional speaker-labeling defaults.
- Keep `Higher accuracy`, `Import approved file`, and `Skip optional speaker
  labeling` as explicit setup alternatives.
- Keep model paths, raw asset paths, and custom import details accessible from
  setup details or Advanced.

Workstream 2 - Recording controls:

- Keep microphone capture separate and explicit because it changes what is
  recorded.
- Move detection threshold and stop timeout tuning under
  `Recording assistance > Custom`.
- Keep manual Start/Stop available regardless of detection state.

Workstream 3 - Teams integration:

- Summarize Teams probe results as simple capability states such as local
  detector active, official path available, or blocked.
- Keep the detailed probe baseline, block reason, and metadata expandable for
  troubleshooting.

Sprint 4 acceptance criteria:

- A non-technical user can reach ready-to-record without understanding model
  paths, thresholds, provider internals, or worker modes.
- Technical setup details remain available for custom and troubleshooting
  cases.
- Recommended setup changes only its stated local transcription prerequisites;
  it neither grants microphone/hosted/optional-feature consent nor hides a
  failed, canceled, unavailable, or blocked readiness condition.

## Sprint 5: Home As Command Center

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 implementation).
- User outcome: Home immediately answers whether recording can start, whether
  it is recording correctly, and the single safest next thing to do.
- Scope / non-goals: consolidate Home presentation/state selection. Do not make
  Home auto-download, auto-install, enable microphone, configure providers,
  apply cleanup, reprocess, change queue priority, or perform any high-risk
  action. Remediation actions navigate to their owning surface.
- Dependencies and decisions: integrate S3 navigation targets and S4 readiness
  snapshot. Preserve existing recording coordinator as capture authority and
  queue/update/provider services as state authority. One shared state feeds
  header and Home; neither surface may independently rank work.
- Authoritative files and contracts: dashboard interaction logic/state models,
  recording coordinator and loopback snapshot, readiness snapshot, processing
  queue status, update/provider status, Home XAML, `DESIGN.md`, and Sprint 1
  safety policy.
- Implementation slices:
  1. Replace ad hoc dashboard ranking with pure `HomeCommandCenterState` and
     `NextBestActionResolver`. Inputs are timestamped readiness, recording,
     capture, queue, update, provider, and selected-meeting recovery snapshots;
     output is one headline, one short reason, zero-or-one navigation target,
     severity, freshness, and suppressed-candidate reasons. Resolver performs
     no I/O and never returns a destructive/external/execution command.
  2. Define fixed priority: active recording/capture risk first; recording
     blocker second; user-requested recovery third; setup required for selected
     current task fourth; actionable queued work fifth; update/provider notices
     last. Optional summary/provider gaps must not outrank a recordable state.
     When actively recording, Stop remains the primary control and the next
     action may only explain/navigate; it cannot change active capture.
  3. Model capture truth separately as `LiveOutput`, `FallbackOutput`,
     `StaticReadiness`, `Degraded`, or `Unavailable`. Show fallback only when
     the coordinator reports a real active fallback source; show unavailable
     when no source is usable; never infer healthy capture from a detector title
     or stale probe. Use friendly device/source labels, elapsed/counter timing,
     and no raw endpoint path, meeting title, or diagnostic payload in normal
     Home status.
  4. Build compact, stable wells for recording/capture, setup readiness,
     queue, and updates. Preserve live elapsed time, editable meeting metadata,
     capture graph, auto-stop countdown, and active source in the recording
     well. Use Segoe UI for outcome copy and Cascadia Mono/Consolas only for
     elapsed time/counters; apply dense opaque wells, 1px structure, 4px radius,
     and no shadows. Update in place without reordering/focus jumps on timer
     ticks or background refresh.
  5. Keep normal Home controls limited to Start/Stop, current title/project/
     attendees, explicit microphone capture, and recording assistance. Every
     other action routes through `SettingsNavigationTarget` or Meetings with a
     focused target. Header and Home aliases must show identical label/reason/
     target/safety behavior from the same state object.
  6. Add resolver tests covering each priority conflict, stale data, active
     recording, missing model, writable-output failure, live/fallback/degraded/
     unavailable capture, optional provider gap, queue backlog, update, and no
     action. Add UI/source guards for one shared state and manual tests that
     timer/status updates preserve focused metadata editor text.
- Tests and rendered checks: run focused command-center and interaction tests.
  Render idle-ready, setup-blocked, active live capture, active fallback,
  degraded capture, auto-stop countdown, queue backlog, update available, and
  provider-not-ready states. Check 1280×800/125% DPI, keyboard Start/Stop,
  current-viewport status, screen-reader labels, and no flicker or focus loss.
- Documentation / installer / release work: document Home state meanings after
  ship. Rebuild installer assets and run packaged smoke for shipped WPF/runtime
  changes; this planning record alone needs neither.
- Evidence and date: 2026-09-27 implemented pure timestamped
  `HomeCommandCenterState` / `NextBestActionResolver` state in
  `src/MeetingRecorder.Core/Services/HomeCommandCenterService.cs`; fixed
  capture-risk, readiness, recovery, current-task, queue, update, and provider
  priority; added shared Home/header presentation and navigation-only routing in
  `src/MeetingRecorder.App/MainWindow.xaml(.cs)`; surfaced truthful
  static/live/fallback/degraded/unavailable capture copy; and documented state
  meanings in `README.md` and `SETUP.md`. Focused command-center, XAML,
  interaction, readiness, and coordinator suite passed 213 tests on 2026-09-27
  (pre-existing `TrackingWaveIn.DataAvailable` unused-event warning only).
- Remaining gap or next action: no live WPF render/manual keyboard, focus,
  screen-reader, or DPI pass ran because another user-owned app instance holds
  the recording mutex. No installer rebuild or packaged smoke ran because the
  shared workspace/release inputs contain unrelated dirty work; run those from
  a clean, isolated shipped change set.

Goal: make Home reduce Settings trips and answer the user's immediate
questions.

Workstream 1 - Next Best Action:

- Replace multiple readiness cards with one ranked `Next Best Action` surface.
- Include targets for setup gaps, recording readiness, update availability,
  provider configuration, processing backlog, and meeting recovery.
- Show one action label and one short reason.

Workstream 2 - Compact status wells:

- Show recording state, detected meeting source, capture device path, setup
  health, update state, and queue status in compact wells.
- Keep the active recording timer, title/project/key-attendee editing, and
  capture graph visible.
- Show auto-stop countdown and active capture path during live recording.

Workstream 3 - Minimal normal controls:

- Keep Home controls to Start/Stop, current meeting metadata, microphone
  capture, and recording assistance.
- Route advanced recording, setup, summaries, and processing controls to the
  exact Settings or Meetings workflow.

Sprint 5 acceptance criteria:

- Home answers: can I record, what is happening, and what should I do next.
- A normal recording flow does not require inspecting Settings.
- Home status distinguishes live output, fallback output, static readiness, and
  unavailable backend states.
- All Home/header recommendations come from one deterministic resolver, preserve
  active recording control, route rather than execute remediation, and remain
  current-viewport-visible without exposing private diagnostics.

## Sprint 6: Meetings View Presets

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 implementation).
- User outcome: Meetings opens to a truthful, useful scope without asking people
  to understand table/group mechanics; precise browsing remains one action away.
- Scope / non-goals: replace view-selection presentation and persisted view
  preference. Do not archive/delete/reprocess/change meeting metadata, invent
  a recommendation, hide records from search, or treat an archive folder as a
  catalog row before catalog support is implemented.
- Dependencies and decisions: reuse current catalog/row ownership and preserve
  existing table/group settings. Needs Attention consumes Sprint 7 state once
  available; until then it uses only normalized local status/recovery fields.
  Archived requires an explicit catalog availability decision; it may initially
  show an honest unavailable state, never an empty list that implies no archive.
- Authoritative files and contracts: `AppConfig` and config migration, meeting
  catalog/row model, current view/sort/group UI, selection and bulk-action state,
  meeting cleanup/archive service, Sprint 1 policy, and `DESIGN.md`.
- Implementation slices:
  1. Add persisted `MeetingsViewPreset` (`Recent`, `NeedsAttention`,
     `Processing`, `Archived`, `Custom`) and a migration version separate from
     current table/group fields. Existing saved Table/Grouped/sort/direction/
     group choices migrate to `Custom` without change. Fresh/no-preference users
     select `NeedsAttention` only when current normalized data has actionable
     unresolved work; otherwise `Recent`. Persist that initial choice only after
     it is shown, never from stale/failed refresh data.
  2. Create pure `MeetingViewPresetResolver`. It returns filter, sort, grouping,
     status summary, empty-state kind, catalog requirement, and whether Custom
     controls are visible. Recent means non-archived meetings sorted newest
     first; NeedsAttention means failed, blocked, queued, processing, or one
     actionable local recommendation; Processing means queued/processing/
     retry-needed work; Archived means catalog-confirmed archived records only.
     Search runs after preset scoping, matches every preset, and never mutates
     the saved preset.
  3. Define `Custom` as exact advanced browsing: current table/group mode, sort
     key/direction, group key, and expand/collapse controls remain intact and
     persist independently. Any user change in that panel selects Custom; a
     preset selection applies its fixed query without overwriting stored Custom
     choices. Do not expose raw controls in normal preset toolbar.
  4. Make list state safe and comprehensible. Changing preset/search/refresh
     clears selection and closes/invalidates bulk command state if selected rows
     leave scope; never run a previously enabled bulk action on hidden rows.
     Status states distinguish `No meetings yet`, `No matches for search`, `No
     meetings need attention`, `No processing work`, `Archive catalog not
     available`, and refresh failure/staleness. Include displayed/total count and
     last successful refresh time without leaking private paths.
  5. Build compact preset selector, concise scope summary, visible search, and
     Custom View disclosure using technical-studio density. Preserve keyboard
     focus when the list refreshes, give selector/search/list accessible names,
     and make custom grouping/expand actions reachable but visually secondary.
  6. Add resolver/config/UI tests: every preset query, search intersection,
     statuses/empty states, fresh default with and without attention, old config
     migration, unknown enum fallback, Custom round trip, user-customization
     preservation, unavailable archive catalog, selection reset, and no archive
     filesystem mutation. Add source guards only for selector/search/custom
     routing and accessible names.
- Tests and rendered checks: run focused config migration, resolver, meeting
  refresh/selection tests. Render fresh empty library, healthy Recent, attention
  queue/failure, Processing, Custom grouped view, Archived available/unavailable,
  zero search result, stale refresh, and multi-selection-to-preset transition at
  1280×800 and 125% DPI. Check keyboard navigation and context menu eligibility.
- Documentation / installer / release work: document presets and Custom View
  after shipping. Rebuild installer assets and smoke packaged UI for shipped WPF
  changes; no package work for this plan record.
- Evidence and date: 2026-09-27 implemented pure
  `MeetingViewPresetResolver` in
  `src/MeetingRecorder.Core/Services/MeetingViewPresetResolver.cs`, separate
  preset migration/initialization state in `AppConfig` and `AppConfigStore`, and
  `Recent` / `Needs Attention` / `Processing` / `Archived` / `Custom` routing
  in `MainWindow.xaml(.cs)`. Existing table/group/sort/direction values migrate
  unchanged to `Custom`; fresh rows choose and persist `Needs Attention` only
  after shown and only when normalized local work is actionable. `Archived`
  reports catalog unavailable without archive filesystem access, and scoped
  search refreshes clear rows that leave selection. Updated `README.md` and
  `SETUP.md`. Focused preset/config/XAML/meeting-refresh/cleanup suite passed
  281 tests on 2026-09-27 (pre-existing `TrackingWaveIn.DataAvailable`
  unused-event warning only).
- Remaining gap or next action: no live WPF render/manual keyboard, context-menu,
  focus, screen-reader, or DPI pass ran because another user-owned app instance
  holds the recording mutex. No installer rebuild or packaged smoke ran because
  shared workspace/release inputs contain unrelated dirty work; run those from a
  clean, isolated shipped change set. Archive remains deliberately unavailable
  until a catalog can confirm archived records.

Goal: make the Meetings workspace useful immediately instead of making the user
configure the list first.

Workstream 1 - Presets:

- Replace always-visible sort/group/direction controls with view presets:
  `Recent`, `Needs Attention`, `Processing`, `Archived`, and `Custom`.
- Keep search visible in every preset.
- Move sort key, sort direction, group key, expand all, and collapse all into
  `Custom View` controls.

Workstream 2 - Defaults and persistence:

- Default new and migrated users to `Recent` unless unresolved work makes
  `Needs Attention` more useful.
- Persist the selected preset.
- Keep grouped browsing available without making it the default complexity.

Workstream 3 - Status:

- Show what a preset is doing, such as "showing failed, queued, and
  recommendation-bearing meetings".
- Show empty states that explain whether no meetings exist, no meetings match
  search, or no meetings need attention.

Sprint 6 acceptance criteria:

- Meetings opens to a useful default list.
- Advanced browsing remains available without dominating the toolbar.
- Search remains first-class.
- Preset migration preserves every current Custom/table/group preference, and
  preset/search changes cannot leave hidden selected rows eligible for actions.

## Sprint 7: Recommendation-First Meetings

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: cleanup recommendation
  engine and badges); `Ready` (2026-09-27 pressure test); `Done` (2026-09-27
  implementation: metadata-only resolver, one safe row/menu/detail route, bounded
  recommendation dismissal, and focused verification).
- User outcome: each meeting states one most useful, safe next action—or clearly
  says complete, blocked, or still being evaluated—without presenting a wall of
  equally urgent maintenance commands.
- Scope / non-goals: create presentation/ranking contract over current local
  metadata. Do not inspect transcript text, send data to providers, auto-apply
  destructive work, change cleanup execution, suppress underlying actions, or
  turn optional summary/provider setup into a recording blocker.
- Dependencies and decisions: consume current catalog, processing, artifact,
  summary, speaker, readiness, and cleanup snapshots. Keep cleanup engine as a
  recommendation source, not final row UX. Sprint 1 safety policy determines
  whether a primary action routes, asks, or can execute after explicit choice.
- Authoritative files and contracts: meeting row/inspector state, cleanup
  recommendation engine/fingerprints/dismissals, processing queue, artifact
  inspection, model/provider readiness, configuration store, and Sprint 6 view
  resolver.
- Implementation slices:
  1. Add pure `MeetingRecommendationResolver` returning exactly one
     `MeetingPrimaryRecommendation`: stable kind, label, reason, severity,
     action target, eligibility, block reason, snapshot fingerprint/version,
     evaluated time, and `NoActionNeeded`/`Evaluating`/`Blocked` alternatives.
     Inputs are bounded metadata only; null/unknown state cannot be treated as
     healthy or eligible.
  2. Implement deterministic priority among *eligible and actionable* work:
     failed or missing transcript with recoverable source; required local setup
     for the selected recovery; suspicious speaker labeling; missing transcript
     artifact; processing/rush decision; reviewed cleanup recommendation;
     summary retry; metadata polish. A non-actionable higher issue becomes
     `Blocked` with exact reason/remedy rather than falling through to lower-
     priority polish. Optional summary/provider readiness never outranks normal
     recording/transcript health.
  3. Define action safety: recommendations may navigate, open review, or invoke
     an already explicit action. They never bypass confirmation for archive,
     delete, reprocessing, interruption, hosted transfer, or credential change.
     If a source/artifact/readiness precondition is stale, disabled, or missing,
     show `Check again`/exact setup route, not a promised action that will fail.
  4. Replace row/detail-local badge selection with one resolved state shared by
     Meetings row, context menu enablement, detail header, and action group.
     Additional cleanup items remain under Review, not competing primary calls
     to action. Healthy output uses `Complete — no action needed`; evaluating or
     refresh-failed output names freshness and never asserts completion.
  5. Make dismissal bounded and honest. Store only recommendation fingerprint,
     version, and timestamp; dismissal hides the same low/medium-risk state but
     expires/reappears when material input or rule version changes. It cannot
     hide failure, required repair, or a newly higher-severity recommendation.
     Preserve existing config pruning and never record meeting content.
  6. Add resolver tests for priority ties, unavailable setup, missing source,
     stale snapshots, disabled action, healthy, no data, dismissal/reappearance,
     stable fingerprints, and row-detail/menu parity. Add source tests that no
     transcript text/provider call enters resolution and each row exposes at
     most one primary action.
- Tests and rendered checks: run focused cleanup/recommendation/config/row-state
  tests. Render failed transcript, missing local model, suspicious labels,
  queued work, cleanup review, summary retry, healthy row, stale refresh, and
  dismissed/reappeared state. Verify one action/reason per row, blocked remedy,
  keyboard use, and no destructive action without confirmation.
- Documentation / installer / release work: update user-facing recommendation
  copy only after ship. Rebuild installer and package smoke for shipped UI/code;
  no package work for this planning record.
- Evidence and date: 2026-09-27 implemented `MeetingRecommendationResolver` with
  typed metadata availability/freshness, deterministic recovery-to-metadata
  priority, stable fingerprints, versioned 30-day bounded dismissals, stale and
  blocked output, and no transcript/provider input. Meetings rows now render one
  visible Next step; row action and context menu only navigate, open Review, or
  refresh; inspector/detail badges consume the same resolved state. Existing
  cleanup fingerprints remain compatible with persisted dismissals. Focused
  resolver/config/cleanup/interaction/XAML suite passed: 276 tests.
- Remaining gap or next action: rendered WPF keyboard/context-menu, focus,
  screen-reader, and DPI verification remains unrun because the user-owned app
  currently holds the recording mutex. Installer rebuild/package smoke remains
  deferred because unrelated release inputs are dirty; Sprint 8 can now centralize
  the full action catalog over this recommendation state.

Goal: stop presenting every meeting action as equally important.

Workstream 1 - Recommendation ranking:

- Rank one primary recommended action per meeting from existing meeting state,
  cleanup recommendations, processing state, transcript availability, summary
  state, speaker-label state, model readiness, and artifact availability.
- Suggested priority should prefer concrete recovery over polish: failed
  transcript, missing setup, suspicious speaker labels, missing transcript,
  processing/rush decision, cleanup recommendation, summary retry, metadata
  polish.

Workstream 2 - Explanation:

- Show a short reason for each recommendation.
- Show healthy meetings as complete or no action needed instead of empty.
- Make row and detail-window recommendations consistent.

Workstream 3 - Eligibility:

- Disable or omit primary actions that cannot currently succeed.
- If an action is blocked, show the setup or artifact reason and route to the
  exact remedy.

Sprint 7 acceptance criteria:

- Each meeting row has at most one primary action.
- Every recommendation is deterministic and testable from metadata-only inputs.
- Healthy rows do not feel broken.
- Row, detail, and menu consume identical resolved state; dismissal cannot hide
  failure or a materially changed recommendation, and stale/unknown data cannot
  claim a meeting is complete.

## Sprint 8: Meetings Action Grouping

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 source audit); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 implementation: catalog-owned action
  eligibility, grouped menu/action-strip presentation, and focused verification).
- User outcome: people find each action by outcome, see why a bulk action is or
  is not available, and never confuse recoverable organization with deletion.
- Scope / non-goals: regroup existing actions and eligibility presentation. Do
  not broaden action authority, remove a working route, auto-apply a
  recommendation, change archive/delete execution, or merge bulk and single
  action semantics.
- Dependencies and decisions: consume Sprint 7 primary recommendation and
  existing selection state. The same action catalog owns context menus,
  selection strip, detail window, keyboard routes, and Help links; a surface may
  hide an irrelevant action but cannot change its eligibility/safety meaning.
- Authoritative files and contracts: current context menu, selected-meeting
  commands, detail events, action-state models, cleanup execution/confirmation,
  queue behavior, and Sprint 1 safety policy.
- Implementation slices:
  1. Add metadata-only `MeetingActionCatalog` entries: stable id, family
     (`Open`, `Fix`, `Organize`, `Processing`, `Danger`), selection cardinality,
     eligibility evaluator, confirmation policy, outcome target, and accessible
     label. Map every existing action; no control is silently dropped.
  2. Resolve eligibility once from selected rows and busy state. Single actions
     require exactly one eligible row; bulk actions show selected, eligible, and
     blocked counts plus first blocked reason before invocation. Preset/search
     changes invalidate selection before any action can run.
  3. Render compact family groups consistently: Open artifacts first; Fix routes
     recommendations/retry/retranscribe/speaker/split; Organize contains
     rename/project/archive; Processing contains ASAP/rush; Danger contains only
     permanent delete. Bulk strip appears only for meaningful multi-select and
     never copies a single-row maintenance editor.
  4. Archive remains a recoverable Organize action and distinct from cleanup
     recommendations; permanent delete is isolated, typed/explicitly confirmed,
     never recommended/automatic, and reports every selected row result.
     Partial bulk failure reports succeeded, failed, skipped, and retryable rows
     without hiding completed work.
  5. Test catalog completeness, cross-surface parity, action eligibility,
     selection invalidation, busy-state disablement, confirmation requirements,
     and mixed-result bulk reporting. Render no/single/multi/mixed eligibility
     and keyboard/context-menu flows at desktop and 125% DPI.
- Tests and rendered checks: execute the slice 5 catalog/eligibility/parity and mixed-result tests; inspect the named selection and context-menu states at the specified DPI before accepting a layout change.
- Documentation / installer / release work: update action terminology only after
  ship; rebuild installer and smoke shipped UI. No package work for this plan
  record.
- Evidence and date: 2026-09-27 implemented metadata-only
  `MeetingActionCatalog` covering every existing action id, family, cardinality,
  confirmation policy, outcome, accessible label, artifact/setup/busy blocks,
  and mixed bulk eligibility counts. Context-menu and Meetings action-strip
  controls now use the same catalog state; the menu groups Open, Fix, Organize,
  Processing, Danger Zone, and Selected meetings. Bulk recommendations now open
  Review rather than execute. Typed-delete and archive semantics are unchanged.
  Focused catalog/recommendation/cleanup/config/selection/interaction/XAML suite
  passed: 290 tests.
- Remaining gap or next action: rendered WPF keyboard/context-menu, focus, and
  125% DPI checks remain unrun because the user-owned app holds the recording
  mutex. Installer rebuild/package smoke remains deferred because unrelated
  release inputs are dirty. Sprint 9 should consume catalog state in its new
  detail task-center layout rather than duplicate eligibility.

Goal: preserve every meeting action while making the action surface coherent.

Workstream 1 - Action families:

- Group actions by intent:
  - `Open`: details, transcript, audio, folder, copy paths.
  - `Fix`: recommended action, retry transcript, re-transcribe, add/repair
    speaker labels, split.
  - `Organize`: rename, suggest title, project, archive.
  - `Processing`: process ASAP, clear ASAP, rush backlog.
  - `Danger Zone`: permanent delete.

Workstream 2 - Surface consistency:

- Apply the same action families to context menus, selection strips, and
  meeting detail controls.
- Keep single-meeting and bulk actions visually distinct.
- Show bulk actions only when multi-selection is active and eligibility is
  meaningful.

Workstream 3 - Guardrails:

- Keep permanent delete isolated and explicitly confirmed.
- Keep archive-first cleanup recommendations separate from permanent delete.
- Preserve per-row outcomes for bulk actions so one failed item does not hide
  successful work.

Sprint 8 acceptance criteria:

- Existing actions remain available.
- Single-meeting and bulk controls no longer compete visually.
- Destructive actions are isolated, explicit, and tested.
- Each action has one catalog owner and identical eligibility/safety semantics on
  every surface; mixed bulk results remain visible per affected meeting.

## Sprint 9: Meeting Detail Task Center

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: detail window, artifacts,
  summary, speaker and maintenance controls); `Ready` (2026-09-27 pressure test);
  `Done` (2026-09-27 implementation: revisioned task center, draft-safe refresh,
  intent sections, and focused verification).
- User outcome: people read a meeting first, see one truthful next action, and
  can reach every repair/organize action without risking unsaved edits or stale
  artifact operations.
- Scope / non-goals: reorganize detail state/presentation. Do not change
  transcript, summary, speaker, archive, delete, or reprocess semantics; detail
  actions consume Sprint 8 catalog and Sprint 7 recommendation state.
- Dependencies and decisions: a detail snapshot has meeting stem, catalog
  revision, artifact availability revision, recommendation revision, and
  freshness. Background refresh may update read-only state, but never overwrite
  pending title/project/speaker/split drafts; it surfaces a reload/conflict
  choice instead.
- Authoritative files and contracts: detail window/state builder, artifact
  catalog, recommendation resolver, action catalog, summary provider state,
  speaker correction/profile services, selection/detail refresh lifecycle, and
  `DESIGN.md`.
- Implementation slices:
  1. Create `MeetingDetailTaskCenterState` from one revisioned metadata snapshot:
     headline/status/freshness, primary recommendation/reason, artifact links,
     Read content, Details facts, Fix actions, Organize actions, and per-action
     eligibility/block reason. Unknown/stale availability disables the action
     and offers refresh/remedy; no button claims an absent file will open.
  2. Make Read default: transcript search/segments and summary share reading
     space; summary unavailable/disabled/provider states never imply transcript
     failure. Artifact shortcuts use friendly availability labels and open only
     confirmed current paths; raw paths and capture diagnostics stay Details or
     Advanced.
  3. Apply Sprint 8 catalog family groups: Fix contains retry/retranscribe,
     summary, labels, split, repair; Organize contains rename/project/archive;
     permanent delete remains isolated and confirmed. Recommended action is one
     focused item near top; additional work stays grouped, never a badge wall.
  4. Separate speaker concepts: anonymous label, person name, profile suggestion,
     and diarization repair. Keep Use/Reject/Apply/Refresh/Undo in Name review;
     Repair Speaker Labels is separate heavyweight work. Missing samples,
     profiles, labels, assets, or setup show exact unavailable reason/route and
     never create a name or training event.
  5. Preserve accessibility and edit safety: keyboard-visible sections, focus
     after navigation, stable scroll during async refresh, aria/accessibility
     names, dense wells/no shadows, and explicit busy/cancel/outcome states.
     Closing archived/deleted detail invalidates the snapshot rather than
     retaining an actionable stale window.
  6. Test state parity with row/menu, stale artifact/recommendation, draft-safe
     refresh/conflict, transcript-only, summary states, speaker unavailable/
     suggestion/undo/repair, archive/delete confirmation, and detail closure
     after mutation. Render normal read, blocked repair, busy, stale, and
     destructive states at 1280×800/125% DPI.
- Tests and rendered checks: execute the slice 6 state, revision, mutation, and speaker-flow tests; inspect every named detail state at 1280×800 and 125% DPI before accepting the reading-first layout.
- Documentation / installer / release work: update detail workflow copy after
  shipped UI. Rebuild installer and smoke package for shipped WPF/runtime work.
- Evidence and date: 2026-09-27 implemented `MeetingDetailTaskCenterState` and
  revision/draft refresh resolver over Sprint 7 recommendation and Sprint 8
  catalog state. The detail header now exposes one primary action, reason, and
  freshness. Transcript/summary remain readable; maintenance now names Organize,
  Fix, Speaker Name Review, recoverable Archive, and an isolated typed-delete
  Danger Zone. A material background revision preserves pending title/project/
  speaker/split drafts and explains the refresh conflict. Focused task-center,
  catalog, recommendation, cleanup, config, interaction, and XAML suite passed:
  294 tests.
- Remaining gap or next action: rendered normal/blocked/busy/stale/destructive
  detail states, keyboard focus, screen reader wording, and 1280x800/125% DPI
  checks remain unrun because the user-owned app holds the recording mutex.
  Installer rebuild/package smoke remains deferred because unrelated release
  inputs are dirty. Sprint 10 can extend the now revisioned detail contract.

Goal: make one-meeting management calm, readable, and complete.

Workstream 1 - Top-level framing:

- Reframe detail around status, recommended action, artifact shortcuts, and
  transcript/summary reading.
- Put the recommended next action and its reason near the top.
- Keep Open Transcript, Open Audio, and Open Folder available without making
  them dominate repair controls.

Workstream 2 - Task sections:

- Organize detail content into `Read`, `Details`, `Fix`, and `Organize`.
- Make transcript and summary reading the default task.
- Keep metadata, attendees, model details, capture diagnostics, and project
  facts under `Details`.
- Keep retry, re-transcribe, speaker labels, summaries, split, and repair under
  `Fix`.
- Keep rename, project, archive, and delete under `Organize`, with delete
  isolated.

Workstream 3 - Speaker clarity:

- Separate anonymous speaker labels, learned speaker names, and repair.
- Keep `Apply Speaker Names`, `Refresh Suggestions`, `Undo Name Recognition`,
  and profile-suggestion review together.
- Keep `Repair Speaker Labels` as the heavier diarization repair path.
- Route missing samples, missing profiles, missing labels, and setup gaps to
  clear unavailable states.

Sprint 9 acceptance criteria:

- Users can read a meeting without being surrounded by maintenance controls.
- Every existing maintenance action remains available from an intent section.
- Speaker-name learning, speaker-label repair, transcript retry, and
  re-transcription are clearly distinct.
- Detail refresh cannot overwrite a pending user draft or leave stale artifact/
  action routes enabled after the meeting changes, archives, or deletes.

## Sprint 10: Backlog And Recovery Simplification

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: queue, rush, recovery,
  persisted backlog, ETA mechanics); `Ready` (2026-09-27 pressure test);
  `Done` (2026-09-27 implementation: metadata-only backlog resolver, guided
  recovery review routes, stale-status fence, and capture-safe rush handling).
- User outcome: users see whether work is idle, processing, paused, setup-
  blocked, decision-blocked, or failed; every non-terminal meeting has one safe
  next step or an honest unrecoverable reason.
- Scope / non-goals: translate queue/recovery state and route existing actions.
  Do not change scheduling/worker priority, invent ETA, auto-retry irreversible
  work, terminate arbitrary processes, or interrupt active recording.
- Dependencies and decisions: reuse queue snapshot, persisted manifest state,
  recovery records, and Sprint 7 recommendations. Queue snapshots carry update
  time and stage; stale/unknown data shows `Status needs refresh`/`ETA
  unavailable`, never Idle or zero work.
- Implementation slices:
  1. Add pure `BacklogExperienceResolver` mapping snapshots to `Idle`,
     `Processing`, `Paused`, `NeedsSetup`, `NeedsDecision`, `Failed`, or
     `RefreshRequired`, with current item, user-facing stage, elapsed, current/
     overall ETA confidence, count, and exact remedy target.
  2. Define stage copy: transcript publication, speaker labels, summary, and
     cleanup; expose raw worker/runtime/process details only in Advanced/Help.
     ETA uses measured stage estimate only when fresh/sample-supported; otherwise
     state why unavailable. Persisted backlog cannot overwrite live truth.
  3. Route recovery to explicit outcome actions: Publish transcript first, run
     labels later, retry transcript, open setup, process this first, inspect
     source, or explain unrecoverable source. Preserve cause/failure timestamp
     and retry eligibility without logs/transcript text in normal UI.
  4. Make ASAP/Rush wording explain scope, deferred labels, current-stage
     interruption, and future-work impact before confirmation. Never interrupt
     recording; worker interruption is limited to verified app-owned worker
     paths/stages and requeues/reuses safe prior output.
  5. Test resolver states, stale snapshot, live-versus-persisted disagreement,
     ETA confidence, every failed recovery route, rush/ASAP confirmation,
     recording protection, worker ownership, and metadata-only status. Render
     each state with keyboard/accessible status at 1280×800/125% DPI.
- Tests and rendered checks: execute the slice 5 resolver/recovery/non-interruption tests; inspect every queue state with keyboard and accessible status at 1280×800 and 125% DPI.
- Documentation / installer / release work: document queue state/copy after
  ship; rebuild installer and smoke packaged worker/UI when behavior ships.
- Evidence and date: 2026-09-27 implemented `BacklogExperienceResolver` over
  live queue, persisted backlog, and Sprint 7 recovery metadata. It emits
  `Idle`, `Processing`, `Paused`, `NeedsSetup`, `NeedsDecision`, `Failed`, or
  `RefreshRequired`, friendly stage/elapsed/ETA confidence, and review-only
  recovery routes without worker messages, paths, logs, or transcript content.
  The header/Meetings strip consume that state and provide one safe route to
  refresh, setup, or the relevant detail. Persisted work no longer claims live
  queue truth. ASAP/Rush copy now states scope and safety; code permits direct
  worker interruption only for app-owned active speaker labeling while no live
  recording is active, never capture or transcription. Focused backlog,
  queue, recommendation, action-catalog, detail, and XAML tests passed: 252.
- Remaining gap or next action: rendered 1280x800/125% keyboard and
  screen-reader checks remain unrun because the user-owned app holds the
  recording mutex. Installer rebuild/package smoke remains deferred because
  unrelated release inputs are dirty. Sprint 10A is the next chronological
  ready sprint.

Goal: make failed or queued work understandable without manifest knowledge.

Workstream 1 - Queue status language:

- Summarize queue state as `Idle`, `Processing`, `Paused`, `Needs setup`, or
  `Needs decision`.
- Keep detailed worker/runtime diagnostics in Help or Advanced.
- Show current item, stage, elapsed time, current ETA, and overall ETA when
  available.

Workstream 2 - Guided recovery:

- Replace raw mechanics with guided actions such as `Publish transcript first`,
  `Run speaker labels later`, `Retry failed transcript`, and
  `Process this first`.
- Ensure every failed or queued meeting has a visible next step or a clear
  "cannot recover because..." reason.
- Preserve existing ASAP and Rush Backlog behavior but present it as intent
  rather than worker mechanics.

Workstream 3 - Safety:

- Do not interrupt active recording without explicit user action.
- Keep worker interruption bounded to app-owned worker paths that avoid
  process-tree security prompts.
- Keep recovery logs metadata-only.

Sprint 10 acceptance criteria:

- Backlog management no longer requires understanding manifests or worker
  internals.
- Failed sessions are visible, explainable, and routed to the right remedy.
- Stale, unknown, or insufficient ETA data never claims idle/completion; queue
  acceleration and recovery stay explicit and cannot interrupt active capture.

## Sprint 10A: ASAP Priority Carries Through Speaker Labeling

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: persisted one-meeting
  request, restart normalization, queue-front selection, preemption, and
  transcript-first backlog rush); `Ready` (2026-09-27 pressure test); `Done`
  (2026-09-27 implementation and focused verification).
- User outcome: marking one meeting `Process ASAP` stays meaningful until its
  transcript and every eligible speaker-labeling pass reach a terminal outcome;
  users can see why priority remains and clear only that priority.
- Scope / non-goals: make single-meeting ASAP a persisted lifecycle contract
  across transcription, publish, and eligible diarization. Do not alter worker
  CPU priority, turn `Rush Backlog` into per-meeting ASAP, promise labels for
  ineligible audio, interrupt live capture, or auto-retry non-recoverable work.
- Dependencies and decisions: retain one persisted `RushProcessingRequest` per
  profile. Define a pure `AsapLifecycleState` from current manifest stages and
  overrides: `TranscriptPending`, `Publishing`, `SpeakerLabelsPending`,
  `Complete`, `Ineligible`, or `TerminalFailure`. `Skipped` speaker labels are
  complete only when intentionally skipped and no forced/eligible continuation
  remains; unavailable setup/audio is explicit `Ineligible`, never success.
- Authoritative files and contracts:
  `ProcessingQueueService`, `AppConfig.RushProcessingRequest`,
  `MeetingSessionManifest` stage statuses and processing overrides,
  `ProcessingQueueStatusSnapshot`, `MainWindow`, `MeetingDetailWindow`, and
  their interaction/state logic. Manifest path remains identity only while it
  exists; restart normalization must not erase a request before lifecycle state
  has been evaluated from durable manifest data.
- Implementation slices:
  1. Extract pure lifecycle evaluator plus terminal-reason/result types. Cover
     missing/corrupt manifest, queued/running/failed stages, deferred labels,
     `SkipSpeakerLabeling`, `ForceSpeakerLabeling`, completed labels, and
     recoverable versus terminal failure. Use it for normalization and every
     completion path; persist neither derived display state nor duplicate queue
     entries.
  2. Change queue selection/requeue to reserve selected ASAP meeting through
     label continuation. After transcript publish, enqueue or retain only its
     eligible label pass ahead of ordinary items; preemption must preserve safe
     transcript output, reset only interrupted stages, and never start work
     during a protected live recording unless existing explicit bypass applies.
  3. Define clear/cancel behavior: `Clear ASAP` removes only matching request,
     releases future transcript/label priority without canceling current work,
     and refreshes all status surfaces. Clear automatically only for
     `Complete`, `Ineligible`, or terminal failure; retain through recoverable
     worker crash/requeue and restart.
  4. Preserve `Rush Backlog`: it can add `SkipSpeakerLabeling` to backlog and
     change future background mode, but cannot create, clear, or inherit a
     single-meeting request. An ASAP meeting explicitly eligible for labels
     must not silently lose priority because backlog drain ran; resolve any
     conflict by documented per-meeting override before worker launch.
  5. Add one metadata-only presentation resolver shared by queue strip, row,
     selection/context menu, detail, and activity. Say `ASAP: transcript`,
     `ASAP: publishing`, `ASAP: speaker labels remain`, or precise unavailable/
     completed reason. Keep raw paths, worker errors, and audio/transcript
     content out of ordinary status. Buttons expose `Clear ASAP` only for
     matching request and explain unavailable label action without false
     completion.
  6. Add migration and recovery boundary: older config request loads without
     new fields; missing/corrupt manifest clears safely with visible refresh
     reason; manifest rewrite/restart preserves valid request. Assert queue
     deduplication, reserved-item cleanup, bounded fairness after ASAP ends,
     and no effect on unrelated meetings.
- Tests and rendered checks: extend `ProcessingQueueServiceTests` for
  transcript-to-label carry-through, restart in each lifecycle state,
  interruption between stages, clear during each stage, deferred-label and
  force-label conflicts, ineligible/missing/corrupt manifest, recoverable and
  terminal failure, deduplication, and unchanged `RushBacklog` behavior. Add
  pure evaluator/presentation tests plus `MainWindowInteractionLogicTests` and
  detail-state tests for exact status/action parity. Render queue, selection,
  context menu, and detail at 1280x800/125% DPI; keyboard-focus `Clear ASAP`
  and verify accessible status updates with screen reader tooling.
- Documentation / installer / release work: after behavior ships, update
  `README.md` or `SETUP.md` with single-meeting versus backlog-rush semantics,
  label eligibility, clear behavior, and recording protection. Rebuild
  installer, run packaged worker/UI smoke, then required release checks for
  shipped runtime/UI changes.
- Evidence and date: 2026-09-27 implemented the pure
  `AsapLifecycleResolver` and routed durable request normalization and
  completion through it. ASAP now survives transcript publishing into eligible
  speaker labeling, overrides transcript-first backlog deferral only for its
  own manifest, and clears only for complete, unavailable, or terminal states.
  Clearing a queued request returns it to ordinary fairness without cancelling
  a reserved or active pass. Queue strip, row, detail, and activity consume
  metadata-only lifecycle copy. Focused lifecycle, queue, interaction, action,
  detail, and XAML tests passed: 249.
- Remaining gap or next action: rendered 1280x800/125% keyboard and
  screen-reader checks remain unrun because the user-owned app holds the
  recording mutex. Installer rebuild/package smoke remains deferred because
  unrelated release inputs are dirty. Sprint 11 is the next chronological
  ready sprint.

Goal: make `Process ASAP` mean the selected meeting stays first until its
transcript and eligible speaker-labeling work are both handled.

Current behavior to preserve:

- `Process This ASAP...` marks one queued or processing meeting through the
  existing `RushProcessingRequest`.
- The user can clear the ASAP request from the Meetings selection, context
  menu, or meeting detail.
- `Rush Backlog` remains a separate transcript-first action that can defer
  speaker labels for many queued items.

Workstream 1 - Priority contract:

- Define single-meeting ASAP as applying to the full processing lifecycle for
  that meeting: transcription, publish, and optional diarization/speaker
  labeling when speaker labeling is configured and the meeting is eligible.
- Do not clear the rush request merely because transcription or primary publish
  finished if the same meeting still has pending or requeued speaker-labeling
  work.
- Clear the rush request only when the meeting has no remaining eligible
  transcript or speaker-labeling work, the user clears it, the manifest becomes
  ineligible, or the meeting fails in a non-recoverable state.

Workstream 2 - Queue behavior:

- Keep the ASAP meeting ahead of ordinary backlog items when it transitions
  from transcript-first processing into a later diarization/speaker-labeling
  pass.
- If the ASAP meeting is interrupted after transcription but before
  diarization, requeue its speaker-labeling continuation ahead of non-ASAP
  work.
- Preserve live-recording pause behavior unless the user chose the existing
  pause-bypass ASAP option.
- Do not change `Rush Backlog` semantics; backlog rushing may still defer
  speaker labels to publish transcripts sooner.

Workstream 3 - UI and status:

- Update queue strip, meeting row, detail window, and activity text to say when
  an ASAP meeting is still priority because speaker labeling remains.
- Make `Clear ASAP` clear both transcript and speaker-labeling priority for
  that meeting.
- Show a clear unavailable state when speaker labeling cannot run because setup
  is missing, the meeting lacks usable audio/transcript inputs, or labels have
  already completed.

Workstream 4 - Tests and docs:

- Add `ProcessingQueueServiceTests` for a persisted ASAP request carrying from
  transcription into deferred speaker labeling.
- Add tests proving ASAP is cleared after all eligible work completes and is
  not cleared immediately after transcript publish when diarization remains.
- Add tests proving `Rush Backlog` still defers speaker labels and does not
  inherit the single-meeting ASAP carry-through behavior.
- Update `MainWindowInteractionLogicTests` for queue/detail text that mentions
  an ASAP speaker-labeling continuation.
- Update `README.md` or `SETUP.md` only if the user-facing ASAP behavior
  changes visibly.

Sprint 10A acceptance criteria:

- Marking one meeting ASAP keeps that meeting ahead of ordinary backlog through
  eligible diarization/speaker-labeling work.
- Clearing ASAP removes the priority from both transcript and
  speaker-labeling continuation work.
- Rush Backlog remains transcript-first and can still defer speaker labels.
- Status text explains why the meeting is still marked ASAP after transcript
  publication when speaker labels remain.

## Sprint 11: Safe Automation Layer

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: full-refresh cleanup
  scan, persisted fingerprint ledger, bounded batch planner, queue-priority
  dispatch, failure suppression, and source/unit tests); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 implementation and focused verification).
- User outcome: app quietly removes only explicitly authorized, reversible or
  safely recoverable chores; users can tell what last ran, what is waiting, and
  why work is not automatic without reading logs or losing manual control.
- Scope / non-goals: coordinate safe catalog/readiness/attendee/update/provider
  refresh and cleanup dispatch with durable receipts. Do not automate permanent
  delete, consent changes, external sharing, model/provider installation,
  irreversible transcript edits, or actions outside policy allowlist. Do not
  expose diagnostic paths, provider secrets, transcript/audio content, or raw
  exception text in normal status.
- Dependencies and decisions: consume Sprint 1 control/consent policy, Sprint
  7 recommendation metadata, Sprint 10 queue/recovery state, and explicit
  `IncrementalWorkPlan` choices. Automation is opt-in for each action family;
  user manual action always wins. Safe action classification stays centralized
  in `MeetingCleanupRecommendationEngine` plus
  `MainWindowInteractionLogic.IsSafeMeetingCleanupRecommendation`; change to
  allowlist requires named user harm analysis, recovery path, and tests.
- Authoritative files and contracts: `MainWindow` refresh lifecycle,
  `MeetingCleanupAutoApplyPlanner`, `MeetingCleanupWorkLedgerService`,
  `MeetingCleanupRecommendationBatchRunner`, processing queue completion
  events, `AppConfig.IncrementalWorkPlan`, recommendation fingerprint schema,
  and UI refresh/status state. Ledger distinguishes discovery, dispatched,
  processing, completed, failed, and manual-review; queue acceptance is never
  completion.
- Implementation slices:
  1. Introduce immutable `AutomationCatalogSnapshot` containing refresh mode,
     version, completed-at, input revision/fingerprints, policy revision, and
     cancellation token identity. Dispatch only a current successful `Full`
     snapshot after baseline and cleanup scans publish; fast, selection-only,
     stale, canceled, failed, or partial scans may refresh UI but cannot dispatch.
  2. Centralize scheduler state as a pure coordinator: idle, scanning,
     waiting for recording/queue/provider/user action/backoff, dispatching,
     awaiting worker completion, and degraded. Apply one per-family interval,
     one in-flight catalog scan, bounded low-pressure allowance, and
     cancellation/replacement ownership. Refreshes stay metadata-only and do
     not overlap a user edit or active capture.
  3. Define durable receipt transition rules keyed by fingerprint plus action,
     affected stems, and input revision. Write `Queued` before queue dispatch;
     record processing/completion from matching worker event; persist failed
     reason in support log/advanced state. Restart reconciles only verifiable
     manifest/output evidence. Failed, queued, processing, completed, dismissed,
     and manual-review entries suppress automatic replay until recommendation
     fingerprint changes; a changed fingerprint becomes eligible.
  4. Execute deterministic batch order with maximum count/time budget and
     independent item results. Revalidate recommendation and policy immediately
     before each side effect; stop safely on shutdown, recording start, stale
     snapshot, or authority revocation. Continue unrelated safe items after one
     failure, refresh catalog once per completed batch, and avoid endless
     refresh-dispatch loops.
  5. Keep manual controls independent of suppression while preserving all
     validation and confirmation rules. `Apply Safe Fixes` reports dispatched,
     completed, failed, skipped, and still-manual-review separately; never
     mark historical review complete solely because automation ran. Archive/
     merge only execute when current classifier proves existing recoverability
     contract; otherwise downgrade to manual review.
  6. Add concise shared status resolver for Home, Meetings, cleanup tray, and
     activity: live, cached, static expected, fallback, or unavailable source;
     last successful scan, waiting reason, and bounded counts. Use accessible
     non-spam announcements; ordinary UI never claims a queued job completed.
- Tests and rendered checks: add deterministic fake clock/catalog/executor and
  worker-event integration tests for full-versus-fast/selection/stale refresh,
  scan cancellation, duplicate triggers, overlapping timers, restart recovery,
  snapshot mutation before dispatch, backoff, recording/user-action/shutdown
  races, per-action policy revocation, one failure plus later success, exactly
  one post-batch refresh, fingerprint change, manual bypass, and permanent
  delete exclusion. Extend ledger/planner tests for atomic transition and
  corrupt-file recovery. Render low-pressure, blocked, mixed-result, cached,
  and unavailable states at 1280x800/125% DPI; keyboard and screen-reader test
  status changes without repeated announcements.
- Documentation / installer / release work: document enabled automation action
  families, scope, status meanings, recovery/manual-review route, storage, and
  disable controls after shipment. Rebuild installer and run packaged startup,
  queue/worker, and UI smoke for runtime/UI behavior; run full relevant tests
  before release.
- Evidence and date: 2026-09-27 added immutable, metadata-only
  `AutomationCatalogSnapshot` provenance for complete meeting scans and a pure
  coordinator that rejects fast, stale, canceled, recording, user-action,
  backoff, provider, and queue-pressure dispatches. Cleanup dispatch now
  carries cancellation identity, policy/input revision, and fingerprints;
  every item rechecks snapshot and policy immediately before its side effect.
  The durable ledger records action, affected meeting identity, and revision
  before queue-style dispatch, preserves worker completion truth, and treats a
  revoked item as skipped rather than completed. Focused automation, ledger,
  planner, queue, interaction, action, detail, and XAML tests passed: 283.
- Remaining gap or next action: rendered 1280x800/125% keyboard and
  screen-reader checks remain unrun because the user-owned app holds the
  recording mutex. Installer rebuild/package smoke remains deferred because
  unrelated release inputs are dirty. Sprint 12 is the next chronological
  ready sprint.

Goal: let the app remove chores without taking risky action.

Workstream 1 - Automated refreshes:

- Auto-refresh meeting catalog, setup readiness, cleanup recommendations,
  attendee backfill, update status, and provider status when safe.
- Keep refreshes bounded, non-overlapping, metadata-only, and visible in status
  text.
- Avoid background churn while recording or while the machine is under obvious
  processing pressure.

Workstream 2 - Safe automatic maintenance:

- Run automatic cleanup only after a current full Meetings cleanup refresh,
  never after fast refreshes or selection-only UI changes.
- Keep `MeetingCleanupRecommendationEngine` and
  `MainWindowInteractionLogic.IsSafeMeetingCleanupRecommendation` as the
  authority for safe classification. Current automatic safe-fix types are
  `Archive`, `Merge`, `Retry Transcript`, `Add Speaker Labels`, and
  `Repair Speaker Labels`; permanent delete remains manual and excluded.
- Start a bounded automatic batch only when the app is not shutting down, no
  other meeting action or automatic batch is active, and at least one current
  recommendation remains eligible.
- Execute recommendations independently so one failure does not abort the
  remaining safe batch, then refresh the meeting list once after completion.
- Persist automatic failure state by recommendation fingerprint. Skip later
  automatic attempts for the same failed fingerprint until it changes, while
  leaving the recommendation visible for review.
- Suppress successfully dispatched transcript and speaker-label queue actions
  while the same fingerprint remains current so refreshes cannot enqueue the
  same work repeatedly.
- Keep manual `Apply Safe Fixes` and selected-recommendation actions available
  even when automatic execution is suppressed for that fingerprint.
- Do not mark historical cleanup review complete merely because automation ran.
  Keep review-first suggestions visible and report concise applied/manual-review
  counts in product status or activity text.

Workstream 3 - Trust:

- Label whether a surface is showing live output, cached output, static
  expected output, fallback output, or no backend call.
- Never expose internal debug, provenance, or evidence chrome on normal
  customer-facing surfaces unless it is intentionally part of the experience.

Sprint 11 acceptance criteria:

- Full cleanup refreshes automatically drain eligible safe fixes; fast and
  selection-only refreshes do not.
- Mixed batches continue after one item fails and refresh the catalog once.
- Unchanged failed or already-dispatched queue recommendations do not enter
  continuous automatic retry loops; changed fingerprints become eligible.
- Manual actions bypass automatic suppression, permanent delete remains manual,
  and non-safe recommendations remain visible.
- Automated work is visible, bounded, non-overlapping, and does not falsely
  complete historical cleanup review.

## Sprint 12: Summary And Hosted AI Trust Flow

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: persisted generation
  preference, ModelProxy/OpenAI provider candidates, protected external secret
  store, synthetic validation, chunking, generated-summary provider metadata,
  and focused tests); `Ready` (2026-09-27 pressure test); `Done` (2026-09-27
  implementation and focused verification).
- User outcome: users choose one understandable summary route, can enable a
  working local route with minimum ceremony, and knowingly authorize any route
  that may send transcript text outside their device. Every generated summary
  identifies route/fallback without repeating warnings during normal reading.
- Scope / non-goals: simplify summary controls, readiness, consent, and
  provenance around existing summarization. Do not change transcript content,
  silently switch provider after consent changes, transmit real content during
  validation/model discovery, log keys/transcript/chunks, promise cost/latency,
  or treat a loopback endpoint as proof that downstream processing is local.
- Dependencies and decisions: reuse Sprint 1 privacy/control ownership and
  hosted-data policy. Map editor values to pure `SummaryExperienceMode`:
  `Off`, `LocalRoute`, `LocalThenHostedFallback`, or `HostedRoute`. Persist
  canonical underlying config plus versioned hosted-route consent; mode is
  derived, never a second conflicting source. Consent names data class
  (published transcript text), route/provider, fallback behavior, and policy
  version; selecting/saving a hosted-capable route without valid consent cannot
  enable generation.
- Authoritative files and contracts: `AppConfig` summary settings and
  migrations, `ISummarySecretStore`, `MeetingSummarizationProvider`,
  `SummaryProviderValidationService`, `SummaryChatProviderOptions`,
  `PublishedMeetingSummaryService`, `MainWindow` Settings/detail state, and
  `MeetingSummaryProviderInfo`. Secrets remain out of app config and UI/logs;
  summary route/provenance has no secret value.
- Implementation slices:
  1. Add pure mode projector/input validator from generation toggle,
     provider preference, secure-key availability, endpoint classification,
     validation freshness, and consent. Reject impossible states (hosted-only
     without key/consent; fallback without consent) with exact repair action;
     retain legacy preference migration without auto-opting users into hosted
     transcript processing.
  2. Build small Settings flow: choose mode, configure only relevant advanced
     fields, run synthetic validation, then save. For hosted or fallback mode,
     show one pre-save boundary dialog with provider, data sent, fallback timing,
     key storage/removal, and opt-out; cancel leaves config/key/drafts unchanged.
     Clearing a key or changing to local/off revokes future hosted use but does
     not silently delete existing local/published summaries.
  3. Model readiness separately for local gateway, hosted credential, and
     selected route. Classify endpoint as local transport, hosted direct, or
     unknown routing. UI may say `on-device` only with verifiable processing
     locality; a `127.0.0.1` gateway with unknown downstream routing is labeled
     accurately. Validation/model catalog calls use synthetic metadata/prompt,
     bounded timeout/cancellation, no web search, and never enqueue meeting
     generation.
  4. Create a route authorization gate immediately before every summary
     request, including background cleanup. It snapshots current consent,
     preference, provider readiness, transcript fingerprint, and cancellation
     epoch; recheck after any long local failure before hosted fallback. Invalid
     or revoked authorization yields a local actionable skipped state, not a
     provider attempt. Chunking/retry honors same snapshot and cannot mix
     providers mid-summary without recorded authorized fallback.
  5. Persist sanitized provenance with generated result: requested mode,
     actual route/provider/model, fallback-used flag, policy/consent version,
     transcript fingerprint, timestamps, and safe result category. Detail list
     shows short provenance once; settings/status distinguish live, cached,
     stale, validation-failed, unavailable, or no backend call. Never render
     raw endpoint diagnostics or provider exception text outside Advanced/Help.
  6. Define failure/retry behavior: no key, unreachable local route, denied
     fallback, hosted failure, malformed response, changed transcript, canceled
     task, and stale summary. Retry uses current authorization and creates a
     new result only for current published fingerprint; existing summary stays
     visible with its old provenance until replacement succeeds.
- Tests and rendered checks: add pure projector/consent-gate tests for every
  mode, legacy migration, cancel/no-write, key removal, revoked consent,
  unavailable local route, approved/denied fallback, unknown gateway routing,
  and stale validation. Add fake HTTP/secret-store integration tests asserting
  synthetic validation payloads contain no meeting content and only approved
  route receives transcript chunks; test cancellation, timeout, chunk retry,
  provider failure, summary fingerprint race, and background generation. Extend
  config/secret/provenance tests. Render Settings and detail states at
  1280x800/125% DPI; keyboard/screen-reader test dialog, mode selection,
  warning, blocked action, provenance, and Advanced disclosure.
- Documentation / installer / release work: update `README.md`/`SETUP.md` with
  four modes, local-gateway versus processing-locality distinction, hosted data
  boundary, consent/revocation, secret location, validation limits, and summary
  provenance. Rebuild installer and smoke local/hosted-disabled startup plus
  no-key/offline paths when shipped.
- Evidence and date: 2026-09-27 added a pure summary experience projection
  over the existing four modes and a versioned hosted-route consent policy.
  Hosted-only generation now skips before provider resolution without current
  consent; local-with-hosted-fallback remains local-only after consent is
  revoked, including after a local failure. Settings now asks once before it
  saves a hosted-capable route and revokes future hosted use when changed to
  local-only or off. The provider gate, config normalization, protected secret
  path, synthetic-validation boundary, and docs were covered by 146 focused
  tests.
- Remaining gap or next action: rendered 1280x800/125% keyboard and
  screen-reader checks remain unrun because the user-owned app holds the
  recording mutex. Installer rebuild/package smoke remains deferred because
  unrelated release inputs are dirty. Sprint 13 is the next chronological
  ready sprint.

Goal: make summaries simple to enable while making hosted transcript boundaries
unmistakable.

Workstream 1 - Summary modes:

- Combine summary enablement and provider preference into
  `SummaryExperienceMode`.
- Keep local-only summaries easy to enable when ModelProxy is reachable.
- Keep local-with-hosted-fallback and hosted-only paths explicit.

Workstream 2 - Provider readiness:

- Show local ModelProxy status separately from hosted OpenAI readiness.
- Keep validation prompts synthetic and never use real meeting content.
- Keep timeout, chunking, provider URL, model names, and provider internals in
  advanced summary details.

Workstream 3 - Privacy:

- Show hosted transcript boundary copy at the point where a user enables
  hosted fallback or saves a hosted key.
- Do not repeat long warnings on every summary display once the user's provider
  choice is explicit and saved.

Sprint 12 acceptance criteria:

- Local summary setup is simple.
- Hosted fallback remains an explicit privacy/cost decision.
- Advanced provider controls remain available.

## Sprint 13: Speaker Labels And Speaker Names UX

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: diarization queue/repair,
  local voice-profile store/matcher, correction/reject/refresh/undo services,
  profile settings, provenance text, and focused tests); `Ready` (2026-09-27
  pressure test); `Done` (2026-09-27 implementation: resolver, safe review
  transaction, local profile controls, terminology, docs, and focused tests).
- User outcome: users know whether they are adding anonymous diarization labels,
  reviewing a local name suggestion, changing a meeting display name, or fixing
  bad diarization; every action has safe scope and a recoverable outcome.
- Scope / non-goals: unify vocabulary, state, routing, and local profile
  controls across Meetings, detail, and Settings. Do not change diarization
  algorithms/thresholds, upload embeddings or audio, silently merge people,
  rewrite user-confirmed names after a profile change, or expose profile vectors
  and raw audio paths in ordinary UI.
- Dependencies and decisions: preserve Sprint 1 local-data/consent policy and
  Sprint 10/10A queue state. Define distinct durable concepts: `Diarization
  Label` (anonymous meeting-local cluster), `Meeting Display Name` (user or
  attributed name for that label), `Voice Profile` (local reusable embedding
  record), and `Name Suggestion` (non-authoritative profile match). UI calls
  each concept by that name; original label, source, confidence, reason,
  profile ID, artifact revision, and user-edit authority remain machine data.
- Authoritative files and contracts: `MeetingSessionManifest` processing
  metadata/voice samples, `TranscriptSpeakerAttributionService`,
  `SpeakerNameCorrectionService`, `SpeakerNameLearningService`,
  `VoiceProfileStore`/matcher, `MeetingOutputCatalogService`,
  `MainWindowInteractionLogic`, detail/editor rows, and Settings profile
  controls. Voice profiles stay only under configured local app data; published
  transcript/artifact metadata never stores raw embedding vectors.
- Implementation slices:
  1. Add pure `SpeakerExperienceState` resolver for every selected meeting:
     `LabelsMissing`, `LabelsQueued`, `LabelsRunning`, `LabelsReady`,
     `LabelsSuspicious`, `NamesReadyForReview`, `LearningDisabled`,
     `ProfilesUnavailable`, `SamplesUnavailable`, `RepairIneligible`, and
     `RefreshRequired`. Return one plain-language explanation, surface-specific
     permitted actions, and no ambiguous generic `speaker labels` command.
  2. Split controls into three stable action families. Meeting processing offers
     `Add Speaker Labels` or `Repair Speaker Labels` only when source/model/
     stage eligibility allows. Speaker-name review offers `Use suggestion`,
     `Reject suggestion`, `Apply Name Changes`, `Refresh Local Suggestions`,
     and `Undo Profile Names`. Settings owns profile enable/disable/delete and
     learning mode. Route identical action intent through common commands from
     row, selection, context menu, and detail.
  3. Add revision-safe correction transaction: load current artifact/manifest
     revision, validate draft label IDs and expected prior values, apply user
     rename/reject mutation atomically across intended transcript and metadata
     artifacts, then refresh only matching meeting. If worker regeneration,
     merge, archive, or another editor changed input, preserve draft, show
     conflict/reload action, and never apply a stale map to a new label.
  4. Define learning authority and reversibility. Only explicit confirmed name
     correction can create/update local profile samples; suggestion/auto-apply,
     refresh, and repair never learn by themselves. Reject stores local scoped
     negative feedback; `Undo Profile Names` removes only profile attribution
     for current meeting and records suppression without erasing user edits.
     Explain what profile disable/delete changes prospectively versus historical
     published names; require confirmation for delete-all and support safe
     cancellation/failure without partial profile loss.
  5. Make attribution provenance compact and truthful: user edited, auto-applied
     local profile, suggested local profile, rejected, or anonymous label,
     with confidence/reason in review detail. Do not equate a prediction with
     confirmed identity. Summary/reading surfaces use selected display names
     while preserving source label accessible to review; repair replaces labels
     only after user-requested reprocessing succeeds.
  6. Gate all unavailable states before queue/write work: missing manifest/audio
     or voice samples, absent/disabled profiles, disabled learning, model asset
     unavailable, current queue task, no labels, suspicious-label diagnosis,
     cancellation, and local-store read/write failure. State exact recovery
     target (Setup, queue labels, review names, retry local store, or no action)
     and never claim a profile/repair completed from queue acceptance alone.
- Tests and rendered checks: extend `VoiceProfileStoreTests`,
  `VoiceProfileMatcherTests`, `SpeakerNameLearningServiceTests`,
  `SpeakerNameCorrectionServiceTests`, `TranscriptSchemaTests`, relevant
  `SessionProcessorTests`, queue tests, and `MainWindowInteractionLogicTests`.
  Cover terminology/state resolver, stale revisions, atomic partial-write
  recovery, concurrent refresh/regeneration, label-to-name mapping, no implicit
  learning, rejected suggestion, undo scope, disabled/deleted profile effects,
  missing samples/assets, repair failure/retry, and local-only persistence.
  Render row, selection, detail, Settings profiles, and every unavailable state
  at 1280x800/125% DPI; verify keyboard order, accessible names/status, and
  no vectors/audio paths appear in normal UI or activity text.
- Documentation / installer / release work: update user docs with four concept
  definitions, local-only profile storage, learning/rejection/undo effects,
  repair versus rename, profile deletion scope, and recovery paths. Rebuild
  installer and run packaged speaker-labeling/profile smoke plus required
  worker/UI test flow when shipped.
- Evidence and date: 2026-09-27 implementation added
  `SpeakerExperienceResolver` with every named state and surface-specific
  actions; queue/detail/Settings routing now separates add/repair labels from
  name review and profile management. Name review validates label ID, expected
  prior values, and opaque artifact revision, then writes manifest/JSON/Markdown
  through a rollback-capable local transaction; stale work preserves drafts and
  asks for reload. Explicit corrections alone learn profiles; accepted
  suggestions do not become rejection feedback; refresh and repair never learn.
  Settings can enable, disable, delete one, or confirm delete-all profiles;
  those changes affect future suggestions, not historical display names. Focused
  `dotnet test` passed 279 tests covering resolver, correction, local profile,
  matcher, transcript schema/attribution, session processor, queue, and WPF
  source contracts. `README.md`, `SETUP.md`, and `ARCHITECTURE.md` document the
  four concepts, local-only boundary, rejection/undo/delete scope, repair, and
  recovery paths.
- Remaining gap or next action: implementation complete. Live 1280x800/125%
  render and packaged installer smoke were not run because an active user-owned
  recording holds the app/runtime and the worktree contains unrelated dirty
  release inputs; run both from a clean release candidate after recording ends.

Goal: reduce confusion between diarization labels, learned names, and repair.

Workstream 1 - Concept separation:

- Use consistent language for:
  - anonymous speaker labels from diarization,
  - learned speaker names from local voice profiles,
  - speaker-label repair for suspicious diarization output.
- Avoid using "speaker labels" and "speaker names" interchangeably.

Workstream 2 - Review workflow:

- Group Use, Reject, Apply Speaker Names, Refresh Suggestions, and Undo Name
  Recognition under speaker-name review.
- Keep voice-profile enable/disable/delete controls in Settings > Processing.
- Keep repair actions on the meeting when the issue is a meeting-specific
  speaker-label problem.

Workstream 3 - Unavailable states:

- Clearly explain when voice samples are missing, profiles are unavailable,
  learning is disabled, labels are absent, or repair is ineligible.
- Preserve local-only behavior and avoid publishing embeddings or raw profile
  payloads.

Sprint 13 acceptance criteria:

- Users can tell whether they are editing names, adding labels, or repairing
  bad labels.
- Speaker-name learning remains local, reversible, and profile-managed.

## Sprint 14: Copy, Trust, And Blocked-State Polish

### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27
  pressure test); `Partial` (2026-09-27: authority plus summary/speaker
  verticals implemented; cross-surface migration remains); `Done` (2026-09-27:
  normal-surface migration and source inventory guard complete).
- User outcome: every command says its outcome, impact, and scope; unavailable
  commands say why and provide one safe next step. Trust/data-locality claims
  remain precise across Home, Setup, Meetings, detail, and Settings.
- Scope / non-goals: consolidate customer-facing copy and blocked-state routing.
  Do not alter action authorization, retry/queue behavior, model/provider
  routing, persistence, or error diagnostics; this sprint exposes truth already
  supplied by authoritative state and other ready sprints.
- Dependencies and decisions: consume Sprint 1 policy, Sprint 4 readiness,
  Sprint 10 queue states, Sprint 12 summary routing, and Sprint 13 speaker
  state. Make `UserActionIntent` and typed `BlockedReason` authoritative over
  ad-hoc strings. A blocked reason contains condition, affected scope, safety
  constraint, primary remedy destination, optional Help, freshness, and safe
  user copy; internal exception/path/provider details remain logs/Advanced.
- Authoritative files and contracts: UI state builders in
  `MainWindowInteractionLogic`, settings/detail state records, WPF binding
  surfaces, existing action eligibility methods, and a central copy resource or
  resolver. Existing localized/formatting conventions remain source of culture
  formatting; new literals are prohibited in event handlers except transient
  developer diagnostics.
- Implementation slices:
  1. Inventory visible controls, confirmations, tooltips, status lines, empty
     states, and errors by action intent/surface. Classify each as outcome,
     progress, success, blocked, failed, confirmation, or advanced diagnostic;
     preserve screenshot IDs and test fixtures. Flag raw exception/path/text,
     duplicate terms, hidden side effects, and untestable string composition.
  2. Introduce pure copy resolver mapping action intent plus typed state to
     label, short helper, confirmation title/body, progress, success, blocked
     title/body, primary action, and accessible live announcement. Action
     labels name affected thing and result (`Add Speaker Labels`, `Archive 2
     Meetings`, `Publish Transcript First`); confirmation names only durable
     side effects/recovery, never routine navigation.
  3. Define exhaustive blocked taxonomy for no/invalid model, setup pending,
     no readable transcript/audio/artifact, labels/samples/profiles missing,
     profile learning disabled, recording active, app busy, queue pause,
     provider/key/consent invalid, permission/storage/read-only failure, stale
     data, network unavailable, and no safe remedy. Resolver must distinguish
     unavailable, disabled, processing, and failed; it never invents a remedy
     or exposes low-level details as product copy.
  4. Route all command enablement and visible status through same eligibility
     result. Disabled controls retain keyboard/screen-reader explanation or
     expose non-destructive `Why unavailable?`; no hidden action becomes
     discoverable only by hover. Preserve selected drafts/focus and avoid
     repeated live-region announcements during refresh loops.
  5. Audit trust claims field by field: on-device computation, local storage,
     local gateway, hosted provider, fallback, downloaded model, external
     import, and removable profile. Say `on device` only where verified;
     hosted transcript boundary appears at choice/save point once per consent
     policy, while ordinary generated output uses short provenance. Do not call
     ModelProxy local processing merely because request starts at loopback.
  6. Replace raw normal-surface exception messages with stable safe category,
     affected item count, retry/help route, and correlation-safe timestamp.
     Keep raw detail in existing logs/Advanced. Every replacement preserves a
     programmatic telemetry/log event for support and avoids message duplication
     between activity, footer, toast, and detail.
- Tests and rendered checks: table-drive resolver for every intent/reason/scope,
  including no-remedy and stale cases; test no ordinary output contains paths,
  keys, exception type/message, transcript, or embedding data. Add parity tests
  for Home/Settings/row/selection/context/detail, confirmation scope, disabled
  control explanation, and consent/data-routing copy. Add snapshot/accessibility
  tests for keyboard focus/live regions at 1280x800/125% DPI and high contrast;
  run copy inventory check preventing new unapproved action-handler literals.
- Documentation / installer / release work: update UI terminology/glossary and
  trust language only after accepted behavior/copy ships. Rebuild installer and
  perform packaged smoke plus rendered QA for UI/runtime changes.
- Evidence and date: 2026-09-27 implemented `UserActionCopyResolver` with
  exhaustive intents, blocked-reason condition/scope/safety/remedy/freshness
  records, safe-copy tests for every enum value and surface, consent coverage,
  a polite summary status live region, and summary plus Voice Profile/detail
  command migrations. Summary state/action eligibility now shares the
  consent-aware resolver and normal summary/speaker status never echoes raw
  exception/path/key/provider/transcript/embedding detail. Focused build and
  test evidence: `MeetingRecorder.Core.Tests` filters for action-copy,
  summary, speaker, interaction, and XAML tests passed 222/222; the only
  warning is the pre-existing unused `TrackingWaveIn.DataAvailable` test event.
  Installer/package/rendered smoke remains deferred because a user recording is
  active and this worktree has unrelated dirty release inputs.
- Remaining gap or next action: source migration is complete. The initial
  2026-09-27 scan found 115 `exception.Message` references in
  `MainWindow.xaml.cs`; all normal-surface instances are now resolver-backed
  safe copy. Four intentional uses remain: Advanced install-root and Teams
  diagnostics plus vetted DirectML-runtime availability text. Sprint 15 owns
  rendered, keyboard, high-contrast, and packaged evidence.

Goal: make controls feel safe, direct, and understandable.

Workstream 1 - Outcome labels:

- Rewrite high-friction labels around outcomes rather than internal mechanisms.
- Prefer action copy that says what will happen to the meeting, transcript,
  audio, profile, or provider.
- Keep compact helper text; avoid warning walls except for hosted or
  destructive decisions.

Workstream 2 - Consistent blocked states:

- Standardize blocked/unavailable states for missing model, invalid model, no
  transcript, no speaker samples, no profiles, app busy, recording active,
  provider unconfigured, missing artifacts, and insufficient permissions or
  storage.
- Each blocked state should name the remedy or state that no safe remedy is
  available.

Workstream 3 - Local-first trust:

- Clearly label local-only behavior for transcription, speaker labeling,
  ModelProxy summaries, and voice profiles.
- Clearly label hosted behavior only where the user chooses it.

Sprint 14 acceptance criteria:

- Users can tell what an action will do before clicking.
- Blocked states are consistent across Home, Settings, Meetings, and detail.

## Sprint 15: Accessibility And Rendered UX QA

### Implementation Record

- Status: `Partial`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27
  pressure test); `Partial` (2026-09-27: source accessibility contracts and
  redacted journey matrix complete; rendered capture blocked); `Partial`
  (2026-09-30: isolated 1280x800 and 1024x768 rendered matrix plus package
  startup evidence recorded; interactive and Narrator paths remain open).
- User outcome: simplified workflows remain usable without a mouse and legible
  at realistic desktop sizes; users receive status and recovery information
  without visual-only or hover-only discovery.
- Scope / non-goals: establish repeatable rendered WPF and accessibility
  evidence for Sprints 0-14. Do not substitute visual tests for functional
  acceptance, change visual brand without `DESIGN.md`, or waive an accessibility
  defect because layout is dense.
- Dependencies and decisions: execute after each changed ready slice and before
  Sprint 16 release. `DESIGN.md` remains UI authority. Treat critical keyboard
  traps, missing accessible action names, hidden destructive meaning, clipped
  remediation, insufficient contrast, and unreadable status as ship blockers.
- Implementation slices:
  1. Create versioned journey matrix: first-run/ready recording, Settings deep
     link and unsaved draft, Meetings preset/search/selection, queue/recovery,
     ASAP label continuation, cleanup automation state, hosted-summary consent,
     speaker review/profile deletion, archive/delete confirmation, and detail
     read/maintain paths. Each has state fixture, expected focus, spoken names,
     screenshot points, and no-secret synthetic/redacted data.
  2. Build deterministic UI harness for packaged and development WPF that sets
     window size/DPI/theme/data fixture, captures screenshots, walks keyboard
     (`Tab`, arrows, menus, Escape, Enter), and extracts automation tree/live
     status. Never drive live microphone, real provider, personal profile, or
     destructive artifact path in automated evidence.
  3. Check 1280x800/100% and 125%, 1024x768/125%, and supported high-DPI scale;
     normal/high-contrast themes if supported. Inspect clipping, overlap,
     scroll/focus visibility, hit targets, long localized-like strings, screen
     reader name/role/value/help, focus return after dialog/route, and activity
     status announcement rate.
  4. Add semantic tests for focus router, control name/description, disabled
     reason exposure, action grouping/order, Escape/cancel, confirmation scope,
     no keyboard trap, and no reliance on color/icon alone. Use screenshot
     visual diffs only with approved baseline/threshold and manual review of
     changes; store no sensitive meeting content in artifacts.
  5. Triage defects by task completion/accessibility impact, attach journey,
     state, viewport, screenshot/automation evidence, and minimal reproduction.
     Fix source owner sprint first; rerun impacted path and full critical matrix.
- Tests and rendered checks: add harness smoke and accessibility-tree contract
  tests; run critical matrix development and packaged release build. Manually
  validate Narrator or equivalent screen reader for Home, Settings, Meetings,
  and detail; record device/OS/tool version/date and known limitations. Verify
  design tokens, contrast, no clipping, and keyboard reachability at every
  target viewport.
- Documentation / installer / release work: document harness command, fixture
  safety, screenshot approval, matrix results, and known limitations. Rebuild
  installer and use packaged smoke/render pass before signing/release.
- Evidence and date: 2026-09-27 added
  `docs/ux-audits/whole-app-sprint-15-journey-matrix.md` with eight redacted
  journeys, target viewports, keyboard/spoken contracts, capture points, and
  evidence limits. `AccessibilityContractTests` verifies critical accessible
  names, polite live status, Settings deep-link focus fallbacks, contained
  keyboard navigation, no-drop-shadow Technical Studio source, and Settings
  minimum geometry. Focused source/UI tests passed 188/188; the only warning is
  the pre-existing unused `TrackingWaveIn.DataAvailable` test event. A current
  `MeetingRecorder.App` process (PID 23488) owns the live user profile; the
  global single-instance mutex and available computer-use inventory make a
  separate synthetic native capture unavailable. 2026-09-29 added
  `WpfRenderedShellHarnessTests`: a real `MainWindow` is rendered at 1280x800
  from a fresh disposable profile on a dedicated STA thread, with a PNG,
  automation-peer trace, and keyboard-focus trace. It does not run app startup,
  acquire the single-instance mutex, access the installed profile, or start
  capture. Harness documentation is in
  `docs/ux-audits/whole-app-sprint-15-wpf-harness.md`.
  The first rendered review exposed truncated setup-remediation text in the
  1280px header; its detail width is now 220px and the harness asserts the
  complete synthetic reason. A no-profile package runner exposed a
  shell-dependent `Get-FileHash` call during portable publish; it now uses
  .NET SHA-256, and no-profile portable publish plus `Build-Installer.ps1`
  completed on 2026-09-29 (ZIP 88,627,930 bytes; MSI 76,292,096 bytes). No
  packaged-render evidence is claimed yet.
- Evidence and date: 2026-09-30: `MeetingRecorder.WpfRenderProbe` captured
  all five safe synthetic shell states at 1280x800/100%, 1280x800/125%, and
  1024x768/125%, plus the processing state at a 200% raster. The harness now
  fails if WPF clamps its requested logical viewport. That check exposed and
  fixed the former 1280x800 minimum and header action overflow; the remaining
  Meetings table is intentionally horizontally scrollable at 1024px. Source
  anchors now use named XAML sections rather than brittle line cutoffs, so the
  refreshed 271-control audit retains accurate Home/Meetings/Settings ownership.
  `Build-Installer.ps1` passed; portable startup, MSI installation, 15-file
  installed-integrity verification, and a five-second installed-app smoke with
  no qualifying crash event passed. See
  `docs/ux-audits/whole-app-sprint-15-rendered-evidence.md`.
- Evidence and date: 2026-09-30: Settings > Recording rendered safely at
  1280x800/125%. The focused Settings section, forward Tab, Escape close, and
  return to the original `Open Settings` control are captured by the isolated
  harness. A regression check now protects this focus-return contract.
- Evidence and date: 2026-09-30: the disposable Settings profile toggled and
  saved the harmless calendar-title fallback, then rendered the persisted
  enabled state at 1280x800/125%. The same trace confirms Save Changes becomes
  available, reports success, and Escape returns focus to the original opener.
- Evidence and date: 2026-09-30: the isolated synthetic permanent-delete
  confirmation renders named controls, requires exact typed confirmation, and
  cancels on Escape without deleting any artifact. Focus returns to Meetings.
- Evidence and date: 2026-09-30: hosted-summary consent now uses an app-owned
  modal that states the published-transcript boundary before authorization.
  Its cancel action and Escape preserve unsaved synthetic Settings changes.
- Evidence and date: 2026-10-04: profile deletion now uses an app-owned,
  accessible confirmation for both single and all-local-profile routes. The
  isolated harness seeds one fake local profile, renders the single-delete
  scope/consequence and named choices, cancels with Escape, and proves the
  disposable profile remains unchanged.
- Evidence and date: 2026-10-04: synthetic Meeting detail now opens before
  asynchronous state hydration, exposes a keyboard-focusable named Close action,
  and returns focus to the Meetings list after Escape.
- Evidence and date: 2026-10-04: the rebuilt MSI installed to the test path,
  launched the installed app, and matched all 15 required bundle-integrity
  hashes. This is package-startup evidence only, not native visual or screen
  reader evidence.
- Remaining gap or next action: capture packaged UI states; validate high
  contrast, OS DPI, and Narrator before marking this sprint done.
  `docs/ux-audits/whole-app-sprint-15-native-validation.md` now defines the
  required test-profile, capture, DPI/high-contrast, Narrator, and evidence
  steps for that external validation.

Goal: verify simplification in the rendered WPF app, not just in code.

Workstream 1 - Keyboard and focus:

- Validate keyboard navigation, focus order, accessible names, disabled states,
  menu grouping, section routing, and dialog flows.
- Ensure deep links land focus on the relevant section and first useful
  control.

Workstream 2 - Layout:

- Check Settings and Meetings at normal and smaller desktop window sizes.
- Ensure text does not overflow buttons, panels, menu items, section nav, or
  status wells.
- Ensure no UI element overlaps another in an incoherent way.

Workstream 3 - Design-system fit:

- Preserve the Technical Studio design: dense, opaque, structured, and
  professional.
- Avoid visually hiding controls through low contrast, tiny hit targets, or
  buried unlabeled icons.

Sprint 15 acceptance criteria:

- Simplified surfaces are usable by keyboard.
- Settings, Meetings, and detail windows remain dense but not cramped or
  broken.

## Sprint 16: Tests, Documentation, Installer, And Release

### Implementation Record

- Status: `Blocked`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27
  pressure test); `Blocked` (2026-09-27: Sprint 15 rendered/accessibility
  evidence is unavailable, so release gate cannot begin).
- User outcome: simplification ships only with proof that user journeys,
  persistence, installer payload, installed startup, and docs agree; failures
  are visible and recoverable before release.
- Scope / non-goals: verify and package accepted Sprints 0-15 from a known
  source revision. Do not mark incomplete dependent sprints `Done`, upload or
  publish release assets, sign binaries, or modify users' live installation
  without separate explicit release/deploy authority.
- Dependencies and decisions: all UX slices must have focused tests and Sprint
  15 critical rendered/accessibility evidence. Create one immutable release
  evidence record with commit/dirty-tree state, OS/runtime, commands/versions,
  artifact hashes, scenario results, known exclusions, and tester/date. Existing
  dirty unrelated work is preserved; release build only begins after a clean,
  reviewed source state or an explicitly recorded scoped source snapshot.
- Implementation slices:
  1. Build traceability matrix mapping every Sprint 0-15 acceptance item and
     regression constraint to unit/integration/source/UI/rendered/package test,
     owner, fixture, and evidence location. Mark no evidence as open; failed or
     skipped checks cannot be summarized as pass.
  2. Run changed-slice tests first, then full `scripts\Test-All.ps1`; run
     `tests\AppPlatform.Tests` separately when shared deployment/extraction
     behavior changed. Capture exact command, configuration, test counts,
     failures/retries, and environment; triage flaky result with reproduction,
     never weaken valid tests.
  3. Perform clean portable publish and `Build-Installer.ps1`; validate bundle
     layout/integrity, loose apphosts, version metadata, MSI/ZIP presence,
     source revision and artifact SHA-256. Reject stale/mixed artifacts,
     uncommitted payload uncertainty, missing model/runtime files, or installer
     source mismatch before smoke.
  4. With no running app/worker, run `Smoke-Test-Release.ps1 -Runtime win-x64`
     against portable and MSI-installed paths. Execute synthetic critical UI
     journey matrix: ready/manual recording settings, preset/custom settings,
     Meetings/recovery/ASAP, cleanup status, summary boundaries, speaker review,
     destructive confirmation, and accessibility focus. Collect sanitized logs,
     screenshots, and Windows crash-event result; no personal audio/keys.
  5. Reconcile documentation with shipped behavior: README/SETUP user flows,
     ARCHITECTURE contracts/data boundaries, PRODUCT_REQUIREMENTS acceptance,
     RELEASING commands/artifact behavior, and release notes. Include changed
     advanced-control locations, consent/revocation, local data, recoverability,
     limitations, and support diagnostics. Run link/command verification.
  6. Define failure/rollback handoff: preserve source/artifact evidence, stop
     release promotion, identify last verified installer, classify source/build/
     package/smoke/doc failure, and open follow-up at owning sprint. Only a
     revalidated clean rebuild replaces candidate artifacts.
- Tests and rendered checks: matrix must include all plan scenarios plus
  accessibility/render captures from Sprint 15. Required gates: focused tests,
  full test script, appropriate AppPlatform tests, installer build, portable/MSI
  smoke, artifact integrity, critical synthetic journeys, docs command/link
  checks, and review of warnings/crash events. Record intentionally unavailable
  hardware/provider tests as gaps, never pass.
- Documentation / installer / release work: this is release/documentation gate;
  update docs and rebuild installer for shipped changes. Do not push/upload from
  this sprint without explicit authority; release evidence is prerequisite for
  separate deployment work.
- Evidence and date: 2026-09-27 audit confirms test, installer, portable,
  release, and smoke scripts plus release guidance exist. Current roadmap has
  no acceptance-to-evidence ledger, clean source/artifact provenance contract,
  synthetic full-journey smoke record, or failure handoff rules.
- Remaining gap or next action: wait for Sprint 15’s synthetic rendered and
  accessibility evidence. Once it exists, create the acceptance matrix and
  synthetic fixture inventory before first implementation slice; attach every
  later Sprint 0-15 result to it, then run focused gates only after
  implementation lands.

Goal: ship simplification as verified product behavior, not just rearranged
XAML.

Workstream 1 - Tests:

- Add focused tests for preset inference, mode-to-config mapping, pending
  config changes, section routing, recommendation ranking, meeting view
  presets, grouped action eligibility, detail-window state, blocked-state copy,
  automation safety, and destructive-action isolation.
- Update existing `MainWindowInteractionLogicTests` for Settings, Home,
  Meetings, queue, summary, speaker-label, and speaker-name behavior.
- Add source/XAML guard tests only where they protect critical routing or
  action grouping.

Workstream 2 - Verification:

- Run focused tests first.
- Run the full gate:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Because this is app/UI/runtime behavior, rebuild installer assets:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Run packaged smoke after confirming no active installed app or processing
  worker:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Workstream 3 - Documentation:

- Update `README.md`, `SETUP.md`, `PRODUCT_REQUIREMENTS.md`,
  `ARCHITECTURE.md`, and release notes.
- Document the new Settings sections, intent modes, Meetings presets,
  recommendation behavior, automation boundaries, hosted summary boundary,
  speaker-name versus speaker-label wording, and advanced-control locations.

Sprint 16 acceptance criteria:

- Focused tests and full gate pass.
- Installer assets are rebuilt.
- Packaged smoke confirms the changed Settings, Home, Meetings, and detail
  paths are usable.
- Docs match the shipped behavior.

## Test Scenarios

- New user reaches ready-to-record through recommended setup.
- User records manually without opening Settings.
- User switches recording assistance to manual-only, then custom.
- User disables microphone capture and sees the recording implication.
- User enables local-only summaries.
- User explicitly enables hosted fallback and sees the privacy boundary.
- Meetings opens to useful recent or needs-attention work.
- A failed transcript has one clear recovery action.
- A healthy meeting shows a complete state.
- User opens a meeting to read transcript and summary without maintenance
  clutter.
- User fixes speaker labels without confusing that with speaker-name learning.
- User refreshes speaker-name suggestions and understands when samples or
  profiles are unavailable.
- User performs bulk actions only when eligible.
- Advanced custom tuning remains possible and marks modes as `Custom`.

## UX Simplification Interfaces And Constraints

- No functionality is removed.
- Presets are transparent editing modes over existing concrete settings.
- Existing artifact formats, `.ready` behavior, publish paths, and meeting
  maintenance contracts remain unchanged.
- Hosted transcript processing remains opt-in.
- Microphone capture remains explicit.
- Destructive actions remain isolated and confirmed.
- Advanced controls remain available and discoverable.
- Add this roadmap as a cross-cutting plan; it does not replace the
  speaker-name, external-import, or GPU-transcription roadmaps.

# Meetings Management UX Simplification Plan

## Summary

Goal: further simplify Meeting Recorder's meeting-management experience without
removing the richness that makes the app useful. The current Meetings workspace
is powerful but overwhelming because browsing, repair, backlog, cleanup,
transcript reading, summaries, speaker labels, archive/delete, and bulk
operations are all presented as competing control systems.

This plan expands the existing Whole-App UX roadmap's Meetings sprints into a
dedicated implementation-ready program. The target is not fewer capabilities.
The target is fewer simultaneous decisions: show useful meetings, rank one next
action, automate safe refresh and analysis, group controls by intent, and keep
every advanced maintenance path available in the right context.

## Pressure-Test Verdict

The prior Meetings plan is useful, but still too oriented around rearranging
controls. The deeper fix is to stop treating `Meetings` as a table plus tool
panels, and instead make it a guided meeting workbench.

The strengthened direction is:

- Default: show useful meetings, one recommended next action, automatic safe
  refresh, and clear complete states.
- Guided control: expose task views and grouped actions when the user is
  actively managing meetings.
- Power control: preserve custom sorting/grouping, bulk operations, repair
  tools, and destructive paths without making them the default experience.

This plan keeps all richness, but reduces simultaneous choices.

## Key Product Changes

- Add `MeetingsViewPreset`: `Recent`, `NeedsAttention`, `Processing`,
  `Archived`, `Custom`.
- Keep search visible in every preset.
- Move sort, group, direction, expand/collapse, and table-style tuning into
  `Custom`.
- Add a shared meeting action taxonomy:
  - `Open`
  - `Fix`
  - `Organize`
  - `Processing`
  - `Danger Zone`
- Add a deterministic recommendation model with: action, reason, safety class,
  blocked reason, and target surface.
- Automate safe refresh and analysis: catalog refresh, cleanup recommendations,
  attendee backfill, queue state, and setup/provider readiness.
- Never automate permanent delete, hosted AI enablement, merge/split, broad
  reprocessing, or active-work interruption.

## Sprint Roadmap

### Sprint 0: Meetings Friction Audit

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: every Meetings capability has evidence-backed default home; no control disappears or becomes unreachable during simplification.
- Scope / non-goals: inventory current UI and action routing only. Do not move controls, alter eligibility, or infer user behavior from source count alone.
- Dependencies and decisions: use Sprint 0-16 UX evidence plus current WPF/action logic. Inventory unit is `intent + scope + surface + eligibility + side effect + recovery`; same intent across surfaces is one capability with parity variants.
- Implementation slices:
  1. Generate machine-readable inventory from toolbar, grid row, selection strip, context menu, cleanup tray, inspector, detail, keyboard command, and legacy/advanced controls; attach source owner, label, accessible name, enablement, confirmation, artifact effect, and test.
  2. Run synthetic user journeys for find/read/recover/labels/archive/delete/merge/split/bulk/backlog. Record clicks/keys, state changes, blocked reason, and screenshot/automation-tree proof; capture no personal data.
  3. Assign disposition `Default`, `Grouped`, `Automated`, `DetailOnly`, `Advanced`, `Custom`, or `DestructiveExplicit`; each needs rationale, discovery route, accessibility path, and rollback/recovery where applicable. Preserve source/action parity until replacement proves equivalent.
  4. Add audit validator: every command/control maps to exactly one capability/disposition; every capability has a surface or documented intentional retirement; destructive actions remain explicit/confirmed; no duplicate dispatch routes conflict.
- Tests and rendered checks: inventory snapshot diff; source/action-handler coverage; journey keyboard/render checks at 1280x800/125%; manually review hidden/disabled/overflow context commands and selection counts.
- Documentation / installer / release work: store sanitized inventory and disposition report under docs/test evidence after implementation. No installer work for audit-only slice.
- Evidence and date: 2026-09-27 source audit found dense Meetings toolbar, grid, context, selection, cleanup, inspector, and detail surfaces; no authoritative capability/disposition matrix. 2026-09-29 added metadata-only `docs/meeting-capability-inventory.json` and `docs/meeting-friction-audit.md`: all 25 canonical catalog actions have exactly one disposition/home, confirmation/recovery, and discovery surface; 16 supplemental toolbar, cleanup, and import controls retain a named existing XAML surface. The validator confirms catalog parity, destructive typed confirmation, and supplemental source presence. Focused inventory/catalog tests passed 10/10. Native 1280x800/125% keyboard/overflow capture remains a later layout-sprint operational check, not claimed by this audit-only record.
- Remaining gap or next action: Sprint 1 — define the shared Meetings experience-state and action ownership contract before moving controls.

- Inventory every Meetings toolbar control, row button, context-menu item,
  cleanup-review action, inspector field, and detail-window action.
- Classify each as browse, read, fix, organize, process, bulk, destructive,
  diagnostic, or advanced.
- Capture interaction counts for: find meeting, open transcript, recover failed
  transcript, add/repair speaker labels, archive, delete, merge, split, bulk
  apply, and manage backlog.
- Produce a disposition map: default, grouped action, automated, detail-only,
  custom-only, or destructive explicit.

Acceptance: every existing capability has a future home.

### Sprint 1: Meetings Experience Contract

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: Meetings behaves as a calm workbench: browse first, one next action when needed, clear focus/bulk scope, and full advanced power without hidden behavior.
- Scope / non-goals: define shared interaction contract before moving controls. Do not delete commands, alter meeting artifacts, change recommendation scoring, or enable automation beyond named policy.
- Dependencies and decisions: build on Sprint 0 inventory and Sprints 1/7/8/9/10 safety contracts. State is `Library`, `SingleSelection`, `MultiSelection`, `Detail`, `Busy`, or `RefreshRequired`; selection scope is source of truth. Default layer offers reading/navigation/one primary remedy; grouped layer exposes relevant safe actions; Advanced retains every dispositioned capability.
- Implementation slices:
  1. Create pure `MeetingsExperienceState` from catalog freshness, selection, focus, busy operation, recommendation, queue/recovery, and capability inventory. Return visible regions, summary, primary action, secondary groups, blocked reason, focus target, and action scope; no WPF object references.
  2. Define action ownership matrix: library (find/filter/preset), single (read/artifacts/focused repair), multi (eligible bulk), detail (contextual maintenance), cleanup (review recommendations), advanced (diagnostics/custom), destructive (separate confirmed route). Each action has exact target count, eligibility, side effect, success/failure truth, and recovery.
  3. Preserve selection, scroll/focus, filters, view mode, grouped expansion, and editor drafts through safe refresh; explicit catalog mutation may re-resolve selection with status, never silently target a different meeting. Busy state disables conflicting actions but explains why and preserves reading where safe.
  4. Set recommendation/control rule: one metadata-only primary next action may be promoted; it cannot auto-dispatch, obscure alternate actions, or outrank active recording/destructive safeguards. All-clear/empty/stale states show useful browse/setup/retry route without fabricated urgency.
  5. Route toolbar, row, context, selection strip, inspector, detail, keyboard, and cleanup actions through shared intent/action catalog. Advanced controls stay discoverable from default/grouped surface; no action is only visually hidden or changes semantics by entry point.
- Tests and rendered checks: table-drive experience state/action matrix for no/single/multi selection, stale/empty/busy/recording/failed states, recommendation conflicts, eligibility changes, refresh mutation, keyboard/context parity, and draft/focus preservation. Render each state at 1280x800/125% DPI with accessible region/action labels.
- Documentation / installer / release work: add contract/glossary to UX evidence/docs after implementation; installer/release work belongs to Sprint 16 for shipped changes.
- Evidence and date: 2026-09-27 source review found `MeetingWorkspaceToolState`, selection command state, grouped/table view, selection messages, and tests. No single authoritative workbench state/action ownership contract covers all entry points or refresh/draft semantics. 2026-09-29 added pure `MeetingsExperienceResolver` and `docs/meetings-experience-contract.md`. The resolver establishes precedence for stale, busy, empty, detail, multi-, single-, and library states; promotes at most one safe primary presentation action; preserves action-family access; withholds recommendation dispatch during recording; and maps catalog actions to library/single/multi/detail/cleanup/destructive owners. Focused experience/catalog/inventory tests passed 16/16. The contract is intentionally not a WPF routing change; rendered keyboard/focus validation belongs to the layout/UI sprint.
- Remaining gap or next action: Sprint 2 — derive metadata-only per-row state with explicit stale/archived/blocked/complete precedence.

- Define Meetings as a guided workbench, not a generic table.
- Document what can be assumed safely and what requires explicit user intent.
- Define copy rules for recommendations, blocked states, archive/delete, bulk
  outcomes, and all-clear states.
- Establish that no functionality is removed or merely visually hidden.

Acceptance: implementation has clear rules for automation versus control.

### Sprint 2: Meeting State Model

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: each row answers what happened, current state, why it matters, and safe next action without exposing manifest/worker internals.
- Scope / non-goals: add a derived metadata-only presentation model. Do not persist duplicate state, mutate meeting/queue artifacts, change recommendation ranking, or hide actionable failures behind `Complete`.
- Dependencies and decisions: source truth remains catalog artifact/manifest state, fresh queue snapshot, recommendation metadata, summary/label facts, archive state, and refresh timestamp. `MeetingExperienceState` precedence: `RefreshRequired`, `Archived`, `FailedOrNeedsAttention`, `Blocked`, `Processing`, `NeedsAction`, `Complete`, `Unavailable`; exact outcome/action explains ties. Stale/unknown data cannot yield Complete/Idle.
- Implementation slices:
  1. Define pure input snapshot and result containing category, short label, explanation, primary/secondary action intents, severity, freshness/provenance, accessible description, and diagnostic reason code; never include raw path/error/transcript/profile data.
  2. Specify precedence/terminal rules for source absent/corrupt, archive, manifest failure, queue running/paused/queued, setup/consent/provider/input blocking, pending transcript/labels/summary, recommendation, successful artifact completeness, and stale catalog/queue disagreement.
  3. Separate `Complete` from `NoData`/`Unavailable`: complete needs verified readable expected artifacts and no eligible unresolved state; successful intentional skips report precise scope. Process/recovery state uses live queue only when snapshot fresh; otherwise `Status needs refresh`.
  4. Map each result to one safe remedy/read action and compatible filters/groups/badges. Multiple issues retain ordered secondary reasons; primary never triggers automation. Archive/delete/destructive choices remain separate from normal remedy.
  5. Replace row/inspector/detail/selection status composition incrementally through resolver; preserve prior artifact fields for Advanced. Keep UI source order stable and no status change solely from display refresh.
- Tests and rendered checks: exhaustive table tests for precedence/ties, stale disagreement, intentional skip, corrupt/missing artifact, archived, queue state, setup blocks, recommendation, healthy complete, multi-issue ordering, and no sensitive output. Assert row/inspector/detail/filter parity; render labels/badges at 1280x800/125% with screen-reader descriptions.
- Documentation / installer / release work: document state glossary and diagnostics boundary after behavior ships; installer/release gate is Sprint 16.
- Evidence and date: 2026-09-27 source review found processing strip, catalog rows, cleanup recommendations, detail statuses, and queue data, but no central row-state resolver or verified precedence/freshness contract. 2026-09-29 added pure metadata-only `MeetingPresentationStateResolver` with explicit unavailable/stale/archive/failure/setup/queue/recommendation/complete precedence, safe action presentation, accessible copy, and diagnostic reason code. Complete requires both readable artifacts; stale/unknown cannot claim Complete. The shared contract documents the row-state glossary and diagnostics boundary. Focused presentation/experience/catalog tests passed 17/17. Binding the resolver to row/detail surfaces remains an incremental UI step in the later view/state sprints; this contract does not mutate artifacts or alter current action eligibility.
- Remaining gap or next action: Sprint 3 — consolidate recommendation precedence and dismissal over the new row-state contract.

- Normalize meeting row state into clear user-facing categories: complete,
  needs attention, processing, blocked, archived, and unavailable.
- Derive states from existing metadata, queue state, transcript availability,
  artifacts, cleanup recommendations, summary state, and speaker-label state.
- Add complete/no-action-needed states so healthy rows do not feel empty.
- Keep internal technical state available in details/diagnostics only.

Acceptance: every row can explain its state in plain language.

### Sprint 3: Recommendation Engine

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: cleanup recommendation engine, primary recommendation, fingerprint dismissal, UI/action wiring); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation and focused verification).
- User outcome: every meeting has zero or one honest primary next action; users know why it is suggested, can decline it, and retain all alternatives.
- Scope / non-goals: unify ranking/presentation of existing safe metadata signals. Do not inspect transcript/audio/profile payloads, auto-execute, bypass confirmations, alter state source, or suppress an error merely to show a recommendation.
- Dependencies and decisions: consume Sprint 2 state result, setup/queue/recovery, cleanup, summary, and speaker facts. `MeetingRecommendationResolver` is pure and outputs action intent, reason code, target/scope, eligibility, blocked reason, severity, fingerprint/input revision, confidence/freshness, and alternatives. Rank: failed transcript; required setup; blocked recovery; suspicious labels; missing transcript; summary retry; safe cleanup; metadata polish. Terminal/archive/stale rules may yield no actionable recommendation.
- Implementation slices:
  1. Normalize candidates from each authority with no UI labels; validate target artifacts/revision and action eligibility before rank. A blocked candidate recommends its remedy/navigation, not impossible execution.
  2. Apply total deterministic ordering with explicit tie-breakers and one primary. Primary cannot be destructive or bulk-wide without separate confirmation; lower candidates remain detail/cleanup alternatives and no recommendation claims work completed.
  3. Fingerprint candidate identity from action, affected meeting/artifact revision, reason/target and policy revision. Dismissal hides only matching recommendation until fingerprint changes; manual apply, safe automation, and historical-review state remain separate.
  4. Resolve recommendation/state conflict: failure/blocked source wins over cosmetic cleanup; stale data disables action/promotion; queue/recording safety can downgrade to explanation; no candidate yields true Complete/No action state only when Sprint 2 proves it.
  5. Bind shared result to row, inspector, detail, selection/context, cleanup review, and automation eligibility. Show concise reason/action, target count, and `Why?`; preserve keyboard access and never expose raw diagnostics.
- Tests and rendered checks: table tests for every rank/tie/conflict, metadata-only guarantee, stale/missing target, archive, blocked action, fingerprint change/dismiss/reappearance, manual override, automation exclusion, and surface parity. Render zero/one/multiple candidate states, selected/bulk scope, and screen-reader explanation.
- Documentation / installer / release work: document recommendations as suggestions, dismissal scope, and automation boundary after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found current cleanup-only recommendation analysis and primary selection. No consolidated precedence across processing/setup/summary/speaker state, freshness contract, or full dismissal/automation parity proof. 2026-09-29 extended the existing pure resolver rather than duplicating it: the policy is now versioned, fingerprints include snapshot revision and target state, and results expose non-sensitive reason code, scope, and freshness. Sprint 2 stale/archived state cannot promote an action; summary retry ranks ahead of cleanup; cleanup selection is confidence-first with stable action/fingerprint ties. Dismissal remains limited to low/medium cleanup, summary, and metadata results. `MeetingRecommendationResolver`, presentation, row-state, and experience tests passed 30/30 using an isolated build root. The established UI already reads the shared primary result; broader layout rendering remains in later view/action sprints.
- Remaining gap or next action: Sprint 4 — derive useful view presets from the shared state and recommendation contracts.

- Centralize recommendation ranking.
- Rank one primary recommendation per meeting.
- Priority: failed transcript, missing setup, blocked processing, suspicious
  speaker labels, missing transcript, summary retry, cleanup, metadata polish.
- Include action, reason, blocked reason, and target.
- Use metadata-only inputs; no transcript text or private payloads in
  recommendation logic.

Acceptance: each meeting has zero or one primary recommendation with a clear
reason.

### Sprint 4: View Presets

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: Meetings opens to useful work without forcing sort/group configuration; users can still restore and edit their exact custom view.
- Scope / non-goals: introduce view intent over existing filter/sort/group state. Do not delete current table/group/search controls, overwrite custom preferences, change row-state/recommendation truth, or persist transient selection.
- Dependencies and decisions: consume Sprint 2 state and Sprint 3 recommendation results. `MeetingViewPresetResolver` maps `Recent`, `NeedsAttention`, `Processing`, `Archived`, and `Custom` to immutable view specification plus summary/empty-state. Search intersects every preset; selection remains by stable meeting identity and is cleared only when target is no longer visible with an explicit count/status.
- Implementation slices:
  1. Define exact predicates/sort/group for each preset: Recent excludes archived and prioritizes latest readable/current work; Needs Attention includes failure/blocked/actionable state; Processing uses fresh queue/manifest process state; Archived contains archived records only. Unknown/stale truth stays visible only under a safe documented rule, never silently omitted as completed.
  2. Derive `Custom` when user changes any preset-owned field; preserve custom table/group/sort/direction/filter configuration separately from active preset. Selecting a preset projects values without destroying stored Custom; switching back restores exact valid custom configuration.
  3. Migrate existing persisted view config non-destructively: map recognized old default to chosen initial preset only once; otherwise initialize Custom from legacy values. Version migration/idempotence handles invalid enum/unknown future values safely and records no transient search or selection.
  4. Select startup default deterministically after first fresh catalog state: Needs Attention only when unresolved work passes explicit threshold; otherwise Recent. Do not thrash active view during background refresh, automated recommendations, or queue changes; offer status/count and user-controlled switch.
  5. Bind single preset catalog to toolbar, keyboard, accessibility text, summary, empty state, and Advanced Custom panel. Advanced describes active filters/sort/group and reset behavior; all view changes preserve drafts/focus when safe.
- Tests and rendered checks: pure predicate/projection/migration tests; legacy/custom round-trip, invalid config, startup/default threshold, stale queue, archive, search intersection, selection visibility, refresh non-thrash, table/group parity. Render each preset/empty state/Custom at 1280x800/125% and keyboard-test picker/Advanced focus.
- Documentation / installer / release work: document preset semantics and Custom reset/migration after ship; installer/release gate Sprint 16.
- Evidence and date: 2026-09-27 review found persisted `MeetingsViewMode`, sort, direction, group key, grouped migration and search; no named presets or non-destructive intent/config mapping. 2026-09-29 verified the existing `MeetingViewPresetResolver`, migration-backed `AppConfig` fields, toolbar picker, Custom control disclosure, status/empty text, one-time initial preset persistence, and XAML parity. Presets have fixed projection and search intersection; Custom round-trips without losing its view settings; legacy/invalid settings normalize safely; startup selects Needs Attention when any unresolved work is present, otherwise Recent, and does not background-switch. Archived projection remains explicitly source-gated: the resolver supports an archive catalog, while the current workbench honestly reports it unavailable because no archive-history catalog exists. Focused preset/config/XAML tests passed 92/92 using an isolated build root.
- Remaining gap or next action: Sprint 5 — consolidate cross-domain triage in Needs Attention.

- Replace always-visible view/sort/direction/group controls with `Recent`,
  `Needs Attention`, `Processing`, `Archived`, and `Custom`.
- Keep `Custom` as the home for current table/group controls.
- Default to `Recent`, unless unresolved work makes `Needs Attention` more
  useful.
- Add preset summary text so users understand what they are seeing.

Acceptance: Meetings opens usefully without configuration.

### Sprint 5: Needs Attention Inbox

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 metadata-only resolver and focused verification).
- User outcome: one calm inbox answers what needs user attention now, why, and safest next step; healthy meetings do not create noise.
- Scope / non-goals: present derived triage from Sprints 2-4. Do not add automatic repair, hide hard failure, mark work resolved on queue acceptance, or reinterpret artifact diagnostics.
- Dependencies and decisions: use fresh `MeetingExperienceState` and Recommendation resolver. Inclusion reasons: hard failure, blocked recovery, user decision required, suspicious labels, missing required artifact, summary failure, or current low-risk recommendation. Severity/order: hard failure, blocked/decision, data integrity, processing recovery, recommendation, cosmetic metadata; deterministic time/identity tie-breaker.
- Implementation slices:
  1. Build pure inbox resolver returning rows with reason family, severity, primary/secondary remedy, dismissibility, group key, freshness, and aggregate counts. Stale/failed catalog produces `Refresh required`, never all clear.
  2. Define dismissal: only matching low-risk recommendation fingerprint; it removes promotion, not underlying state/action/history. Hard failure/blocked/missing artifact and safety-critical attention are non-dismissible; changed fingerprint reappears.
  3. Group by reason only when it reduces scan cost; preserve global severity, counts, keyboard order, selected row identity, and bulk eligibility. One meeting with multiple problems appears once with ordered reasons; primary follows rank.
  4. Define all-clear/empty variants: no meetings, all healthy, no current attention after fresh scan, filtered search no matches, and data unavailable. Each shows truthful refresh/browse action and last update; none claim background work completed.
  5. Route triage actions through common action catalog, with one-row scope and explicit bulk selection. Queue/retry/repair confirmation/result text differentiates dispatched, processing, failed, and completed.
- Tests and rendered checks: resolver tests for every inclusion/exclusion, severity/tie, multi-reason single row, stale data, dismissal/reappearance, archive, search/preset intersection, all-clear variants, selection/group mutation, and no sensitive content. Render grouped/flat/all-clear/refresh-required at 1280x800/125%; keyboard/screen-reader test reason and action.
- Documentation / installer / release work: explain attention reasons/dismissal/all-clear semantics after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 source audit found cleanup review and primary recommendations but no unified cross-domain triage or safe all-clear state. 2026-09-29 added `MeetingAttentionInboxResolver`: it produces at most one metadata-only row per non-archived meeting, orders hard failure/block ahead of integrity/processing/suggestions, retains the common recommendation action target, and restricts dismissibility to eligible low/medium cleanup, summary, and metadata suggestions. A stale catalog reports refresh required rather than all-clear; fresh empty and fresh healthy cases are distinct. Resolver plus recommendation/state/preset focused tests passed 45/45 using an isolated build root. Layout binding and keyboard rendering are sequenced with the later view/action UI sprints.
- Remaining gap or next action: Sprint 6 — derive queue/backlog work as a dedicated Processing view.

- Make `Needs Attention` the triage center.
- Include failed, blocked, queued-needs-decision, suspicious, missing,
  summary-failed, and cleanup-recommended meetings.
- Group by reason when helpful.
- Allow dismissing low-risk recommendations without hiding hard failures.
- Show an all-clear state.

Acceptance: the user has one obvious place for "what needs me?"

### Sprint 6: Processing View

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: backlog work is readable as meetings and stages, with honest ETA/pause/priority state and safe recovery actions.
- Scope / non-goals: derive a Processing preset from existing queue/persisted backlog. Do not change worker scheduling, priority, interruption, ETA algorithms, or preempt capture.
- Dependencies and decisions: reuse Sprint 10 backlog resolver and S10A ASAP lifecycle. Live snapshot wins only while fresh; persisted state is `previously known backlog`, not live work. A queue item states identity/title, current/next stage, run/paused reason, priority/ASAP, recoverability, ETA confidence, and action eligibility; raw manifest/process detail remains Advanced.
- Implementation slices:
  1. Build pure `ProcessingViewResolver` over live snapshot, persisted backlog, meeting state, and now. Return active/queued/paused/recovery item rows, header counts, freshness, and `Live`, `Stale`, `Persisted`, or `Unavailable` source state.
  2. Define ETA: show stage/current/overall estimate only when sample-supported and fresh; otherwise `ETA learning`/`unavailable` with reason. Never derive zero/idle from missing snapshot; reset stale estimate on item/stage change.
  3. Filter Processing preset by current/recoverable queued/processing/paused work; include recently failed only with explicit recovery category, not as live queue. Preserve meeting identity/selection across queue events and explain disappeared item as completed, failed, archived, or refresh needed.
  4. Map actions: Process this first, Publish transcript first, Run labels later, Retry transcript, Clear ASAP, inspect setup/source. Each includes target/stage, precondition, confirmation when worker interruption or policy change possible, recording protection, and dispatch-versus-completion result text.
  5. Keep Rush Backlog/ASAP distinct, show one bounded priority target, preempted item recovery, label continuation, and pause-bypass scope. No bulk acceleration implicitly interrupts a worker or active recording.
- Tests and rendered checks: queue resolver tests for active/queued/paused, stale/persisted/empty, item disappearance, stage/ETA confidence, ASAP/preempted/clear, deferred labels, failed recovery, recording protection, and action eligibility. Extend existing strip/header tests; render long queues/action confirmations at 1280x800/125% with keyboard/accessibility labels.
- Documentation / installer / release work: document queue vocabulary/ETA confidence/priority boundaries after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found `ProcessingQueueStatusSnapshot`, strip/header projections, persisted fallback, ETA/diarization/ASAP tests. No dedicated bounded Processing view or cross-item action state contract. 2026-09-29 verified the existing app-wired `BacklogExperienceResolver` and `AsapLifecycleResolver`: fresh live queue truth, stale/saved-only refresh-required state, recording-protected pause, measured-only ETA, safe recovery intents, failure-stage routing, and transcript/publication/speaker-label ASAP continuation are all preserved. Focused backlog, ASAP, and XAML tests passed 72/72 using an isolated build root. Worker scheduling, priority, and capture behavior were not changed.
- Remaining gap or next action: Sprint 7 — unify selection eligibility and action preview state.

- Make `Processing` the backlog/work-queue view.
- Show active item, queued items, paused reason, ETA, ASAP request, and rush
  backlog state.
- Convert mechanics into actions: `Process this first`,
  `Publish transcript first`, `Run speaker labels later`, and
  `Retry failed transcript`.
- Keep interrupting or priority-changing actions explicit.

Acceptance: backlog management does not require worker or manifest knowledge.

### Sprint 7: Selection Strip Redesign

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: selection strip explains exactly what selected meetings can do, what will be skipped, and whether an action changes data, queues work, or opens reading.
- Scope / non-goals: unify selection presentation and action preview. Do not broaden bulk operation support, bypass existing validators, silently include hidden rows, or perform action from selection change.
- Dependencies and decisions: use Sprint 1 action catalog, Sprint 2 states, and stable meeting identity. `SelectionExperienceState` distinguishes none/single/multiple/busy/stale; every action returns eligible IDs, blocked IDs grouped by reason, target count, confirmation need, execution category, and recovery/result semantics. Selection snapshot freezes at action invocation; refresh mutation requires revalidation.
- Implementation slices:
  1. Build pure selection resolver for visible selected rows plus catalog freshness and busy state. No selection shows view/global next step; single shows read/details/recommendation; multiple shows grouped bulk choices and exact counts; stale/busy gives safe explanation/retry.
  2. Create per-action eligibility evaluator for read/artifact open, recommendation, rename/project, re-transcribe, speaker labels, merge/split, archive, delete, and priority actions. A count alone never asserts eligibility; output `eligible n`, `blocked n`, reason categories, and action-specific scope.
  3. Before dispatch, freeze selected identity/revision/filter context and show preview for consequential/bulk actions. Revalidate source/state/permissions after confirmation; changed/missing records are skipped with outcome, never substituted by new row index/selection.
  4. Separate action families visually: reading/navigation, safe metadata/queue, structural changes, and destructive delete. Artifact shortcuts remain secondary and accessible. Context menu, strip, keyboard, and detail action reuse same intent/result state; no silent semantic differences.
  5. After operation, retain surviving selection where safe, report succeeded/skipped/failed counts and safe details route, refresh once, and preserve unsaved non-conflicting drafts. Never claim queued work or partial batch as completed.
- Tests and rendered checks: selection resolver/action eligibility matrices including mixed selection, hidden/filter rows, stale/busy state, source mutation after preview, recording/queue guards, all confirmation/cancel paths, result summaries, and cross-surface parity. Render zero/single/multi/mixed/destructive states at 1280x800/125%; keyboard and screen-reader verify count/reason/confirmation.
- Documentation / installer / release work: document bulk preview/skip/result semantics after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found single/multi selection command/tool state, project bulk handling, context actions, and count-oriented tests. No shared per-action eligible/blocked model or immutable selection execution contract. 2026-09-29 verified the existing `MeetingActionCatalog` selection-availability input and resolved eligibility state, together with the workspace/selection command presentation and XAML wiring. The catalog carries cardinality, eligible/blocked counts, first blocked reason, confirmation policy, action family, and outcome target; busy state blocks re-entry and permanent delete remains typed-confirmed. Focused catalog, interaction, and XAML tests passed 188/188 using an isolated build root. Existing handlers retain their source revalidation and result reporting; no bulk support was broadened.
- Remaining gap or next action: Sprint 8 — verify and consolidate intentional action-family grouping across surfaces.

- Redesign by selection state:
  - no selection: view summary and global next step,
  - one selection: recommendation plus `Open Details`,
  - multi-selection: eligible bulk actions and counts.
- Keep artifact shortcuts reachable but secondary.
- Show blocked counts before bulk actions.

Acceptance: the strip explains context instead of listing unrelated commands.

### Sprint 8: Action Grouping

#### Implementation Record

- Status: `Done`
- Status history: `Planned` (2026-09-27 baseline); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: actions are easy to scan by intent, behave identically wherever invoked, and make permanence/recovery unmistakable.
- Scope / non-goals: reorganize existing action presentation/catalog. Do not alter command implementations, add new permissions, auto-run a group, or remove advanced access.
- Dependencies and decisions: consume Sprint 1 ownership, S7 eligibility/preview, Sprint 10 queue safety, and Sprint 14 copy. `MeetingActionCatalog` is single authority for intent, group, scope, label, accessible description, eligibility/blocked reason, confirmation, dispatch/result, recoverability, and advanced visibility. Groups: Open, Fix, Organize, Processing, DangerZone; navigation never mixed with mutation.
- Implementation slices:
  1. Inventory/map every current row/menu/strip/cleanup/detail/keyboard action to one catalog item; use explicit `Advanced` placement for infrequent diagnostics/custom controls. Validator fails unmapped, duplicated-conflicting, or unreachable actions.
  2. Resolve catalog per surface/selection using S7 evaluator; labels/eligibility/blocked text/confirmation/scope remain same. Surface may omit irrelevant item only when discovery route is documented; no direct handler creates alternate action semantics.
  3. Define group ordering and progressive disclosure: Open read/artifacts/details; Fix repair/retry/recommendation; Organize title/project/merge/split/archive; Processing priority/label stage/backlog; DangerZone permanent delete. Group summary exposes eligible/blocked counts, not disabled mystery buttons.
  4. Isolate archive versus delete: archive says location/recoverability and is non-destructive-only category; permanent delete uses typed meeting/count confirmation, immutable target preview, cancel, and result/recovery/support rules. Never include delete in recommendation/automatic/safe bulk group.
  5. Preserve keyboard/context behavior, visible focus, tooltips/accessibility, selection preview, busy/reentry protection, operation result summary, and Advanced discoverability through layout change.
- Tests and rendered checks: catalog completeness/parity tests; action group/order/scope/blocked/confirmation matrix for none/single/multi/busy/stale; delete exclusion from auto/recommendation; archive/delete copy/result; keyboard/context/detail parity and focus. Render compact/expanded groups at 1280x800/125% and screen-reader test headings/menu labels.
- Documentation / installer / release work: update action glossary/recoverability guidance after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 plan/source audit found actions spread through tool cards, context menu, cleanup and detail, with partial shared selection state but no canonical all-surface grouping contract. 2026-09-29 verified `MeetingActionCatalog` as the canonical Open/Fix/Organize/Processing/Danger taxonomy, context-menu family visibility and catalog presentation, and `MeetingDetailTaskCenter` family-to-section mapping. Archive is explicitly Organize/recoverable while permanent delete is Danger with typed confirmation; the catalog tests mechanically cover the deletion policy. Focused catalog, interaction, and XAML tests passed 188/188 using an isolated build root.
- Remaining gap or next action: Sprint 9 — consolidate cleanup discovery/review over the shared recommendation and inbox contracts.

- Group row/menu/detail actions into `Open`, `Fix`, `Organize`, `Processing`,
  and `Danger Zone`.
- Share labels, eligibility, and blocked reasons across row buttons, context
  menus, selection strip, cleanup review, and detail window.
- Keep delete isolated with typed confirmation.
- Keep archive clearly separate from permanent delete.

Acceptance: all power remains, but actions no longer compete visually.

### Sprint 9: Cleanup Consolidation

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: cleanup recommendation engine, review grid, safe batch execution, dismissal persistence, automatic-work ledger); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: cleanup appears as relevant meeting maintenance, while users retain a dedicated review path for scope, history, and exceptions.
- Scope / non-goals: consolidate presentation/entry points. Do not expand automatic safe actions, change fingerprint meaning, execute on refresh, include permanent delete, or treat dispatch as completion.
- Dependencies and decisions: Sprint 3 recommendation resolver and Sprint 5 inbox become normal discovery; existing cleanup engine/ledger remains execution authority. Each cleanup item has fingerprint/revision, action, affected records, safety/automation class, preview, current ledger state, dismissal, and manual-review route.
- Implementation slices:
  1. Route active cleanup candidates to row primary/secondary recommendation and Needs Attention with reason/scope; retain one primary across all domains and show lower cleanup candidates only as alternatives/review.
  2. Make Advanced Cleanup Review filtered/grouped view over same candidate snapshot, not separate recomputation. It exposes all candidates, dismissed/current/failed/queued/processing/completed ledger state, batch history, selection, and related meetings without changing ranking.
  3. Define `Apply Safe Fixes` preview: eligible count, excluded/blocked reason counts, action families, archive/recovery location, queue-dispatch versus immediate result, and confirmation only for scoped consequential behavior. Revalidate snapshot/fingerprints before each dispatch; independent failures continue and report exact outcomes.
  4. Preserve dismissal: only matching low-risk fingerprint is hidden from promotion; hard failures and non-dismissible safety issues stay in inbox/review. Changed inputs reappear; manual execution bypasses automatic suppression but retains validation/audit state.
  5. Enforce permanent-delete exclusion mechanically in engine/catalog/auto planner/UI. Archive and merge require their own recoverability/confirmation contract; no cleanup label disguises destructive action.
- Tests and rendered checks: cross-domain primary/inbox/review parity; snapshot consistency; preview/revalidation; mixed safe/blocked/failed batch; queue lifecycle; dismissal/reappearance; historical review; permanent delete exclusion; archive/merge route; no duplicate execution after refresh. Render row/inbox/Advanced review/batch result at 1280x800/125% with keyboard/screen-reader labels.
- Documentation / installer / release work: document cleanup as suggestion/review, preview/result vocabulary, dismissal, and automatic boundaries after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found separate cleanup grid/actions and persisted ledger with row primary recommendations, but no single recommendation/inbox/review information architecture or shared snapshot contract. 2026-09-29 verified the existing shared cleanup recommendation engine, primary recommendation route, filtered review projection, persistent automatic-work cache, bounded safe-batch planner/runner, and permanent-delete exclusion. Cleanup work is revalidated independently and result copy distinguishes dispatch from completion; dismissals remain promotion-only. Focused cleanup, recommendation, interaction, and source-contract tests passed 180/180 using an isolated build root.
- Remaining gap or next action: Sprint 10 — verify the read-first meeting detail task center and revision safety.

- Fold cleanup recommendations into row recommendations and `Needs Attention`.
- Keep bulk cleanup review as an advanced review path.
- Keep `Apply Safe Fixes` explicit with preview counts.
- Never include permanent delete in cleanup recommendations.
- Persist dismissed recommendations without hiding failures.

Acceptance: cleanup feels like guided maintenance, not a separate mini-app.

### Sprint 10: Meeting Detail Task Center

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: detail state builder/window, transcript/summary, maintenance events, stable-stem refresh, focused tests); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: a meeting opens to calm reading first, with truthful recommendation/status and every maintenance action available in an intentional section.
- Scope / non-goals: restructure detail presentation/state. Do not duplicate artifact ownership, mutate on open/refresh, change summary/speaker engines, or permit detail actions that bypass catalog/selection safety.
- Dependencies and decisions: use S1/S8 action catalog, S2 state, S3 recommendation, S7 preview, S11 reading, S12 speakers. `MeetingDetailExperienceState` is revision/freshness-aware and contains Read, Details, Fix, Organize, recommendation, artifact availability, busy/blocked states, and focus target. Detail follows selected stable identity; missing/archived/deleted source gets explicit closed/stale state.
- Implementation slices:
  1. Define Read default: transcript/summary/search/artifact open actions and concise status/provenance; no maintenance panel steals initial focus. Keep transcript/summary stale/failure/readability states truthful and keyboard accessible.
  2. Build Details as metadata/provenance/diagnostic disclosure; Fix from common retry/re-transcribe/summary/labels/repair/ASAP actions; Organize title/project/split/merge/archive; DangerZone permanent delete isolated. One recommendation/reason appears before action groups but cannot auto-run.
  3. Add revision token to loaded artifact/metadata/drafts. Refresh preserves unchanged drafts/scroll/search/focus; revision mismatch shows reload/conflict and revalidates mutation. Async result applies only if window still targets same identity/revision; canceled/closed window receives no stale update.
  4. Route event handlers through catalog/selection evaluator with action-specific confirmation/result. Detail action target is one meeting; multi/bulk route returns to list selection. Queue dispatch, summary generation, repair, archive/delete each distinguish accepted/running/succeeded/failed and refresh from source truth.
  5. Define deletion/archive behavior: archive keeps reading/recovery route per contract; permanent delete closes/clears detail after confirmed success, prevents deferred callbacks, and never reads stale artifacts. Missing/corrupt artifact routes to Repair/Help with raw diagnostics Advanced only.
- Tests and rendered checks: pure detail-state section/action/recommendation/blocked tests; revision/draft/async identity race, archive/delete/missing source, summary/speaker/queue transitions, catalog parity, focus/keyboard escape. Render Read/Details/Fix/Organize/DangerZone at 1280x800/125% with long transcript, no artifact, and high contrast.
- Documentation / installer / release work: document detail section/action semantics after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found current event-rich detail window and state tests but no read-first section contract, revision-safe editing model, or all action family/catalog parity. 2026-09-29 verified the existing `MeetingDetailTaskCenterResolver`, revision token, read/fix/organize/danger catalog mapping, task-center application in the detail window, and draft-safe refresh disposition. Material background revisions preserve active drafts and offer reload; archived/deleted/identity-invalidated detail closes safely. Focused detail, recommendation-presentation, and XAML tests passed 56/56 using an isolated build root.
- Remaining gap or next action: Sprint 11 — verify transcript-first reading and independent summary state.

- Rebuild detail around `Read`, `Details`, `Fix`, and `Organize`.
- Default to `Read`: transcript, summary, search, artifact shortcuts.
- Put one recommendation and reason at the top.
- Move retry, re-transcribe, speaker labels, summaries, split, and repair to
  `Fix`.
- Move rename, project, archive, and permanent delete to `Organize`.

Acceptance: users can read calmly while every maintenance action remains
reachable.

### Sprint 11: Transcript And Summary Reading

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: transcript reader/filter, detail summary states, provider configuration/routing, summary tests); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 reader-state implementation and focused verification).
- User outcome: users can read/search transcript calmly regardless of summary availability; summary is useful supplemental output with truthful provenance and one setup/retry path when appropriate.
- Scope / non-goals: unify reading presentation/state. Do not alter summary provider selection, consent, summarization/chunking, transcript artifacts, or expose hosted/private payloads.
- Dependencies and decisions: consume Sprint 10 Read state and Sprint 12 trust flow. `MeetingReadingState` independently models transcript (`Readable`, `Missing`, `Loading`, `Corrupt`, `SearchNoMatch`) and summary (`Generated`, `Generating`, `Disabled`, `Unconfigured`, `Unavailable`, `Failed`, `Stale`) with source/revision/provenance. Transcript success is never inferred from summary; summary failure does not block reading.
- Implementation slices:
  1. Build pure reading resolver from published artifact/read result, search query, summary status, provider configuration/consent, and focus. Return panels/order, primary reader content, actionable summary state, sanitized status, and accessible navigation.
  2. Default focus/content to transcript; persist or safely restore in-window search/query/scroll when same transcript revision. Summary expands only when generated/user-opened or a concise state requires notice; it cannot reorder/obscure transcript or trigger generation by opening.
  3. Define summary routes: disabled/unconfigured/consent/setup opens correct Settings intent; unavailable explains required structured transcript; failed exposes retry from same current artifact; generating shows cancellation-safe progress; stale result shows transcript revision mismatch and regenerate option. Provider/model metadata is secondary, no raw endpoint/error/key.
  4. Keep search behavior scoped to readable transcript with match count/next/previous/no-match/accessibility; no transcript means artifact/Open/Repair routes. Generated summary is immutable result tied to fingerprint and shows actual authorized route/fallback compactly.
  5. Route all reader controls through detail state/action catalog; async generation/read refresh only applies to same detail identity/revision and preserves reader draft/focus. Normal activity/status never repeats privacy warnings or raw exceptions.
- Tests and rendered checks: reading resolver matrix for all transcript/summary combinations, stale fingerprint, generated fallback provenance, disabled/setup/consent, corrupt/missing artifacts, search navigation/no match, refresh identity race, and detail/list parity. Render long transcript/empty/error/summary at 1280x800/125%, high contrast, keyboard and screen-reader headings/live status.
- Documentation / installer / release work: document transcript-first and summary state/provenance/setup behavior after ship; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found `MeetingTranscriptReaderResult`, summary-specific detail state and setup actions; no unified transcript/summary reader contract or proof that summary issues never disrupt reading. 2026-09-29 added pure metadata-only `MeetingReadingResolver`: it keeps readable transcript first when summary fails, distinguishes no-match from missing transcript, and gives concise setup/consent/unavailable/retry/stale-summary routes without provider or transcript payloads. Focused reader, existing detail, and interaction tests passed 136/136 using an isolated build root.
- Remaining gap or next action: Sprint 12 — unify Meetings-specific speaker workflow routes.

- Treat transcript and summary as one reading workflow.
- Show summary unavailable states without implying transcript failure.
- Route summary setup gaps to Settings.
- Keep summary metadata secondary.
- Hide irrelevant summary action buttons when summaries are disabled or
  unavailable.

Acceptance: reading is calm, summaries are supplemental, and setup gaps are
actionable.

### Sprint 12: Speaker Workflow Clarity

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: speaker actions, detail review controls, labels/profile services); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: users never confuse adding anonymous labels, repairing diarization, reviewing local name suggestions, or managing reusable profiles.
- Scope / non-goals: apply Whole-App Sprint 13 speaker contract within Meetings row/selection/detail/action grouping. Do not duplicate profile/embedding logic, alter diarization model behavior, or change learning policy.
- Dependencies and decisions: reuse `SpeakerExperienceState`, terminology, learning authority, and local-only privacy boundary from Whole-App Sprint 13. Meetings action router maps `LabelsMissing/Queued/Running/Ready/Suspicious`, `NamesReadyForReview`, `SamplesUnavailable`, `ProfilesUnavailable`, `LearningDisabled`, `RepairIneligible`, `RefreshRequired` to exactly one next route/action.
- Implementation slices:
  1. Map anonymous labels and repair to Fix/Processing meeting actions; map Use/Reject/Apply Name Changes/Refresh Local Suggestions/Undo Profile Names to detail speaker review; map profile enable/disable/delete and learning preference to Settings. Preserve advanced diagnostic route without raw vectors/audio paths.
  2. Apply common state/terminology to row badges, recommendation, inbox, selection strip, context menu, detail, and bulk preview. Label action never means name assignment; suggestion never means confirmed identity; repair never overwrites user edit before reprocessing succeeds.
  3. Define availability/eligibility and target scope: model/setup/audio/transcript/manifest, queue state, labels, samples, profile/learning state, current artifact revision, and selected count. Bulk Add/Repair previews eligible/blocked counts; names review is per meeting/revision unless explicit future plan expands it.
  4. Route result states with truthful dispatch/running/completed/failed outcomes and local data explanation. Profile change affects future matching as defined by Whole-App plan; historic user names remain authoritative.
- Tests and rendered checks: cross-surface state/action parity, terminology lint, unavailable state, single/bulk label eligibility, detail name review, stale revision, repair retry, local-only copy, and no profile data leakage. Render row/selection/detail/Settings transitions at 1280x800/125%; keyboard/screen-reader validate names/reasons.
- Documentation / installer / release work: reuse Whole-App Sprint 13 docs; release gate Sprint 16.
- Evidence and date: 2026-09-27 review found controls but overlapping wording/action surfaces and no Meetings-specific shared route contract. 2026-09-29 verified the existing `SpeakerExperienceResolver` and meeting/settings wiring: labels missing/queued/running/ready/suspicious, repair eligibility, names ready for review, unavailable samples/profiles, disabled learning, and refresh state each route to bounded actions with local-only copy. Focused speaker, recommendation, and XAML tests passed 82/82 using an isolated build root.
- Remaining gap or next action: Sprint 13 — complete common bulk-operation planning and per-target outcome handling.

- Separate anonymous speaker labels, speaker-label repair, speaker-name
  learning, and profile suggestions.
- Put `Add Speaker Labels` and `Repair Speaker Labels` under meeting fixes.
- Put `Apply Speaker Names`, `Refresh Suggestions`,
  `Undo Name Recognition`, `Use`, and `Reject` under speaker-name review.
- Explain unavailable states for no labels, no samples, no profiles, disabled
  learning, and repair ineligibility.

Acceptance: users know whether they are labeling, naming, or repairing
speakers.

### Sprint 13: Bulk Operations

#### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: privacy redaction, accessible review semantics, and focus return implemented); `Done` (2026-09-29 bulk plan foundation and focused verification).
- User outcome: people can apply a supported repeated action to a known set of meetings, know what will happen before dispatch, and recover from individual failures without losing the unfinished rows.
- Scope / non-goals: build one bulk-operation contract for existing archive, project/label, recommendation, speaker-label, re-transcribe, and merge entry points. Keep merge as its own multi-source workflow; do not invent bulk permanent deletion, cross-meeting name assignment, or a generic transaction where the underlying operation cannot be atomic. Permanent delete/recovery belongs to Sprint 14.
- Dependencies and decisions: reuse Sprint 2 meeting state/revision, Sprint 3 recommendation, Sprint 7 selection, Sprint 8 action catalog, Sprint 9 cleanup, Sprint 12 speaker, and Whole-App Sprints 8/10/11 safety contracts. `MeetingCleanupRecommendationBatchRunner` already continues through ordinary per-item errors, but its result model is cleanup-specific and has no skipped/cancelled states; replace or adapt it behind a common planner rather than leaking it into every action.
- Implementation slices:
  1. Define `BulkOperationPlan` from an immutable target snapshot: meeting identity, displayed title, artifact revision, source view/filter, requested action, and capability/version. Revalidate each target immediately before mutation/queueing; state changed, missing, active recording, duplicate queue, unsupported capability, or insufficient selection becomes an explicit skip/block reason.
  2. Add a planner for each action that returns target counts and a per-row preview: eligible, skipped/blocked with reason, action-specific side effect, queue versus immediate behavior, expected recoverability, and irreversible-risk flag. Only enable a command when its planner finds eligible targets; do not force users to infer eligibility from a disabled button.
  3. Run targets independently except where the existing domain action is genuinely atomic. Capture per-target outcomes as `Succeeded`, `Queued`, `Skipped`, `Failed`, or `Cancelled`; first cancellation stops undispatched work, preserves already completed work, and records remaining targets rather than falsely claiming rollback. Continue after ordinary errors only when the action policy permits it.
  4. Refresh the affected views once after completion, retain failed/skipped/cancelled rows in a result panel with retryable reason and safe retry action, reconcile selection against deleted/stale rows, and write one concise activity entry with counts—not one disruptive dialog per target. Keep command execution serialized with conflicting cleanup/queue/recording work.
  5. Make destructive behavior explicit: archive is reversible only where its storage contract says so; permanent delete is absent from the generic bulk menu until Sprint 14 supplies typed confirmation, artifact-class preview, and recovery semantics. Never silently apply a recommendation or overwrite a user edit because it appeared in a bulk selection.
- Tests and rendered checks: unit-test plans for mixed eligibility, stale revision, selected-count boundaries, action-specific risk copy, revalidation race, duplicate dispatch, continue/fail-fast policy, cancellation before and during dispatch, and exact result counts. Integration-test queue contention and refresh/selection reconciliation. Render single and 2/10/100-row selection at 1280x800/125%, keyboard-only preview/cancel/retry, screen-reader result/reason announcements, and no permanent-delete affordance outside Sprint 14.
- Documentation / installer / release work: document bulk scope, result meanings, cancellation, retry, queue behavior, and archive versus delete boundary in the user help/release notes. No installer work until shipped behavior changes; release gate remains Whole-App Sprint 16.
- Evidence and date: 2026-09-27 source audit found independent selected-row loops for project, cleanup, merge, speaker labels, archive, and recommendations. Cleanup has `MeetingCleanupRecommendationBatchRunner` success/failure counts, but selection/action UI lacks one preview/revalidation/result contract. 2026-09-29 added `BulkOperationPlanner` with immutable target snapshot fields, capability-scoped per-row eligibility, queue/archive side-effect copy, and distinct succeeded/queued/skipped/failed/cancelled outcomes. Permanent delete is intentionally excluded until Sprint 14. Focused bulk/catalog/cleanup planner tests passed 26/26 using an isolated build root.
- Remaining gap or next action: Sprint 14 — add receipt-backed archive/delete preflight and recovery trust.

- Add bulk previews with eligible count, blocked count, destructive risk, and
  per-row outcome behavior.
- Keep merge, add labels, re-transcribe, archive, apply recommendations, and
  delete under bulk categories.
- Bulk permanent delete requires typed confirmation and lists affected artifact
  classes.
- After execution, show success/failure counts and keep failed rows visible.

Acceptance: bulk work stays powerful but no longer surprising.

### Sprint 14: Archive, Delete, And Recovery Trust

#### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: archive/delete service, typed confirmation, action-state tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 shared preflight implemented; receipt-backed restore remains).
- User outcome: users can distinguish moving published artifacts aside from erasing them, find enough evidence to recover an archive safely, and see exactly which meetings succeeded or need attention.
- Scope / non-goals: make user-requested archive, recovery, and permanent delete safe and consistent across row, selection, and detail surfaces. Do not promise OS recycle-bin recovery, restore a deleted meeting, delete arbitrary folders, conflate maintenance repair backups with user archive, or permit archive/delete through recommendations or safe automation.
- Dependencies and decisions: build on S2 freshness, S7 immutable selection, S8 taxonomy, S9 cleanup exclusion, S10 detail lifecycle, and S13 batch results. Current `ArchiveMeetingAsync` moves published audio/markdown/json/ready-marker to a timestamped archive category and leaves linked session folders; `DeleteMeetingPermanentlyAsync` removes those published artifacts and then a linked session folder when present. Model that exact scope with a receipt; do not call archive a complete backup or delete a reversible operation.
- Implementation slices:
  1. Create a preflight from immutable, revalidated target records that inventories each artifact class, source existence, linked-session scope, archive destination/category, collision/permission/busy state, and recovery eligibility. Preview eligible/blocked targets and state “move to archive” versus “permanently erase” in plain language before dispatch.
  2. Write an archive receipt only after each successful move, with stable meeting identity, original logical locations/artifact classes, archive execution id/category, timestamp, and non-secret integrity/size evidence. Keep it beside the archive execution and expose a human-safe “Open archive/recovery details” route; raw paths stay advanced/copy-on-request. Repair/automatic archives use a distinct category/receipt and are never offered as ordinary user archive.
  3. Add archive recovery only for a complete, receipt-backed published-artifact set whose original targets remain safe to restore. Preflight missing/tampered receipt, artifact drift, destination collision, active processing, and unavailable original location; use collision-safe staging/rollback for a single restore. A recovery result must say whether the published record returned and whether its separate work-session folder was never archived, rather than fabricating a full restoration.
  4. Route permanent delete through the same preflight/result engine. Require a focused typed `DELETE` confirmation after the final count/artifact list is shown; modal escape/cancel does nothing. Revalidate immediately before each delete, never remove beyond documented publish artifacts and that target's linked session folder, and report `Succeeded`/`Skipped`/`Failed`/`Cancelled` per target without claiming rollback.
  5. Treat archive/recovery/delete as exclusive maintenance work: prevent concurrent queue/recording/conflicting cleanup mutations, refresh source truth once, clear/close stale selection/detail safely, retain failures with retry/recovery reason, and record concise local audit activity. No permanent delete appears in recommendation or automatic paths.
- Tests and rendered checks: service tests for complete/partial archive receipt, missing artifact, move/copy failure, collision-safe restore/rollback, tampered receipt, distinct repair archive, source/session-folder scope, and cloud placeholder behavior. UI/integration tests for shared preflight in row/bulk/detail, exact typed confirmation, cancel/escape, stale target/revalidation, partial batch error/cancel, blocked recording/queue, selection/detail refresh, and delete absence from recommendations/automation. Render one/many target preview and results at 1280x800/125%, high contrast, keyboard focus return, screen-reader artifact/count/irreversibility announcements.
- Documentation / installer / release work: document archive location/recovery limits, repair-backup distinction, artifact/session scope, typed delete, and unsupported recovery plainly in help/release notes. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 source audit found a common `TryConfirmPermanentDelete` typed-`DELETE` dialog and service tests for artifact move/delete. It also found no archive receipt or restore service, archive/delete loops that stop on first exception, and no cross-surface preflight/result/recovery contract. 2026-09-29 added pure `MeetingArchivePreflight`, defining revalidated archive/recovery/delete scope, artifact classes, busy/missing/receipt/collision blocks, linked-session scope, and typed-delete requirement without file mutation. Focused preflight, execution-service, and confirmation tests passed 139/139 using an isolated build root.
- Remaining gap or next action: add a receipt schema, write receipt only after a complete archive move, then implement collision-safe receipt-backed restore and failure/rollback tests before returning Sprint 14 to Ready.

- Make archive visibly recoverable.
- Show archive destination and recovery expectation without unnecessary
  internal paths.
- Keep generated repair backups distinct from user-managed archive folders.
- Standardize delete confirmation across row, bulk, and detail paths.
- Keep permanent delete outside recommendations and safe automation.

Acceptance: users understand reversible versus irreversible actions.

### Sprint 15: Search And Metadata Simplification

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: workspace substring search, attendee backfill, detail/inspector metadata); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 query projection and focused verification).
- User outcome: users can find a meeting from its human metadata or visible state, understand why it matched, and distinguish absent metadata from metadata still being safely enriched.
- Scope / non-goals: simplify the existing Meetings search and metadata display. Search only local meeting metadata and derived visible states; do not index transcript body, call a hosted service, expose calendar/raw source data, create an advanced query language, or make enrichment a prerequisite for normal search.
- Dependencies and decisions: reuse Sprint 2 freshness/state, Sprint 3 recommendation reason, Sprint 4 view/filter persistence, Sprint 5 inbox, Sprint 7 selection, and Sprint 16 refresh. Current `MeetingMatchesWorkspaceSearch` is a case-normalized substring match over title/project/platform/status/key attendees/attendees; attendee backfill is capped, cache-aware, local persistence after a qualified calendar match. Replace ad-hoc row predicates with a pure query projection rather than changing enrichment matching policy.
- Implementation slices:
  1. Define `MeetingSearchDocument` per current row with normalized display-safe fields: title, project, platform, lifecycle/status, started local date/day, transcript availability, primary recommendation reason/action, key attendees, and confirmed attendee names. Include field provenance/freshness (`Captured`, `Enriched`, `Unavailable`, `Pending`, `NotApplicable`) without exposing paths, calendar subjects, or unverified candidate data.
  2. Keep one forgiving text box: whitespace tokens must all match any searchable field; quoted phrase matches one normalized field phrase; empty text matches all. Add a small deterministic date filter (Today, Last 7 days, This month, Custom local-date range) and optional status/transcript/recommendation chips only when each replaces an existing control or is materially selected. Invalid/incomplete date input is visible, non-destructive, and never silently widens a search.
  3. Return a pure `MeetingSearchResult` with visible rows, total/matched count, active-filter summary, per-row match categories for accessible explanation, and no-result guidance. Counts reflect the same snapshot as the list; search/filter changes keep selected identities but indicate when selected rows are hidden, never mutate metadata, and preserve the query/focus across safe refreshes.
  4. Present metadata progressively: title/date/platform/status first; project, attendees, transcript/recommendation detail only when present or selected. Show `Looking for attendees`, `No matching attendee found yet`, `Enrichment unavailable`, and `Attendees added` as distinct non-alarming states. Automatic metadata-only backfill stays bounded/cache-aware and cannot alter title, project, user-entered key attendees, or active search selection unexpectedly.
  5. Wire grouping, preset, inbox, detail, selection strip, and cleanup review to the same query snapshot or an explicit documented subset. Search text/chips never control action eligibility; after enrichment or artifact refresh, recompute against a revision/version and apply only if the same query/view remains current.
- Tests and rendered checks: unit-test token/quote normalization, all field coverage, local-date/time-zone boundaries, invalid range, chip intersection, empty/no-result copy, recommendation/status/transcript truth, provenance redaction, and query snapshot stability. Test enrichment pending/no-match/error/success, cache/batch limits, manual key-attendee protection, re-query after async completion, selection hidden/restored, and list/detail/inbox subset parity. Render empty/one/many results and long names at 1280x800/125%, keyboard query/chip/reset flow, focus retention, high contrast, and screen-reader count/filter/match explanations.
- Documentation / installer / release work: document searchable fields, date interpretation, transcript metadata-only boundary, attendee source/provenance, and refresh/enrichment limits in help/release notes. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 source audit found `MeetingMatchesWorkspaceSearch` filtering title/project/platform/status/attendees, background attendee backfill with no-match cache and 25-record batch, and detail attendee metadata. No common search-document/result model, date/transcript/recommendation fields, metadata provenance states, or cross-surface query consistency proof was found. 2026-09-29 added pure `MeetingSearchResolver` with display-safe document fields, all-token and quoted-phrase semantics, result count, and accessible match categories. It never indexes transcript body or mutates metadata. Focused search and existing workspace interaction tests passed 138/138 using an isolated build root.
- Remaining gap or next action: Sprint 16 — derive safe refresh/coalescing policy and last-good presentation state.

- Search title, project, key attendees, platform, status, date, transcript
  availability, and recommendation reason.
- Add lightweight chips/counts only when they replace complexity.
- Keep attendee enrichment automatic and metadata-only where already supported.
- Show enrichment loading versus unavailable states.

Acceptance: finding meetings is simple without losing project/attendee
richness.

### Sprint 16: Safe Background Refresh

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: queued fast/full refreshes, version cancellation, attendee/cleanup background work); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 refresh-state implementation and focused verification).
- User outcome: the Meetings view stays current without ritual manual refresh, while capture and active reading remain responsive and every delayed or failed update is clearly explained.
- Scope / non-goals: coordinate catalog/list/metadata/recommendation refreshes and their visible status. Do not poll external services continuously, alter recording/processing semantics, execute cleanup automatically beyond existing policy, or refresh hidden data merely to make a status label look current.
- Dependencies and decisions: reuse S2 freshness, S4 persisted view, S9 cleanup ledger, S15 query snapshot, S17 imported parity, and Whole-App S10 queue/recovery. Existing code coalesces `Fast`/`Full` requests, versions/cancels superseded work, and starts cleanup/attendee work only for full refresh, but `ShouldDeferMeetingRefresh` currently defers only recording despite receiving tab context, and baseline failure clears the displayed list. Replace booleans/counters as public meaning with a pure refresh coordinator state; retain the existing generation token at the async boundary.
- Implementation slices:
  1. Define a `MeetingsRefreshRequest` (reason, requested freshness, source version, selected identity, query/view revision, user-initiated flag) and `MeetingsRefreshState`: `Current`, `Refreshing`, `Deferred`, `Stale`, `RetryNeeded`. State includes last successful baseline/full times, current/deferred reason, completed stages, and next safe action; it never exposes internal paths/exceptions.
  2. Coalesce requests by newest source/config revision and strongest mode, retaining the latest stable selected identity—not arbitrary arrival order. A recording gate defers catalog/heavy work; a Home/non-Meetings gate defers full cleanup/enrichment unless a correctness-critical publish/mutation needs a lightweight snapshot. On stop/tab entry, dispatch one accumulated request. Manual refresh is always visible and intentional, but it joins the same coordinator and never duplicates a running scan.
  3. Split refresh stages: baseline catalog is fast and authoritative for list truth; recommendation and attendee enrichment are bounded, cache/fingerprint-aware, cancellable stages that may finish later. Track stage provenance/version; a late result applies only to the same source/query/view generation and schedules one follow-up baseline only when persisted metadata actually changed. Do not rerun cleanup/enrichment for unchanged records or on each visibility toggle.
  4. Preserve the last successful rendered snapshot, selection, expanded groups, query, detail identity, and in-progress safe drafts when a refresh is deferred, canceled, or fails. On failure, mark `RetryNeeded` with sanitized reason and a retry action; never replace known rows with empty data. On successful deletion/archive/import/processing publish, reconcile removed/changed identities safely and announce precise freshness.
  5. Make state visible but quiet: current/fresh time only when useful; a compact live status for refreshing, deferred with why, stale with what is safe to view, and retry-needed. Do not call normal cached data inaccurate merely because optional enrichment is pending; distinguish `List current; details loading` from a stale catalog.
- Tests and rendered checks: pure transition matrix for sources/modes/gates/coalescing/last-request wins, recording and Home deferral, manual request, version supersession, source/config changes, stage completion, retry, and shutdown. Integration tests for published/renamed/archived/deleted/imported/queue-completed changes, no duplicate expensive scans, metadata-only follow-up, cancellation race, last-good retention, selection/query/detail/draft preservation, and freshness copy. Render every freshness state at 1280x800/125%, tab switch/recording stop, keyboard retry/manual refresh, focus retention, and screen-reader non-spam live announcements.
- Documentation / installer / release work: document automatic refresh triggers, deferred conditions, freshness wording, manual retry, and metadata-only background work. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 source audit found fast/full refresh modes, pending request coalescing, refresh-version checks, full-only cleanup/attendee stages, and rendered refresh text. It found no explicit freshness state/state tests, no Home-tab deferral despite tab input, and a baseline exception path that empties the existing meeting list. 2026-09-29 added pure `MeetingsRefreshCoordinator` covering Current/Refreshing/Deferred/Stale/RetryNeeded presentation, strongest-mode/latest-selection coalescing, recording deferral, last-good retention, and manual retry. Focused coordinator and existing refresh/source-contract tests passed 45/45 using an isolated build root.
- Remaining gap or next action: Sprint 17 — add display-safe imported-meeting provenance and parity contract.

- Auto-refresh Meetings when opened, after publish, after retry/repair, and
  after safe enrichment.
- Coalesce refreshes while recording or when Home is active.
- Make manual refresh secondary, not the normal correctness path.
- Show current, refreshing, deferred, stale, and retry-needed states.
- Avoid repeated expensive cleanup or attendee scans for unchanged rows.

Acceptance: users trust the list without habitually pressing refresh.

### Sprint 17: Imported Meeting Parity

#### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: import preflight/queue, manifest provenance, catalog marker handling, import tests); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation audit and focused verification).
- User outcome: an imported audio file becomes an ordinary meeting in the same library and task flow, with one concise origin cue and recovery guidance only when import-specific information matters.
- Scope / non-goals: carry existing imported-audio sessions through the same Meeting state/action/detail/archive/delete/retry/speaker/summary routes. Do not modify the external original file, expose its absolute path, treat imported audio as live capture, fabricate provenance for legacy records, or add a second “Imports” library/tab.
- Dependencies and decisions: reuse S1 contract, S2 state, S4 presets, S8 action catalog, S10 detail, S12 speakers, S14 archive/delete, S16 refresh, and Whole-App S10 queue recovery. `ExternalAudioImportService` copies the source into its session processing root, preserves the original, and writes `ImportedSourceAudioInfo` (path, display name, size/time, method, retained flag, probe); catalog manifest selection carries only an imported flag. `.ready` remains the published-artifact authority, not a proxy for whether an original source is still available.
- Implementation slices:
  1. Define an `ImportedMeetingProvenance` projection with `Imported`, display-safe source name, import method, retained/availability state, and processing-copy/published-artifact state. Keep original path, source fingerprint, and probing diagnostics out of default row/detail/search; show `Source: Imported audio` plus optional safe display name in Details only.
  2. Map imported sessions through the same canonical state resolver and action catalog. Presets, search, recommendations, inbox, grouping, selection, detail sections, bulk eligibility, archive/delete confirmation, summary, speaker labels/names, and refresh consume identity/revision/state—not capture origin—unless a capability genuinely requires a source file.
  3. Define the narrow divergence table: pre-publish imported source has queued/processing/retry/cancel recovery; missing processing copy/source drift is a `Fix` route that explains whether re-add/re-import is required; imported source is never opened/deleted/renamed by a normal meeting action. Re-transcribe uses the managed processing/published artifact when valid; otherwise it routes to import recovery with preserved title/date/project draft rather than silently queueing an impossible job.
  4. Preserve artifact contracts across import/publish/retry: source copy verification precedes queue, manifest provenance persists through state transitions, `.ready` appears only after valid publish, duplicate/superseded import detection is deterministic, and an imported row refreshes into the same published representation without duplicate rows. Do not auto-merge records solely because source file names match.
  5. Apply S14 semantics consistently: archive/delete preview lists published artifacts and linked session scope, not the retained external original. Archive receipt/recovery names imported provenance safely; permanent delete never deletes the original import source. After an action, reselect by stable meeting identity and keep any contextual recovery detail current.
- Tests and rendered checks: test picker/drag/drop/watched-folder origin mapping, source copy/preserve, private path redaction, missing/offline/changed source, malformed/legacy provenance, retry/re-import decision, duplicate/superseded imports, `.ready`/publish/retry transitions, and no original-source mutation on archive/delete. Assert parity matrices for imported and captured states across preset/search/inbox/action/detail/bulk/refresh and expected capability exceptions. Render queued/processing/published/failed imported detail at 1280x800/125%, keyboard recovery/focus return, long source display names, high contrast, and screen-reader origin without a path.
- Documentation / installer / release work: document that importing copies audio for processing while leaving the chosen original intact, what `Source: Imported audio` means, which artifacts archive/delete affects, and source-missing recovery. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 source audit found source-copy queueing with `ImportedSourceAudioInfo`, duplicate/ready-marker checks, imported manifest catalog priority, and service coverage for preservation/metadata. It found no visible imported provenance contract in Meetings, no parity matrix across actions/detail, and no explicit boundary proving archive/delete cannot touch the original source. 2026-09-29 verified the existing `MeetingOriginResolver` and catalog origin projection: imported source display/method/retention fields are safe, paths/fingerprints/sizes are absent, and source-copy/publish/catalog preservation remains covered. Focused origin, import service, and catalog tests passed 58/58 using an isolated build root.
- Remaining gap or next action: Sprint 18 requires a selected supported visual runtime; it remains Blocked by the recorded runtime/fixture prerequisite.

- Ensure imported meetings use the same presets, recommendations, action
  groups, detail sections, archive/delete rules, retry, summary, and
  speaker-label paths.
- Show `Source: Imported audio` in detail without full private paths.
- Keep import-specific recovery contextual.
- Preserve `.ready` and artifact contracts.

Acceptance: imported meetings do not create a second mental model.

### Sprint 18: Rendered UX Polish

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-27: S1 has no selected signed/licensed runtime and Standard-model tuple).
- User outcome: Meetings feels like one intentional, dense desktop workbench—not a collection of repaired panels—and its important controls/statuses remain legible at supported sizes.
- Scope / non-goals: render/polish the Meetings library, selection strip, action groups, cleanup/recovery results, and meeting detail after the prior interaction sprints. Follow `DESIGN.md`; do not redesign product flows, invent visual branding, hide required controls merely to reduce density, or replace accessible text with icon-only controls.
- Dependencies and decisions: render only against the settled S4–S17 state/action contracts. `DESIGN.md` is authoritative: opaque tonal nesting, 1px technical outlines/inset wells, no drop shadows/gradients, maximum 4px radius, Segoe UI interaction text, Cascadia Mono with Consolas fallback for changing technical data, and dense spacing. Existing XAML structure/tests establish a 1280px minimum width; validate that supported floor before treating a narrower viewport as a defect.
- Implementation slices:
  1. Create a rendered-state fixture catalog for library empty/one/many/grouped/search-no-result/processing/needs-attention/multi-select/batch-results/archive-delete/retry/imported and detail read/fix/organize/error states. Fixture data is local/sanitized, deterministic, and captures the relevant state/action/copy contract—not screenshots of private meetings.
  2. Establish component measurements/tokens: stable preset/search/refresh toolbar lanes; minimum action-button hit area and label behavior; bounded row-action columns; compact status wells; consistent group/selection/result spacing; detail section heading/action lanes. Use grid/star sizing and wrapping/overflow rules deliberately; no overlapping controls, clipped essential labels, unbounded grids, or visual reliance on hover-only actions.
  3. Apply Technical Studio surface hierarchy: base surface, low structural section, high inset data well, opaque backgrounds, 1px outline/outline-variant edges, 4px-or-less corners, and no shadows. Reserve signal green for live recording and amber for technical warning only; normal queue/refresh/recommendation text must not masquerade as live/error severity.
  4. Preserve hierarchy under density: primary recommendation/current state is visible without displacing transcript/list work; secondary metadata collapses progressively; destructive/recovery result stays in the initiating viewport; long title/project/attendee/error text wraps or truncates with accessible full text. Use mono only for timestamps/counts/technical metadata, not arbitrary prose.
  5. Run a repeatable image QA matrix at the supported 1280x800 floor and normal 1440x900/1920x1080 layouts, 100% and 125% scaling, with long localized-style strings and high contrast. Capture before/after artifacts for intentional layout changes, classify defects by clipping/overlap/hierarchy/contrast/state mismatch, and fix smallest scope first.
- Tests and rendered checks: retain structural XAML checks for stable named surfaces, add token/forbidden-shadow/oversize-radius assertions where practical, and run snapshot/interactive checks from the fixture catalog. Verify resize, tab/detail opening, scroll/focus position, selection strip insertion/removal, dialog result visibility, disabled/blocked states, high contrast, and no horizontal essential-content loss at the supported floor. Human review explicitly checks visual hierarchy against `DESIGN.md`, not only pixel similarity.
- Documentation / installer / release work: record supported visual QA sizes/scales and Technical Studio acceptance in developer UI guidance/release evidence. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 review found `DESIGN.md`, structural MainWindow/Meetings XAML tests, and a 1280px minimum-width assertion, but no deterministic Meetings state fixture catalog, viewport/scaling matrix, or evidence that action/status/detail states meet the design-system rules.
- Remaining gap or next action: inventory the concrete Meetings XAML containers against the fixture catalog, then render the 1280x800 empty, multi-select, and detail-error cases before adjusting any shared styles.

- Verify redesigned Meetings surfaces against `DESIGN.md`.
- Use stable dimensions for presets, toolbar, row actions, status wells,
  grouped actions, and detail sections.
- Prevent text overflow and overlapping controls.
- Keep primary feedback in the current viewport.
- Validate normal and smaller desktop window sizes.

Acceptance: simplification is visible, dense, and polished.

### Sprint 19: Accessibility And Keyboard QA

#### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-28 pure opaque aggregate GPU policy and provider-readiness fence).
- User outcome: a person can discover, navigate, understand, and complete every Meetings task by keyboard or screen reader without hidden context, lost focus, or noisy background announcements.
- Scope / non-goals: make the post-S4–S18 Meetings surfaces semantically operable. Do not create alternate hidden control trees, replace WPF native controls unnecessarily, assign undocumented global shortcuts, or make tooltip hover the only explanation of an action/state.
- Dependencies and decisions: depend on S4 preset semantics, S7 selection, S8 catalog, S10 detail, S13 result panel, S14 confirmation, S15 search, S16 refresh, and S18 rendered fixtures. Current XAML has limited explicit tooltips/access-key recognition and code has isolated focus returns; use native semantics first, then `AutomationProperties.Name`, `HelpText`, `LabeledBy`, `ItemStatus`, and carefully scoped live settings where native names are insufficient.
- Implementation slices:
  1. Publish a focus graph for each state: tab/preset → search/filter/reset → refresh/view controls → meeting grid/row actions/context menu → dynamic selection strip/result panel → cleanup review → detail Read/Details/Fix/Organize/Danger Zone → modal confirmation/recovery. Define initial focus, Tab/Shift+Tab order, arrows within list/group/menu, Enter/Space behavior, Escape/cancel, and the exact return target after close/success/failure.
  2. Make every interactive compact/grouped control self-describing: visible text labels where space permits; otherwise accessible name plus action, target count, eligibility/blocked reason, destructive/recovery consequence, and shortcut only when implemented. Associate inputs to labels; expose group headings/counts and selected-state; keep disabled action reason available through help text/status, not color or tooltip alone.
  3. Treat DataGrid and context/menu behavior deliberately: row selection and current row are announced coherently; selection changes announce count once; action menus are keyboard-openable and contain only currently applicable commands; no double-click-only capability. A dynamic selection strip cannot steal focus merely by appearing, and disappearing results/filters return focus to the surviving relevant row/search rather than the window root.
  4. Define non-spam status policy: user-requested actions announce dispatch/result/error once; background refresh/enrichment only announces meaningful current/deferred/retry transitions; recommendations do not auto-announce on every row update. Status/error text is sanitized, persists long enough to inspect, and is not duplicated across list/detail/activity controls.
  5. Harden dialogs/detail: opening detail lands on Read heading/content, not a destructive action; opening confirmation lands on its summary/cancel-safe control; typed delete requires exact text with name/help and Escape always cancels. Modal focus is trapped correctly, close restores row/detail initiator, and async completion/cancellation never focuses disposed/hidden content. Visible focus meets high-contrast and dense-layout requirements.
- Tests and rendered checks: add semantic-XAML/unit checks for labels/names/help/live settings and no unlabeled icon command; run UI-automation or documented assistive manual scripts for the focus graph. Cover empty/one/many/grouped/search-no-result/processing/failed/multi-select, blocked action, batch result retry, archive recovery, typed delete cancel/confirm, imported detail, refresh state, and resized/high-contrast view. Validate keyboard only with Narrator or equivalent reader; capture observed announcements/focus targets as release evidence.
- Documentation / installer / release work: document keyboard conventions/shortcuts, action/focus expectations, and the accessibility QA script in user/support and release evidence. No installer work until shipped behavior changes; Whole-App Sprint 16 owns release evidence.
- Evidence and date: 2026-09-27 source audit found a few tooltips/access-key controls and isolated `Focus()` calls, including detail title focus and cleanup-grid focus, but no Meetings-wide focus map, semantic-name inventory, dynamic-selection rule, or live-announcement policy.
- Remaining gap or next action: inventory all Meetings buttons/menu items/inputs in the S18 fixture states; add names/help text to the first selection-strip action group and execute the keyboard-only row-to-detail-to-return journey.

- Validate keyboard navigation through presets, search, list, menus, selection
  strip, cleanup review, detail sections, and dialogs.
- Ensure focus returns to the relevant row after actions.
- Add accessible names for grouped controls and compact actions.
- Test no meetings, many meetings, active processing, failed meetings, and
  multi-selection.

Acceptance: the workflow is keyboard-usable and screen-reader legible.

### Sprint 20: Tests, Docs, Release

#### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-28 opaque policy and monitor); `Done` (2026-09-28 bounded queue lanes and package verification).
- User outcome: the simplified Meetings experience reaches users as coherent, documented, tested, installable behavior—not a UI-only change with unverified package or recovery paths.
- Scope / non-goals: release-gate the Meetings S1–S19 work. Do not claim `Done` from source presence, weaken existing tests, upload/push/release without scoped authorization, or rebuild installer assets for this plan-only documentation edit.
- Dependencies and decisions: each preceding sprint supplies acceptance fixtures and focused tests; this sprint owns their evidence matrix and release handoff. `scripts/Test-All.ps1` builds core/app/worker/installer/tests and runs Core + Integration tests; AppPlatform changes additionally require `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false`. Shipped app/runtime/installer changes require installer rebuild and package smoke under repository release hygiene; current task changes only `plan.md`, so no installer artifact rebuild is required now.
- Implementation slices:
  1. Create a release evidence matrix mapping S1–S19 acceptance to source contract, focused unit/integration/UI automation or documented manual check, fixture/state, owner/date, and result. Include control/action parity, recommendation/dismissal, selection/bulk result, archive/recovery/delete, reading/summary/speaker, search/enrichment, refresh, import, rendered, and keyboard/assistive paths. A failed or unexecuted cell stays open; do not collapse it into a generic “Meetings passed.”
  2. Add/finish smallest deterministic tests first: pure resolvers/planners/state transitions; service file/receipt/import behavior under temp roots; action/refresh identity-race integration; structural XAML semantic assertions; then repeatable rendered/accessibility journey checks. Keep test data local and sanitized. Record pre-existing failures/flakes separately with repro/owner; never mask them by skipping affected coverage.
  3. Update docs from shipped truth: `README.md` for user workflow and key safety boundaries; `SETUP.md` for actionable setup/recovery; `PRODUCT_REQUIREMENTS.md` for product contracts; `ARCHITECTURE.md` for state/action/refresh/provenance boundaries; `RELEASING.md` for package/smoke evidence. Include archive versus delete limits, local/hosted summary truth, speaker privacy, imported-source behavior, automatic refresh, keyboard/accessibility, and supported visual QA dimensions—without private paths, payloads, or unsupported promises.
  4. Run focused affected tests during each slice, then `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`. If the change touches AppPlatform/shared deployment, run its dedicated test command. Record command, commit SHA, clean/dirty source provenance, duration/result, and exact failures. Execute manual/rendered/accessibility checklist from S18/S19 on the candidate build before packaging.
  5. For an authorized shipped behavior change, rebuild from the verified intended source state with `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`; inspect bundle layout/integrity/release metadata and installer assets; then, with no app instance running, run `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`. Verify bundle and MSI-installed startup plus new crash-event absence. Package or smoke failure blocks release, not documentation follow-up.
  6. Commit only intended green source/docs with unrelated workspace work preserved. Push and upload release assets—including the MSI and versioned app ZIP—only after user authorization and recorded evidence; attach version/asset hashes/URLs after successful external publication. Mark individual sprints `Done` only with dated matrix and package evidence, not when their implementation record is merely `Ready`.
- Tests and rendered checks: acceptance matrix must cover normal, empty, blocked, busy, stale, failed, recovery, import, multi-selection, long-data, high-contrast, 100%/125%, and keyboard/screen-reader journeys. Verify docs links/commands, installer artifact presence, startup smoke, update-asset identity, and no undisclosed test gap. Re-run any flaky/recovered scenario once under logged conditions before closing.
- Documentation / installer / release work: this is the owner sprint. Update docs with the implementation slice that changes behavior; rebuild installer and smoke-test only for actual app/installer/script/runtime changes; preserve current plan-only change as docs-only. External release/push is an explicit later authorization boundary.
- Evidence and date: 2026-09-27 audit confirmed `Test-All.ps1` scope and the packaged smoke script’s bundle/MSI startup/crash-event checks; README/Product Requirements/Release docs already describe portions of Meetings and packaging. No single S1–S19 traceability matrix, rendered/accessibility evidence bundle, or clean candidate package evidence exists.
- Remaining gap or next action: create the S1–S19 evidence matrix beside the first implementation slice; start with S2 state, S8 action catalog, S13 bulk result, and S14 archive/delete tests before calling any Meetings behavior release-ready.

- Add tests for presets, recommendation ranking, action taxonomy, eligibility,
  blocked reasons, selection strip states, detail state, cleanup consolidation,
  bulk outcomes, archive/delete confirmation, refresh staleness, and import
  parity.
- Update `README.md`, `SETUP.md`, `PRODUCT_REQUIREMENTS.md`,
  `ARCHITECTURE.md`, and release notes.
- Run focused tests, then `powershell -ExecutionPolicy Bypass -File
  .\scripts\Test-All.ps1`.
- Rebuild installer assets with `powershell -ExecutionPolicy Bypass -File
  .\scripts\Build-Installer.ps1`.
- Run packaged smoke with `powershell -ExecutionPolicy Bypass -File
  .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Acceptance: the simplified Meetings experience ships as verified product
behavior.

## Test Scenarios

- Open Meetings with no meetings, many meetings, active processing, failed
  meetings, and archived meetings.
- Find a meeting by title, project, attendee, platform, status, and transcript
  availability.
- Use `Recent`, `Needs Attention`, `Processing`, `Archived`, and `Custom`.
- Apply a row recommendation.
- Recover a failed transcript.
- Add and repair speaker labels.
- Read transcript and summary without maintenance clutter.
- Archive one meeting and understand recovery.
- Permanently delete one meeting with typed confirmation.
- Bulk archive mixed eligible/blocked meetings.
- Bulk apply recommendations with per-row outcomes.
- Process one meeting ASAP, then clear or replace the request.
- Manage an imported meeting through the same paths as a recorded meeting.
- Navigate the flow by keyboard.

## Assumptions And Constraints

- This expands the existing whole-app UX roadmap's Meetings sprints.
- No meeting-management functionality is removed.
- Search remains visible in every preset.
- `Custom` preserves advanced sorting, grouping, and table behavior.
- Permanent delete is never automated.
- Hosted summaries remain opt-in.
- Archive is recoverable and may be recommended only when eligibility is clear.
- Manual refresh remains available as recovery, but not as the normal
  correctness mechanism.

# End-To-End Speaker Diarization Experience Roadmap

## Summary

Goal: make speaker diarization useful as a complete post-meeting workflow, not
just as anonymous labels in a transcript. A user should be able to identify who
`Speaker 1` means, hear short inline evidence clips, rename that speaker to a
person, repair normal diarization mistakes, and let local voice profiles safely
remember confirmed speakers for future and past meetings.

Pressure-test verdict: this roadmap improves the experience end to end only if
it treats diarization as three linked workflows:

- Identify the voice with transcript and audio evidence.
- Apply the right person name at the right scope.
- Remember confirmed identities locally, with conservative auto-apply and
  reversible feedback.

The current implementation already provides useful foundations through
speaker-name corrections, local voice profiles, suggestion provenance,
conservative auto-apply guardrails, refresh, reject, undo, and repair concepts.
This roadmap turns those primitives into a review experience that matches how
users expect speaker cleanup to work.

## Provider Patterns To Incorporate

- Otter-style teaching: tagging a generic speaker should improve future speaker
  identification, and a later learned profile should be able to rematch older
  generic-speaker conversations.
- Otter-style review: transcript review should be paired with audio playback
  and speaker context, not separated into a maintenance-only editor.
- Fireflies-style scope clarity: speaker edits should make the scope clear,
  especially all matching `Speaker X` turns versus a single attribution once
  segment-level overrides exist.
- Fireflies-style downstream consistency: summaries and action items should not
  silently remain stale after speaker attribution changes.
- Descript and Rev-style editing: speaker labels should be editable where they
  appear in the transcript, and global rename/replace should be fast for long
  transcripts.
- Fellow-style controls: voice matching should be explicit, local, and honest
  about shared-mic, hybrid, noisy, or overlapping-speech limits.
- Sonix-style pre-known names: attendee names and known local profiles can
  assist the user as visible suggestions, but runtime diarization and
  speaker-name recognition must not consume attendee names as hidden identity
  hints.

## Sprint 1: Speaker Identity Contract

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: transcript name-source metadata, profile matcher/correction service, detail label rows); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29 implementation: revisioned safe review snapshot and migration-state tests).
- User outcome: every displayed speaker name has a plain explanation and stable scope; a correction cannot be mistaken for a diarization label or silently overwritten by a profile match.
- Scope / non-goals: establish the shared identity/review schema used by later diarization sprints. Do not change clustering quality, add per-paragraph attribution, expose embeddings, or broaden profile sharing beyond the existing local-only store.
- Dependencies and decisions: retain generic diarization speaker id/label as source truth and use current published-transcript schema compatibility. Existing `SpeakerLabelInfo` carries display name, source, suggestion, profile id/confidence/reason and detail rows add accept/reject draft state, but no single revisioned review record has evidence/learning/repair readiness. Speaker identity is `(meeting stable identity, transcript/artifact revision, diarization speaker id)`; display label is mutable presentation, never the key.
- Implementation slices:
  1. Define `SpeakerReviewSnapshot`/row from one artifact revision: stable speaker id, anonymous label, current display name, proposed draft, `Generic`/`UserEntered`/`Suggested`/`AutoApplied` source, confidence bucket/reason, profile reference, user-edited flag, evidence availability, learning eligibility, repair warning, and freshness. Keep raw numeric scores/embedding details advanced or absent; every normal state maps to one plain explanation and allowed next action.
  2. Define precedence and writes: generic label until a user confirmation; user-entered name wins over suggestion/auto-match; suggestion is never a rename; auto-applied retains source/reason and can be undone later; re-diarization creates a new revision and never silently maps a label-string to a different cluster. Apply a confirmed rename to every turn for that speaker in the current artifact revision only; segment overrides wait for Sprint 6.
  3. Define profile/learning boundary: a user-confirmed correction may create/update a local profile only when speaker evidence is eligible and learning enabled; rejection records a scoped suppression without erasing user labels; disabled/missing/insufficient-profile states are explicit. Do not train from a bare suggestion, generic label, failed repair, or stale revision. Profile id is opaque/local; source labels/decision reason are publish-safe.
  4. Version/migrate published metadata additively. Legacy label-only transcript maps to `Generic`; absent profile provenance maps to no claim; unknown future enum/value is rendered as safe review-needed state. Ensure JSON/Markdown/summary/log/activity serialization admits only allowed attribution fields and avoids original audio paths, voice vectors, samples, profile payload, or hidden score data.
  5. Make stale edits safe: edit request carries snapshot revision/speaker id and compares source before apply; conflict yields reload/review, not best-effort merge. Persist exactly once and return a result that distinguishes label update, suggestion accepted/rejected, learning skipped/created/updated, and warning.
- Tests and rendered checks: schema round-trip and backward/unknown-value migration; snapshot resolver matrix for generic/user/suggested/auto/stale/repair/unavailable/learning-disabled; precedence and all-turn scope; stale-write/retry/idempotence; rejection/suppression; privacy serialization/log scan. Test detail/list result parity and render source/reason/warning text with long names and no raw identifiers at 1280x800/125%, keyboard/screen-reader label/source association.
- Documentation / installer / release work: document label versus person name versus local voice profile, scope, source explanations, and local-data boundary when shipped. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found `SpeakerNameSource`/decision metadata, profile matcher/correction/learning tests, and `MeetingDetailSpeakerLabelEditorRow`, but no common review snapshot, revision key, explicit precedence/migration contract, or proof all render paths preserve the privacy boundary.
- Evidence and date: 2026-09-29 added `SpeakerReviewSnapshotResolver` and matrix tests for generic/user/suggested/auto/unknown sources, freshness, repair, evidence, learning readiness, and privacy-safe explanation fields. Existing correction/learning services retain revision-checked all-turn writes, suppression, and local-only profile behavior.
- Remaining gap or next action: Sprint 2 can bind snapshots to the detail-grid replacement; legacy label-only artifacts resolve as generic rows without profile claims.

Goal: remove ambiguity between anonymous diarization labels and remembered
person names.

Workstream 1 - Product terms:

- Define `Speaker Label` as the anonymous diarization cluster, such as
  `Speaker 1`.
- Define `Person Name` as the display name shown after a user or local profile
  names the speaker.
- Define `Voice Profile` as the local remembered voice signature taught by
  confirmed corrections.
- Use these terms consistently in Settings, meeting detail, transcript rows,
  logs, docs, and release notes.

Workstream 2 - Review model:

- Add a `SpeakerReviewRow` state model carrying speaker id, current label,
  edited person name, name source, confidence, suggestion reason, profile id,
  user-edited flag, evidence state, profile-learning readiness, and repair
  warning.
- Preserve the existing safe public metadata fields in transcript JSON, such as
  profile id, confidence, source, suggested display name, and decision reason.
- Keep voice embeddings and full profile payloads out of published transcript
  JSON, Markdown, summaries, logs, and status text.

Workstream 3 - Default scope:

- Default speaker rename behavior to `Apply to all turns for this speaker`.
- Do not introduce `Apply to this paragraph only` until segment-level
  attribution overrides are implemented.

Sprint 1 acceptance criteria:

- Every visible speaker name can be explained as generic, user-entered,
  suggested, or auto-applied.
- Published artifacts remain free of embeddings and raw voice-profile payloads.

## Sprint 2: Speaker Review Surface Foundation

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: `SpeakerLabelsDataGrid`, apply/refresh/undo controls, detail state wiring); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 snapshot-backed review-well slice).
- User outcome: a person sees where to name anonymous speakers while reading a meeting, understands suggestion provenance, and cannot confuse naming a cluster with repairing diarization.
- Scope / non-goals: replace the current detail maintenance speaker grid with the initial Speaker Review well using Sprint 1 snapshots. Do not add segment-level edits, inline playback, automatic profile changes on open, or a second profile-management surface.
- Dependencies and decisions: consume Sprint 1 revisioned `SpeakerReviewSnapshot`; use S3 evidence, S4 clips, S8 correction/learning, S11 repair guidance, S15 accessibility/render acceptance as later extensions. Current `MeetingDetailWindow` shows a 130px `SpeakerLabelsDataGrid` among processing/split maintenance controls and applies all drafts at once. The new well is an editing surface with one explicit persisted commit; row buttons edit draft state only.
- Implementation slices:
  1. Place a collapsible-but-discoverable `Speaker Review` well immediately after transcript/read context and before generic Fix/Organize maintenance. Its header shows concise state/count (`3 speakers to review`, `Labels unavailable`, `Repair recommended`) and one route; it never opens a destructive or model-setup flow merely by expanding.
  2. Render one row per snapshot with anonymous label, editable person-name draft, source/provenance, confidence/reason in plain language, available evidence indicator, and status. Use a real labelled editable field with suggestions from current meeting names, confirmed local profile names, and attendees; suggestions are choices, never a claimed identity, and duplicates/empty/generic names are handled predictably.
  3. Keep changes transactional at user intent: Use/Reject/clear changes only a row draft and shows pending count; `Apply names` summarizes current-revision target count and learning consequences, revalidates snapshot, then reports exact persisted/learning results. Cancel/close/selection change warns or preserves draft intentionally; no refresh/background result overwrites a dirty draft.
  4. Separate routes visually and semantically: `Refresh suggestions` refreshes eligible profile candidates without renaming; `Repair speaker labels` belongs to Fix with clear cluster-quality/reprocessing copy and cannot overwrite a user name until the later repair policy resolves it; `Add labels` is setup/queue state, not a naming control; Undo affects profile-origin attribution only and explains scope.
  5. Build unavailable/blocked states into the same well: no labels, processing, transcript missing/corrupt, no eligible evidence/profile, learning disabled, stale revision, and repair queued/running/failed each expose one safe action/reason. Keep profile ids, raw confidence scores, embedding/sample data, and private paths out of normal rows and tooltips.
- Tests and rendered checks: snapshot-to-row mapping; source/reason/availability routes; draft accept/reject/edit/clear, duplicate and whitespace normalization, dirty-draft refresh/close/revision conflict, commit/retry idempotence, profile learning disabled, and repair/name separation. Render 0/1/5/12 speakers, long/duplicate names, suggestion/no-suggestion, queued/failed states at 1280x800/125% using `DESIGN.md` wells; keyboard/screen-reader verify labelled row fields, pending count, provenance, and focus return after apply.
- Documentation / installer / release work: add user wording for Speaker Review, local suggestion/provenance, draft/apply behavior, and repair distinction when shipped. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found current detail rows with Current/Display Name/Voice Profile/Suggestion/Use/Reject and Apply/Refresh/Undo controls, but no transcript-adjacent review hierarchy, snapshot-driven state, dirty-draft contract, or explicit repair versus naming route.
- Evidence and date: 2026-09-29 moved the snapshot-backed `Speaker Review` well ahead of Organize & Fix, added anonymous-label/provenance rows, accessible review context, pending draft text, and clear local-refresh versus repair wording. Existing apply/reject/refresh/undo handlers retain revision-checked transactional writes.
- Remaining gap or next action: add bounded dropdown choices from attendees/current names/confirmed local profiles, then complete the 0/1/5/12-speaker rendered fixture sweep before marking this sprint Done.

Goal: make speaker cleanup an obvious first-class meeting-detail workflow.

Workstream 1 - Detail-window placement:

- Replace the small maintenance speaker-name grid with a `Speaker Review` well
  near the transcript in `MeetingDetailWindow`.
- Keep maintenance actions available, but do not bury everyday generic-label
  cleanup in the same visual group as archive, delete, split, or retry.
- Follow `DESIGN.md`: compact technical well, opaque surfaces, 1px structure,
  4px radii, no shadows, and dense readable rows.

Workstream 2 - Review rows:

- Show one row per speaker cluster with current label, editable person name,
  provenance, suggestion, confidence, decision reason, and action availability.
- Use attendee names, prior profile names, and existing meeting speaker names
  only as dropdown suggestions that the user can choose.
- Keep `Repair Speaker Labels` visible but separate from person-name editing.

Sprint 2 acceptance criteria:

- A user can find where to rename `Speaker 1` without hunting through generic
  maintenance controls.
- Users can tell whether they are naming a person, refreshing suggestions, or
  repairing bad speaker labels.

## Sprint 3: Contextual Transcript Evidence

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 profile migration, settings projection, and queue-status slice).
- User outcome: before naming a speaker, a user can inspect a few representative local transcript cues and knows when the app lacks reliable evidence.
- Scope / non-goals: add read-only evidence selection/presentation to the Sprint 2 review well. Do not infer identity from text, send excerpts anywhere, rank people, alter diarization segments, or implement audio playback (Sprint 4).
- Dependencies and decisions: consume Sprint 1 identity/revision and Sprint 2 row lifecycle. Existing detail transcript view has timestamped/speaker-labelled segments and filtering, but no speaker-scoped evidence model. `SpeakerEvidenceService` is pure over the already-read current artifact; it must not reread/log transcript content on UI refresh or expose a raw diagnostic transcript dump.
- Implementation slices:
  1. Define `SpeakerEvidenceSnapshot` keyed by meeting/artifact revision and speaker id: up to three `EvidenceCue`s (timestamp range, bounded visible excerpt, source kind, segment index) plus availability/weakness reasons and audio-reference availability. Structured JSON/segment data is preferred; Markdown fallback is allowed only for cleanly parsed timestamps/labels and carries `MarkdownFallback` provenance.
  2. Select deterministically: discard blank/tiny/duplicate/overlapping cues, prefer readable turns meeting minimum duration/text thresholds, and spread selected cues across early/middle/late meeting buckets where available. Tie-break with stable segment ordering; cap excerpt length at grapheme-safe boundary with ellipsis and never use an excerpt in status/log/activity text.
  3. Produce explicit evidence states rather than empty ambiguity: `Available`, `Limited`, `TranscriptUnavailable`, `TimestampsUnavailable`, `NoTurnsForSpeaker`, `TurnsTooShort`, `DiarizationChurn`, `AudioUnavailable`, `ArtifactStale`. Multiple reasons can be shown compactly, but only one primary user route/action is offered (read transcript, refresh/reload, repair labels, or no action).
  4. In Speaker Review, expose “View evidence (n)” as an in-place disclosure per row; it lists timestamped cues, source/provenance, and a `Show in transcript` navigation action. It does not auto-expand, alter drafts, steal keyboard focus, or claim that a cue proves identity. Apply/revision refresh invalidates/rebuilds only the affected snapshot.
  5. Treat text as local sensitive content: preserve current transcript reader access boundary, keep snippets in-memory only for open detail/revision, sanitize all error/log/status messages, and avoid adding evidence to published output, summary, profile store, clipboard, or telemetry.
- Tests and rendered checks: selection fixture matrix for one/many turns, ties, short/churn/overlap/duplicates, long Unicode text, spread across meeting, missing timestamps, malformed Markdown, absent/stale transcript/audio, and revision change. Assert max count/length/no transcript logging and deterministic results. Render evidence disclosure/open-close/show-in-transcript at 1280x800/125%, keyboard/screen-reader cue timestamp/source/weakness announcement, and long-cue wrapping without hiding edit actions.
- Documentation / installer / release work: document that evidence is local transcript context, is not identity proof, and may be unavailable/limited. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found detail transcript segments with text/speaker/timestamp filtering and speaker review rows, but no speaker-scoped excerpt selector, fallback provenance, weak-evidence taxonomy, or privacy test for evidence presentation.
- Remaining gap or next action: introduce pure structured-segment evidence fixtures and implement deterministic selection/tests before adding a disclosure to the review well.

Goal: help the user infer who a speaker is before playing audio.

Workstream 1 - Evidence selection:

- Add a `SpeakerEvidenceService` that reads structured transcript segments,
  speaker turns, speaker metadata, and audio availability.
- Choose two or three representative snippets per speaker.
- Prefer readable, sufficiently long turns that are not tiny fragments and are
  spread across the meeting.
- Avoid printing transcript text in logs or diagnostic status.

Workstream 2 - Weak-evidence flags:

- Mark evidence weak when structured JSON is unavailable, timestamps are
  missing, turns are too short, audio is missing, or speaker-run churn suggests
  suspicious diarization.
- For Markdown-only transcripts, use timestamped Markdown only when timestamps
  parse cleanly and make the weaker evidence source visible.

Sprint 3 acceptance criteria:

- Each speaker review row can show concise timestamped transcript cues.
- The app explains when it lacks enough evidence to help identify a speaker.

## Sprint 4: Inline Audio Clip Playback

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 implementation); `Done` (2026-09-29 verification).
- User outcome: a reviewer can hear a short, relevant local excerpt from a speaker cue without leaving the decision context or risking published meeting artifacts.
- Scope / non-goals: derive/play temporary clips for Sprint 3 evidence from published meeting audio. Do not edit/re-encode published audio, stream/upload audio, add continuous transcript playback, store clip references in meeting artifacts, or permit review playback during active recording.
- Dependencies and decisions: consume artifact-revision evidence from Sprint 3 and use existing NAudio/WAV utility patterns only behind a review-specific service. Current code has capture/merge/audio validation utilities but no review player. Playback is a single app-owned session; active recording blocks playback/generation so review audio cannot contaminate loopback capture.
- Implementation slices:
  1. Define `SpeakerReviewClipRequest` keyed by meeting stable id/revision, speaker id, evidence cue time range, and published-audio fingerprint. Validate source exists, is supported/readable, revision matches, and duration is known; select a target 6–12-second window with bounded lead/trail padding, clamped to `[0, duration]`. Too-short/locked/missing/unsupported audio returns explicit unavailable state, never a rename blocker.
  2. Implement a cancellation-aware extraction service that reads the source read-only and writes a temporary normalized clip through a sibling temp path then atomic move. Capture format/decode/write failure safely, validate duration/size after write, dispose every reader/writer, and verify the source fingerprint/length remains unchanged. No task may hold a file lock after completion/cancellation.
  3. Store clips under an app-local/portable `speaker-review` cache using opaque hashed request keys, restrictive inherited user-local access, bounded total size/count/age, and an in-use lease. Purge expired/orphan/temp files at safe startup and after detail close; invalidate when artifact fingerprint changes; never include stem/title/speaker name/raw path in default cache filename, transcript JSON, Markdown, manifest, summary, export, activity, or logs.
  4. Add one `SpeakerReviewPlaybackController`: explicit play/pause/stop/retry state, progress/duration and failure reason, no autoplay, one clip at a time, stop/dispose on new clip/detail close/revision change/shutdown, and no hidden background playback. Check recording/maintenance/endpoint availability at dispatch; if blocked, explain why and offer no unsafe bypass.
  5. Place keyboard-reachable inline controls beside the evidence cue with visible current-viewport state and accessible name including cue timestamp/state. Play/pause does not apply a name draft, change transcript selection, or shift focus unexpectedly; show generation/error/retry locally to that cue without raw paths or decoder exceptions.
- Tests and rendered checks: fixture WAVs for window/padding/clamp, short/missing/locked/corrupt/unsupported source, cancellation, source hash/mtime preservation, temp cleanup, cache hit/invalidation/eviction/in-use lease, concurrent request dedupe, and revision race. Controller tests for one-session stop/dispose, recording block, endpoint failure, close/shutdown. Render generating/play/pause/completed/unavailable/error at 1280x800/125%; keyboard/screen-reader controls, progress, error/retry, focus and no-autoplay validation.
- Documentation / installer / release work: document temporary local review clips, recording-time block, cache cleanup, and audio does not leave device. Reassess package dependency/licensing impact before shipping NAudio playback additions; Sprint 16 owns installer/release gates.
- Evidence and date: 2026-09-27 audit found NAudio capture/merge/input-inspection infrastructure and no review-clip cache/player or speaker-evidence audio surface. Existing audio code is not proof that a review clip is source-preserving or recording-safe.
- Remaining gap or next action: create a read-only clip-window/extraction service with source-fingerprint and cancellation tests, then add controller tests before placing any play button.

Goal: provide the required "hear this speaker" cue in the same review workflow.

Workstream 1 - Clip extraction:

- Expose a bounded audio-segment extraction helper using existing
  `WaveChunkMerger` and NAudio patterns.
- Generate short clips from the published meeting audio only.
- Target 6-12 second clips with safe padding and clamping to the audio
  duration.
- Handle missing, locked, unreadable, or too-short audio as a disabled playback
  state rather than a rename blocker.

Workstream 2 - Clip cache:

- Store generated clips under a local review cache such as
  `%LOCALAPPDATA%\MeetingRecorder\speaker-review` or the portable equivalent.
- Cache by meeting stem, speaker id, and time range.
- Clean stale clips on app startup and when the detail window closes.
- Never persist clip paths in transcript JSON, Markdown, manifests, summaries,
  or exports.

Workstream 3 - Playback UX:

- Add inline play/pause controls per evidence snippet.
- Show active playback state in the current viewport.
- Keep playback controls keyboard reachable and avoid below-the-fold-only
  feedback after a click.

Sprint 4 acceptance criteria:

- Users can play relevant clips inline to decide which person a speaker label
  represents.
- Clip generation never rewrites published audio or transcript artifacts.

## Sprint 5: Transcript-Label Click Editing

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: while reading a paragraph, a user can start cleanup from its speaker label and safely rename that speaker everywhere it appears in the current meeting.
- Scope / non-goals: add a transcript-label navigation/edit entry into the S2 review workflow and reserve JSON shape for S6. Do not make transcript text editable, add a per-paragraph rename/override control, infer speaker identity from click position, or bypass S1 correction/learning/write safety.
- Dependencies and decisions: consume S1 stable speaker/revision identity and S2 dirty-draft/commit semantics; S6 owns actual segment override behavior. Current transcript filtering uses rendered speaker-label text, so the reader adapter must carry stable speaker id/segment id from structured data. Markdown fallback without stable association offers a non-mutating `Open Speaker Review`/unavailable explanation, never maps labels by display string alone.
- Implementation slices:
  1. Extend read-model segments with optional stable `SpeakerId`, `SegmentId`, artifact revision, and a label-action availability state. Render a semantically named label control adjacent to the read-only paragraph (mouse, keyboard Enter/Space, context menu); preserve copy/select/search of paragraph text and prevent label activation from selecting or editing transcript content.
  2. On activation, resolve the current revision/speaker id, reveal/scroll the matching Speaker Review row, set row focus to its name field or first safe action, and announce label/source/provenance. If another row has a dirty draft, preserve it and route focus without applying/discarding data; if the target is stale/missing, request reload with a clear reason.
  3. Provide a label-context `Name Speaker …` entry and `Apply to all turns for Speaker X` only after a non-generic name draft is supplied/confirmed. Reuse the exact review commit/correction service and revalidate `(meeting, revision, speaker id)` at apply; confirm affected-turn count/current name/learning consequence, then refresh transcript/detail/list/profile state from source truth. `Apply all` never silently turns a profile suggestion into a user confirmation.
  4. Define an additive, forward-compatible reserved segment override record—stable segment id, original diarization speaker id, replacement attribution reference, actor/source, timestamp, artifact revision—inside structured transcript metadata. Parsers preserve/ignore unknown reserved records safely; current rendering/writers never emit one, alter text/timestamps, or change existing label resolution until S6 owns migration/UI/precedence.
  5. Contain async/focus races: close/detail switch/cancel/reprocessing prevents a late resolve or write result applying to another revision; result status is user-visible at initiating label/review, sanitized, and only one source update occurs. No raw profile/embedding/paths appear in label tooltips or accessibility text.
- Tests and rendered checks: adapter matrix structured/stable, generic/suggested/auto/user, missing/stale id, Markdown fallback, selection/search coexistence, keyboard/context action, focus scroll/return, dirty other-row draft, current-revision all-turn count, confirm/cancel/retry/idempotence, and refresh race. Schema tests preserve absent/reserved/unknown override data without behavior change. Render dense/long labels at 1280x800/125%, reader label accessible names, high contrast, and no text-selection regression.
- Documentation / installer / release work: document that label action names a current-meeting speaker everywhere, does not edit one paragraph, and uses the same local learning rules as Speaker Review. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found transcript rows filtered by speaker-label text and a separate speaker grid, but no stable label-control model, focus route, label-origin write, or reserved segment-override schema.
- Remaining gap or next action: add stable speaker/segment identifiers to the detail transcript read model and test label activation-to-review focus before adding the global-name confirmation.

Goal: match the expected editor pattern where speaker labels are actionable in
the transcript itself.

Workstream 1 - Focus from transcript:

- Let a click on a transcript speaker label focus the matching row in Speaker
  Review.
- Keep transcript text editing out of scope.
- When the transcript label belongs to an auto-applied or suggested profile,
  show the profile provenance in the focused review row.

Workstream 2 - Global label action:

- Provide `Apply to all Speaker X` from the transcript label context.
- Route the action through the same correction service as the review well.
- Refresh the transcript, meeting list, detail state, and profile settings
  after apply.

Workstream 3 - Segment-level preparation:

- Define the additive transcript JSON shape for a future segment-level speaker
  override.
- Keep this shape backward-compatible and avoid changing existing transcript
  text or timestamps.

Sprint 5 acceptance criteria:

- Users can start speaker cleanup directly from the transcript label they are
  reading.
- Global rename uses the same safe artifact update and learning path as the
  review well.

## Sprint 6: Segment-Level Attribution Overrides

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: a reviewer can correct one wrongly attributed paragraph without relabelling an entire diarization cluster or unintentionally teaching the app a voice identity.
- Scope / non-goals: activate the additive reservation from Sprint 5 for structured transcript segments. Do not rewrite diarization output/text/timestamps, merge clusters, improve model calibration, train profiles, or claim overrides survive a newly generated incompatible transcript without review.
- Dependencies and decisions: consume S1 identity/revision, S2 review, S5 stable segment id/reserved schema, S8 undo/correction history, and S12 derived-output consistency. Original diarization attribution stays immutable. Each override is keyed by meeting/artifact revision + stable segment id and holds original speaker id plus exactly one target: an existing meeting speaker id or an explicit user display name; effective rendering resolves replacement speaker display dynamically, while the original remains inspectable.
- Implementation slices:
  1. Finalize an additive versioned override record with id, artifact revision, segment id, original speaker id, target kind/value, actor/source (`UserOverride` only), created/updated timestamp, and optional safe reason. Validate non-empty stable ids, target existence for replacement speaker, normalized non-generic display names, and no self-referential/no-op target; unknown fields/values round-trip or safely become review-needed.
  2. Add a pure effective-attribution resolver: default diarization speaker → current speaker-level name/source → active segment override target. It returns effective label/source/indicator and original attribution for Details, never mutates input segments. User cluster rename updates only replacement-speaker references naturally; explicit paragraph display remains paragraph-scoped; deleted/invalid target becomes visible conflict, never a guessed fallback.
  3. Add `Apply to this paragraph only` from a segment’s label menu after a scope summary contrasting it with `Apply to all turns for Speaker X`. Offer existing speaker targets and explicit display text with clear labels; persist only after confirmation/revision revalidation. Display a compact manual-override indicator with “View original” and safe edit/revert route; a one-off action cannot silently create/update/suppress a voice profile.
  4. Write structured JSON and derived Markdown through a transactional artifact update: validate current revision, stage new outputs, preserve original segment/text/timestamps, atomically replace only complete outputs, then refresh reader/detail/list. If any write fails, retain previous published pair and report retry; summary/other derived artifacts are marked stale/review-needed rather than silently contradictory.
  5. On re-transcribe/re-diarize/new artifact revision, retain overrides as prior-revision audit records but do not apply them to unmatched new segments. Surface a concise “review previous paragraph corrections” state; only explicit future matching/migration policy may propose carry-forward. Stale detail callbacks cannot apply/revert overrides against a new revision.
- Tests and rendered checks: schema backwards/forwards/unknown values; resolver precedence/target rename/deleted target/no-op; explicit-display versus replacement target; scope confirmation/cancel; privacy/no-learning/no-calibration assertion; artifact transaction failure/rollback; stale/reprocess/orphan behavior; revert/history. Render normal/overridden/conflict/review-needed paragraph at 1280x800/125%, keyboard menu/scope distinction, original/effective screen-reader text, long target labels, and no visual suggestion that diarization was rewritten.
- Documentation / installer / release work: document paragraph-only correction, original-versus-effective attribution, no profile training, reprocess review, and derived-output refresh semantics. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found only speaker-label text in transcript rows and no override record/resolver, scope UI, artifact transaction, or no-learning enforcement for paragraph correction.
- Remaining gap or next action: implement the pure override schema/resolver and round-trip fixtures before changing transcript menus or writers.

Goal: let users correct one wrongly attributed paragraph without renaming an
entire speaker cluster.

Workstream 1 - Override model:

- Add segment-level speaker attribution override metadata to structured
  transcript JSON.
- Preserve original diarization speaker id and distinguish it from user
  override display name or replacement speaker id.
- Render Markdown and detail transcript rows from the effective speaker
  attribution.

Workstream 2 - UI behavior:

- Add `Apply to this paragraph only` after the override model exists.
- Make the scope explicit before saving: this paragraph versus all turns for
  the speaker cluster.
- Show an indicator when a paragraph has a manual attribution override.

Workstream 3 - Learning boundary:

- Do not train voice profiles from one-off paragraph overrides unless the
  override is promoted to a confirmed speaker-level correction.
- Do not use paragraph overrides as diarization calibration hints.

Sprint 6 acceptance criteria:

- A single misattributed paragraph can be corrected without changing all
  instances of the speaker cluster.
- Segment overrides remain additive and backward-compatible.

## Sprint 7: Merge Duplicate Speakers

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 callback-boundary implementation).
- User outcome: a reviewer can collapse a normal duplicate-cluster split into one person without rerunning processing, losing source attribution, or accidentally teaching duplicate voice samples.
- Scope / non-goals: add user-confirmed merge of two or more current-revision speaker clusters in Speaker Review. Do not alter worker clustering thresholds, mutate embeddings, merge meetings, silently merge suggested names, or use manual merge for severe diarization fragmentation.
- Dependencies and decisions: consume S1 identity precedence, S2 review, S3 evidence, S6 effective attribution, S8 history/undo, S11 repair guidance, and S12 derived consistency. Existing `SpeakerClusterMergeService` is worker-side similarity recovery and is not the user action. User merge writes an additive current-revision canonical-cluster mapping: source cluster ids remain immutable/auditable; all effective rows/segments resolve to a selected canonical speaker id.
- Implementation slices:
  1. Define `SpeakerClusterMergeRecord` with meeting/artifact revision, immutable source speaker ids, chosen canonical id, user actor/source, timestamp, evidence summary fingerprint, and reversible operation id. Validate at least two distinct current clusters, canonical membership, no existing-cycle/redundant mapping, and current revision; compose mappings deterministically while retaining every original id.
  2. Add `Merge speakers` from Speaker Review only after multi-select/explicit canonical choice. Preview affected turn/evidence counts, current names/sources, effective outcome, and no-learning rule; require confirmation. Default to generic canonical when names conflict—never pick a profile/person name by cluster order—and return row-level stale/blocked reason before write.
  3. Gate conflicts: divergent user-entered names or distinct high-confidence auto/profile matches require explicit resolve-to-one name or cancel; ambiguous/weak/generic state can merge with a clear review warning. Many tiny/rapid-churn clusters, missing evidence, unsupported cluster count, or repair recommendation routes to `Repair Speaker Labels` instead of exposing a dangerous mass merge.
  4. Extend effective attribution so transcript/review/segment overrides resolve through canonical mapping while original diarization id and per-segment override remain inspectable. Write structured transcript metadata and Markdown atomically from revision-checked source; preserve text/timestamps/original speakers, refresh detail/list state, and mark summary/derived outputs review-needed for Sprint 12 rather than silently stale.
  5. Keep local learning isolated: merge alone creates no profile sample/update/suppression; a later explicit confirmed speaker-level name applies once to canonical evidence, deduplicated by meeting/revision/original cluster. Persist an undo-ready audit event; S8 supplies user-visible undo, but merge writer must support exact inverse validation without affecting unrelated corrections.
- Tests and rendered checks: mapping validation/composition/cycle, two/many merge, canonical choice, generic/weak/user/profile conflict gates, evidence/fragmentation repair route, stale revision/concurrent edit, source-id preservation, effective segment/override resolution, artifact atomic rollback, no profile learning/duplicate samples, inverse operation. Render merge selection/preview/conflict/repair/complete at 1280x800/125%, keyboard canonical selection and screen-reader affected-turn/conflict wording.
- Documentation / installer / release work: document manual merge versus repair, source attribution retention, no automatic identity learning, and later undo/derived-output status. No installer work until behavior changes; Sprint 16 owns diarization release gates.
- Evidence and date: 2026-09-27 audit found worker-side `SpeakerClusterMergeService` and oversegmentation recovery tests, but no user merge record/UI, artifact-layer canonical resolver, conflict gate, or proof merge avoids profile-learning duplication.
- Remaining gap or next action: implement pure canonical-map resolver/validation and conflict fixtures before adding the multi-select review UI.

Goal: handle the common over-split case where one person appears as two or more
generic speakers.

Workstream 1 - Merge action:

- Add `Merge Speakers` from the Speaker Review surface.
- Let users merge `Speaker 2` and `Speaker 3` into one person name when the
  evidence shows they are the same voice.
- Update transcript JSON, Markdown rendering, manifest speaker metadata, and
  review rows.

Workstream 2 - Merge safety:

- Preserve enough metadata to explain that a merge was user-confirmed.
- Avoid training duplicate profile samples for the same meeting/person after a
  merge.
- Warn when merging speakers with conflicting high-confidence profile matches.

Workstream 3 - Repair boundary:

- Route severe speaker explosions, many tiny fragment speakers, or unclear
  split patterns to `Repair Speaker Labels` instead of manual merge.

Sprint 7 acceptance criteria:

- Users can resolve normal over-splitting without rerunning the worker.
- Severe diarization failures remain routed to the heavier repair path.

## Sprint 8: Corrections, Rejections, Undo, And Local Learning

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: correction/rejection/refresh/undo services and profile tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 identity-keyed correction decision slice).
- User outcome: confirmed names persist even when local learning cannot, bad suggestions stay rejected only where wrong, and undo removes only recognition-derived changes.
- Scope / non-goals: harden speaker-level correction, rejection, profile learning, and undo. Do not make profiles cloud/shared, train from segment overrides/merges, silently reapply rejection, or add profile deletion here.
- Dependencies and decisions: use S1 revisioned speaker ids, S6 override boundary, S7 canonical clusters. Existing service writes artifacts before best-effort learning and has scoped rejection/undo, but its correction map is keyed by display label. Mutations carry meeting/revision/speaker id (canonical id when merged), never display text as identity.
- Implementation slices:
  1. Replace label-keyed input with `SpeakerCorrectionRequest`: operation id, revision, speaker/canonical id, prior fingerprint, action/name, learning state. Reject stale/duplicate/no-op requests and revalidate before write.
  2. Persist artifact-first correction receipt with prior/next attribution, accepted/rejected match, learning disposition, and reversible id. Artifact success survives disabled/corrupt/unwritable profile storage; learning failure is sanitized/retryable and never rolls back the name.
  3. Deduplicate learning by profile plus meeting/revision/canonical speaker, not meeting alone. Train only explicit confirmed speaker-level correction with valid sample/config; never train from suggestion/auto match/merge/paragraph override/repair/stale retry.
  4. Scope rejection to profile + meeting + revision + speaker/canonical id and honor it before proposal. Overwrite/reject clears suggestion then records feedback; it does not globally ban the profile.
  5. `Undo recognition` clears only auto/suggested profile attribution and adds suppression. Correction undo restores receipt prior state only after revision check; neither alters user edits, unrelated speakers, overrides, or profiles/samples. Both are idempotent/result-counted.
- Tests and rendered checks: duplicate/renamed display labels, stale revision, canonical merge, artifact success/profile failure, receipt retry/idempotence, learning-dedupe/no-learning matrix, scoped rejection/refresh, undo boundaries, conflict/cancel, privacy/log redaction. Render warning/result/undo focus and accessible status.
- Documentation / installer / release work: document local-only learning, confirmation, scoped rejection, artifact-first success, and undo limits. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found artifact-first learning, scoped rejected matches, user-edit-preserving/idempotent undo tests. It found display-name keyed correction maps, no revisioned receipt, and no merge-aware learning-dedupe proof.
- Evidence and date: 2026-09-29 added `SpeakerCorrectionReceiptResolver` tests covering canonical speaker identity, stale fingerprint/revision, duplicate/no-op requests, learning eligibility, scoped rejection, and recognition-only undo. Existing correction service remains artifact-first with scoped feedback and idempotent undo.
- Remaining gap or next action: persist additive correction receipts and migrate the detail apply path to issue identity-keyed operation ids before marking this sprint Done.

Goal: make speaker-name corrections durable while teaching future recognition
only when safe.

Workstream 1 - Artifact update:

- Reuse `SpeakerNameCorrectionService` so JSON, Markdown, and manifest updates
  happen before best-effort profile learning.
- Rename should still succeed if the profile store is missing, corrupt,
  disabled, or unwritable.
- Refresh meeting list, detail state, transcript display, and Settings profile
  rows after every change.

Workstream 2 - Learning:

- When local learning is enabled and usable voice samples exist, confirmed
  speaker-level corrections create or update local voice profiles.
- Keep repeat saves idempotent so one meeting speaker does not train the same
  profile repeatedly.
- Keep corrections local-only and bounded to the existing voice-profile store.

Workstream 3 - Negative feedback:

- Rejecting a suggestion stores scoped feedback for
  `meetingId + speakerId + profileId`.
- Overwriting an auto-applied or suggested profile name should also suppress
  that meeting-speaker/profile match.
- `Undo Recognition` clears profile-sourced names while preserving explicit
  user edits.

Sprint 8 acceptance criteria:

- Speaker-name corrections stick and can teach future matching.
- Bad suggestions are remembered for that meeting speaker without globally
  banning the profile.

## Sprint 9: Automatic Future Naming

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: `VoiceProfileMatcher`, config thresholds, decision tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 eligibility decision matrix).
- User outcome: future meetings may receive a remembered name only when local evidence clears conservative rules; all uncertainty remains clearly reviewable.
- Scope / non-goals: harden post-processing profile attribution. Do not identify live speakers, use attendee/calendar/file/title/count hints, alter diarization, send embeddings off-device, or turn a suggestion into a correction.
- Dependencies and decisions: consume S1/S8 identity, revision, suppression and receipts. Current matcher applies threshold/margin/profile-maturity/minimum-duration and one auto winner per profile. Run only after final speaker samples/artifact revision exist; candidate input is profile/sample embedding compatibility plus scoped rejection, nothing else.
- Implementation slices:
  1. Define a pure `FutureNamingEligibility`/decision snapshot keyed by meeting revision, canonical speaker id and matcher/profile-policy version. Enumerate `Disabled`, `NoSamples`, `NoProfiles`, `IncompatibleModel`, `Immature`, `Short`, `BelowThreshold`, `Ambiguous`, `DuplicateCandidate`, `Suppressed`, `Suggested`, `AutoApplied` with one user-safe explanation.
  2. Apply predictions once after publish/speaker labeling completion; revalidate artifact revision, preserve user corrections and segment overrides, and write only an auto attribution receipt. Refresh/retry consumes same snapshot and never runs during capture or from opening a row.
  3. Keep false attribution highest severity: auto applies only all gates; suggestion stays generic display plus explicit candidate/reason; conflicting or suppressed state cannot fall through. Provide one immediate `Undo recognition` path and durable scoped suppression; disable/rollback returns future processing to generic labels without erasing user names.
  4. Add privacy-safe local diagnostics: policy/version, disposition/counts, and no raw vectors/audio/text/profile paths. Prove absence of calendar/attendee/title/fixture/file/speaker-count identity inputs mechanically; calibration/threshold changes require fixture evidence and compatibility version, not silent config drift.
- Tests and rendered checks: exhaustive gate/precedence matrix, one-profile-per-speaker collision, revisions/retry/idempotence, user/override preservation, suppression/undo/disabled rollback, incompatible model, no-hint static dependency test, and local-only log redaction. Render each readiness state and immediate undo at 1280x800/125%, keyboard/screen-reader source/reason.
- Documentation / installer / release work: document post-processing-only/local recognition, conservative rules, suggestion/undo and no hidden identity hints. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found threshold/margin/maturity/duration/duplicate gates and tests, but no lifecycle snapshot, rollout/rollback contract, revisioned attribution receipt, or mechanical no-hint evidence.
- Evidence and date: 2026-09-29 added `FutureNamingEligibilityResolver` with conservative precedence tests for disabled, evidence/profile gaps, suppression, thresholds, ambiguity, suggestions, and auto-apply. It admits no attendee, calendar, title, filename, or speaker-count inputs.
- Remaining gap or next action: bind the versioned eligibility snapshot to post-publish prediction dispatch and persistent attribution receipts before marking this sprint Done.

Goal: automatically convert generic labels to person names in future calls
without increasing false attribution risk.

Workstream 1 - Conservative auto-apply:

- Preserve confidence threshold, match-margin threshold, profile maturity,
  minimum speech duration, and one winning speaker per profile.
- Keep lower-confidence matches as suggestions.
- Treat false auto-apply as the highest-severity recognition failure.

Workstream 2 - Readiness and explanation:

- Show row-level readiness reasons: learning disabled, no voice samples, no
  profiles, immature profile, ambiguous match, short sample, duplicate profile
  candidate, or high-confidence auto-match.
- Keep automatic naming after processing finishes; do not promise live speaker
  identification.

Workstream 3 - Privacy:

- Do not use attendee names, calendar names, expected fixture names, filenames,
  or speaker counts as hidden runtime identity hints.
- Keep profile matching local-only.

Sprint 9 acceptance criteria:

- Future meetings can safely replace `Speaker #` labels with remembered person
  names.
- Uncertain matches stay as suggestions with clear reasons.

## Sprint 10: Rematch Past Meetings

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: detail refresh action, profile refresh service/tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 rematch planner slice).
- User outcome: profiles learned today can safely improve eligible older generic labels without reprocessing or disturbing meeting artifacts.
- Scope / non-goals: expand existing one-meeting refresh into explicit single then opt-in bounded bulk rematch. Do not automatically scan history, queue worker work, retranscribe/re-diarize, alter audio/text/timestamps, override user names/paragraph overrides, or use external identity data.
- Dependencies and decisions: use S1/S8 revisioned identity/suppression and S9 eligibility; S13 provides generic bulk mechanics only after single path. Existing refresh updates identities/Markdown through catalog service but lacks target snapshot/outcome ledger. Rematch acts only on current published structured records with compatible stored samples and writes attribution metadata/derived labels; immutable audio/transcript content fingerprints are captured before/after.
- Implementation slices:
  1. Build `PastRematchPlan` from immutable meeting/revision snapshots: eligibility, generic/suggested/profile state, sample/profile compatibility, suppression/user/override/merge state, active mutation, and metadata-only fingerprint. Return per-target `Eligible`, `Blocked`, or `Skip` reasons; detail action previews one exact target.
  2. Revalidate immediately before dispatch; run the same S9 matcher/correction receipt and refuse changed/missing/busy/reprocessed targets. Record `AutoApplied`, `Suggested`, `Unchanged`, `Skipped`, `Failed`, `Cancelled` with reason; preserve selection/detail only for same identity/revision. Cancel stops undispatched work and never claims rollback.
  3. Add explicit `Rematch eligible meetings` only after single acceptance. Preview count/scope/exclusions, process bounded batches serially/cooperatively, persist resumable operation receipt/dedup key, and surface result/retry for failed/currently blocked targets. No refresh visibility toggle starts a historical batch.
  4. Assert metadata-only operation: never open/write audio, alter transcript words/timestamps/original diarization ids, enqueue worker, or rerun summary/diarization. Mark derived display/summary attribution only if S12 contract requires it; otherwise state that summary content is unchanged. Keep logs/counts path/text/vector free.
- Tests and rendered checks: planner eligibility/exclusion/fingerprint, user/override/suppression preservation, stale/busy/cancel/retry/resume/dedupe, partial batch outcomes, one-meeting/bulk parity, no worker invocation/audio/content mutation, and revision race. Render preview/progress/result/no-eligible at 1280x800/125%, keyboard cancel/retry and screen-reader count/reasons.
- Documentation / installer / release work: document manual metadata-only rematch, eligibility, profile/suppression behavior, cancellation and no transcript/audio reprocessing. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found detail `Refresh Suggestions` and matcher update tests without retranscription, but no historic target planner, bulk preview/result/resume path, concurrency policy, or artifact-integrity proof.
- Evidence and date: 2026-09-29 added `PastRematchPlanner` tests for current/published/compatible generic targets and exclusions for busy, stale, user-entered, structured-transcript, sample, and profile state. The planner holds metadata fingerprints only and does not open audio or invoke processing.
- Remaining gap or next action: revalidate and dispatch the one-meeting path with durable outcome receipt before creating the explicit bounded batch UI.

Goal: let newer confirmed voice profiles improve older generic-speaker
transcripts.

Workstream 1 - Single-meeting rematch:

- Add `Rematch Names` for one published meeting.
- Eligibility: structured transcript, generic speaker labels, stored speaker
  voice samples, local profiles available, and no active mutation for that
  meeting.
- Rematch must not retranscribe, rerun diarization, queue the worker, or rewrite
  audio.

Workstream 2 - Bulk rematch:

- Add `Rematch Eligible Meetings` after the single-meeting path is proven.
- Present a preview count and scope before applying.
- Keep bulk output metadata-only and avoid printing transcript content.

Workstream 3 - Match policy:

- Auto-apply only mature high-confidence matches.
- Write lower-confidence matches as suggestions.
- Respect prior scoped rejections.

Sprint 10 acceptance criteria:

- A profile taught today can safely improve older meetings that still show
  generic speakers.
- Rematch is metadata-only and does not disturb audio, transcription, or
  diarization artifacts.

## Sprint 11: Bad Diarization Repair Guidance

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 structural diagnosis slice).
- User outcome: a reviewer knows whether a speaker problem needs naming, paragraph correction, merge, rematch, or a true label repair—and repair does not silently erase their work.
- Scope / non-goals: make existing suspicious-label/worker-repair capabilities explainable and safe. Do not silently tune clustering, turn repair into name refresh, guarantee quality improvement, or auto-transfer stale speaker identities to newly generated clusters.
- Dependencies and decisions: consume S1–S10 identity/revision/receipt rules. Current catalog flags suspicious distributions and cleanup can queue `RepairSpeakerLabels`; worker has oversegmentation recovery. Replace boolean-only presentation with a pure `SpeakerQualityDiagnosis` from bounded structural metadata: cluster count, tiny-turn ratio, run churn, unsupported count, duplicate effective names, sample/turn coverage, and current processing state—never transcript/audio contents.
- Implementation slices:
  1. Return severity, primary reason, secondary signals, evidence freshness, and one recommended route: Name, Paragraph Override, Merge, Rematch, Repair, or Setup/Wait. Calibrate thresholds on fixtures; no single high count alone automatically claims failure. Missing/corrupt metadata is `Unknown`, not bad diarization.
  2. Present a compact Speaker Review quality well with plain comparison copy and one action. `Refresh suggestions` remains metadata-only; manual merge only for bounded normal split; repair is worker-backed re-labeling with source/queue/readiness/priority/cancel state. Prevent duplicate repair dispatch and exclude active recording/unsafe concurrent mutation.
  3. Preflight repair: snapshot current artifacts/revision, validate retriable source/setup, identify affected speaker corrections/overrides/merge/rematch receipts, and state exactly what will be invalidated or retained. Preserve prior records/read-only audit and clips as stale; never map old cluster ids/names onto new clusters automatically. New repair result opens review-needed state with generic labels until an explicit later action.
  4. Publish repair through current safe worker/publish flow; distinguish queued/running/succeeded/failed/cancelled and source/publish failure. Apply results only to matching meeting identity/new revision, invalidate evidence/clips/rematch snapshots, refresh list/detail once, and retain raw diagnostics only in Advanced/local logs.
- Tests and rendered checks: diagnosis matrix (normal, oversplit, churn, tiny turns, duplicate names, unsupported, missing/stale), route precedence, threshold fixture evidence, queue/retry/cancel/dedupe, preflight preservation, new-revision invalidation, publication failure, and no hidden transcript/audio logging. Render each route/repair lifecycle at 1280x800/125%, keyboard/screen-reader difference between name/override/merge/rematch/repair.
- Documentation / installer / release work: document repair scope, expected wait, preservation/invalidations, no guarantee, and action-choice guide. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found suspicious distribution detection, repair cleanup recommendation/queueing, worker recovery, and tests, but no complete diagnosis/routing contract or correction-preservation/new-revision UX proof.
- Evidence and date: 2026-09-29 added `SpeakerQualityDiagnosisResolver` tests for unknown/current/processing, fragmented labels, repair readiness, naming, and normal merge routing. It uses bounded structural metadata only and does not inspect transcript or audio contents.
- Remaining gap or next action: surface the diagnosis in Speaker Review and add repair preflight receipt/invalidation behavior before marking this sprint Done.

Goal: prevent users from trying to rename their way through broken clustering.

Workstream 1 - Quality signals:

- Detect too many fragment speakers, excessive speaker-run churn, duplicate
  names across clusters, many tiny turns, and unsupported speaker counts.
- Show a speaker-label quality issue in Speaker Review when these conditions
  are present.

Workstream 2 - Action routing:

- Keep `Refresh Suggestions` metadata-only.
- Keep `Repair Speaker Labels` worker-backed and transcript-first.
- Invalidate stale clips and review rows after repair.

Workstream 3 - Copy:

- Explain the difference between:
  - name cleanup,
  - paragraph attribution override,
  - merging duplicate speakers,
  - rematching names from profiles,
  - repairing bad speaker labels.

Sprint 11 acceptance criteria:

- Users understand when to rename, override, merge, rematch, or repair.
- Repair remains discoverable without implying it is a name-refresh shortcut.

## Sprint 12: Summary And Derived Output Consistency

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 attribution-state slice).
- User outcome: people can still read an earlier summary while knowing speaker attribution changed, and can deliberately regenerate it from current effective names.
- Scope / non-goals: track speaker-attribution staleness for summary and named derived meeting outputs. Do not auto-call a provider, re-run transcription/diarization, edit generated prose in place, overwrite a readable prior result, or broaden external data-sharing consent.
- Dependencies and decisions: consume S1 effective identity, S6 overrides, S7 merges, S11 repair revisions, and existing summary provider/consent flow. Current summary fingerprint handles transcript generation but not necessarily effective attribution. Define `AttributionFingerprint` over artifact revision plus ordered stable segment ids, effective speaker ids/display labels/sources and override/merge version—never raw profiles/vectors/audio; pair it with existing transcript fingerprint.
- Implementation slices:
  1. Store generation provenance with both fingerprints, speaker-attribution schema version, provider/model/config route, and generated timestamp. Resolver returns `Current`, `AttributionChanged`, `TranscriptChanged`, `Unavailable`, `Generating`, `Failed`; old summary remains readable with compact exact stale reason and no misleading current-name claim.
  2. Every committed speaker-level correction, override, merge, rematch, or repair recomputes attribution freshness after source write. Repair/new transcript makes old summary historic/review-needed; it never maps old named prose to new clusters. Summary/action-item ownership labels derived from stale content are marked historic rather than silently rewritten.
  3. Offer `Regenerate summary with current speaker names` only when structured current artifact, provider configuration/consent, and revision are valid. Reuse manual summary path with immutable input snapshot; no transcription/diarization/voice matching and no automatic dispatch. Cancel/failure preserves prior snapshot and status; successful output atomically replaces only matching revision/fingerprints.
  4. Keep output boundaries consistent: transcript JSON/Markdown use effective labels per their own writer; summary snapshot/export/reader show provenance/freshness; logs/status contain counts/version only. A configured hosted provider receives only the already-authorized current summary input, not profile internals or hidden original-attribution audit data.
- Tests and rendered checks: fingerprint sensitivity for rename/override/merge/rematch/repair and insensitivity to irrelevant metadata; legacy snapshot migration; reader stale/current/failure/cancel; provider/consent/setup blocks; atomic revision race; no worker invocation/profile leak. Render current/stale/historic/regenerate-result at 1280x800/125%, keyboard/screen-reader freshness/provenance.
- Documentation / installer / release work: document stale-summary meaning, manual regeneration, transcript/diarization boundary, and provider/privacy conditions. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found summary transcript fingerprint reuse and speaker identity artifact updates, but no speaker-attribution fingerprint/stale resolver, repair preservation rule, or current-name regeneration proof.
- Evidence and date: 2026-09-29 added attribution-fingerprint/state fixtures for effective label/source changes, transcript changes, and readable historic summary regeneration state. The model excludes profiles, vectors, and audio.
- Remaining gap or next action: persist generation provenance and bind manual current-name regeneration to matching revision/fingerprint before marking this sprint Done.

Goal: make speaker edits flow into downstream meeting outputs.

Workstream 1 - Stale summary detection:

- Mark summaries stale when speaker names, paragraph attribution, or speaker
  merges change after summary generation.
- Do not auto-regenerate summaries after every rename.
- Preserve readable summary display while clearly showing that speaker
  attribution changed later.

Workstream 2 - Regeneration:

- Offer `Regenerate Summary` only when structured JSON and summary provider
  configuration are available.
- Use the existing manual summary-generation path.
- Do not rerun transcription or diarization during summary regeneration.

Sprint 12 acceptance criteria:

- Summaries and action items do not silently keep obsolete `Speaker 1`
  attribution.
- Regenerated summaries use current effective speaker names.

## Sprint 13: Profile Management And Privacy

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: local store, settings controls, disable/delete tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 lifecycle contract slice).
- User outcome: users see what voice memory is stored locally, control future matching safely, and understand exactly what disable/delete changes.
- Scope / non-goals: complete local profile lifecycle and privacy UI/docs. Do not create cloud/shared profiles, export embeddings, expose raw vectors/samples by default, or erase published user corrections when a profile changes.
- Dependencies and decisions: consume S1/S8 receipts/suppression and S9 matching. `VoiceProfileStore` persists local centroids, sample count, meeting ids and rejections; it is sensitive voice-derived data. Default UI projects only name, active/disabled state, maturity, sample count, last match, and local-storage explanation; file path is support/Advanced-only.
- Implementation slices:
  1. Define lifecycle consequences: disable immediately excludes future matching/learning while preserving profile data and historic attributions; enable requires valid compatible profile; delete permanently removes centroid, samples, meeting/rejection history and blocks future matching. Existing user names stay; prior auto attribution becomes historic/unavailable-profile provenance, never silently changed or re-matched.
  2. Add preflight/confirmation for selected/delete-all with affected profile count/name, irrevocability, no export/cloud claim, active matching/learning conflict, and result. Revalidate store revision, serialize profile mutation with learner/matcher updates, cancel safely, and refresh settings/review eligibility after committed source truth.
  3. Handle corrupt/missing/unwritable local store safely: name corrections still work; Settings shows local-memory unavailable with repair/open-safe-support route, never recreates/deletes data silently. Store migration/version/atomic write/backups preserve valid data and avoid raw profile content in normal errors/logs.
  4. Make privacy durable: clear local-only/sensitive voice-derived disclosure, retention location class, disable/delete meanings, and absence from transcript JSON/Markdown/summary/export/log/provider input. Add serialization/packaging scans to prove exclusions; no profile copy travels through archive/import/merge/release bundles unless explicitly documented local runtime data.
- Tests and rendered checks: disable/enable/delete/delete-all effects on matcher/learning/historic attribution, confirmation/cancel/stale revision, concurrent learn/delete, corrupt/missing/unwritable store, atomic recovery/migration, no published/export/log/provider profile payload, and settings/detail refresh. Render mature/immature/disabled/missing/corrupt profiles at 1280x800/125%, keyboard/screen-reader sensitive-data/consequence copy.
- Documentation / installer / release work: document local path class, sensitive voice-derived data, lifecycle, artifact exclusions, backup/recovery limit, and no cloud sharing. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found profile list/disable/delete controls and storage tests, but no full lifecycle consequence contract, matching race proof, corrupt-store UX, or privacy exclusion audit across shipped artifacts.
- Evidence and date: 2026-09-29 added `VoiceProfileLifecycleResolver` tests for disable/enable/delete consequences, confirmation, active-mutation blocking, unavailable-store behavior, and historic-attribution preservation.
- Remaining gap or next action: add store-revision serialization and corrupt-store settings state plus profile-payload exclusion scans before marking this sprint Done.

Goal: make voice memory trustworthy and controllable.

Workstream 1 - Settings controls:

- Show local profile name, sample count, last matched, active or disabled
  state, disable, delete selected, and delete all.
- Explain when a profile is too immature for auto-apply but eligible for
  suggestions.
- Keep profile management in Settings while review actions stay on the meeting.

Workstream 2 - Privacy posture:

- Document local profile paths.
- Label embeddings as sensitive voice-derived data.
- State that transcript JSON exports do not include embeddings or full profile
  payloads.

Workstream 3 - Out-of-scope boundary:

- Keep shared organization speaker profiles out of this roadmap.
- Keep cloud voice matching out of this roadmap.

Sprint 13 acceptance criteria:

- Users can see and control what the app remembers.
- Local-only voice memory is clear in UI and docs.

## Sprint 14: Calibration And Experience Harness

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: fixture/calibration scripts, threshold parser, replay tests); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 promotion gate slice).
- User outcome: threshold/model changes demonstrably reduce manual cleanup without silently increasing wrong automatic names.
- Scope / non-goals: turn existing synthetic fixture tooling into a controlled quality/release harness. Do not use fixture labels/names/files/attendees as runtime hints, train production profiles from corpus data, collect user meeting data, or auto-promote thresholds.
- Dependencies and decisions: S9 defines false attribution severity; S11 diagnosis and S15 UX states consume results. Existing scripts support metadata-only calibration candidates. Build a versioned corpus manifest separating public synthetic fixtures from access-controlled consented audio; runtime sees neither labels nor expected outcomes. Candidate config is test-only/explicit and cannot change production defaults outside reviewed release config.
- Implementation slices:
  1. Define corpus metadata: opaque fixture id/version/hash, consent/classification, duration/noise/overlap/shared-mic/speaker-count bands, blinded expected diarization/identity outcomes, and split assignment. Include one/two/three-plus, similar voices, overlap, noise, short, hybrid/shared-mic, no-speech and failure cases; no private transcript text/path/name in reports.
  2. Produce deterministic metrics per candidate/baseline: cluster count/support, turn/coverage/churn, merge/repair/override rates, generic/suggestion/auto counts, accepted/rejected/suppressed decisions, clip availability, and identity precision/false-auto-apply. Report confidence interval/sample size/unknowns; speaker-count correctness and false-auto-apply gate outrank cosmetic churn reduction.
  3. Make promotion explicit: pinned source/model/runtime/corpus/candidate hashes, clean environment, repeat runs, baseline comparison, protected-case no-go thresholds, human reviewer/sign-off, rollback config, and retained metadata-only evidence. Any false auto-apply in protected set blocks auto-name threshold promotion pending investigation.
  4. Enforce boundary mechanically: fixture assertions only in test harness; production matcher has no catalog/name/file/attendee/count input and ignores calibration env overrides unless an approved development harness context is active. Add static dependency tests and redaction tests for reports/logs.
  5. Add experience fixtures from outputs—not expectation hints—for generic/suggested/auto/suppressed/ambiguous/repair/clip/override/merge states; render S2 review and S15 accessibility journeys and compare state/copy/focus behavior.
- Tests and rendered checks: manifest/schema/hash/split validation, deterministic replay, metric arithmetic/gates, false-auto no-go, baseline/candidate regression, missing/corrupt/unauthorized fixture handling, promotion/rollback metadata, production isolation/redaction, and rendered state matrix at 100/125%.
- Documentation / installer / release work: document corpus governance, command, promotion authority, no-go rules, report retention/redaction, and release evidence. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found candidate allowlists, metadata-only dry runs and fixture replay, but no labeled/blinded corpus contract, release promotion decision record, false-auto gate, production-isolation proof, or experience-fixture matrix.
- Evidence and date: 2026-09-29 added `DiarizationPromotionGate` tests that prohibit promotion for incomplete protected evidence, protected false automatic attribution, reduced speaker-count correctness, or higher false-auto counts; passing candidates still require human review.
- Remaining gap or next action: add corpus-manifest schema and synthetic protected-case metrics before any threshold change.

Goal: prove the system reduces manual effort without unsafe automatic naming.

Workstream 1 - Fixture metrics:

- Extend diarization fixture reports with generic speaker count, suggestion
  count, auto-apply count, false auto-apply count, rejected suggestion count,
  clip availability, speaker-run churn, merge candidates, segment overrides,
  and repair flags.
- Keep reports metadata-only.

Workstream 2 - Protected cases:

- Include one-speaker, two-speaker, three-plus-speaker, similar-voice,
  overlapping-speech, noisy-audio, short-call, and hybrid/shared-mic fixtures.
- Treat false auto-apply as a no-go for threshold promotion.
- Treat speaker-count correctness as more important than reducing speaker-run
  count.

Workstream 3 - Runtime boundary:

- Keep expected names, expected speaker counts, fixture labels, attendee names,
  and filenames as assertions or UI suggestions only, never runtime hints.

Sprint 14 acceptance criteria:

- Calibration can detect regressions in both diarization quality and
  speaker-name recognition.
- Auto-naming cannot regress silently.

## Sprint 15: UI Polish, Accessibility, And Rendered QA

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 focus/semantic acceptance contract).
- User outcome: speaker cleanup is quick, dense, legible, keyboard-operable, and calm across ordinary and difficult review states.
- Scope / non-goals: polish/verify Speaker Review, transcript label actions/evidence clips, repair states, and Settings profile management after S1–S14 behavior settles. Do not add a marketing wizard/card grid, hide required actions, replace native WPF semantics gratuitously, or redesign unrelated Meetings flows.
- Dependencies and decisions: `DESIGN.md` is authority: opaque tonal nesting, 1px technical/inset edges, no shadows/gradients, max 4px radius, Segoe interaction text, Cascadia Mono/Consolas technical time/counts, dense spacing. Current detail/Settings use DataGrids and maintenance controls with limited explicit semantic properties; build visual/assistive acceptance from S1–S14 output fixtures, not private real meetings.
- Implementation slices:
  1. Build deterministic fixtures for no labels/generic/suggested/auto/user/ambiguous/suppressed, evidence/clip states, override/merge/conflict, rematch, repair lifecycle, stale summary, profile mature/disabled/missing/corrupt, long/duplicate names, and busy/recording blocks. Use sanitized local data; fixtures drive render and keyboard checks.
  2. Render one transcript-adjacent Speaker Review well with stable header/count/status, compact rows, bounded suggestion/evidence/playback/action lanes, and visible draft/result feedback. Keep repair separate. Use well surface hierarchy/outline/spacing; labels wrap or disclose accessibly, essential actions never clip/overlap/require hover, and active playback/progress stays in current viewport.
  3. Publish speaker-specific focus graph: transcript label → matching review row/name/evidence/clip → apply/result → return transcript; profile table → disable/delete confirmation → originating row; repair/rematch/dialog lifecycle. Define Tab/Shift+Tab/arrows/Enter/Space/Escape, initial/modal focus, dirty-draft behavior, and safe focus after async completion/close.
  4. Add native/explicit semantics: labelled fields, `AutomationProperties.Name`/HelpText for compact buttons, source/provenance/reason/disabled explanation, row/group counts, clip state/progress, destructive-profile confirmation. Status announces user actions once; background refresh/clip progress avoids live-region spam. Color/icon never carries source, safety, or readiness alone.
  5. Run rendered matrix at supported 1280x800 and 1440x900/1920x1080, 100%/125%, high contrast and long localized-style strings. Check resize/scroll/focus, detail/Settings navigation, no horizontal essential loss, contrast/focus visibility, and Technical Studio no-shadow/no-oversize-radius rules. Classify/fix clipping, hierarchy, semantics, or state mismatch before cosmetic changes.
- Tests and rendered checks: structural XAML/style assertions, state-to-view/focus tests, UI automation or documented Narrator script for fixture matrix, keyboard-only copy/select/label action/suggestion/clip/repair/profile delete, and high-contrast visual review. Capture reproducible screenshots/log-free accessibility observations as release evidence.
- Documentation / installer / release work: document supported QA sizes/scales, keyboard behavior, speaker accessibility script, and Technical Studio criteria. No installer work until behavior changes; Sprint 16 owns release gates.
- Evidence and date: 2026-09-27 audit found speaker DataGrids/detail controls/profile table and limited tooltips/access-key patterns, but no speaker fixture catalog, focus graph, semantic-name inventory, or rendered/accessibility evidence against `DESIGN.md`.
- Evidence and date: 2026-09-29 added `docs/speaker-review-accessibility.md` with deterministic supported-size/scale checks, speaker/profile focus graph, semantic/disabled-state expectations, and Technical Studio criteria.
- Remaining gap or next action: inventory actual Speaker Review/profile controls against the fixture catalog; render generic/suggested/repair/profile-delete states before style changes.

Goal: make the workflow fast, dense, and usable in the real WPF app.

Workstream 1 - Interaction polish:

- Ensure active clip playback is visible in the current viewport.
- Keep buttons and status text compact, clear, and action-oriented.
- Avoid hidden or below-the-fold-only feedback after clicks.

Workstream 2 - Keyboard and accessibility:

- Support keyboard navigation across speaker rows, evidence clips, transcript
  speaker labels, and action buttons.
- Add accessible names for clip controls and suggestion actions.
- Validate disabled states for missing audio, no samples, no profiles, busy
  app, recording active, or ineligible repair.

Workstream 3 - Rendered layout:

- Check normal and smaller desktop window sizes.
- Ensure names, suggestions, provenance, and buttons do not overflow.
- Preserve the Technical Studio design rather than adding a marketing-style
  wizard or card-heavy flow.

Sprint 15 acceptance criteria:

- Speaker review is usable during real post-meeting cleanup.
- The UI remains dense but not cramped or incoherent.

## Sprint 16: Documentation, Installer, And Release Smoke

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 acceptance-matrix slice).
- User outcome: shipped speaker cleanup, local voice-memory, and recovery behavior is traceable, privacy-safe, and demonstrably works from installed product—not only source checkout.
- Scope / non-goals: turn accepted S1–S15 behavior into release evidence, docs, package validation, and support-ready recovery guidance. Do not claim unbuilt roadmap behavior, publish any release, upload assets, include private meetings/profiles/audio in evidence, or treat a developer deployment as installer/update validation.
- Dependencies and decisions: S1–S15 define contracts; `README.md`, `SETUP.md`, `ARCHITECTURE.md`, `PRODUCT_REQUIREMENTS.md`, `RELEASING.md`, `DESIGN.md`, fixture docs, and package scripts are authority. Existing docs already describe local diarization and MSI path, but no release matrix proves expanded review/profile behavior. Only versioned `MeetingRecorder-v<version>-win-x64.zip` is a valid in-app-update asset; installer/MSI/bundle and installed startup must separately pass.
- Implementation slices:
  1. Create a S1–S15 acceptance matrix mapping each user contract to source revision, focused test/fixture command, rendered or accessibility evidence, doc location, artifact-exclusion/privacy proof, and release gate. Mark missing evidence as open; no `Done` status through inference.
  2. Update product docs with exact user-facing boundaries: generic/suggested/auto/user labels; review, evidence, clips, overrides, merge/rematch/repair/undo; stale-summary/manual regeneration; local voice-derived profile storage, disable/delete, recovery limits, and no cloud/export/profile-payload claim. Keep support examples synthetic and paths/classifications safe.
  3. Add focused tests and fixture commands per acceptance-matrix row: identity precedence/revisions, review/evidence/clip lifecycle, correction/rejection/undo, matching/rematch/repair, derived freshness, profile lifecycle/exclusions, calibration no-go, and S15 keyboard/render checks. Preserve Test-All and AppPlatform test boundaries; failure evidence records phase without weakening tests.
  4. From a clean, committed source revision, run focused tests, `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`, then `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`. Capture version, commit, command, result, bundle layout/integrity hashes, installer/MSI hashes, model/runtime version, and redacted logs. Verify V2 stable apphosts/loose DLL manifests remain aligned; never include profiles, fixture audio, transcript content, or secrets.
  5. With no active installed app or worker, install/use the packaged path and run `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`. Exercise a sanitized speaker-review journey through supported UI: generic name/read-only evidence state, allowed correction, blocked recording/busy case, profile disable/delete confirmation, and stale-summary disclosure; record package-installed result and Windows crash-event outcome. Test in-app-update acceptance/rejection separately using only synthetic/versioned assets; do not call live update endpoints or publish without authorization.
- Tests and rendered checks: acceptance-matrix completeness/no false `Done`; source and fixture regression suite; serialization/log/export/provider scans for profile/audio exclusion; installer layout/integrity/apphost assertions; installed smoke; S15 fixture screenshots at 1280x800 and 1440x900/1920x1080, 100%/125%, keyboard/Narrator or UI-automation focus graph, high contrast, long strings, active/busy/error/recovery states. Any clip-cache cleanup, generated summary, or worker result is checked after restart where its contract requires persistence.
- Documentation / installer / release work: README gives concise capability/privacy boundary; SETUP covers model readiness, local profile data, recovery, and accessibility route; ARCHITECTURE documents artifact boundaries/revisions/freshness; PRODUCT_REQUIREMENTS states user promises; RELEASING carries command order, package provenance, smoke, rollback/support evidence and authorized publish boundary. Behavior changes require fresh installer assets and relevant docs; this planning-only change does not rebuild packages or run a release.
- Evidence and date: 2026-09-27 audit found local-diarization/readme, MSI/setup, release-script, and fixture-template coverage, but no S1–S15 traceability matrix, installed speaker-review smoke, profile-payload artifact scan, or package provenance tying expanded behavior to a shipped build.
- Evidence and date: 2026-09-29 added `docs/speaker-review-release-matrix.md`, which maps S1–S15 contracts to current test/doc evidence and explicitly marks missing persistence, UI, fixture, and package proof as open.
- Remaining gap or next action: implement and verify the listed upstream gaps, then run Test-All, installer build, and installed smoke from a clean committed revision before declaring any speaker sprint Done.

Goal: ship the behavior as a documented product path.

Workstream 1 - Documentation:

- Update `README.md`, `SETUP.md`, and `ARCHITECTURE.md`.
- Document rename, inline clips, segment attribution, merge, local learning,
  rematch, reject, undo, repair, summary refresh, profile controls, privacy,
  and clip-cache behavior.
- Do not document fixture-only labels, private names, or real meeting content
  as runtime behavior.

Workstream 2 - Tests:

- Add or update tests for speaker review row construction, evidence selection,
  clip extraction and cleanup, transcript label focus, paragraph override,
  merge, correction learning, reject, undo, rematch, summary stale state,
  profile management, and privacy exclusion.

Workstream 3 - Release:

- Run focused tests first.
- Run the full gate:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Because this is app/UI/runtime behavior, rebuild installer assets:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Run packaged smoke after confirming no active installed app or processing
  worker:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Sprint 16 acceptance criteria:

- Docs, tests, installer assets, and packaged smoke match the shipped behavior.
- Release evidence proves the key speaker review and local voice-memory paths.

## Test Scenarios

- User opens a diarized meeting and sees a first-class Speaker Review well.
- User plays clips for `Speaker 1`, identifies the person, and applies the name
  to all turns for that speaker.
- User clicks a speaker label in the transcript and lands on the matching
  review row.
- User corrects one wrongly attributed paragraph without renaming the entire
  speaker.
- User merges two speaker labels that represent the same person.
- User accepts a profile suggestion and applies names.
- User rejects a bad suggestion and the same profile is not offered again for
  that meeting speaker.
- User refreshes suggestions after a profile improves and no transcription,
  diarization, or audio rewrite occurs.
- User teaches a name in one meeting and sees it auto-applied or suggested in a
  future meeting according to confidence guardrails.
- User rematches an older generic-speaker meeting from newer local profiles.
- User undoes profile-sourced recognition while preserving explicit edits.
- User sees a summary marked stale after speaker attribution changes and can
  regenerate it when configured.
- User sees repair guidance for suspicious speaker-label explosions.

## Interfaces And Constraints

- No cloud voice matching.
- No shared organization speaker profiles.
- No full transcript text editing in this roadmap.
- Audio clips are temporary local review aids, not published artifacts.
- Voice embeddings stay local and are never exported in transcript JSON,
  Markdown, summaries, logs, or release evidence.
- Attendee names are user-visible suggestions only, not hidden identity hints.
- Runtime recognition must not use expected speaker counts, expected names,
  fixture labels, filenames, or attendee counts as hints.
- Automatic future naming remains conservative; uncertain matches stay as
  suggestions.

# Speaker Name Recognition Revised Plan

## Summary

This plan revises the downloaded `PLAN.md` against the current Meeting Recorder implementation as of 2026-05-24. The original roadmap is still directionally right, but the repo already contains much of Sprint 1, Sprint 2, and part of Sprint 4: correction-based local learning, profile-backed suggestions, conservative auto-apply guardrails, meeting-detail review actions, Settings profile management, and no-deploy fixture scripts now exist.

The next work should stop treating this as a greenfield feature and instead stabilize the current implementation, expand fixture evidence, tune thresholds from measured examples, and add safer repair/undo paths for already diarized meetings.

## Current Implementation Compared With Downloaded Plan

- Original Sprint 1, Close The Learning Loop: mostly implemented. Speaker-name corrections route through `SpeakerNameCorrectionService`, artifacts are updated before learning, profile-store failures are non-blocking, both library and meeting-detail flows call the learning path, provenance fields exist, and docs now describe local voice-profile behavior.
- Original Sprint 2, Review, Confirm, Reject: partially implemented. Meeting details show profile provenance and suggestions, Use/Reject actions exist, rejected profile matches are stored, and Settings can enable learning plus disable/delete profiles. Remaining work is polish, duplicate-training safeguards, manual UX validation, and stronger view-model coverage.
- Original Sprint 3, Evaluation Harness And Calibration: partially implemented. `Analyze-Diarization.ps1`, `Test-DiarizationFixture.ps1`, and `Test-DiarizationFullAudioFixture.ps1` provide no-deploy fixture loops, including optional expected speaker-name assertions. Remaining work is a labeled fixture corpus, richer metrics, and threshold tuning from evidence.
- Original Sprint 4, Safe Automatic Recognition: partially implemented. Auto-apply now requires confidence, margin, profile maturity, and sample duration; lower-confidence matches remain suggestions with decision reasons; already diarized meetings can refresh speaker-name attribution without retranscribing. Remaining work is undo, repair workflow polish, calibrated thresholds, and release notes.

## Sprint 1: Stabilize The Current Feature Baseline

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: correction, matcher, store, and basic editor paths exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 review-state projection slice).
- User outcome: conservative local speaker-name corrections remain trustworthy when learning/profile operations fail, while review controls accurately show what can happen before the user commits a name.
- Scope / non-goals: stabilize existing manual correction, local learning, suggestion/rejection, provenance, and refresh behavior. Do not add recognition sources, train from arbitrary text, change thresholds, re-run diarization/transcription during refresh, introduce cloud/shared profiles, or imply automatic identity certainty.
- Dependencies and decisions: `SpeakerNameCorrectionService` already writes meeting artifacts before best-effort learning; `VoiceProfileMatcher` supplies conservative suggestion/auto decisions; `VoiceProfileStore` is local. Existing detail row exposes `HasSuggestion`, Use, Reject, and Voice Profile; inspect library and detail separately before sharing state. Define correction idempotency with immutable `meetingId + stable speakerId + normalized target name + voice-sample/profile-model fingerprint`; profile persistence owns duplicate prevention, while artifact writes stay replayable. Define rejection key exactly `meetingId + stable speakerId + profileId + source artifact revision`, never visible label/text alone; revision mismatch invalidates stale row state and requires fresh decision.
- Implementation slices:
  1. Extract a pure review-row/status projection from artifact speaker detail: `Unavailable`, `NoSuggestion`, `Suggested`, `AutoApplied`, `Rejected`, `Stale`, `Busy`, with reason/provenance. No-suggestion/stale/busy rows disable Use/Reject and expose why; Use drafts suggestion only, Reject drafts original-label restoration plus scoped suppression, and only Apply commits source truth. Keep focus/result stable after refresh.
  2. Make correction commit a revision-checked transaction: persist JSON/Markdown/manifest effective name/provenance first; then invoke idempotent learner with immutable receipt. On learner/store/manifest failure return committed-artifact versus uncommitted-artifact truth, actionable local warning, and retry-safe receipt. Retry never doubles samples or clears prior rejection; source-write failure never trains.
  3. Persist rejection in local bounded profile decision state keyed by required tuple, with created artifact/model version and explicit expiry/revalidation rule. Clear matching suggestion metadata only after successful artifact commit. Suppression applies to same meeting speaker only; different speakers/meetings retain normal matching. Profile disable/delete/corruption/unwritable storage produces no false success/no unbounded retry/log payload.
  4. Define Refresh Suggestions as metadata/embedded-voice-sample matching over immutable snapshot: no capture/transcription/diarization/learning, no overwrite of user edits/explicit rejection, and one aggregate result with counts/reasons (`auto-applied`, `suggested`, `ineligible`, `suppressed`, `unavailable`, `stale`). Revalidate revision before save; cancel/stale result changes nothing.
  5. Enforce published-boundary projection: transcript JSON/Markdown can contain safe ids, decision source/reason, normalized display name and bounded confidence only when intended; never embeddings, raw clips/audio, store documents, rejection history outside needed meeting artifact, diagnostic transcript text, prompts, credentials, or private paths. Use allowlist serialization and redacted count-only logging.
- Tests and rendered checks: correction artifact-first/learning-failure/source-write-failure/retry/idempotency and revision race; matcher gates/rejection tuple/different-speaker behavior; missing/corrupt/disabled/unwritable store; Refresh never calls transcription/diarization/learner and preserves user/rejected states; JSON/Markdown/manifest schema/migration/allowlist and logs/provider/export redaction. Render/detail+library matrix for no suggestion/suggestion/auto/rejected/busy/stale at 1280x800 and 125%, keyboard Use/Reject/Apply/Refresh and screen-reader reason/status; use synthetic data only.
- Documentation / installer / release work: document local-only recognition, conservative suggestions versus auto apply, rejection scope, Refresh boundary, learning failure behavior, and artifact privacy. Runtime/UI changes later require focused tests, `Test-All.ps1`, installer rebuild, and installed smoke; no build/package action for this plan-only refinement.
- Evidence and date: 2026-09-27 audit found existing row state with Use/Reject handlers, provenance fields, artifact update tests, matcher guardrail tests, profile controls, and transcript schema embedding exclusion. Missing proof includes a single authoritative cross-surface state projection, stable-id/idempotency receipt, source-revision rejection scope, refresh side-effect fence, and end-to-end privacy/logging matrix.
- Evidence and date: 2026-09-29 added `SpeakerRecognitionReviewStateResolver` with unavailable/stale/busy/rejected/no-suggestion/suggested/auto states and focused correction/matcher/schema verification.
- Remaining gap or next action: wire revisioned receipt/rejection scope to the live handler and verify installed manual smoke once the package gate is unblocked.

Goal: make the current speaker-name recognition implementation reliable enough to ship as a conservative, auditable local feature.

Current baseline to preserve:

- `SpeakerNameCorrectionService` already updates meeting artifacts before best-effort speaker-name learning.
- Meeting detail already has Voice Profile, Suggestion, Use, Reject, and Refresh Suggestions paths.
- Settings already has local speaker-name learning and profile management controls.
- `VoiceProfileMatcher` already uses conservative auto-apply guardrails for confidence, margin, profile maturity, and sample duration.
- Fixture scripts already exist, but labeled fixture corpus work and threshold calibration stay in Sprint 2 and Sprint 3.

Workstream 1 - UI review polish:

- Disable or hide Use and Reject buttons for rows that do not have a profile suggestion.
- Keep Use behavior unchanged for suggested rows: copy the suggested display name into the editable display-name field and leave final application to the Apply Speaker Names action.
- Keep Reject behavior unchanged for suggested rows: restore the original label for that row, mark the suggestion rejected, and leave rejection persistence to Apply Speaker Names.
- Make Refresh Suggestions status explicit in meeting detail: it should say whether names were auto-applied, suggestions were added, no eligible profile matches were found, or refresh was skipped because voice samples/profiles are unavailable.
- Verify the Voice Profile/provenance column is populated for suggested and auto-applied profile matches in both the library speaker-name editor and the meeting-detail speaker-name editor.
- Keep anonymous or low-confidence voices anonymous; do not add new auto-apply paths in this sprint.

Workstream 2 - Learning idempotency and correction safety:

- Prevent duplicate profile training when a user saves the same unchanged speaker-name correction more than once for the same meeting speaker.
- Treat idempotency at the training boundary, not by blocking artifact updates: transcript, manifest, and Markdown corrections should still be safely re-applied when needed.
- Preserve the existing safety order: update artifacts first, then attempt learning.
- If the manifest is missing or the profile store is corrupt, missing, disabled, or unwritable, speaker-name correction must still succeed and return a clear learning warning.
- Keep profile updates local-only and bounded to the existing voice-profile store path.

Workstream 3 - Negative feedback clarity:

- Document in tests or a short code comment that suggestion rejection is scoped to `meetingId + speakerId + profileId`.
- Do not treat rejection as a global ban on that profile or that person.
- Rejecting a suggestion must clear profile suggestion metadata from the meeting artifacts and store rejection memory so the same profile is not offered again for that same meeting speaker.
- A rejected suggestion should not prevent a future different meeting speaker from matching the same profile.

Workstream 4 - Privacy and logging guardrails:

- Verify published Markdown and transcript JSON can include safe provenance fields, such as profile id, confidence, suggested display name, name source, and decision reason.
- Verify published Markdown, transcript JSON, summaries, logs, and status text never include voice embeddings, raw audio snippets, full profile payloads, transcript text in diagnostic logs, prompts, API keys, auth headers, or private local context.
- Keep fixture and smoke output metadata-only when testing speaker-name behavior.

Workstream 5 - Manual smoke path:

- Validate the installed app UI after implementation when no active app or worker session is running.
- Smoke the Settings flow: toggle local speaker-name learning, inspect profile rows, disable a profile, delete one profile, and delete all profiles using non-production test data.
- Smoke the meeting-detail flow on a safe test meeting: view provenance, Use a suggestion, Reject a suggestion, Apply Speaker Names, and Refresh Suggestions.
- Do not force-close a running Meeting Recorder instance or active processing worker for smoke testing; record smoke as blocked if the installed app is active.

Sprint 1 acceptance criteria:

- Correcting a speaker label updates transcript JSON, Markdown, and manifest artifacts even if learning fails.
- Re-saving the same unchanged correction for the same meeting speaker does not increment the learned profile sample count.
- Rows without suggestions cannot produce confusing Use or Reject actions.
- Reject clears suggestion metadata and suppresses that same profile for that same meeting speaker.
- Refresh Suggestions never retranscribes audio and never re-runs diarization.
- Published artifacts contain safe provenance but never voice embeddings or raw profile payloads.

Sprint 1 tests to add or confirm:

- `SpeakerNameCorrectionServiceTests`: artifact update survives learning failure; duplicate unchanged correction does not add a training sample; refresh uses existing speaker voice samples without transcription or diarization.
- `SpeakerNameLearningServiceTests`: repeated same-meeting/same-speaker correction is idempotent; corrected profile updates do not train a rejected wrong profile.
- `VoiceProfileMatcherTests`: rejected matches are meeting-speaker-specific; low-confidence, low-margin, immature, short, and duplicate-profile matches stay suggestions.
- `VoiceProfileStoreTests`: corrupt/missing/disabled/unwritable profile-store scenarios preserve safe behavior and profile management operations remain local.
- `MeetingOutputCatalogServiceTests` and `TranscriptSchemaTests`: provenance fields round-trip while embeddings and profile payloads stay out of published artifacts.
- WPF interaction or view-model tests: no-suggestion rows disable or hide review actions, suggestion Use/Reject updates row state, and pending config changes include speaker-name learning mode.

Sprint 1 verification:

- Focused test filter covering `SpeakerNameCorrectionServiceTests`, `SpeakerNameLearningServiceTests`, `VoiceProfileMatcherTests`, `VoiceProfileStoreTests`, `MeetingOutputCatalogServiceTests`, `TranscriptSchemaTests`, and relevant `MainWindowInteractionLogicTests`.
- Full gate after implementation: `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Installer rebuild after runtime/UI implementation: `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Packaged startup smoke after verifying no active installed app or worker session: `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

## Sprint 2: Build Fixture Evidence And Metrics

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: catalog runner, schema/example, replay/full-audio scripts, hash/redaction tests exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 redacted metric slice).
- User outcome: maintainers can measure diarization/name-recognition regressions locally without exposing meeting content or letting expected answers influence production decisions.
- Scope / non-goals: operationalize private, consented fixture evidence and metadata-only reports over existing runners. Do not commit real fixtures, names, transcripts, audio, embeddings, profile stores, expected answers, or production threshold changes; do not call app/worker/installer/update services from harness.
- Dependencies and decisions: S1 freezes correction behavior; S3 consumes evidence. `Test-DiarizationFixtureCatalog.ps1`, full-audio/stored-turn runners, schemas/examples, and contract tests already cover parts of catalog, output placement, protected hashes, and redaction. Treat catalog metadata as untrusted input. Separate opaque fixture identity from a restricted truth store: production invocation receives only audio/manifest/config; assertion process receives expected count/name/mapping after production result is immutable. Never pass truth through environment, command line, working folder visible to worker, profile store, manifest, attendee metadata, or runtime config.
- Implementation slices:
  1. Define private catalog/truth-store governance: opaque id/version/content hash, mode, scenario tags, consent/classification/owner/expiry, enabled/skip reason, input/protected-artifact allowlists, baseline split, and expected-outcome reference. Validate schema before file access; reject roots inside source control, published output, app data/profile paths, or report output. Replace person/customer-specific plan labels with approved opaque ids plus scenario taxonomy; availability never weakens required-category coverage.
  2. Harden deterministic runner dispatch: derive stored-turn versus full-audio mode from validated metadata, copy only permissible source input into unique temp root, set clean/fixed test config, prohibit network/hosted-summary/profile-store reuse, apply timeout/cancellation, and remove temp outputs on success/failure while preserving metadata-only failure receipt. Default selection is explicit/limited; disabled/missing/expired/unauthorized entries skip with safe reason and make coverage gap fail intended promotion run.
  3. Define result and truth evaluation schemas. Production result has only opaque id, version/hash, mode, environment/model/config hashes, count/turn/sample/elapsed metrics, safe result hashes, and outcome. Assertion result adds protected expected count/name/mapping evaluation in redacted counts/statuses: no actual names or text. Define false auto-apply as an automatic decision whose stable speaker-to-approved identity mapping disagrees with restricted truth; unknown/unmapped is not a true match. Keep count correctness, protected-case false auto-apply, and failure/skip rates separate from usability metrics.
  4. Prove read-only behavior over every declared source and published artifact: canonicalize paths, hash before/after, reject symlink/reparse-point escapes, detect additions/deletions/metadata mutation where supported, and make changed artifact a hard failure. Reports omit absolute paths by default—use opaque input tokens or path-class; support-only detailed path receipt stays local/outside release evidence and still excludes content.
  5. Establish runnable fixture matrix: one/two/three-plus speakers, short, noise/overlap/similar voice, shared/hybrid, silence/failure, known regression/protected cases, each with enabled or explicit blocked record. Generate deterministic baseline-vs-candidate comparison with sample size/unknowns; no quality claim or threshold promotion from insufficient/disabled coverage.
- Tests and rendered checks: schema/path/reparse/allowlist validation; fixture/category/filter/disabled/expired dispatch; truth isolation static and runtime spy; clean-config/no-network/profile-store fence; deterministic report and run identity; timeout/cancel/temp cleanup; protected hash/add/delete mutation; false-auto/unknown/missing-name/count-direction arithmetic; safe console/JSON sentinel scans. Preserve existing `DiarizationCalibrationScriptTests` and `DiarizationFixtureReplayTests`; check report UX as text at normal/redirected console without names/content.
- Documentation / installer / release work: document private-fixture setup, consent/retention/owner, trust-store location, one-command filters, redacted report, coverage failure, baseline comparison, and no-runtime-hint rule. Script/test/docs-only work skips installer rebuild with recorded rationale; any worker/app/runtime behavior change takes full gate, installer rebuild, and installed smoke.
- Evidence and date: 2026-09-27 audit found tracked fixture catalog schema/example, catalog runner with safe output path and protected artifact hashes, and tests scanning private sentinels. No accessible local consented corpus, restricted truth-store governance, full matrix coverage receipt, deterministic clean-environment proof, reparse/add-delete mutation proof, or mapping-level false-auto evidence was found.
- Evidence and date: 2026-09-29 added `DiarizationFixtureEvidenceMetrics` tests that keep speaker-count correctness, false automatic names, and unknown mappings separate with no fixture name/text/path inputs.
- Remaining gap or next action: add catalog/truth schema and synthetic isolation tests, then register first approved local fixture without exposing its content.

Goal: create a repeatable local calibration loop that evaluates full-audio meetings without deploys, app clicks, or runtime hints.

Dependency: start this sprint after Sprint 1 acceptance criteria are met so fixture failures reflect diarization or calibration issues rather than unstable speaker-name review behavior.

Current baseline to preserve:

- `scripts\Analyze-Diarization.ps1` already reports speaker-label counts and contiguous speaker runs from an existing transcript JSON without mutating manifests or printing transcript text.
- `scripts\Test-DiarizationFixture.ps1` already replays stored speaker turns and voice samples through the production cluster-merge path without launching the app, worker, installer, or mutating meeting files.
- `scripts\Test-DiarizationFullAudioFixture.ps1` already runs a slower full-audio probe in a temporary `probe-output` folder, can assert expected speaker names when explicitly enabled, and verifies protected published artifacts by hash.
- Existing fixture labels are assertions only. Runtime diarization and speaker-name matching must not consume expected speaker counts, expected names, attendee counts, or fixture labels as hints.

Workstream 1 - Fixture catalog and privacy boundary:

- Add a local private fixture catalog at `.artifacts\diarization-fixtures\fixture-catalog.local.json`; do not commit this file or any real audio/transcript content.
- Add a repo-tracked schema/example catalog with synthetic placeholder paths only, so future contributors know the expected fields without exposing private meeting data.
- Each fixture entry should include: stable fixture id, friendly title, scenario category, manifest path, optional audio path, transcript JSON path, optional Markdown path, expected speaker count, optional expected speaker names, expected-name mode, protected artifact paths, notes, and enabled/disabled state.
- Store labels as test expectations only. The harness may pass expected values to assertion code, but must never pass them into production diarization, clustering, speaker-name matching, profile matching, or transcript rendering paths.
- Keep private fixture files outside published meeting folders when possible; when published artifacts are used as read-only inputs, protect them with before/after hashing.

Workstream 2 - Fixture runner and reporting:

- Add a single runner entrypoint, tentatively `scripts\Test-DiarizationFixtureCatalog.ps1`, that reads the local catalog and runs the correct existing script for each fixture.
- Use `Test-DiarizationFixture.ps1` when a fixture already has stored speaker turns and voice samples.
- Use `Test-DiarizationFullAudioFixture.ps1` when a fixture needs full-audio replay.
- Allow filters by fixture id, scenario category, and enabled state.
- Write one metadata-only JSON report per run under `.artifacts\diarization-fixtures\reports`, plus a concise console summary.
- Include elapsed time per fixture and total elapsed time so the team can compare tuning cost over time.

Workstream 3 - Required fixture mix:

- Include the known Google Cloud VMO two-speaker example.
- Include the known Khalid Khan two-speaker example.
- Add at least one one-speaker meeting.
- Add at least one three-plus-speaker meeting.
- Add at least one short call under five minutes.
- Add at least one noisy, overlapping, or similar-voice call.
- Mark any fixture disabled when its local files are unavailable, but keep its catalog entry so the gap is visible.

Workstream 4 - Metrics:

- Report diarization metrics: expected speaker count, detected speaker count, status (`pass`, `too_few`, `too_many`, `missing_data`, `failed`), raw speaker-label count, contiguous speaker-run count, segment count, voice-sample count, and elapsed time.
- Report speaker-name metrics when name matching is enabled: expected names, detected names, missing expected names, unexpected real names, suggestion count, auto-apply count, false auto-apply count, unknown-speaker count, and speaker-name status.
- Report artifact safety metrics: protected artifact paths checked, before/after hash status, temp output path, and whether any protected artifact changed.
- Do not compute mapping accuracy from transcript text in Sprint 2. If mapping accuracy needs turn-level human labels, defer that to a later labeled-turn dataset after privacy review.

Workstream 5 - Privacy and log safety:

- Console output and JSON reports must not include transcript text, raw audio snippets, embeddings, profile-store payloads, API keys, auth headers, prompts, summaries, or private profile content.
- Reports may include local file paths, fixture ids, counts, statuses, elapsed time, and safe hashes for protected artifacts.
- Treat all fixture inputs as local private data; the runner should fail clearly if asked to copy fixture audio/transcripts into source-controlled paths.

Sprint 2 acceptance criteria:

- A local fixture catalog can run the required fixture mix with one command, skipping disabled fixtures with a clear reason.
- The runner chooses stored-turn replay or full-audio replay deterministically from fixture metadata.
- Expected speaker counts and expected names are used only for assertions and report statuses.
- The report identifies pass/fail status, speaker-count error direction, speaker-name misses, false auto-applies, unknown speakers, elapsed time, and protected artifact hash status.
- Running the fixture catalog does not mutate published meeting artifacts.
- No fixture report or console output contains transcript text, embeddings, raw audio snippets, profile payloads, prompts, keys, auth headers, or private profile content.

Sprint 2 tests to add or confirm:

- Script contract tests for the catalog runner: parses the catalog, filters fixtures, skips disabled entries, dispatches to the correct replay mode, writes metadata-only reports, and preserves protected artifact hashes.
- Privacy tests: runner output and report fixtures do not include known transcript text, embedding arrays, profile payload markers, prompts, keys, or auth-header-like strings.
- Fixture validation tests: missing required paths, invalid expected speaker counts, expected names without speaker-name matching enabled, and source-controlled output paths fail with clear errors.
- Existing tests to keep green: `DiarizationCalibrationScriptTests`, `DiarizationFixtureReplayTests`, `SpeakerClusterMergeServiceTests`, and speaker-name matcher/correction tests touched by report metrics.

Sprint 2 verification:

- Focused script/test pass for fixture harness changes: `dotnet test .\tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj -p:NuGetAudit=false --filter "FullyQualifiedName~DiarizationCalibrationScriptTests|FullyQualifiedName~DiarizationFixtureReplayTests|FullyQualifiedName~SpeakerClusterMergeServiceTests|FullyQualifiedName~VoiceProfileMatcherTests|FullyQualifiedName~SpeakerNameCorrectionServiceTests"`.
- Run the catalog runner against available enabled local fixtures and save the metadata-only report under `.artifacts\diarization-fixtures\reports`.
- Full gate after implementation: `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Installer rebuild is required only if Sprint 2 changes app/runtime code. If Sprint 2 only adds scripts/tests/docs under the fixture harness, record why installer rebuild was skipped.

## Sprint 3: Calibrate Recognition And Diarization Thresholds

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: threshold guardrails, candidate schema/example, calibration runner, and promotion-status tests exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 comparison gate slice).
- User outcome: any recognition/diarization threshold change has repeatable evidence that it improves approved cases without silently naming someone wrongly or destabilizing known hard cases.
- Scope / non-goals: compare named, allowlisted candidate configurations from S2 evidence and manually promote one validated default. Do not tune against live meetings, use expected fixture values at runtime, auto-write source/config defaults, expand automatic identity confidence, or conflate model/runtime replacement with threshold calibration.
- Dependencies and decisions: S2 provides consented corpus/truth isolation and run identity; S1 preserves conservative correction UX. Existing `Test-DiarizationCalibration.ps1` runs baseline/candidates and reports `eligible_for_manual_promotion`; this is a recommendation, never source mutation. Freeze environment for comparison: source commit, build configuration, OS/runtime, CPU/DirectML mode, diarization/model hashes, candidate-schema version, fixture-catalog/truth-set version, input hashes, seed if available, and exact allowlisted parameter map. CPU/reference path is calibration authority unless an explicitly separate accelerator comparison proves equivalent output.
- Implementation slices:
  1. Establish pre-registered baseline and corpus split: discovery/dev candidates may guide threshold selection; protected holdout verifies final candidate once. Each category has minimum enabled count and protected cases; unavailable/failed/missing-data/changed-artifact result is insufficient evidence, never pass or neutral. Repeat baseline/candidate enough times to expose nondeterminism; preserve redacted run receipts and compare only identical fixture/config identities.
  2. Make candidate input typed and narrow: map each named parameter to one runtime setting/default with units, valid range, default, restart scope, and rollback value; schema rejects duplicate/unknown/conflicting values and unsafe environment inheritance. Parameters are injected only into isolated harness configuration; test proves process/runtime reads no expected labels, category, attendee, or truth fields. Candidates change one coherent hypothesis at a time, not an opaque batch.
  3. Define comparison severity and promotion gates. Hard reject: any protected artifact mutation, missing required coverage, new false auto-apply, regression in protected count/identity case, or unexplained failure. Then compare count correctness/false-auto before suggestion and cosmetic run-churn; require declared practical improvement and uncertainty/sample-size context, not merely aggregate score. Report unknown, suggestion, auto, rejection/suppression, count direction, duration, and regression reason per opaque fixture.
  4. Add decision review: a candidate can be `inconclusive`, `rejected`, or `eligible_for_manual_promotion`; eligibility names reviewer, evidence receipt, change set, rollback/default, user impact, and holdout result. Before production default edit, rerun selected candidate cleanly against untouched holdout, run focused services/fixture tests, and require source-code review of config wiring. No automated script writes production constants.
  5. Apply minimal winning default with compatibility/migration behavior, bounded diagnostics, and feature/rollback escape hatch. Confirm unknown/low-confidence stays anonymous; decision/status copy reports source/version/reason without private input. If results remain inconclusive, publish redacted outcome and retain defaults unchanged.
- Tests and rendered checks: candidate schema/range/unit/unknown/conflict validation; deterministic run identity and baseline parity; no truth leak/environment contamination; missing/disabled/failed/changed-artifact coverage rejection; severity/false-auto/protected-regression/uncertainty gates; repeat-run nondeterminism classification; manual-promotion-only source guard; parameter-to-runtime/default/rollback regression tests in cluster selection, merge, and matcher. Verify report redaction and Settings/detail explanation for a changed/default/rollback status with synthetic fixtures.
- Documentation / installer / release work: record command, pinned input/config/model identities, corpus split, candidate rationale, reviewer, no-go/rollback, result, and retention location in calibration docs; update user/admin docs only for changed defaults/visible diagnostics. Docs/scripts-only evidence skips installer rebuild; production worker/app/default changes require focused suite, `Test-All.ps1`, installer rebuild, and installed smoke before release consideration.
- Evidence and date: 2026-09-27 audit found baseline/candidate comparison, allowed candidate variables, false-auto/protected-regression rejection, and manual-promotion recommendation. Missing proof includes fixture split/coverage minimums, reproducibility/pinned environment, typed parameter-to-default mapping, nondeterminism treatment, explicit holdout review, and default rollback evidence.
- Evidence and date: 2026-09-29 added `CalibrationCandidateComparison` tests for incomplete coverage, protected regression, false-auto regression, no measured improvement, and human-review-only eligibility.
- Remaining gap or next action: freeze a redacted baseline receipt and parameter map; add a synthetic candidate with missing protected coverage to prove promotion rejection.

Goal: tune the implementation from fixture results instead of guessing.

Dependency: start this sprint after Sprint 2 produces a labeled, metadata-only fixture report with at least the required example mix.

Current baseline to preserve:

- Diarization already probes a default clustering threshold and fallback thresholds for collapsed or over-segmented speaker counts.
- Cluster selection already filters tiny unsupported speakers, prefers compact automatic speaker counts, and keeps automatic output inside the supported 2-16 speaker range.
- Speaker-cluster merge already merges highly similar clusters, small similar clusters, and tiny unsampled fragment clusters without a speaker-count hint.
- Speaker-name matching already uses suggestion, auto-apply, match-margin, profile-maturity, and minimum-speech-duration guardrails.
- Sprint 2 fixture reports are metadata-only and expected labels remain assertions, not runtime hints.

Workstream 1 - Baseline calibration report:

- Run the full Sprint 2 fixture catalog against the current defaults before changing any thresholds.
- Save the baseline report under `.artifacts\diarization-fixtures\reports` with a timestamp and a clear `baseline-current-defaults` label.
- Record fixture-level metrics for speaker count status, raw speaker count, merged speaker count, speaker runs, segment count, voice-sample count, speaker-name suggestions, auto-applies, false auto-applies, missing expected names, unknown speakers, elapsed time, and protected artifact hash status.
- Add a short metadata-only calibration summary that identifies the top failure modes: collapsed speakers, over-segmentation, fragment speakers, false auto-apply, missed suggestions, missing voice samples, or fixture data gaps.

Workstream 2 - Candidate threshold experiments:

- Add an experiment mode to the fixture runner or a companion script that can run named candidate threshold sets without editing source constants first.
- Candidate sets may vary only these knobs:
  - diarization threshold search policy, including default, collapsed-speaker retry thresholds, and over-segmented retry thresholds,
  - supported-speaker filtering, including minimum speaker duration share and duration caps,
  - speaker-cluster merge thresholds and tiny/small cluster duration limits,
  - speaker-name recognition thresholds for suggestion, auto-apply, match margin, profile maturity, and minimum speech duration.
- Each candidate run must produce the same report shape as the baseline plus the candidate parameter set.
- Do not test candidate parameters by using attendee count, expected speaker count, expected speaker names, or fixture category as runtime input.

Workstream 3 - Scoring and promotion rules:

- Score candidate threshold sets against the baseline, not against intuition.
- Promote a candidate only if it improves at least two relevant fixtures or one fixture plus one counterexample class without regressing any protected class.
- Protected classes are: the known two-speaker examples, one-speaker examples, three-plus-speaker examples, short calls, noisy/similar-voice calls, and speaker-name false auto-apply behavior.
- Treat false auto-apply as the highest-severity speaker-name failure. A candidate that introduces any false auto-apply cannot be promoted.
- Treat speaker-count correctness as more important than reducing speaker-run count. Readable paragraph coalescing should not hide wrong diarization.
- If no candidate clearly beats the baseline, keep current defaults and record the evidence gap instead of changing thresholds.

Workstream 4 - Apply calibrated defaults:

- Move only the winning candidate thresholds into production constants/config defaults.
- Keep default behavior conservative: unknown or low-confidence voices remain anonymous with an explanation.
- Keep the supported automatic speaker range unchanged at 2-16 unless the fixture report proves the range itself is the problem across multiple examples.
- Keep any changed threshold names and diagnostics understandable in logs and reports, but avoid logging transcript text, embeddings, raw audio snippets, profile payloads, prompts, keys, or auth headers.
- Update durable docs only when defaults or recommended calibration commands change.

Sprint 3 acceptance criteria:

- Baseline and candidate reports exist, are metadata-only, and can be compared by fixture id.
- Any threshold change is traceable to a named candidate run and a report showing why it beat the baseline.
- The two known two-speaker examples pass speaker-count assertions after calibration.
- One-speaker, three-plus-speaker, short-call, and noisy/similar-voice fixtures do not regress versus baseline.
- Speaker-name calibration produces zero false auto-applies in the enabled fixture set.
- Runtime diarization and speaker-name recognition still do not consume expected labels or attendee counts as hints.
- If evidence is inconclusive, Sprint 3 ends with no threshold change and a documented calibration report.

Sprint 3 tests to add or confirm:

- Candidate runner tests: named threshold sets are applied only to fixture execution and do not rewrite source constants/config defaults.
- Report comparison tests: baseline and candidate reports compare pass/fail counts, regressions, false auto-applies, and protected fixture classes deterministically.
- Threshold promotion tests: candidates with false auto-applies or protected-class regressions are rejected.
- Regression tests for any promoted threshold change in `DiarizationClusterSelectionServiceTests`, `SpeakerClusterMergeServiceTests`, `VoiceProfileMatcherTests`, and affected fixture-script tests.
- Privacy tests: baseline/candidate reports and console output remain metadata-only.

Sprint 3 verification:

- Run the fixture catalog once as `baseline-current-defaults`.
- Run each candidate set with the same enabled fixture list and save reports under `.artifacts\diarization-fixtures\reports`.
- Run the focused calibration suite: `dotnet test .\tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj -p:NuGetAudit=false --filter "FullyQualifiedName~DiarizationClusterSelectionServiceTests|FullyQualifiedName~SpeakerClusterMergeServiceTests|FullyQualifiedName~VoiceProfileMatcherTests|FullyQualifiedName~DiarizationCalibrationScriptTests|FullyQualifiedName~DiarizationFixtureReplayTests"`.
- Full gate after implementation: `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Installer rebuild is required if production runtime thresholds or app/worker code change. If Sprint 3 only produces reports and no source/default changes, record why installer rebuild was skipped.

## Sprint 4: Repair, Undo, And Release Readiness

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: undo action/service/status, refresh boundaries, suspicious-label repair queue, and focused tests exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 undo preflight slice).
- User outcome: a bad profile-driven name can be safely reversed for one meeting, while users clearly distinguish fast local name refresh from slower diarization repair and can trust released behavior.
- Scope / non-goals: complete/verify undo, scoped feedback, refresh/repair separation, safety feedback, docs, package evidence, and installed smoke. Do not delete/disable profiles through undo, change recording/audio/text/timing/meeting metadata, mutate unrelated speakers, re-run diarization during refresh, force-close active capture/worker, or publish a release without authorization.
- Dependencies and decisions: S1 defines stable speaker identity/revision and receipt semantics; S3 defaults are frozen with calibration receipt; S11/S12 of speaker roadmap define repair and derived-output freshness. Existing `UndoProfileSpeakerNameRecognitionAsync`, refresh service, WPF actions, `PublishedMeetingRepairService`, and tests give baseline. Undo operates on an immutable artifact revision and only profile-sourced attribution (auto/suggested/accepted profile decision); restores pre-profile name/provenance snapshot if validated, otherwise stable anonymous label. It records idempotent scoped suppression keyed by meeting/speaker/profile/source revision, preserving explicit user names and historic evidence.
- Implementation slices:
  1. Define name-attribution receipt/snapshot schema: stable speaker id, prior effective name/source/reason, profile/match decision, artifact revision, created/action time, and operation id. Validate snapshot against current revision before undo; present stale/no-undoable/result/warning outcome without guessing. Successful undo atomically updates JSON/Markdown/manifest and derived-attribution freshness; feedback persistence runs after artifact commit, carries warning on failure, and retry uses operation id without duplicate suppression.
  2. Fence action routes. `Refresh Suggestions` reads existing eligible voice samples and active profiles from current snapshot only; it cannot invoke queue/worker/transcription/diarization/learning or overwrite user/rejected/undone attribution. `Repair Speaker Labels` needs separate suspicious-quality eligibility, user-confirmed job/receipt, transcript-snapshot provenance, no active capture conflict, queue outcome, and cancellation/retry state. It may change diarization metadata only under repair contract; old named/summary outputs become historic/stale rather than silently reassigned.
  3. Project a single meeting-detail state model: Undo enabled only for current undoable profiles; refresh eligible/unavailable/suppressed/busy; repair discoverable only when quality evidence supports it. Include reason, impact, irreversibility, worker/no-audio/setup blocks, last result, and focus-safe busy completion. Copy explicitly contrasts name, refresh, undo, rematch, and repair; no status reports feedback saved when it failed.
  4. Harden feedback/provenance boundary: revisioned scoped rejection/undo receipts survive repeat action and do not globally punish profile. Serialize an allowlist of safe decision metadata only; keep embeddings/audio/profile documents/diagnostic text out of output, summary/provider/log/release evidence. Artifact-write failure produces no feedback mutation; feedback-store failure cannot erase committed user result.
  5. Build release evidence matrix tying calibrated final defaults, undo/refresh/repair state tests, privacy scans, docs, package commit/version/layout/integrity hashes, and installed journey to this sprint. From clean intended source run focused tests, `Test-All.ps1`, installer build, and release smoke only after app/worker idle check. A blocked active process records blocker; no arbitrary process termination. Commit/push/upload needs separate authorization.
- Tests and rendered checks: undo snapshot/current/stale/no-op/user-name preservation/idempotency/artifact-write failure/feedback failure/race; feedback key isolation and profile lifecycle interaction; Refresh spy proves zero worker/transcription/diarization/learning calls; repair eligibility/snapshot/queue/cancel/retry/artifact preservation; stale summary/derived output after undo or repair; JSON/Markdown/manifest/output/log/provider allowlist/redaction; detail keyboard/screen-reader/busy/unavailable/failure states plus synthetic installed smoke. Re-run calibration report with final default identity and preserve metadata-only receipt.
- Documentation / installer / release work: README/SETUP explain local-only profiles, suggestions/auto decision/undo/refresh/repair and retention/recovery limits; ARCHITECTURE covers receipts, queue/derived freshness, and exclusion boundary; release notes name only verified behavior. Runtime/UI changes require installer build and package smoke; docs-only planning does not build artifacts.
- Evidence and date: 2026-09-27 audit found current undo button/event/service, user-edit preservation/idempotency/feedback-failure tests, refresh no-retranscription tests, repair queue tests, and UI copy separating repair. Missing proof includes revisioned undo snapshot/recovery policy, one state model across routes, repair-versus-derived output safety, full artifact/log/provider exclusion scan, calibrated release evidence matrix, and installed end-to-end smoke.
- Evidence and date: 2026-09-29 added `SpeakerRecognitionUndoPreflight` tests for current profile attribution, stale revisions, and user-entered-name protection; existing undo service tests cover artifact update, suppression, and idempotence.
- Remaining gap or next action: add undo receipt/revision-race fixtures and action-route spies before modifying existing undo or repair implementation.

Goal: make automatic speaker naming reversible and understandable after release.

Dependency: start this sprint after Sprint 1 hardening is complete and Sprint 3 has settled the default thresholds.

Current baseline to preserve:

- `Refresh Suggestions` already refreshes speaker-name attribution from stored speaker voice samples without retranscribing audio or re-running diarization.
- `Reject` already clears suggestion metadata and records profile rejection feedback for the meeting speaker.
- `Repair Speaker Labels` already exists for suspicious published speaker-label explosions and should remain the heavier repair path that re-runs speaker labeling from an existing transcript snapshot.
- Settings already provides local learning and profile management controls, including profile disable/delete paths.
- Release notes and setup docs already describe local voice profiles at a high level, but they need final behavior details once undo and repair polish land.

Workstream 1 - Undo bad name recognition:

- Add a meeting-detail action for undoing profile-driven name recognition on a meeting, such as `Undo Name Recognition`.
- Scope undo to speaker-name attribution only. Do not change transcript text, audio files, diarization turns, timestamps, summaries, meeting metadata, projects, attendees, or recording status.
- Restore previous names from safe provenance or a pre-change snapshot when available.
- If no safe previous-name snapshot exists, clear profile-sourced names and suggestion metadata back to anonymous labels such as `Speaker 1`, while preserving explicit user-entered names.
- Record negative feedback for any undone `meetingId + speakerId + profileId` mapping so the same bad profile match is less likely to reappear for that meeting speaker.
- Keep undo separate from profile deletion. Undo must not delete, disable, or globally ban a local voice profile.
- Show a clear result message: names restored, profile suggestion suppressed, no undoable profile attribution found, or undo completed but feedback storage failed.

Workstream 2 - Repair and refresh for already processed meetings:

- Keep the two existing repair paths distinct in UI copy, service names, tests, and docs.
- `Refresh Suggestions` should re-run local profile matching against existing stored speaker voice samples only. It must not retranscribe, re-diarize, rewrite audio, or queue the processing worker.
- `Repair Speaker Labels` should remain the path for suspicious diarization output, such as over-fragmented speaker catalogs. It can queue worker speaker-label repair from the existing transcript snapshot when the meeting is eligible.
- If a meeting has no stored speaker voice samples, `Refresh Suggestions` should explain that profile matching cannot run for that meeting and should not silently do nothing.
- If a meeting has suspicious speaker labels, meeting detail should make the heavier `Repair Speaker Labels` path discoverable without implying it is a name-refresh action.
- Preserve published audio, transcript text, meeting title, meeting source, summaries, and existing user-entered names unless a repair operation explicitly needs to rewrite speaker-label metadata.

Workstream 3 - Feedback, auditability, and safety:

- Persist enough safe metadata to explain automatic name decisions after the fact: profile id, confidence, name source, suggested display name, decision reason, and whether the name was auto-applied, suggested, accepted, rejected, refreshed, or undone.
- Treat rejection and undo feedback as scoped correction data, not as global profile punishment.
- Keep profile-store failures non-blocking for transcript-artifact repair. If feedback cannot be saved, the visible undo or repair should still complete when artifact writes succeed and should surface a warning.
- Make repair operations idempotent where practical: running undo or refresh twice should not corrupt artifacts, duplicate feedback, or keep changing labels.
- Keep logs metadata-only and bounded. Do not log transcript text, raw audio snippets, embeddings, full profile payloads, prompts, keys, auth headers, or private local context.

Workstream 4 - Release documentation:

- Update release notes after implementation to explain local-only voice profiles, what is learned from corrections, where local controls live, how to disable/delete profiles, how suggestions differ from auto-applied names, and how to undo a bad name.
- Update `README.md` and `SETUP.md` only where user-facing behavior changes: Refresh Suggestions, Repair Speaker Labels, Undo Name Recognition, Settings profile controls, and privacy expectations.
- Update `ARCHITECTURE.md` if metadata shape, repair queues, provenance, or profile-feedback semantics change.
- Do not document fixture-only expected speaker counts or private test labels as runtime product behavior.

Workstream 5 - Release readiness:

- Run the calibrated Sprint 3 fixture catalog before shipping and keep the metadata-only report as release evidence.
- Run focused tests for undo, refresh, repair, profile feedback, schema privacy, and UI interaction behavior.
- Run the full repo gate before packaging: `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Rebuild installer assets after app/runtime/UI changes: `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Before packaged smoke, verify no installed `MeetingRecorder.App.exe` or processing worker is actively running. If a stale installed process blocks smoke, stop only the confirmed installed app process, not arbitrary same-named processes.
- Run packaged smoke after packaging: `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Sprint 4 acceptance criteria:

- A user can undo profile-applied speaker names for a meeting without changing transcript text, audio, diarization turns, summaries, or meeting metadata.
- Undo records scoped negative feedback for the undone profile match and does not delete or globally disable the profile.
- Refresh Suggestions never retranscribes audio, never re-runs diarization, and gives a clear unavailable state when voice samples or profiles are missing.
- Repair Speaker Labels remains available for suspicious speaker-label explosions and is clearly presented as a heavier speaker-label repair, not a name-refresh shortcut.
- Re-running undo, reject, refresh, or repair does not duplicate feedback, corrupt artifacts, or create confusing visible state.
- Published Markdown and transcript JSON contain safe provenance when needed but never contain embeddings, raw audio snippets, full profile payloads, prompts, keys, auth headers, or private profile-store content.
- Release notes and setup docs explain local-only learning, suggestions versus auto-apply, profile controls, refresh, repair, and undo in user-facing terms.

Sprint 4 tests to add or confirm:

- `SpeakerNameCorrectionServiceTests`: undo clears profile-sourced names, preserves user-entered names, records scoped feedback, remains idempotent, and succeeds when feedback persistence fails after artifact writes.
- `SpeakerNameLearningServiceTests`: undo/reject feedback suppresses the same profile for the same meeting speaker without globally banning the profile.
- `MeetingOutputCatalogServiceTests` and `TranscriptSchemaTests`: undo and refresh keep safe provenance fields while excluding embeddings and profile payloads from published artifacts.
- `PublishedMeetingRepairServiceTests`: suspicious speaker-label repair remains transcript-first, preserves protected meeting artifacts, and does not run for ordinary name refresh.
- WPF interaction or view-model tests: Undo Name Recognition visibility, disabled states, success messages, warning messages, Refresh Suggestions unavailable state, and Repair Speaker Labels discoverability.
- Fixture privacy tests: release evidence reports and console output remain metadata-only.

Sprint 4 verification:

- Run the Sprint 3 fixture catalog with the final calibrated defaults and save the metadata-only release-readiness report.
- Run focused service/schema/UI tests for speaker-name correction, profile feedback, output catalog, transcript schema, published repair, and interaction logic.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64` after confirming no active installed app or worker session.

## Interfaces And Constraints

- No public/cloud API changes. Speaker recognition remains local-only.
- Internal metadata should continue carrying profile id, confidence, suggested display name, name source, and decision reason.
- Voice embeddings stay only in the local profile store and work-manifest boundary. They must not be published to transcript JSON, Markdown, summaries, or logs.
- Runtime diarization and speaker-name recognition must not use attendee count, expected speaker count, expected names, or fixture labels as hints.
- The default rollout posture is suggest-first, with auto-apply only for mature, long-enough, high-confidence, clearly separated profile matches.

# External Audio Import Seamless Experience Plan

## Summary

Goal: let a user add audio files from another source and have Meeting Recorder
pick them up, transcribe them, optionally speaker-label them, optionally
summarize them, and publish them as if they came through the normal recording
flow.

The current implementation already has a hidden external-audio path through
`ExternalAudioImportService`: it scans the published recordings folder, copies
settled supported files into a work-session `processing` folder, creates a
queued manifest with `ImportedSourceAudio`, and relies on the normal processing
queue. That is a useful foundation, but it is not yet a seamless product
experience because it is hidden, coupled to the output folder, historically
deletes source files after copy, and does not give the user a clear import
review, setup-blocked state, codec preflight, or import-specific recovery loop.

The finished experience should be:

- The user adds audio with `Add Audio Files`, drag/drop, or a dedicated Import
  Inbox.
- Original files are never deleted by default.
- Meeting Recorder copies, probes, normalizes, queues, transcribes,
  speaker-labels when configured, summarizes when configured, and publishes the
  same `.wav`, `.md`, `.json`, and `.ready` outputs as ordinary recordings.
- Import status is visible from selection through publish and survives restart.
- Full original paths stay in local work state only and are not published in
  Markdown, transcript JSON, summaries, prompts, or ready markers.

## Sprint 0: User Journey And Compatibility Audit

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 source-to-terminal contract, source-change admission guard, and characterization tests completed; rendered synthetic import-state evidence remains).
- User outcome: people can add local recordings without guessing whether app copied, queued, retained, skipped, or will later reprocess their original file.
- Scope / non-goals: establish factual current contract and tests before changing import architecture. Do not alter queue behavior, copy/move/delete data, add a new intake store, widen media support, scan personal folders recursively, or claim all current UI paths are parity-complete.
- Dependencies and decisions: audit `ExternalAudioImportService`, `ImportedSourceAudioInfo`, manifest/queue/catalog/retry/startup paths, `MainWindow` file-picker/drag-drop/review state, configuration, and import tests. Source ownership is user-owned from discovery through terminal import state: no import path deletes, moves, truncates, renames, locks long-term, or treats source as app cleanup target. App-owned immutable staging/copy is separately classified. `SourceRetained=true` is a claim to verify against actual operation, not a default literal.
- Implementation slices:
  1. Produce a source-to-terminal-state inventory for watched folder, picker, drag/drop, and folder selection: entry control, expansion/recursion, allowed extensions, path canonicalization/reparse/UNC behavior, quiet/stability gate, preflight, dedupe key, title/metadata derivation, staging/copy, manifest write, queue/startup/retry, catalog precedence/archive/delete, and every original/staged/published artifact mutation. Link exact test/contract owner and unknowns.
  2. Define explicit state/ownership model: `Selected`, `Expanding`, `Preflighting`, `Ready`, `Duplicate`, `Unsupported`, `Unreadable`, `Changing`, `BlockedSetup`, `Queued`, `Processing`, `Published`, `Failed`, `RemovedFromInbox`, `SourceMissingAfterQueue`. Each state names user-visible copy/action, retained originals, app-owned files, durable id/revision, cancellation/retry, and whether processing may proceed. “Removed” removes only intake/reference state—not source or published meeting.
  3. Freeze compatibility fence. Legacy watched-folder detection remains opt-in/secondary, non-destructive, non-recursive unless explicitly configured, and produces same candidate/manifest provenance as explicit intake. Define collision policy across paths/methods via canonical source identity plus size/last-write/content fingerprint as available; changed source after preflight cannot silently queue old metadata. Never use filename/title alone for duplicate identity; hash cost is bounded/deferred and reporting remains local-safe.
  4. Add characterization tests before refactor: extension/quiet/locked/zero/changed/missing/reparse/UNC/long path; duplicate same/different sources; picker/drop/folder expansion; source unchanged after success/failure/cancel; staged artifact cleanup and manifest atomicity; setup blocks; restart/retry/archive/catalog precedence and published-output exclusion. Tests assert visible state/provenance rather than private fields and use temp synthetic audio only.
  5. Capture synthetic journey evidence for single memo, exported meeting, bulk folder, setup block/recovery, duplicate/source-change, restart, and removal. Audit accessibility/focus/error recovery later against this state model; no screenshots/fixtures reveal real titles, paths, audio, or meeting text.
- Tests and rendered checks: run/extend `ExternalAudioImportServiceTests`, imported queue/startup/catalog precedence tests, and WPF interaction tests; add source-hash/mutation probe and no-delete/move source assertions. Render current import review at supported viewport/125% for every audit state with keyboard path from Add/Drop to row/edit/queue/remove/setup recovery. Record observed behavior versus desired future contract, not inferred class presence.
- Documentation / installer / release work: write an internal import contract/state map and user-facing source-retention/legacy-scan boundary once verified. No installer work for audit/tests/docs only; later app/runtime changes require standard focused tests, `Test-All.ps1`, installer build, and packaged smoke.
- Evidence and date: 2026-09-27 implemented `docs/external-audio-import-contract.md`, which maps picker/drop/watch discovery through work copy, queue resume/archive, and catalog precedence; defines state/ownership and compatibility fence; records known path/inbox/atomicity gaps. `ExternalAudioImportService.QueueImportAsync` now re-stats a reviewed source before session creation and records retention from its copy-only operation. Focused 148-test contract suite passed: `ExternalAudioImportServiceTests`, `ProcessingQueueServiceTests`, `MeetingOutputCatalogServiceTests`, `SessionManifestStoreTests`, and `MainWindowXamlTests`. New source tests prove unchanged bytes/path/length/time after success, canceled request, and locked-source failure, plus rejection without a work session after source mutation.
- Remaining gap or next action: capture current explicit-import synthetic review states and keyboard/focus journey at supported viewport/125%. Native WPF capture is unavailable in this task environment; existing live app session must not be interrupted. This non-structural evidence gap does not block S1's pure import-job/source-safety contract work.

Goal: establish the exact current-state contract before replacing the hidden
import path with an explicit product workflow.

Workstream 1 - Current behavior inventory:

- Map the existing watched-folder import path, including supported extensions,
  quiet-period behavior, duplicate suppression, source deletion, transcript
  artifact checks, app-owned published-audio skipping, and manifest creation.
- Map downstream behavior for imported manifests in `ProcessingQueueService`,
  `MeetingOutputCatalogService`, retry, stale imported manifest archival, queue
  ETA, startup resume, and published-row precedence.
- Identify every existing test that encodes import behavior, especially
  `ExternalAudioImportServiceTests`, imported-source queue tests, and catalog
  precedence tests.

Workstream 2 - Product journey definition:

- Define target journeys for three first-class scenarios:
  - phone memo or voice recorder file selected with `Add Audio Files`,
  - downloaded meeting export dragged into Meetings,
  - bulk folder drop into an Import Inbox for automation or batch processing.
- Define visible states for each journey: selected, preflighting, ready to
  queue, blocked by setup, queued, processing, published, failed, removed.
- Decide the compatibility fence: legacy scanning of the recordings folder
  remains supported but becomes non-destructive and secondary to explicit
  import and the Import Inbox.

Workstream 3 - Characterization tests:

- Add or update tests that lock in the intended future behavior before the
  larger refactor begins.
- Mark old source-delete expectations as intentionally changed to source
  retention.
- Keep tests focused on behavior rather than internal implementation names so
  the import service can be reshaped safely.

Sprint 0 acceptance criteria:

- The current hidden import path and all downstream dependencies are documented
  in the roadmap or code comments where needed.
- The source-retention policy is explicit: no import path deletes original user
  files by default.
- Existing import, queue, and catalog behavior is covered by characterization
  tests before structural changes begin.

## Sprint 1: Import Domain Model And Source Safety

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: import request/candidate/preflight, `ImportedSourceAudioInfo`, staging/manifest path, and duplicate tests exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 pure versioned import-job/source-observation slice started); `Done` (2026-09-27 job/source-safety acceptance completed).
- User outcome: every imported recording has a durable, explainable identity and app-owned copy while original remains safe—even across restart, duplicate attempt, or source disappearance.
- Scope / non-goals: introduce compatible import-job/source records, transaction boundaries, identity, privacy projection, and tests. Do not build inbox UI/store orchestration (S2), change source retention default, scan network locations broadly, hash every file synchronously, or support unsupported/corrupt formats.
- Dependencies and decisions: S0 owns observed contract; S2 persists job lifecycle; S3 owns probe depth. Current manifest provides `OriginalPath`, size/time, display/method/duration/retained flag and candidate preflight, but no durable job/revision/staging identity. Use separate versioned `ImportJob` record linked to manifest/session, not overload raw capture/processing status. IDs are opaque GUIDs; source fields distinguish `OriginalLocator` (local-only), display name, canonical locator class, observed size/UTC last-write, optional content hash, method, and observation revision. Public/published projections use safe display/provenance, never full source locator or local path.
- Implementation slices:
  1. Define backward-compatible schema/version and state machine: immutable job id/source observation id; states S0 contract plus terminal reason/result/retry fields; optimistic revision, created/updated timestamps, staged work identity, metadata overrides and provenance. Legacy manifest deserialization synthesizes a read-only compatible source observation without changing behavior; next write migrates atomically. Unknown future version fails closed/read-only with recovery guidance, not default overwrite.
  2. Stage with two-phase transaction: re-stat/revalidate candidate immediately before copy; copy source to unique app-owned temp staging path, validate length/basic format/optional bounded hash, atomically promote to job-owned path, then atomically persist job/manifest/queue reference. Cancellation/error cleans only verified app-owned temp/job files; leaves original untouched. If original changes/disappears/locks during copy, transition to `Changing`/`SourceMissing` with retry—never queue partial/old metadata. Processing reads staged immutable copy, so later original deletion/rename cannot alter queued work.
  3. Define identity levels. `SourceObservationKey` is canonicalized locator + size + UTC write (plus file identity where platform can provide it); `ContentKey` optional SHA-256 computed only after eligible staging/bounded background policy. Exact completed/active key means duplicate; same locator with new observation becomes new revision requiring explicit review; same content from different source is `PotentialDuplicate` until user choice or policy. Never treat friendly title, date, project, or format as identity; prevent duplicate races via per-key durable lease in S2.
  4. Separate path/security/retention policy: canonicalize and validate before every filesystem operation; reject source equal to app work/publish/staging, reparse escape, invalid/unsupported remote/offline placeholder, and path disclosure outside local job state. Source-retention invariant is enforced mechanically with a no-source-write/delete/move API boundary/test. Job removal/retry/archive deletes only allowlisted app-owned staged files after reference/lease check; published artifact and source have independent lifecycle.
  5. Specify migration/observability: each state transition records opaque ids, old/new state, reason code, timing and byte/count metrics—not title/path/audio/content. User display reports source retention, copy/probe status, duplicate basis and recovery. Export/summary/log/provider schema assertions reject locators, audio blobs, hashes where privacy policy excludes them, and arbitrary exception paths.
- Tests and rendered checks: legacy/current/future schema migration; state-transition legality/revision CAS; copy commit/cancel/failure/partial cleanup/source mutation/disappearance; no-source-write sentinel; processed-from-staging after source loss; locator/reparse/path-class protections; exact/potential/new-revision duplicates and concurrent import lease; job deletion allowlist; serialization/projection/log redaction. Render synthetic review states—ready/changing/duplicate/potential duplicate/source missing/failed—at 125%, keyboard recovery and visible retention copy.
- Documentation / installer / release work: document original-retention, local staging, source-missing consequence, duplicate/potential-duplicate choices, and local-path privacy boundary. Schema/runtime change needs focused tests, Test-All, installer rebuild and package smoke once implemented; plan-only edit requires none.
- Evidence and date: 2026-09-27 added `ExternalAudioImportJob`, immutable source observations, opaque observation keys, schema compatibility/recovery projection, compare-and-swap legal transitions including retryable `Changing`/`SourceMissing` states, legacy manifest read projection, safe public projection, and source-operation policy in `MeetingRecorder.Core.Domain`. `ExternalAudioImportJobStore` atomically writes a local companion record, projects an existing manifest read-only without writing one, refuses future/read-only schema overwrites, and safely migrates a missing companion only when the imported manifest's staged audio exists inside that session's app-owned `processing` directory. `ExternalAudioImportJobRecoveryService` invokes that narrow recovery before the normal startup queue scan. `QueueImportAsync` rejects UNC, linked/redirected, overlong, and work-root sources; blocks repeat unchanged sources before creating another session; copies through unique app-owned `.import-staging`; revalidates source and staged length; atomically promotes to session processing; then persists manifest plus queued job. Decode-failure copy is now safe. Focused import/job/manifest/queue/catalog/WPF suite passed 209/209 with workspace-local build intermediates; integration suite passed 8/8. Full core suite: 1,326/1,333 passed; six installer tests are blocked here because ordinary `.exe` `File.Move` operations under `%TEMP%` report success without moving the file (isolated probe reproduced), and a separate diarization-catalog dry-run failed. `Build-Installer.ps1`, using the new optional workspace-local intermediate-build override, produced fresh `MeetingRecorder-v0.3-win-x64.zip` and `MeetingRecorderInstaller.msi` at 18:37 local. Packaged smoke is deferred: user-owned `C:\Users\psharm04\MeetingRecorder\MeetingRecorder.App.exe` remains active (PID 23488). Future admission-receipt coverage, content-identity conflict handling, durable leases/deletion, and publish-boundary projection are expressly handed off to S2/S6.
- Remaining gap or next action: S2 must add configured Inbox ownership, health checks, a durable lease, and opt-in archive/delete semantics without widening external-source authority.

Goal: create a durable import model that can support explicit UI imports,
watched inbox imports, restart, recovery, and privacy boundaries.

Workstream 1 - Import data model:

- Add explicit import job/status types for `PendingReview`, `Probing`,
  `ReadyToQueue`, `BlockedBySetup`, `Queued`, `Processing`, `Published`,
  `Failed`, and `Removed`.
- Extend `ImportedSourceAudioInfo`, or add a companion manifest record, with:
  original path, friendly source display name, import method, imported-at time,
  source size, source last-write time, copied work path, probed duration,
  decode status, duplicate key, source-retention policy, and optional user
  title/date/source/project overrides.
- Keep backward compatibility for existing manifests that only contain the
  current `OriginalPath`, `SourceSizeBytes`, and `SourceLastWriteUtc` fields.

Workstream 2 - Non-destructive storage:

- Copy external files into a work-session import staging or `processing` path
  before queueing.
- Preserve the original file for explicit file-picker and drag/drop imports.
- Preserve the original file for legacy watched-folder imports unless a future
  explicitly named inbox archive mode is enabled.
- Store source paths only in local manifest/work state and metadata-only logs.

Workstream 3 - Duplicate identity:

- Build duplicate keys from normalized path, source size, last-write time, and
  copied work identity.
- Leave room for later content hashing without requiring an expensive hash for
  the first import pass.
- Prevent repeated import of unchanged failed files from creating repeated
  backlog rows.

Sprint 1 acceptance criteria:

- Import metadata round-trips through manifest serialization.
- Existing minimal imported-source manifests still load.
- Original files remain untouched in all default import paths.
- Duplicate unchanged files do not create repeated import jobs.

## Sprint 2: Dedicated Intake Storage And Import Inbox

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: explicit review intake and background watched-folder cycle exist, but no configured durable inbox/job store); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 pure inbox path/ownership/health boundary slice started); `Partial` (2026-09-27 runtime, lifecycle, and settings slices complete; rendered/package evidence blocked).
- User outcome: users have one clear place for batch audio without mixing raw source files with published recordings, and can see when storage/setup prevents safe intake.
- Scope / non-goals: add configured Import Inbox, durable intake records/leases, storage-health gate, and controlled archive/error lifecycle. Do not repurpose recordings/transcripts as inbox, silently move arbitrary picker/drop files, recursively ingest unknown trees, promise quota reservation, or delete any user source by default.
- Dependencies and decisions: S0 source/state inventory and S1 job/source identity/staging transaction are authority. Default inbox is a new sibling under managed Documents Meetings root, never inside recordings/transcripts/work/published sidecar paths; legacy watched location stays separate/secondary. An inbox item becomes app-manageable only after validated canonical path under configured inbox, non-reparse ancestry, and durable `InboxManaged` receipt created before any archive move. Picker/drag-drop remains external and source-retained.
- Implementation slices:
  1. Add config/migration model: `ImportInboxDir`, enable/background-scan policy, explicit archive/error retention policy, bounded scan interval/batch size, and path version. Resolve defaults via path service; reject empty/overlapping/nested in output/work/archive/error roots, unsafe reparse/UNC/offline/readonly paths, and changed config while active jobs own path. Migrate missing config as disabled-safe/default path without moving existing files or changing legacy scan semantics.
  2. Implement durable inbox journal backed by S1 job records: discovery observation, canonical relative path/token, source snapshot, ownership receipt, job state/revision, lease/heartbeat, retry/backoff, archive intent/result, retention expiry, and user action audit. File watcher is hint only; periodic bounded reconciliation owns truth. Multiple app instances/restarts use durable lease and idempotency; locked/changing/missing files remain explicit state, never duplicate queue rows.
  3. Gate acceptance on health: validate inbox/staging/output directory access with create/delete probe confined to app-owned temp name; estimate staging plus conversion/publish headroom from source bytes/duration/format using conservative unknown-size warning. Report `BlockedStorage` with safe path class/free-space/required-space/rule and recovery; no partial job/staging files on failed health/preflight. Recheck immediately before staging/queue because storage/config can change.
  4. Make archive/error opt-in and recoverable. Default successful inbox job retains original; optional policy moves only receipt-verified inbox-owned source after staging/manifest commit and lease validation. Same-volume rename is preferred; cross-volume action copies/verifies then removes original only under explicit policy/confirmed safe ownership. Archive failure leaves source/inbox job in `ArchivePending`, never treats it as data loss; retry/restore never touches external picker/drop source. Cleanup honors retention and locks, with dry-run/audit before deletion.
  5. Define UI/control boundaries: Add/Drop tells user selected file stays put; Import Inbox explains scan/policy/ownership, count/state, pause/rescan/open location, storage block, archive/error/retry/remove semantics. Settings owns path/policy; Meetings owns review/queue. Use no real local paths in telemetry/logs/support bundles by default; path reveal requires local user action.
- Tests and rendered checks: default/config migration/overlap/reparse/path-class; inbox watcher+reconciliation and restart/multi-instance lease; managed versus external ownership; health access/disk estimate/recheck failure; source unchanged by default; archive pending/copy/verify/rollback/restore/retention; watcher flood/lock/change/missing/duplicate; no arbitrary source/outbox action. Render empty/ready/blocked/paused/archive-pending/error/recovery at 1280x800/125%, keyboard and assistive status/focus for settings and inbox review using synthetic paths.
- Documentation / installer / release work: document distinct inbox/recordings/transcripts/work roles, defaults, retention/optional archive, disk headroom, pause/rescan/recovery, and source-retention guarantee. App/config/runtime changes later require test gate, installer rebuild and packaged smoke; planning-only record requires none.
- Evidence and date: 2026-09-27 added disabled-by-default `ImportInboxDir`, enablement, interval, bounded batch, Archive, and terminal Error policies. Missing config migrates to `Documents\\Meetings\\Import Inbox` without creating or moving a folder; invalid overlap resets to disabled-safe defaults. `ImportInboxPathPolicy` keeps the Inbox sibling to recordings/transcripts, rejects remote/reparse/overlap locations, reports path classes without paths, probes only app-owned temp files, and rechecks conservative source-copy/normalization storage headroom for work/recordings/transcripts before queueing. `ImportInboxJournalStore` persists relative source paths, opaque observations, queue/archive/error receipts, and a time-bounded cross-process lease. `ImportInboxReconciliationService` records supported top-level discovery only when explicitly enabled. `ImportInboxIntakeService` runs under the existing non-overlapping app import gate on startup/background cadence, commits the normal staged job under a lease, and then optionally moves only receipt-backed Inbox source into managed Archive after queue commit or terminally unreadable source into managed Error; failed moves leave the source plus `ArchivePending`/`ErrorPending` retry receipt. Explicit picker/drop sources remain source-retained. Settings > Files & Updates owns opt-in/path/policies and exposes pause, status, rescan, and open actions; config changes cannot strand active receipts. Contract, README, setup, and architecture docs distinguish Inbox from published folders. Focused Inbox/config/import/UI suite passed 250/250; app build passed. Full core suite passed 1,357/1,364: six pre-existing installer/EXE move failures under this host's `%TEMP%` policy and one diarization fixture dry-run failed; integration passed 8/8. `Build-Installer.ps1` completed after the final Error-lifecycle changes, producing `MeetingRecorder-v0.3-win-x64.zip` (88,375,173 bytes, 19:17 local) and `MeetingRecorderInstaller.msi` (76,066,816 bytes, 19:16 local).
- Remaining gap or next action: capture synthetic 1280x800/125% empty/ready/blocked/paused/archive-pending/error/recovery settings and Inbox journeys plus packaged smoke. These are blocked now because the user-owned installed `MeetingRecorder.App.exe` remains active; do not interrupt it.

Goal: separate raw user-provided files from published recordings so import does
not require dropping files into the output folder.

Workstream 1 - Default paths:

- Add a default Import Inbox such as `Documents\Meetings\Import Inbox`.
- Keep the existing recordings folder as a legacy watched location only for
  compatibility.
- Add config/default handling for the Import Inbox path without disrupting
  existing `AudioOutputDir`, `TranscriptOutputDir`, or `WorkDir` behavior.

Workstream 2 - Inbox-managed file lifecycle:

- For explicit file-picker and drag/drop imports, copy from the selected path
  and leave the source exactly where it is.
- For Import Inbox automation, support optional archive/error folders under the
  inbox so batch drops can be managed predictably.
- Only inbox-managed files may be moved to an inbox archive or error folder,
  and that behavior must be visible and documented.

Workstream 3 - Storage health:

- Check that the import staging/work root is writable before accepting a file.
- Check available disk space for the work copy and speech-optimized WAV output
  before queueing when practical.
- Surface clear messages when the work folder, inbox, or output folders are
  missing or unwritable.

Sprint 2 acceptance criteria:

- Users are no longer guided to put raw files into the published recordings
  folder.
- Import Inbox scanning works without deleting files from arbitrary locations.
- Disk and folder-access failures are reported before processing starts.

## Sprint 3: Media Probe And Preflight

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: extension/file/placeholder/still-copying/duration preflight and candidate states exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 shared probe-contract slice started); `Partial` (2026-09-27 shared source/staged preparation, durable Inbox retry evidence, and package build completed; rendered/installed evidence remains externally blocked).
- User outcome: a file is either safely ready to process or has one truthful, recoverable reason before it enters backlog.
- Scope / non-goals: build shared bounded media preflight and durable result/retry contract. Do not transcribe, diarize, publish, repair source media, silently transcode at selection, treat filename extension as codec proof, or turn external files into archive/quarantine targets.
- Dependencies and decisions: S1 staging/source observation and S2 inbox state/health own identity and persistence. Probe authority is same approved decoder/audio-preparation capability that will process staged content, but probe is read-only and resource bounded. Result binds job id + source/staged observation revision + decoder/runtime version; cached result is invalid on any change. Container acceptance means decodable supported audio stream, not guaranteed quality or video preservation.
- Implementation slices:
  1. Define probe interface/result: file/stream safety, supported stream/channel/sample rate/duration/bytes, decoder capability/version, normalization feasibility, estimated work/disk, and stable code. Separate `Ready`, `WaitingForSettle`, `BlockedStorage`, `PotentialDuplicate`, and terminal `UnsupportedExtension`, `UnsupportedCodec`, `NoAudio`, `Empty`, `TooShort`, `DecodeFailed`, `CopyFailed`, `UnsafePath`; UI maps code to action without raw local path/content.
  2. Defend TOCTOU/resource use: canonicalize/open with restricted sharing, snapshot size/time/file identity before and after bounded header/decode probe, timeout/cancel, max metadata/read/duration limits, and reject changed/locked source as retryable. Revalidate staged copy after atomic promotion; process only matching probe/copy observation. Run probing off UI thread with per-volume/job concurrency caps and no external codec download/network.
  3. Reuse preparation capability through explicit adapter/contract tests: accepted WAV/MP3/M4A/AAC/MP4 fixtures must enter same supported normalizer; probe cannot claim ready if preparation rejects. Unsupported/corrupt/malicious/truncated/multi-stream/silence/short/huge metadata cases yield stable safe codes; no raw bytes/audio/text in status/logs.
  4. Persist retry/quarantine decision by observation revision: terminal failures suppress startup rescans until source changes/user retries; settle/lock/storage failures back off with visible next check and cap; inbox-managed file can be marked error without moving it by default, optional S2 archive policy only after explicit condition. Cancel/removal clears app state, never source. All state changes are lease/revision checked.
  5. Project duration/ETA as estimate with missing/unknown truth; preflight success never promises transcription success. Include accessibility/keyboard reason, Retry/Open Settings/Remove appropriate to ownership and recovery.
- Tests and rendered checks: adapter parity against preparation; valid-format/missing/locked/changing/offline/unsafe/empty/short/corrupt/unsupported/cancel/timeout/resource cap; post-copy mutation; cache invalidation/version; failure retry/backoff/suppression/revision/lease; no source mutation/path/audio redaction. Render synthetic ready/waiting/terminal/storage/unknown duration states at 125% with accessible status and keyboard recovery.
- Documentation / installer / release work: document supported-as-decoded (not extension guarantee), local-only probe, settle/retry and source-change behavior, storage recovery, and no-source-change policy. Runtime decoder/preparation changes require full test/install/smoke gates; plan-only work none.
- Evidence and date: 2026-09-27 added `ExternalAudioMediaProbe` with a metadata-only public result contract and same `TranscriptionAudioPreparer` adapter used by queued transcription work. It accepts only public containers when that adapter succeeds, snapshots length/UTC write before and after decoding, uses restricted sharing, two per-volume probes, two-minute cancellation, a 2 GiB cap, randomized app-owned temporary WAVs, and safe ready/waiting/changing/unsupported-codec/no-audio/empty/short/decode/resource codes. `ExternalAudioImportService` now uses it as preflight authority and repeats it after atomic staged-copy promotion; a failed staged preparation cleans app-owned work and no queue record survives. Queued `import-job.json` now holds an opaque source/staged observation, source revision, decoder version, normalized duration/rate/channels, and ready receipt—never locators, decoder errors, or audio. Inbox journal entries persist terminal suppression for an unchanged observation and 15-second-to-five-minute retry backoff for retryable results; neither path moves a source by default. Added probe/container/mutation/cancellation, staged-failure, receipt-redaction, and Inbox retry/suppression tests. Focused coverage passed 65/65; integration passed 8/8. Full Core passed 1,373/1,380, with six known host `%TEMP%` EXE-move installer failures and separate diarization fixture dry-run failure. Installer rebuild passed: `MeetingRecorder-v0.3-win-x64.zip` (90,873,998 bytes, 19:52 local) and `MeetingRecorderInstaller.msi` (76,083,200 bytes, 19:52 local).
- Remaining gap or next action: capture synthetic 125% ready/waiting/terminal/storage/unknown-duration review states and run packaged smoke. Both require the user-owned installed `MeetingRecorder.App.exe` (PID 23488) to close; do not interrupt it. Explicit review-row retry/accessibility work remains Sprint 4.

Goal: catch file and codec problems before they become mysterious processing
failures or stuck backlog items.

Workstream 1 - Real decode probe:

- Add a media probe service that uses the same decode/preparation stack as
  transcription and publish normalization.
- Validate supported extension, file existence, file lock/settled state,
  readable duration, non-empty audio, supported channel conversion, and codec
  support.
- Use the probed duration later for queue ETA when `EndedAtUtc` is absent.

Workstream 2 - Failure categories:

- Categorize import preflight failures as duplicate, locked or still copying,
  unsupported extension, unsupported codec, empty or too short, copy failed,
  decode failed, insufficient disk, or unavailable storage.
- Keep failure messages user-actionable and metadata-only.
- Do not log transcript text, raw audio snippets, full original paths in
  published artifacts, voice embeddings, prompts, API keys, or auth headers.

Workstream 3 - Quarantine and retry:

- Keep bad files in an import failure state rather than queueing them for the
  worker.
- Do not retry unchanged bad files on every startup or timer scan.
- Allow a locked or still-copying file to wait and retry once it settles.

Sprint 3 acceptance criteria:

- Bad codecs and unreadable files fail before queueing with clear messages.
- Valid `.wav`, `.mp3`, `.m4a`, `.aac`, and `.mp4` files pass preflight when
  the local decoder can read them.
- Locked files do not become permanent failures while they are still being
  copied.

## Sprint 4: First-Class Add-Files UX

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: Add Audio Files, drag/drop, review grid, metadata editor, remove/queue/setup controls already exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 review projection/action-contract implementation started); `Partial` (2026-09-27 implementation, documentation, focused/full/integration checks, and installer rebuild complete; rendered/installed checks blocked by active user app).
- User outcome: users can select many recordings, understand exact readiness and retention, correct metadata, and queue safe rows without losing source files or control over exceptions.
- Scope / non-goals: make S1–S3 intake visible and accessible in Meetings. Do not redesign library wholesale, add file-browser recursion, enqueue invalid/stale rows, expose raw local paths/debug state, turn Remove into source deletion, or use activity log as primary result.
- Dependencies and decisions: S1 job/revision and S2 inbox ownership/health/S3 probe results are display authority; S5 owns setup routing. Existing picker/drop/review UI is baseline, not acceptance. Use one `ImportReviewRow` projection keyed by job/candidate revision, with display-safe name/method/metadata/probe/state/action eligibility. User edits are drafts with field validation/provenance; selection and queue operate on immutable snapshot/revision so refresh/background probe cannot retarget a click.
- Implementation slices:
  1. Consolidate entry and drop contract: Meetings primary Add Audio Files and bounded drop zone accept files/folders according to explicit expand policy, reject non-file/reparse/unsafe paths before UI, dedupe current selection without hiding persisted duplicate state, and announce accepted/rejected count. Folder/open-inbox links are secondary navigation only; picker remembers safe display location but no paths enter telemetry.
  2. Build dense Technical Studio review well: stable header/count/status, compact sortable state/provenance/duration/duplicate columns, row expansion/editor for title/date/source/project with local timezone/format validation, and visible per-row preflight/retention/recovery result. Apply opaque tonal nesting, 1px outlines/inset wells, max 4px radius, no shadows/gradients, Segoe interaction text and monospace only technical time/counts. Essential status/actions stay in viewport/keyboard flow.
  3. Define action semantics: Queue Valid snapshots eligible row ids/revisions and reports queued/skipped/blocked/failed totals plus per-row recovery; Skip Duplicate keeps no source mutation; Remove drops draft or S2 intake reference only after clarification; Retry re-probes current revision; Open Inbox/Setup navigates without applying unknown settings. Partial completion keeps failed rows and safely focuses result; cancel/busy/close cannot double queue.
  4. Add accessible focus/status model: entry/drop → review header/count → grid row/details/editors → bulk/per-row actions/result → return; Tab/Shift+Tab/arrows/Enter/Space/Escape, validation/error initial focus, async completion/live-region one-shot behavior. `AutomationProperties` names include source method, state/reason and impact; color/icon never sole state; long/localized-like names/dates wrap/truncate with accessible full value.
  5. Enforce privacy/results boundary: display friendly source name/path class only; no absolute raw path, error stack, codec internals, transcript/audio/profile data. Row/status logs contain job counts/opaque id; source stays unchanged through all UI action tests.
- Tests and rendered checks: projection/action eligibility/revision race/draft validation/timezone; multi-file duplicate/invalid/setup/storage/change/cancel/partial queue; remove/retry/close idempotency/no-source-mutation; keyboard/focus/automation names/live status; screenshots at 1280x800 and 1440x900, 100/125%, high contrast, long strings, resize/scroll. Compare actual well to `DESIGN.md`, not a card/wizard substitute.
- Documentation / installer / release work: document Add/Drop, review metadata, Queue Valid/Remove meaning, retained originals, Inbox and setup recovery. UI/runtime change requires focused/UI tests, full gate, installer rebuild and installed smoke; planning only none.
- Evidence and date: 2026-09-27 added pure `ExternalAudioImportReviewProjection` rows keyed by opaque source observation revision. The projection contains only safe filename/method/duration/status/retention/recovery text, converts setup/draft/queue failures to action-safe state, preserves exact duplicate/retry distinctions, and rejects raw local path from display. The Meetings review well now exposes compact `Source retention` and `Next step` columns, per-row automation names, live summary/detail status, labelled editors, `Retry Selected`, `Skip Duplicate`, and `Open Inbox` actions. Retry re-preflights the current immutable row then replaces only that row's candidate; Queue Valid snapshots eligible rows while controls are disabled and focuses remaining failures after partial completion. Remove/skip are explicit review-only actions and state that neither source nor existing import changed. Inbox route focuses its Files & Updates control through settings information architecture. Projection/UI/probe/import/Inbox focused suite passed 95/95; integration passed 8/8. Full Core passed 1,376/1,383; six known host `%TEMP%` EXE-move installer tests and separate diarization fixture dry-run failed. Installer rebuild passed: `MeetingRecorder-v0.3-win-x64.zip` (90,880,017 bytes, 20:09 local) and `MeetingRecorderInstaller.msi` (76,083,200 bytes, 20:09 local).
- Remaining gap or next action: capture actual no-file/ready/duplicate/setup-blocked/partial-result states at 1280x800 and 1440x900, 100/125%, high contrast, long strings, resize/scroll, keyboard and screen-reader; then run installed smoke. Current user-owned `MeetingRecorder.App.exe` (PID 23488) prevents safe source/packaged render/smoke; do not interrupt it.

Goal: make import obvious, low-friction, and consistent with the desktop design
system.

Workstream 1 - Entry points:

- Add `Add Audio Files` to the Meetings workspace.
- Add drag/drop support on the Meetings surface.
- Keep the current `Open audio folder` and `Open transcript folder` links, but
  do not rely on them as the primary import path.

Workstream 2 - Import review surface:

- Add an import review tray or dialog that lists selected files with filename,
  inferred title, inferred date/time, probed duration, source label, project,
  duplicate warning, setup-blocked status, and preflight result.
- Support editing title, date/time, source label, and project before queueing.
- Support bulk decisions: queue valid files, skip duplicates, remove invalid
  files, and open the Import Inbox.

Workstream 3 - Design constraints:

- Follow `DESIGN.md`: dense technical-studio layout, opaque surfaces, 1px
  technical lines, 4px radii, no drop shadows, and compact data wells.
- Keep status visible in the current viewport. Do not rely on activity-log text
  as the only indication that import worked.
- Ensure keyboard flow, focus order, and status text are accessible.

Sprint 4 acceptance criteria:

- A user can drag in multiple files, edit one title/date, skip one duplicate,
  and queue the rest.
- The current viewport clearly shows which files copied, queued, blocked, or
  failed.
- The UI does not expose raw internal paths or debug chrome.

## Sprint 5: Setup-Aware Queueing

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: queue path checks setup and review exposes Setup route); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27 opaque readiness, staged block/resume, UI action, tests, documentation, and installer rebuild complete; rendered/installed checks blocked by active user app).
- User outcome: missing local prerequisites pause an import with one recovery path; no reselect/re-copy or misleading failure, and optional speaker labeling never withholds transcript.
- Scope / non-goals: persist readiness block/resume behavior around import jobs. Do not auto-download models, bypass user-configured processing policy, mark optional diarization mandatory, queue work from stale sources, or automatically resume cancelled/removed jobs.
- Dependencies and decisions: S1/S2 durable job/lease/staging and S3 probe are prerequisite. Use a versioned readiness snapshot from same authority as queue/worker: required transcription model/runtime/config/storage status, checked time, reason code, and remediation route; no raw paths/keys/provider data. `BlockedBySetup` is a nonterminal user-intent state with no worker lease. Readiness transitions revalidate job/probe/staging/config revision before queue.
- Implementation slices:
  1. Define deterministic gate order: job/source/probe/storage valid; transcription readiness; queue capacity/policy; then optional diarization readiness. Required failure writes `BlockedBySetup` with stable code, current prerequisite, retry time/user action and preserved draft/staged copy. Transient check failure is `ReadinessUnknown`/retryable, not model-missing; no work is queued on unknown truth.
  2. Persist user queue intent/consent separately from state. Setup completion notification triggers bounded re-evaluation, not blind processing: configured policy chooses explicit `Resume queued imports` action versus clearly disclosed automatic resume; never resumes cancelled/removed/edited-stale jobs. CAS/lease makes startup, Settings save, timer, and click converge on one enqueue; queue receipt atomically links job/session/priority.
  3. Preserve metadata/work safely while blocked: stage only validated S1 copy, retain override drafts/revision and show source no longer needed after stage. On config/model/storage change, recheck health/probe/policy; corrupt/missing staged content returns actionable nonterminal state. Retention cleanup excludes blocked jobs with active user intent until visible policy/expiry.
  4. Enforce stage independence: transcription readiness gates publish path; diarization `Inline`/`Deferred`/`Throttled` routes after transcript according to existing mode. Missing/invalid optional assets show label status/setup route, never block audio/transcript publish. If inline labeling fails, record stage failure/retry/add-label action without redoing successful transcription.
  5. Project accessible recovery: exact required item, what stays safe, Setup/Retry/Resume/remove choices, background check/result once, and queue ordering/ETA only when known. Settings return focuses originating import row and re-evaluates revision; no status claims model ready before worker-compatible validation.
- Tests and rendered checks: gate order/snapshot codes/unknown; model absent/invalid/repaired/config change; blocked restart/staged preservation/draft preservation/expiry; explicit vs configured automatic resume/cancel/remove/no duplicate enqueue/concurrent transition; storage/probe stale at resume; optional diarization absent/fail/deferred/inline with transcript publish. Render blocked/unknown/ready/resuming/optional-label-error at 125%, keyboard Setup-return/focus/status and no raw diagnostic leakage.
- Documentation / installer / release work: document prerequisite versus optional speaker label, exact blocked/resume behavior, setup recovery, staged-source retention and policy. App/runtime changes require focused/full tests, installer build, packaged smoke; plan-only no build.
- Evidence and date: 2026-09-27 added `ExternalAudioImportReadinessSnapshot` with opaque configuration revision, stable missing/invalid/unknown model states, retry truth, and safe recovery copy. Queue Valid now stages an already preflighted source under normal source/storage/staged-probe admission even when transcription setup is unavailable, then persists user queue intent, probe receipt, and `BlockedBySetup` job state; no processing enqueue is issued. `SessionManifestStore` treats a durable blocked companion or unreadable job as no worker-admission state, while legacy imported manifests preserve their existing resume behavior. `ExternalAudioImportReadinessCoordinator` only resumes user-requested blocked jobs after a currently valid model snapshot and exact staged-observation revalidation; changed/missing staged work remains nonterminal blocked with no source mutation. Meetings exposes `Resume Blocked Imports`; it is explicit, disabled until readiness is verified, and reports safe no-work/staged-repair outcomes. Existing optional diarization policy remains downstream of transcription admission, so no optional asset becomes a transcript gate. Added resolver, block persistence, changed-stage, explicit resume, and startup-admission tests. Focused suite passed 110/110; integration passed 8/8. Full Core passed 1,381/1,388, with six known host `%TEMP%` EXE-move installer failures and separate diarization fixture dry-run failure. Installer rebuild passed: `MeetingRecorder-v0.3-win-x64.zip` (90,888,103 bytes, 20:27 local) and `MeetingRecorderInstaller.msi` (76,083,200 bytes, 20:27 local).
- Final rebuild: 2026-09-27 after the manual queue/retry/resume shared-gate fence, `MeetingRecorder-v0.3-win-x64.zip` (90,888,415 bytes, 20:33 local) and `MeetingRecorderInstaller.msi` (76,091,392 bytes, 20:33 local) passed.
- Remaining gap or next action: capture blocked/unknown/ready/resuming/changed-staged states at 125%, keyboard Setup-return and live status; then run installed smoke. Current user-owned `MeetingRecorder.App.exe` (PID 23488) prevents safe source/packaged render/smoke; do not interrupt it. Full queue-capacity and optional-label stage matrices remain Sprint 6 parity work.

Goal: make imports recoverable when local model setup is not ready.

Workstream 1 - Transcription readiness:

- Check transcription model readiness before queueing imported audio into active
  processing.
- If the Whisper model is missing, invalid, or blocked by setup, keep the import
  as `BlockedBySetup` instead of marking it as failed.
- Add a direct Setup action from the import review/status surface.

Workstream 2 - Resume after setup:

- Once transcription setup becomes ready, allow blocked imports to queue without
  asking the user to reselect source files.
- Preserve copied work files and metadata across restart while blocked.
- Keep user edits to title/date/source/project while blocked.

Workstream 3 - Speaker labeling posture:

- Keep speaker labeling governed by existing `Deferred`, `Throttled`, or
  `Inline` mode.
- Missing optional diarization assets must not block transcript generation.
- If speaker labeling is deferred, imported meetings should still publish audio
  and transcripts first and remain eligible for `Add Speaker Labels` later.

Sprint 5 acceptance criteria:

- Importing before model setup feels paused and recoverable, not broken.
- Fixing transcription setup lets previously blocked imports proceed.
- Optional speaker labeling setup never prevents transcript publication.

## Sprint 6: Normal Processing Parity

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: imports reach existing queue/session processing and manifest source metadata); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: staged-only adapter and normal processor checkpoint slice implemented).
- User outcome: an imported recording receives normal, resilient processing and recognizable output without pretending it was captured live.
- Scope / non-goals: prove/complete imported-session adapter, stage/failure parity, output/queue truth. Do not alter live recording behavior, erase import provenance, guarantee all source codecs, reprocess completed artifacts on restart, or make summary/diarization required for transcript publish.
- Dependencies and decisions: S1 immutable staged source/S3 probe/S5 readiness states precede enqueue. One `SessionProcessingInput` adapter translates a queued import to processor contract with source mode/probe/staged identity/metadata override/duration; processor never reaches original source. Parity means same stage semantics/output quality/atomicity where applicable, not identical captured-chunk evidence or timestamps. Import provenance remains local manifest/catalog-safe display and is not silently written as live capture metadata.
- Implementation slices:
  1. Define import adapter and validation: use only verified staged normalized/speech-ready input; map title/date/project/source override to stable output stem through collision-resistant allocator; preserve original/probe/job ids in local processing provenance. Revalidate job/lease/readiness/input hash immediately before processor begins; stale/missing stage returns recoverable state, not a fake session.
  2. Share stage state machine with capture: prepare/normalize, transcription, optional diarization, optional summary, publish, ready marker, cleanup with explicit input/output revision. Each stage has idempotent receipt/atomic temp-to-final promotion and resume decision; retry never emits duplicate meeting or overwrites unrelated stem. `.ready` appears only after contract-required artifacts/hashes/manifest match current run.
  3. Specify failure preservation: transcription failure may retain allowed published audio and state but no transcript JSON/Markdown/ready; diarization unavailable/failure leaves transcript current with stage result/retry route; summary failure leaves transcript/ready valid with supplemental status. Publish/storage failure never declares current output; cleanup preserves diagnostics/staged data only under retention policy and no external source mutation.
  4. Make queue projection source-aware/truthful: queued/processing/finalizing/retry/blocked/failure/completed imported jobs appear once in count/order/ASAP/live-capture pause/interruption policy. ETA uses probe/decoded duration and measured stage history when valid; otherwise says estimate unavailable, not zero/unknown omission. Snapshot includes safe import mode/state/reason, no local locator/media content.
  5. Verify catalog/output parity: imported published row supports read/detail/retry/archive/delete rules consistent with source policy; duplicate/stem collision imports remain distinct by manifest/session id. Restart checks all stage boundaries and orphan-temp recovery/lease ownership before a second worker begins.
- Tests and rendered checks: adapter source/staged-only/revision checks; format normalization/output artifact/ready ordering; same-stem/concurrent publish/idempotent retry; stage failure matrix; restart at each boundary/lease interruption/orphan recovery; optional diarization/summary; queue count/order/ASAP/live-capture pause/ETA duration/unknown. Compare capture/import artifact schemas/provenance/redaction and render mixed queue/library/detail states at 125% with accessible source/status explanation.
- Documentation / installer / release work: document imported versus live provenance, normal processing stages, what failures still publish, expected ETA, and safe restart/retry. Worker/app changes require focused+full gate, installer rebuild, installed smoke; plan-only none.
- Evidence and date: 2026-09-27 implemented `SessionProcessingInputResolver`: an imported processor run now validates a writable queued/processing job, session id, user intent, readiness snapshot, receipt schema/source revision, app-owned `processing` identity, and current staged observation before it returns an input path. It never reads import provenance's original locator. `SessionProcessor` persists an imported output stem (edited date/title plus session id), advances reachable import jobs through `Queued → Processing → Published` or `Failed`, and records source/audio/publication write failures without issuing a ready marker. `MeetingOutputCatalogService` uses that persisted stem for new imports while preserving legacy original-stem precedence. Focused Core coverage: 50/50 across resolver, processor, catalog, and stem tests; full gate: 1,386/1,393 passed, with the existing seven unrelated installer/fixture failures (six missing/locked test apphost-payload cases plus metadata-only diarization fixture script); integration: 8/8 passed. `Build-Installer.ps1` passed: `MeetingRecorder-v0.3-win-x64.zip` (90,893,736 bytes, 20:57 local) and `MeetingRecorderInstaller.msi` (76,087,296 bytes, 20:57 local).
- Remaining gap or next action: add per-stage idempotent output receipts/atomic current-run marker checks, same-stem concurrent publish/retry and restart/lease-boundary proof, then project the safe import state and probe duration through queue/catalog render states. Capture 125% keyboard/accessibility and installed smoke only after the user-owned `MeetingRecorder.App.exe` (PID 23488) is safely closed; do not interrupt it.

Goal: imported audio should behave like a normal completed recording once it
enters the processing queue.

Workstream 1 - Processor integration:

- Route imports through the existing `SessionProcessor`, transcription
  provider, optional diarization provider, optional summary provider, publish
  service, and `.ready` contract.
- Normalize non-WAV inputs into the same speech-optimized WAV path used for
  transcription and final published audio.
- Preserve the existing stable stem behavior using user-edited title/date when
  provided and app-style stem parsing when available.

Workstream 2 - Failure semantics:

- Preserve existing behavior: transcription failure publishes final audio but
  does not create transcript artifacts or `.ready`.
- Diarization failure, missing diarization assets, or diarization skip must not
  block transcript output.
- Summary failure remains supplemental and must not block `.md`, `.json`, or
  `.ready`.

Workstream 3 - Queue behavior:

- Use probed or decoded duration for item and overall ETA when imported
  manifests have no `EndedAtUtc`.
- Preserve startup resume, ASAP processing, live-recording pause behavior, and
  worker interruption behavior for imported sessions.
- Keep queue status snapshots truthful for imported sessions.

Sprint 6 acceptance criteria:

- Imported audio produces the same output artifact set as normal recordings.
- Restart during queued, processing, finalizing, failed, or blocked states is
  safe.
- Queue status and ETA do not treat imports as unknown or invisible when
  duration is available.

## Sprint 7: Restart, Resume, And Backlog Truth

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: startup/background import cycle and imported catalog handling exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: recovery classifier and startup reconciliation slice implemented).
- User outcome: after restart, every import appears once with an honest current state; completed recordings stay readable and active work never vanishes or restarts twice.
- Scope / non-goals: reconcile S1/S2 job state, S6 processor receipts, queue, and Meetings catalog. Do not auto-archive/delete original files, resurrect explicitly removed/cancelled jobs, re-run complete output, or merge unrelated meetings based on title.
- Dependencies and decisions: durable job/session id, source/staging observation, CAS revision/lease, processor/publish receipt and catalog provenance are authority. Startup is reconcile-then-act: load/migrate journal, expire dead leases safely, validate artifacts/ready markers, project UI, then enqueue only eligible user-intent jobs. Published matching session/receipt wins display over stale import work; split/merge/repair outputs link explicit lineage, never filename similarity.
- Implementation slices:
  1. Define recovery classifier for every persisted state/receipt/version: retain, reclaim lease, resume exact stage, retryable block, manual recovery, terminal published/removed. Atomic checkpoints prevent queued/processing/finalizing ambiguity; unclean shutdown and clock skew use lease heartbeat/monotonic attempt identity, no immediate duplicate worker.
  2. Reconcile catalog/queue/job into one logical meeting identity: exact session/job/output receipt/provenance mapping, stable stem allocator, supersession pointer and display precedence. A published valid artifact suppresses stale pending/retry entry; invalid/missing output surfaces repair state rather than hidden archive. User rename/metadata, split/merge, regenerate/retry retain lineage and no duplicate row.
  3. Make stale cleanup conservative: only verified app-owned obsolete staging/manifest after published receipt/references/retention/lease checks; retain audit reason/restore window where feasible. Missing original is not a cleanup failure when staged/published contract is valid. Background cycle reconciles bounded batches and cannot queue job twice.
  4. Project startup/backlog truth: loading/reconciling counts, blocked reason, active stage/attempt, retry time, source availability, published recovery, and per-item action. Snapshot and ETA exclude terminal/suppressed duplicate rows and no zero-progress phantom work; background update preserves selection/drafts/focus.
- Tests and rendered checks: migration/old journal/future version; crash at every checkpoint/lease steal race; queue-worker-startup concurrency; valid/invalid/missing ready/artifact; stale/published precedence; same title/stem/new source/retry/rename/split/merge lineage; cleanup allowlist/no-source mutation. Render startup/reconcile/active/blocked/published/recovery/duplicate states and keyboard focus persistence at 125%.
- Documentation / installer / release work: document restart recovery, no duplicate promise/limits, published precedence, source-missing consequences and cleanup scope. Runtime change later needs full package gate; current planning none.
- Evidence and date: 2026-09-27 implemented a pure `ExternalAudioImportRecoveryClassifier` covering readable/writable schema, queued/processing/failed/blocked/published/removed/pending states, staged input truth, and the crash-after-publish rule: a published manifest with current WAV/Markdown/JSON/ready artifacts wins over stale processing state and cannot re-enter backlog. `ExternalAudioImportStartupReconciliationService` now reads all manifests before worker admission, returns only reconciled queued/interrupted items, suppresses changed/missing stage and failed/blocked/manual states, and preserves safely migrated legacy app-owned staging without ever reading the original. `ProcessingQueueService` consumes that result. Focused Core coverage: 21/21 classifier/reconciliation/staged-input tests plus 3/3 affected imported queue resume/precedence cases passed. `Test-All.ps1` passed 1,403/1,410 tests; its seven pre-existing failures are six locked/missing installer-EXE payload cases and the metadata-only diarization fixture-script case. Integration passed 8/8. Installer rebuild succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,899,387 bytes, 2026-09-27 22:47) and `MeetingRecorderInstaller.msi` (76,120,064 bytes, 22:47).
- Remaining gap or next action: add durable lease/heartbeat and atomic stage/publish receipts, then use the classifier decision/count in the backlog projection with per-item recovery action. Cover worker/startup concurrency, each crash boundary, invalid/missing ready artifacts, rename/split/merge lineage, conservative cleanup, and 125% keyboard/screen-reader state rendering. Installed smoke remains deferred while user-owned `MeetingRecorder.App.exe` (PID 23488) is running.

Goal: prevent imported work from becoming false backlog or duplicate logical
meetings.

Workstream 1 - Durable import state:

- Persist import status separately enough to distinguish review-ready,
  setup-blocked, queued, processing, failed, removed, and published imports.
- On startup, restore the visible import state before queueing or archiving
  anything.
- Avoid assuming that `Queued` alone means the user can understand what is
  happening.

Workstream 2 - Stale work handling:

- Archive superseded imported work only when published transcript artifacts
  prove completion.
- Keep published rows authoritative over stale imported-source manifests.
- Prevent an imported retry manifest with a filename-like title from appearing
  as a second logical meeting beside the original published session.

Workstream 3 - Stem and identity consistency:

- Preserve stable stems through retries and repairs.
- Avoid duplicate rows for app-style stems, renamed imports, split/merge
  outputs, and regenerated transcripts.
- Make import identity rules deterministic and covered by tests.

Sprint 7 acceptance criteria:

- Restart does not hide active import work or resurrect completed imports as
  backlog.
- Published imported meetings remain openable and truthful in Meetings.
- Import retries and repairs do not create duplicate logical rows.

## Sprint 8: Recovery Controls And Import Maintenance

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: recovery-action resolver and receipt-verified staged-work retry slice implemented).
- User outcome: failed imports explain one safe next action, and bulk cleanup never hides successes or deletes the original recording.
- Scope / non-goals: add recovery projection/actions over S1–S7 job/source/lease model. Do not overwrite source, delete external/published files through intake removal, force retry incompatible files, expose source path automatically, or make bulk operations all-or-nothing.
- Dependencies and decisions: job revision/ownership/staged identity, preflight/readiness/recovery classifier and catalog published-delete contract are authority. Retry from work uses only validated app-owned staged copy; Retry from Original re-observes/preflights current source; Replace Source creates a new observation revision with explicit lineage, leaving old source untouched. Remove has named variants: dismiss draft, abandon unqueued job/owned stage, or normal published meeting archive/delete—never ambiguous.
- Implementation slices:
  1. Create pure recovery resolver mapping state/reason/ownership/artifact availability to allowed action, preconditions, consequence/copy and result receipt. Revalidate revision/lease at execute; disabled action names exact missing prerequisite. Open source location is explicit local user action after safe existence/path policy and logs no path.
  2. Implement retry/replace transaction: capture immutable action snapshot; reset only necessary failed stage and preserve user metadata/audit; validate/probe/readiness/space then enqueue. Replacing source creates job source revision/preserves history/suppresses old retry; cancel/failure leaves prior current state. No repeated clicks, timer or second window duplicates action.
  3. Bulk controls snapshot selected eligible job ids/revisions, execute bounded/concurrent-safe items, retain per-row result/recovery and aggregate truthful queued/skipped/blocked/failed counts. Each operation independently commits; cancel stops remaining and never rolls back successes.
  4. Cleanup works only against verified app-owned temp/stage copies after lease/reference/published/retention checks, records opaque receipt and supports safe restore window where practical. Published permanent delete uses normal confirmation/receipt and preserves external original; job removal cannot erase a meeting.
- Tests and rendered checks: resolver state/action matrix; work/original/missing/replaced source; revision/lease/cancel/race/idempotency; bulk mixed outcomes; source/published/staging mutation sentinels; cleanup eligibility/restore; accessibility confirmations/errors/focus and synthetic failure/recovery/bulk rows at 125%.
- Documentation / installer / release work: document each recovery action, source/published distinction, bulk partial results, retention and cleanup limits. Later runtime/UI change runs required package gates; planning none.
- Evidence and date: 2026-09-27 implemented `ExternalAudioImportRecoveryActionResolver`: it projects one named next action with exact, path-free consequence and disabled reason from durable job/schema/staged/original/published facts. Failed imports prefer verified work-copy retry, otherwise current-original re-observation or replacement; setup blocks only route to Setup; published and active imports expose no import mutation; draft removal is explicitly confirmation-gated and always states that the original stays in place. `ExternalAudioImportRecoveryActionService` implements the first mutation: same-service repeated clicks serialize, expected revision and exact current staged receipt revalidate before `Failed → Probing → Queued` (or `BlockedBySetup`), user metadata remains intact, and the durable job receives a bounded opaque recovery receipt with no locator or audio content. Focused import recovery/regression coverage passed 55/55. `Test-All.ps1` passed 1,416/1,423 tests; its seven pre-existing failures are six locked/missing installer-EXE payload cases and the metadata-only diarization fixture-script case. Integration passed 8/8. Installer rebuild succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,907,654 bytes, 2026-09-27 23:00) and `MeetingRecorderInstaller.msi` (76,107,776 bytes, 23:00).
- Final package evidence: the rebuild including the final recovery-service lifetime cleanup succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,907,706 bytes, 2026-09-27 23:04) and `MeetingRecorderInstaller.msi` (76,099,584 bytes, 23:03).
- Remaining gap or next action: implement current-original re-observation and replacement-source lineage transactions, then use their receipts for per-item bounded bulk actions and app-owned-stage cleanup. Wire the resolver only after those actions share a revision/lease boundary. Rendered accessibility and installed smoke remain deferred while the user-owned `MeetingRecorder.App.exe` (PID 23488) is running.

Goal: every failed or blocked import should have an obvious next action.

Workstream 1 - One-file recovery:

- Add actions for failed imports: retry from work copy, retry from original if
  still present, replace source file, remove import, and open source location
  when safe.
- If the original file is missing but the work copy exists, allow retry from the
  work copy.
- If both original and work copy are missing, explain that the import cannot be
  retried until a replacement source is selected.

Workstream 2 - Bulk recovery:

- Add bulk actions for the import surface: retry failed, remove failed, queue
  ready, skip duplicates, and open Import Inbox.
- Make bulk outcomes per-file so one bad file does not hide successful imports.

Workstream 3 - Cleanup:

- Clean redundant work-cache audio after successful publish without touching
  original external files or the retained published WAV.
- Keep cleanup logs metadata-only.
- Ensure delete-permanently for a published imported meeting follows the normal
  meeting delete confirmation and does not delete the original source file.

Sprint 8 acceptance criteria:

- Failed imports always show what can be done next.
- Bulk import recovery is per-file and does not require manual manifest edits.
- No cleanup path deletes original external files.

## Sprint 9: Native Meetings Library Behavior

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: imported artifacts reach catalog/library with source metadata); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: immutable published-origin projection implemented).
- User outcome: after publish an imported recording looks and behaves like a meeting, with only helpful safe source context and no hidden dependency on temporary work files.
- Scope / non-goals: complete published import projection, maintenance-action eligibility, source provenance lineage, and output contract. Do not expose original/local paths, make import state an external automation protocol, promise every maintenance action for corrupt/missing artifacts, or infer speaker identity from source metadata.
- Dependencies and decisions: S6 publish receipt/output schema and S7 catalog precedence are authority. Library detail reads published artifact/sidecar plus safe persisted provenance, not mutable work manifest; missing local work state degrades context to `Imported audio` safely. Action eligibility comes from canonical meeting capability/revision state, with import-specific recovery only where source/staging differs. `.ready` and public artifact contract remain identical except approved safe source category/provenance.
- Implementation slices:
  1. Define immutable published `MeetingOrigin` projection: capture/import category, method label, safe display label, original job/session/lineage id, source availability class, publish revision—not full path/size/hash/profile/audio. Migrate legacy imported outputs and keep catalog resilient to missing/corrupt work manifest.
  2. Drive row/detail from canonical status/capability resolver: read/rename/project/regenerate/add/repair labels/summary/archive/delete/split/merge/retry only if artifacts/leases/revisions satisfy normal requirements. Unsupported state explains why/recovery; imported retry routes S8 source/work rules. Never show a normal action that alters external original.
  3. Preserve provenance through rename/regenerate/retry/republish/split/merge with explicit parent/child lineage and stable logical identity; catalog dedupes by receipt/lineage rather than stem/title. Merge conflicts require preview/confirmation and source context of all inputs; derived source label remains truthful but not excessive.
  4. Lock output/automation boundary: final WAV, Markdown, JSON sidecar and `.ready` atomic contract match normal session. Test consumer sees completion once/no import branch; JSON/Markdown allowlist only safe source category/label if needed, never local locator/source bytes/voice profile. Catalog refresh remains revision-safe/focus-stable.
- Tests and rendered checks: published-only/missing-work/corrupt-legacy provenance; capability matrix/action routing; retry/rename/regenerate/split/merge lineage/dedupe; imported/captured mixed library sort/filter/selection refresh; output/ready consumer parity and redaction. Render row/detail normal/processing/published/missing source/unsupported action at 125%, keyboard/screen-reader source/status/action reason.
- Documentation / installer / release work: document imported source label, maintenance behavior/limits, provenance retention and normal `.ready` automation. Later app/runtime changes require package gates; plan-only none.
- Evidence and date: 2026-09-27 implemented immutable `MeetingOrigin` projection and wired it through `MeetingOutputCatalogService`. Imported published rows now expose only `Imported audio`, a stable intake-method label, opaque session id, and recorded-at-import availability; the resolver and serialized projection omit external locator, staged path, file size/hash, profiles, and audio content. Captured or missing-manifest rows remain safely non-imported. Focused origin/catalog coverage passed 36/36.
- Full/package evidence: `Test-All.ps1` passed 1,422/1,429 tests; its seven pre-existing failures are six locked/missing installer-EXE payload cases and the metadata-only diarization fixture-script case. Integration passed 8/8. Installer rebuild succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,909,340 bytes, 2026-09-27 23:12) and `MeetingRecorderInstaller.msi` (76,107,776 bytes, 23:12).
- Remaining gap or next action: add canonical maintenance-capability and lineage records with published-only/missing-work compatibility tests, then project this origin into the row/detail surface without exposing a local path.

Goal: imported meetings should feel like first-class meetings after publish.

Workstream 1 - Library display:

- Show imported meetings as normal Meetings rows with duration, status,
  transcript availability, summary state, source label, and project.
- Show an import-specific but user-friendly source line in detail view, such as
  `Source: Imported audio`, without exposing internal work paths.
- Preserve imported-source context when the work manifest is still available.

Workstream 2 - Maintenance parity:

- Support rename, project edit, transcript regeneration, add speaker labels,
  repair speaker labels, summary retry, archive, delete, split, and merge where
  existing meeting rules allow.
- Preserve import context through rename, split, merge, retry, republish, and
  summary refresh.
- Keep imported meetings eligible for cleanup recommendations when the same
  recommendation would apply to a normal recording.

Workstream 3 - Output consistency:

- Keep the publish contract unchanged: final `.wav`, Markdown transcript, JSON
  sidecar, and `.ready` marker.
- Keep downstream automation behavior unchanged: consumers watch `.ready`, not
  import state.
- Ensure published Markdown/JSON contain safe source labels but not full
  original paths.

Sprint 9 acceptance criteria:

- Imported meetings can be maintained like normal recorded meetings.
- Split, merge, retry, and rename do not lose source context or create duplicate
  logical rows.
- Downstream `.ready` automation works without import-specific branching.

## Sprint 10: Speaker Label And Identity Parity

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: imports share speaker pipeline and local profile features); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: staged-audio speaker-label/sample parity test implemented).
- User outcome: imported audio gets same conservative local speaker labels/names when eligible, without filenames or source metadata becoming identity evidence.
- Scope / non-goals: verify/complete imported-session adapter to existing diarization/name review/feedback lifecycle. Do not widen auto-name thresholds, use title/path/project/attendee/import method as matching signal, create separate import profiles, upload voice data, or require labels for transcript publish.
- Dependencies and decisions: S3 probe/S6 processor parity/S9 published origin, Speaker roadmap S1–S16 and revised recognition S1–S4 are authority. Eligibility consumes only staged decoded audio, diarization output, stable speaker ids/voice samples, approved local profile store and revision—not import metadata. Import and capture share matcher/correction/rejection/undo/repair implementations; provenance says source of decision, not source-file identity.
- Implementation slices:
  1. Add explicit imported speaker stage adapter/eligibility matrix: mode/assets/CPU fallback, duration/sample quality, cancellation/restart and deferred/throttled/inline behavior. Missing/failed labels leave transcript ready with clear Add/Retry route; no duplicate diarization/worker after resume.
  2. Preserve stable speaker/segment identity through normalization, retry/repair/republish/split/merge; run shared review/evidence/Use/Reject/Refresh/Undo only against current artifact revision. Import retry cannot retrain duplicate samples; correction receipts/rejection scope/undo semantics match capture.
  3. Enforce hard input boundary with tests/spies: matcher/learner/diarizer has no import path/name/title/project/date/attendee/catalog/fixture truth input. Voice embeddings remain profile/work boundary; published transcript/Markdown/summary/log/provider/export use allowlists and no source path/audio/profile payload.
  4. Project imported meeting speaker states in detail through same accessibility/focus/copy contract as recorded meeting, with only relevant eligibility/no-audio/setup/repair distinctions. Derived summary freshness respects speaker revision; no silent reattribution.
- Tests and rendered checks: capture/import shared fixture parity; CPU fallback/optional unavailable/deferred/inline/failure/restart; stable ids/voice-sample idempotency; matcher input spy; suggestion/use/reject/refresh/undo/repair and profile lifecycle; published/privacy/log/provider scans. Render imported generic/suggested/auto/rejected/no-audio/repair states at 125%, keyboard/screen-reader parity.
- Documentation / installer / release work: document local-only label/name parity, optional prerequisites, no metadata-as-identity boundary, and same privacy controls. Runtime change requires full package gates; plan-only none.
- Evidence and date: 2026-09-27 added direct imported-session processing proof: after the original source is absent, the normal `SessionProcessor` passes the verified staged copy to the same diarization provider, persists its speaker labels and local voice samples, and publishes normally. The test covers no import-specific provider parameter or identity hint; labels derive from the staged audio/transcript pipeline only. Focused imported-processing, shared correction, and matcher coverage passed 27/27.
- Remaining gap or next action: add explicit imported-stage eligibility/mode/restart and matcher-input-spy matrix, then verify stable identity through retry, split/merge, correction/rejection/undo and render the shared detail state.

Goal: imported audio should use the same speaker-labeling and local identity
features as recorded meetings.

Workstream 1 - Diarization parity:

- Ensure imported sessions can run normal speaker labeling when the configured
  mode and assets allow it.
- Preserve CPU fallback and skip-label behavior for imported sessions.
- Keep `Repair Speaker Labels` available when imported published JSON sidecars
  show suspicious speaker-label explosions and the meeting is eligible.

Workstream 2 - Speaker-name learning parity:

- Ensure imported diarized meetings can produce speaker voice samples for local
  speaker-name suggestions where the existing pipeline supports them.
- Support Use, Reject, Refresh Suggestions, Undo Name Recognition, and explicit
  speaker-name edits for imported meetings under the same rules as recordings.
- Do not infer real speaker names from filenames, source labels, or metadata.

Workstream 3 - Privacy:

- Keep voice embeddings local-only in the profile store and work-manifest
  boundary.
- Do not publish embeddings, full profile payloads, raw audio snippets,
  transcript text in diagnostic logs, prompts, API keys, auth headers, or full
  private source paths.

Sprint 10 acceptance criteria:

- Imported meetings can receive speaker labels and local speaker-name
  suggestions when normal prerequisites are met.
- Speaker-name learning and feedback semantics match recorded meetings.
- No import metadata is used as a runtime speaker-name hint.

## Sprint 11: Automation Intake And Completion Signals

### Implementation Record

- Status: `Partial`
- Status history: `Partial` (2026-09-27 source audit: background import cycle exists); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: setup-blocked discovery/queue-admission split implemented).
- User outcome: dropping files into managed Inbox is predictable without a fragile always-on automation layer; consumers continue watching normal meeting completion.
- Scope / non-goals: connect S2 inbox journal/scanner to bounded app lifecycle and completion receipts. Do not add cloud/API/webhook contract, watch arbitrary external folders, emit source paths/content, create unregistered background services, or alter `.ready` meaning.
- Dependencies and decisions: S2 ownership/lease, S3 probe, S6 publish, S7 recovery are authority. Watcher is advisory; durable bounded reconciliation is truth. Scanner has cancellation, serialized per-inbox lease, backoff/jitter, error receipt, pause/rescan controls, and no app crash propagation. Any new persistent external process must be registered per repository guidance; preferred design remains in-app lifecycle work.
- Implementation slices:
  1. Define scanner lifecycle/policy and durable receipts: start/stop/idle/background interval, batch/concurrency limits, error classification, last-success/retry, inbox config revision, and cancellation/shutdown boundary. File-event flood/reparse/OneDrive/offline/change races resolve through journal reconciliation.
  2. Keep automation contract narrow: accepted file becomes normal job; completion only existing atomic published artifact plus `.ready`; no import-specific callback or early ready. Failed/blocked jobs stay local inbox UI/state with recovery, not fake completion.
  3. Add standard-user/OneDrive tests and controls: access/placeholder/lock/rename/delete/reconnect, pause/rescan/open inbox/failed-only cleanup against managed receipts. Logs/status use counts/reasons/opaque id only.
- Tests and rendered checks: scanner lifecycle/lease/crash/backoff/flood, config changes, OneDrive placeholder/offline, shutdown, duplicate no-op, `.ready` ordering/consumer parity, standard-user failure and UI pause/rescan/focus/status. Use synthetic files only.
- Documentation / installer / release work: document inbox scan cadence/pause/recovery, no external completion channel, and standard-user limits. Runtime change needs package gates; plan-only none.
- Evidence and date: 2026-09-27 implemented `ImportInboxLifecyclePolicy` and wired it into the app's serialized import cycle. A disabled Inbox never runs; enabled discovery honors its configured cadence, including when transcription setup is missing. In that case reconciliation records only safe local Inbox discovery receipts and updates bounded status text; it cannot invoke intake/queueing. Normal setup-ready flow retains current reconciliation plus one-at-a-time intake. Focused lifecycle, reconciliation, journal, and intake tests passed 19/19.
- Full/package evidence: `Test-All.ps1` passed 1,425/1,432 tests; its seven pre-existing failures are six locked/missing installer-EXE payload cases and the metadata-only diarization fixture-script case. Integration passed 8/8. Installer rebuild succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,909,960 bytes, 2026-09-27 23:23) and `MeetingRecorderInstaller.msi` (76,115,968 bytes, 23:23).
- Remaining gap or next action: persist scanner lifecycle/error receipts and add pause/failed-only recovery controls, then prove `.ready` ordering and standard-user/OneDrive races without adding a watcher process.

Goal: support power-user and automation workflows without weakening the simple
manual path.

Workstream 1 - Import Inbox watcher:

- Formalize Import Inbox scanning on startup and the existing background timer.
- Keep scans bounded and non-overlapping through the existing import gate or a
  replacement import coordinator.
- Keep inbox scan output metadata-only and concise.

Workstream 2 - Automation lifecycle:

- For inbox-managed files, support predictable archive/error handling once a
  file has been copied, queued, failed preflight, or published.
- Document that automation should watch transcript `.ready` files for
  completion, not import job state.
- Document expected sibling artifacts by stem.

Workstream 3 - Operational controls:

- Provide a way to open the Import Inbox, retry failed inbox items, and clear or
  remove failed import jobs without touching original non-inbox files.
- Keep import operations recoverable under standard-user Windows permissions and
  OneDrive-backed paths.

Sprint 11 acceptance criteria:

- Dropping many files into the Import Inbox is predictable and recoverable.
- Power Automate-style consumers still only need `.ready`.
- Import scanner failures do not destabilize the app or queue.

## Sprint 12: Privacy, Consent, Accessibility, And Polish

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29: durable preflight-code allowlist now prevents every untrusted diagnostic from reaching the review UI; native rendered-state evidence remains open).
- User outcome: import feels safe and understandable; local provenance and sensitive data stay bounded; keyboard and assistive use work in all import states.
- Scope / non-goals: audit/fix import disclosure, redaction, consent copy and UI quality from S0–S11. Do not seek legal consent automatically, export source data, change meeting-recording consent policy, or replace native WPF controls with decorative UI.
- Dependencies and decisions: `DESIGN.md`, S1 source projection, S2 ownership, S3 codes, S4 focus, S9 published boundary and S11 scanner states are authority. Source locator/audio/profile/embeddings/transcript diagnostic text/credentials never enter published artifacts, summaries, provider input, telemetry, support bundles, or default UI; local retry may reveal path only on explicit user action.
- Implementation slices:
  1. Build data-flow/redaction matrix across picker/drop/inbox/job/manifest/stage/output/catalog/log/error/support/release evidence; enforce allowlists and sentinel tests. Define concise local/original-retained/consent/responsibility/source-missing/optional-archive copy without overclaiming privacy.
  2. Publish import focus/semantics matrix for entry, review, editor, blocked/failure/recovery, bulk confirmation, inbox/settings. Native names/help/reason/status announce once; color/icon not sole state; no raw paths/debug chrome.
  3. Render synthetic state matrix at 1280x800/1440x900, 100/125%, high contrast and long strings. Enforce Technical Studio surfaces/1px edges/no shadow/max 4px radius; classify/fix clipping, focus loss, contrast or misleading copy.
- Tests and rendered checks: redaction sentinels plus source-reveal authorization; copy/state/focus/keyboard/AutomationProperties; resize/high contrast/long strings and pause/setup/storage/duplicate/retry/bulk outcomes. No private file/audio used.
- Documentation / installer / release work: update README/SETUP privacy/consent/import guidance and accessibility route after verified behavior. UI/runtime changes require package gates; planning none.
- Evidence and date: 2026-09-29 strengthened the existing privacy sentinel to render review status only from the stable preflight-code allowlist, including when an untrusted diagnostic has no locator; this prevents decoder diagnostics and source-derived content from reaching default UI text. The review shows a concise local-copy/source-retention/recording-consent notice, exposes named review actions and bounded grid help, avoids a `SourcePath` binding, and moves focus to the selected review row after files enter it. Focused review, public-projection, and accessibility tests are required below. The static review privacy/focus matrix is documented in `docs/external-audio-import-contract.md`.
- Full/package evidence: `Test-All.ps1` passed 1,427/1,434 tests; its seven pre-existing failures are six locked/missing installer-EXE payload cases and the metadata-only diarization fixture-script case. Integration passed 8/8. Installer rebuild succeeded: `MeetingRecorder-v0.3-win-x64.zip` (90,911,162 bytes, 2026-09-27 23:36) and `MeetingRecorderInstaller.msi` (76,095,488 bytes, 23:36).
- Remaining gap or next action: capture synthetic ready, setup-blocked, retryable, duplicate, and Inbox-disabled states at required sizes/DPI/high contrast. The current computer-use inventory exposed no Meeting Recorder native window; no rendered accessibility or layout claim is recorded. See `docs/ux-audits/external-audio-import-sprint-12-audit.md`.

Goal: remove the last seams that make import feel technical or risky.

Workstream 1 - User-facing trust:

- Add concise copy that import is local, originals are retained, and the user is
  responsible for recording consent and workplace policy compliance.
- Make source-retention behavior visible during import review.
- Make blocked/setup/failure messages plain and actionable.

Workstream 2 - Privacy review:

- Verify logs, summaries, transcript artifacts, diagnostics, status text, and
  prompts do not leak full source paths, raw audio, transcript text in logs,
  embeddings, profile payloads, keys, or auth headers.
- Keep local source paths available only where needed for retry and repair.
- Ensure failure reports and smoke outputs stay metadata-only.

Workstream 3 - Accessibility and polish:

- Validate keyboard import flow, focus order, accessible names, status messages,
  and disabled states.
- Keep controls and text inside their containers at desktop and smaller window
  sizes.
- Match `DESIGN.md`: high-density layout, opaque surfaces, technical lines, no
  drop shadows, no oversized rounded UI, and no decorative one-off styling.

Sprint 12 acceptance criteria:

- A non-technical user can tell what happened to each imported file.
- Privacy-sensitive provenance stays local and bounded.
- Import UI feels native to the current WPF application.

## Sprint 13: Verification, Documentation, And Release

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: S0–S12 synthetic acceptance matrix implemented).
- User outcome: released import behavior is proven from package—not assumed from source—with clear docs and no private source data in evidence.
- Scope / non-goals: verify/ship accepted S0–S12 import behavior. Do not claim feature completion from plan status, run user recordings, publish/push/upload without authority, include private paths/audio/transcripts/profiles/credentials in evidence, or treat developer deploy as installed MSI/update proof.
- Dependencies and decisions: all import sprint contracts, `README.md`, `SETUP.md`, `ARCHITECTURE.md`, `RELEASING.md`, scripts and installer layout are authority. Build only from clean intended commit; release evidence records commit/version/toolchain/model-runtime hashes, commands/results, installer/bundle layout+integrity hashes, and redacted logs. `Test-All.ps1` omits AppPlatform tests, so add `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false` only if import change touches shared deployment/update/extraction.
- Implementation slices:
  1. Create S0–S12 acceptance matrix: contract, source/test owner, synthetic fixture, rendered/accessibility check, doc location, privacy scan, package/installed gate, evidence date/result. Open cells remain open; no sprint becomes `Done` by code presence.
  2. Run focused test bands for source/job/migration/probe/inbox/queue/processor/catalog/recovery/speaker/privacy/UI, then full gate. Record first failure phase and preserve artifacts; do not weaken valid tests. Add static allowlist/redaction/source-mutation and output/ready contract scans.
  3. From clean committed build run installer build and inspect bundle layout/integrity/stable apphost artifacts. Run packaged startup smoke with no active app/worker; then a separate installed-app synthetic journey using a consent-safe generated WAV plus legally distributable/generated non-WAV fixture. Exercise Add/Drop, preflight, setup block/recovery, source retention, publish/ready, restart, retry, and mixed result; uninstall/cleanup only verified test data. Smoke failure records state—never force-closes active user work.
  4. Update docs with source retention, Import Inbox/legacy scan, supported-as-decoded formats, setup/recovery, `.ready` semantics, local/no-cloud boundary, privacy/consent, and accessibility route. Docs use synthetic examples/no raw local paths; release notes list only verified behavior and known limits.
  5. Separate externalization: after evidence and authorization, commit/push, rebuild if required from pushed commit, upload authorized assets and verify remote hashes/release. No operation in this sprint assumes that authority.
- Tests and rendered checks: matrix review, focused suite/full gate/conditional AppPlatform suite, installer integrity, package startup, installed synthetic WAV/non-WAV workflow, 100/125% high contrast/keyboard/screen-reader import states, path/audio/profile/log/provider redaction and no-source-mutation. Preserve metadata-only report/known limits.
- Documentation / installer / release work: README/SETUP/ARCHITECTURE/RELEASING and release notes update with behavior. Implementation requires installer rebuild and package smoke; this planning refinement does neither.
- Evidence and date: 2026-09-27 added `docs/external-audio-import-acceptance-matrix.md`, a no-private-data S0–S12 ledger linking contract owners, synthetic test bands, rendered/package gates, and all current open evidence. It records the current focused/full/integration/package results without treating them as clean-release proof.
- Remaining gap or next action: after this shared tree has a clean intended commit and an isolated non-user install, inspect bundle layout/integrity and stable apphosts, run packaged startup, then run generated WAV/non-WAV Add/Drop/setup/restart/retry evidence. Native rendered state capture remains separately open.

Goal: ship the feature through the repo's normal verification and release
discipline.

Workstream 1 - Test coverage:

- Add or update tests for import metadata, backward-compatible manifest loading,
  source retention, media probe, duplicate suppression, setup-blocked state,
  bulk import review state, processing parity, queue resume, stale import
  archival, catalog precedence, retry, split/merge, and speaker-label parity.
- Focus expected test coverage around:
  - `ExternalAudioImportServiceTests`,
  - `SessionProcessorTests`,
  - `ProcessingQueueServiceTests`,
  - `MeetingOutputCatalogServiceTests`,
  - `MainWindowInteractionLogicTests`,
  - XAML/source guard tests for the import entry points and status surfaces.

Workstream 2 - Verification commands:

- Run focused import, processor, queue, catalog, and UI logic tests first.
- Run the full gate: `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Because this is app/runtime/UI behavior, rebuild installer assets:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Smoke packaged behavior with a synthetic WAV and at least one real non-WAV
  sample after confirming no active installed app or processing worker:
  `powershell -ExecutionPolicy Bypass -File .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Workstream 3 - Documentation:

- Update `README.md`, `SETUP.md`, `ARCHITECTURE.md`, and release notes.
- Document supported formats, Import Inbox behavior, source-retention behavior,
  setup-blocked imports, retry/recovery actions, and `.ready` completion
  semantics.
- State clearly that no cloud upload path is added by import.

Sprint 13 acceptance criteria:

- Focused tests and full gate pass.
- Installer assets are rebuilt after implementation.
- Packaged smoke confirms explicit import and at least one non-WAV import path.
- Docs describe the feature accurately without exposing private local examples.

## External Audio Import Interfaces And Constraints

- No cloud upload path is added.
- Original external files are retained by default in every import path.
- Explicit import and drag/drop are primary; watched-folder import is
  compatibility plus automation.
- Import reuses existing local transcription, diarization, summary, publish,
  queue, and Meetings maintenance systems.
- Full original source paths stay local to work manifests, retry state, and
  metadata-only logs; they are not published to transcript artifacts.
- Runtime speaker labeling and speaker-name recognition must not use filenames,
  source labels, attendee counts, expected speaker counts, expected names, or
  fixture labels as hints.

# GPU Transcription Seamless Acceleration Plan

## Summary

Goal: add optional local GPU acceleration for transcription while preserving the
current CPU Whisper path as the always-available baseline.

The user experience standard is intentionally strict: GPU transcription must
feel like a background acceleration lane, not a new setup obligation. Setup must
stay centered on `Standard`, `Higher Accuracy`, and custom transcription model
choices. CPU-only machines, blocked GPU runtimes, failed probes, bad GPU output,
and repeated GPU crashes must still publish transcripts through CPU whenever CPU
can succeed. Advanced settings can expose provider truth for power users, but
normal users should not need to understand GPU model formats, runtime packages,
drivers, or fallback decisions.

External feasibility anchors:

- ONNX Runtime DirectML requires DirectX 12-capable hardware and has provider
  configuration constraints that must be respected by any in-process provider.
- Microsoft guidance points newer Windows ONNX work toward Windows ML/WinML
  while DirectML remains supported.
- `whisper.cpp` exposes cross-vendor GPU paths such as Vulkan, but using it
  would change the runtime and packaging shape from the current `Whisper.net`
  CPU integration.

## Seamless User Standard

- CPU transcription remains permanent and fully supported.
- GPU never blocks recording, publishing, retry, setup, update, or `.ready`
  generation.
- `Auto` means the app may use GPU only after local readiness, quality, and
  performance gates pass.
- Setup remains unchanged: users still choose `Standard`, `Higher Accuracy`, or
  custom import.
- GPU failures are quiet when CPU fallback succeeds.
- Advanced settings show whether GPU is ready, which provider actually ran, why
  fallback happened, and whether CPU-only override or local suppression is
  active.
- No user-managed Python, CUDA, driver setup, manual runtime install, or manual
  model conversion is allowed in the shipped path.

## Sprint 0: Product Promise And No-Go Gates

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: CPU-first no-go policy and acceptance contract implemented).
- User outcome: GPU may speed eligible backlog work, but every machine remains a trustworthy CPU product with no driver/admin/setup surprise.
- Scope / non-goals: define success/no-go/measurement/decision authority before provider, asset, UI or runtime changes. Do not select backend, ship GPU code/assets, collect hardware telemetry, alter transcription defaults, or promise acceleration on a device class.
- Dependencies and decisions: CPU baseline and existing local/offline/privacy/installer/update contracts are non-negotiable. GPU is opportunistic and revocable per job/device/version; Auto must be indistinguishable from CPU except truthful bounded status. No external telemetry; synthetic/consented local benchmark data only. Architecture/dependency tracker records decision after evidence, not speculative dependency selection.
- Implementation slices:
  1. Write acceptance matrix for CPU-only, supported/unsupported/blocked GPU, driver/runtime mismatch, low disk/memory/battery, active recording, worker crash/retry, update/rollback and repeated suppression. Define observable success: same transcript/metadata/ready contract and no worse protected quality/reliability, with acceleration only when measured versus matched CPU baseline; state sample size, latency/throughput/energy limits and uncertainty.
  2. Define capability/fallback state machine: `NotRequested`, `Probing`, `Eligible`, `Accelerating`, `CPUFallback`, `Suppressed`, `Unavailable`, `Failed`; reason code/remediation, per-job timeout/cancellation, CPU retry, circuit breaker and rollback. GPU never blocks recording/setup/publish/retry/update and never triggers driver/install/download prompts or user-managed CUDA/Python/model conversion.
  3. Establish hard no-go/stop review: unsupported license/supply chain/signing/asset size, admin/driver requirement, CPU regression, output divergence, privacy/EDR risk, battery/thermal harm, unstable crash rate, weak capability probe, inaccessible copy, or unpackageable update rollback. Define evidence owner/review and safe cancellation/no-source changes.
  4. Set benchmark protocol: pinned source/model/runtime/device mode, cold/warm CPU/GPU runs, representative synthetic/consented corpus, deterministic quality comparison, system load/battery recording, no private transcript/audio in reports. Separate feasibility from release promotion and forbid hardware-based assumptions.
- Tests and rendered checks: policy/state transition/no-go decision fixtures; CPU equivalence baseline/retry/circuit-breaker and redacted status checks. Render truthful Auto/CPU/unavailable/suppressed/failure states with keyboard/screen reader after UI sprint; no UI work now.
- Documentation / installer / release work: add architecture promise/no-go/fallback and dependency tracker entry only after decision evidence. No package/runtime change in this planning slice.
- Evidence and date: 2026-09-27 added pure `GpuTranscriptionPolicy` with no hardware/process/network/asset access. It keeps CPU mandatory, blocks probe/execution for every named no-go condition, active recording, unvalidated CPU baseline, missing asset contract, unconfirmed capability, and an open circuit breaker; the only positive S0 decision is a CPU-safe `Probing` state. `docs/gpu-transcription-acceptance.md`, `ARCHITECTURE.md`, and `docs/dependency-api-tracker.md` now define the redacted state contract, hard stop review, and matched cold/warm benchmark protocol. Focused policy/preset tests passed 14/14.
- Remaining gap or next action: S1 must build the isolated CPU-normalized fixture harness and score candidates without adding a product dependency, asset, setting, or provider.

Goal: define what counts as seamless before choosing or building a backend.

Workstream 1 - Product promise:

- Write explicit acceptance criteria for CPU-only, GPU-capable, GPU-blocked,
  battery, active-recording, update, forced-retry, and repeated-crash scenarios.
- Treat CPU as the product baseline and GPU as opportunistic acceleration.
- Define user-visible success as faster backlog drain with no new setup burden.

Workstream 2 - No-go gates:

- Reject any path that requires Python, CUDA-only setup, user-managed drivers,
  manual model conversion, admin install steps, or browser/session scraping.
- Reject any path that makes GPU assets required for CPU transcription,
  recording, setup, publish, update, or transcript retry.
- Reject any path that creates endpoint-security prompts through new process
  inspection, broad process-tree control, or invasive runtime probing.

Workstream 3 - Documentation gate:

- Record the product promise, no-go gates, and default fallback posture in
  `ARCHITECTURE.md` before implementation begins.
- Keep `docs/dependency-api-tracker.md` ready to capture the chosen runtime,
  model format, checked versions, and deferred alternatives.

Sprint 0 acceptance criteria:

- A technically working GPU path can still be rejected if it fails the user
  promise.
- The implementation track has clear stop rules before package, UI, or provider
  work begins.

## Sprint 1: Backend Feasibility Spike

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-27: output fingerprint and candidate scorecard implemented without product runtime changes).
- User outcome: GPU work proceeds only if one Windows backend demonstrably accelerates compatible local transcription without destabilizing CPU product.
- Scope / non-goals: run isolated, reproducible feasibility experiments and select/stop. Do not integrate app/worker/UI, add dependencies/assets to product, change CPU model/defaults, scrape hardware/processes, or turn diarization DirectML support into transcription proof.
- Dependencies and decisions: S0 gates and current CPU `ggml` path are authority. Current DirectML Sherpa package is speaker-labeling-specific; it cannot be reused by assertion. Score WinML, ONNX Runtime DirectML, and whisper.cpp Vulkan only against identical gate: license/security/maintenance, Win10/11 standard-user packaging, local model provenance, no driver/admin/Python/CUDA/download, CPU fallback, output compatibility, size, probe safety, and measured benefit. Candidate order is investigation priority, not preselection.
- Implementation slices:
  1. Build an out-of-product spike harness pinned by source/toolchain/runtime/model hash and synthetic/consented WAV corpus. Candidate runs in disposable temp location and reports opaque device capability class, elapsed/cold-warm/memory/package file list/error category—no audio/text/paths. No untrusted runtime is copied into repo/product during spike.
  2. Prove model contract per candidate: current ggml direct reuse or documented independently reproducible conversion/download chain with license/hash/signature, tokenizer/language/timestamp/segment mapping, model size and fallback asset relationship. Standard first; higher-accuracy only after Standard meets gate. Reject hidden conversion at customer install/run.
  3. Compare CPU/candidate through shared normalized output fixtures: complete/sparse/silent/long/cancel/error output schema, timestamps/order/segments/language, user-visible failure, and CPU fallback after candidate initialization/run failure. Establish tolerance/quality review protocol; speed alone cannot win with material transcript regression.
  4. Produce scored decision record and stop/advance recommendation. Any licensing/supply-chain/package/driver/security/CPU-fallback/output/no-benefit failure stops that candidate; no candidate passing means stop roadmap before S2. Advancing candidate names exact runtime/model asset, supported OS/device class, risk/rollback and unproven claims.
- Tests and rendered checks: harness repeatability, output normalizer/fixture schema, CPU fallback/cancel/crash, model hash/license manifest, package file diff/size budget, clean VM/standard-user invocation, and redaction scan. No product UI/render change in spike.
- Documentation / installer / release work: record candidates/evidence/deferred alternatives in dependency tracker and architecture only after test result; no installer rebuild or release for isolated spike.
- Evidence and date: 2026-09-27 added `GpuTranscriptionOutputFingerprint`, a transcript-text/path-free structural comparison for language, ordered timestamps, segment count, and character count. It rejects incompatible schema/timing but always requires manual quality review. `docs/gpu-transcription-feasibility-scorecard.md` records current primary-source review: Windows ML/legacy DirectML is the sole pending candidate, while no matching Whisper ONNX model/asset contract is proven and whisper.cpp Vulkan lacks a no-user-setup package contract. Focused policy/fingerprint/session-processor tests passed 20/20.
- Remaining gap or next action: obtain a signed/licensed, standard-user Windows ML/DirectML Standard-model candidate and a synthetic/consented speech corpus, then run the isolated matched CPU/candidate spike before adding any dependency, model, asset, setting, or provider.

Goal: prove the exact backend and model path before app integration.

Workstream 1 - Backend candidates:

- Evaluate Windows ML/WinML first for Windows-first ONNX acceleration.
- Evaluate ONNX Runtime DirectML second if WinML cannot satisfy the provider
  contract cleanly.
- Evaluate `whisper.cpp` Vulkan only if Windows ML/DirectML cannot satisfy the
  model and packaging requirements.

Workstream 2 - Model-format proof:

- Determine whether the current `ggml` model assets can be reused. Assume they
  cannot until proven otherwise.
- If GPU needs a different format, define hidden GPU model assets mapped behind
  the visible `Standard` and `Higher Accuracy` profile names.
- Prove at least the `Standard` model path first; defer `Higher Accuracy` GPU
  assets if they make package size or install flow too heavy.

Workstream 3 - Local prototype:

- Run one short safe WAV and one longer safe WAV through the candidate backend.
- Compare against the current CPU provider for elapsed time, segment count,
  character count, timestamp shape, sparse-output behavior, and memory use.
- Capture runtime file list, model file list, package-size estimate, and any
  machine requirements.

Sprint 1 acceptance criteria:

- One reproducible prototype proves GPU transcription, model loading, output
  normalization, and CPU fallback feasibility.
- If no candidate satisfies the no-go gates, stop the track before product
  integration.

## Sprint 2: Hidden Asset Contract

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: Sprint 1 has no selected signed/licensed runtime-model candidate or matched CPU-fallback evidence).
- User outcome: GPU-capable builds can use verified optional assets, while missing/bad/incompatible assets never make normal transcription or setup look broken.
- Scope / non-goals: define catalog/readiness/packaging/update contract after one S1 candidate passes. Do not select/download assets yet, change visible profile names, auto-fetch runtime/model, make GPU required, or hide security/privacy consequences from advanced support status.
- Dependencies and decisions: S1 chosen candidate evidence and CPU catalog/readiness are prerequisites. Each asset set is immutable tuple: runtime id/version/architecture/OS range, model profile+format/tokenizer, file relative path/length/SHA-256, signing/license/source/provenance, compatibility ABI/model manifest version, release channel and rollback target. CPU `ggml` remains sole baseline readiness; GPU status is additive/local metadata only.
- Implementation slices:
  1. Extend catalog schema with optional `executionAssets` keyed by visible CPU profile, strict version/unknown-field handling and no raw download URLs in normal UI. Resolver validates all required files/hash/length/relative-path traversal/signature policy and runtime-model compatibility before `Ready`; distinguish absent, corrupt, incompatible, release-disabled, locally suppressed, probe-failed and policy-blocked.
  2. Make install/update atomic: stage complete asset tuple under app-owned versioned root, verify manifest before promotion, retain known-good tuple/rollback, garbage collect only unreferenced verified old tuples. CPU package/update works if GPU tuple absent/invalid; V2 apphost/loose manifests remain aligned. No partial asset is process-loadable.
  3. Define bundle/release budget and strategy: runtime/model split decisions use S1 size/install/startup evidence, offline/clean-install test and user-visible CPU fallback. Standard may be optional release asset only if source/hash/channel/install UX is verified; Higher Accuracy remains deferred until its own evidence. No in-app asset acquisition without later explicit authority/design.
  4. Project minimal trustworthy status: normal Setup says transcription ready from CPU; advanced diagnostics exposes GPU availability/version/reason and local suppression/repair route without raw paths/hashes unless explicit support action. Status never claims acceleration just because files exist.
- Tests and rendered checks: schema migration/unknown/future version; traversal/duplicate/file/hash/length/signature/ABI/model mismatch; atomic stage/promote/crash/rollback/cleanup; CPU readiness with every GPU failure; bundle/update layout and redacted status. Render CPU-ready/GPU-absent/invalid/incompatible/suppressed in advanced view with accessibility.
- Documentation / installer / release work: architecture/dependency tracker records only selected S1 runtime/model/licensing and tuple policy; package docs describe optional assets/fallback/update rollback after implementation. No installer change in planning.
- Evidence and date: 2026-09-27 S1 added the isolated redacted output-comparison boundary and primary-source scorecard, but identified no approved runtime/model tuple. Existing DirectML diarization assets are not transcription evidence.
- Remaining gap or next action: S1 must first supply the selected candidate's signed/licensed runtime/model format, tokenizer/timestamp mapping, size, source, and standard-user CPU-fallback proof; then define the additive immutable asset schema.

Goal: make GPU assets explicit without changing the setup experience.

Workstream 1 - Catalog extension:

- Extend the model catalog to describe hidden execution assets behind each
  visible transcription profile.
- Preserve the current CPU `ggml` entries as the setup readiness source.
- Add optional GPU asset descriptors with runtime id, model format, file list,
  managed relative path, expected size, and hash manifest.

Workstream 2 - Readiness service:

- Add a GPU transcription asset readiness service that reports `Ready`,
  `Absent`, `Invalid`, `IncompatibleRuntime`, `DisabledByRelease`, or
  `SuppressedLocally`.
- Keep readiness metadata local and metadata-only.
- Ensure missing GPU assets do not mark transcription setup as incomplete.

Workstream 3 - Packaging policy:

- Bundle the GPU runtime only if the measured runtime is stable and release size
  stays acceptable.
- Bundle the `Standard` GPU model only if it does not make MSI/ZIP size or
  install time unacceptable.
- Keep `Higher Accuracy` GPU assets out of the critical path until a separate
  release-asset strategy is proven.

Sprint 2 acceptance criteria:

- Installed builds can inspect GPU asset readiness without changing Setup.
- CPU assets remain sufficient for transcription readiness and recording
  eligibility.

## Sprint 3: Metadata And Snapshot Compatibility

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires Sprint 2's immutable verified asset tuple, which cannot exist before Sprint 1 selects a candidate).
- User outcome: support can explain CPU/GPU attempt/fallback truth without changing transcript/output behavior or leaking device/private data.
- Scope / non-goals: add versioned optional execution provenance before provider factory. Do not change provider selection, `.ready`, transcript text/segments/timestamps, CPU default, summary input, or expose full device/driver/path/model telemetry.
- Dependencies and decisions: S0 state machine and S2 asset tuple define vocabulary. Record an immutable per-attempt `TranscriptionExecutionMetadata`: requested preference, eligible/selected/effective provider, runtime/asset opaque ids+versions, start/end/elapsed, fallback/suppression code, retry/attempt id, schema version. `requested` never means used; CPU fallback must be observable. Unknown enum fields are preserved/treated safely by readers, not coerced to GPU success.
- Implementation slices:
  1. Add additive DTO/schema with explicit null/missing semantics and safe fixed enums; attach to result, processing metadata and snapshot as attempt history/current final receipt. Legacy snapshots deserialize CPU/unknown provenance without rewrite; future incompatible metadata preserves transcript and surfaces no unsafe decision.
  2. Make persistence revision/idempotency safe: write metadata only with matching processing attempt/input/output revision, atomic snapshot updates, bounded attempt history and deterministic last-effective selection. Retry/cancel/crash/update migration cannot overwrite good CPU result with stale GPU status or re-publish.
  3. Define publication boundary: meeting transcript JSON may include approved high-level provider/fallback/version/count timing only if existing output contract permits; otherwise status remains local snapshot/support view. Never serialize device identifiers, driver versions, raw command/env, paths, audio/text diagnostics, profile/embedding, prompt/key/header/source context. Logs use reason code/count.
  4. Verify consumer stability: old readers/automation ignore optional fields; `.ready` unchanged; summary/diarization sees only transcript contract; export/catalog UI maps no metadata to neutral CPU/unknown copy.
- Tests and rendered checks: legacy/current/future schema, enum/null/malformed metadata, JSON roundtrip/allowlist/redaction, atomic revision/race/retry/cancel/crash, output/ready byte-contract where required, old consumer fixture. Render local diagnostics CPU/eligible/accelerating/fallback/suppressed without technical overload and screen-reader reason.
- Documentation / installer / release work: document safe diagnostic fields/retention and CPU fallback truth after implementation; no package change in schema plan.
- Evidence and date: 2026-09-27 audit found analogous diarization metadata but no transcription execution model/snapshot migration/redaction/consumer proof.
- Remaining gap or next action: add pure metadata DTO/schema fixtures and legacy snapshot reader tests before provider wiring.

Goal: add provider observability without changing CPU behavior.

Workstream 1 - Types:

- Add `TranscriptionExecutionProvider` with the chosen values, such as `Cpu`,
  `Directml`, and `Winml`.
- Add `TranscriptionAccelerationPreference` with `Auto` and `CpuOnly`.
- Add `TranscriptionMetadata` with provider, runtime id, model asset id, GPU
  requested, GPU available, elapsed time, fallback reason, and safe diagnostic.

Workstream 2 - Persistence:

- Extend `TranscriptionResult`, `transcription.snapshot.json`,
  `MeetingProcessingMetadata`, and transcript JSON with optional metadata.
- Keep existing snapshots readable and reusable.
- Keep `.ready` semantics unchanged.

Workstream 3 - Privacy:

- Do not write raw audio, transcript text in diagnostic logs, prompts, API keys,
  auth headers, embeddings, full profile payloads, or private source context
  into metadata.

Sprint 3 acceptance criteria:

- CPU transcripts still publish as before, with only additive safe metadata.
- Downstream JSON consumers remain compatible.

## Sprint 4: Provider Factory And Shared Quality Policy

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires the Sprint 2 asset tuple and Sprint 3 execution metadata contracts).
- User outcome: acceleration cannot change what a trustworthy transcript means; CPU remains exact default and recovery path.
- Scope / non-goals: introduce minimal provider factory/policy seams after S1–S3 proof. Do not enable GPU, rewrite CPU provider, duplicate normalization, alter retry UI, or make output quality a provider-specific subjective rule.
- Dependencies and decisions: existing `WhisperNetTranscriptionProvider` is canonical CPU. Factory input is immutable job/config/readiness/asset snapshot; provider returns normalized candidate plus S3 metadata, never writes artifacts. Selection precedence: `CpuOnly` always CPU; Auto selects GPU only S2-ready/S5-probed/unsuppressed snapshot; any factory/probe/init/run/quality failure returns CPU under same attempt policy. Program wiring remains composition-only.
- Implementation slices:
  1. Extract provider-neutral pipeline contracts: prepared audio, language/options, cancellation/progress, raw candidate, normalized transcript, quality decision, reason codes. Own audio preparation/active-audio/sparse/completeness/English fallback once; all providers receive identical prepared immutable input.
  2. Implement factory and CPU adapter without behavioral edits, with dependency injection/test doubles and explicit unknown-provider failure-to-CPU. Guard against recursive fallback/double transcription, concurrent writes, stale snapshot reuse and forced retry bypassing policy.
  3. Define deterministic quality comparator: schema/order/timestamp bounds, active-audio/sparse policy, language/result integrity and required artifacts. GPU candidate may be rejected/fallback CPU; never blend segments or publish candidate before gate. Quality failure is safe metadata/reason, not transcript text logging.
  4. Preserve resume/retry: persisted CPU snapshots retain existing reuse semantics; provider/asset/input/options revision controls reuse, forced retry creates new receipt, and cancellation/crash leaves last valid result untouched.
- Tests and rendered checks: CPU equivalence/source guard, CpuOnly zero GPU construction/probe, Auto selection matrix, factory/init/run/quality fallback exactly once, common prepared input, normalization/quality edge fixtures, snapshot legacy/current/retry/force/cancel/race. Diagnostic state render later; no UI redesign.
- Documentation / installer / release work: architecture documents factory ownership/CPU default/fallback once implemented; runtime package gates apply then, no build now.
- Evidence and date: 2026-09-27 audit found CPU provider but no transcription factory or shared policy; diarization factory patterns are reference only, not direct contract.
- Remaining gap or next action: characterize CPU provider inputs/outputs/retry snapshots before extracting interfaces.

Goal: prevent CPU and GPU transcription behavior from drifting.

Workstream 1 - Factory:

- Add a transcription provider factory in the worker.
- Keep `WhisperNetTranscriptionProvider` as the canonical CPU provider.
- Keep `Program.cs` thin by resolving provider selection through the factory.

Workstream 2 - Shared quality policy:

- Move prepared-audio expectations, active-audio analysis, sparse-output
  detection, and English fallback policy into provider-neutral logic.
- Require GPU output to pass the same transcript completeness checks as CPU.
- Ensure forced retranscription and persisted snapshot reuse still work.

Workstream 3 - CPU baseline tests:

- Add tests proving `CpuOnly` never attempts GPU.
- Add tests proving old snapshots load after metadata is added.
- Add source tests to keep provider selection intentional and auditable.

Sprint 4 acceptance criteria:

- Multi-provider architecture exists, but default runtime behavior remains CPU.
- Existing CPU behavior and retry semantics are preserved.

## Sprint 5: Probe And Readiness System

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires a Sprint 2 verified tuple and Sprint 4 provider policy; neither is available).
- User outcome: app can truthfully say GPU is usable or falls back, without touching meeting data or making startup/recording less reliable.
- Scope / non-goals: add isolated worker capability probe/readiness cache after S1–S4. Do not benchmark user meetings, auto-download models, inspect arbitrary processes/devices, block setup/queue, or make manual Probe a diagnostic escape from safety policy.
- Dependencies and decisions: S2 verified asset tuple/S3 metadata/S4 factory/S0 scheduler gates. Worker receives minimal generated probe config with only verified app-owned asset paths/tuple id and no provider credentials/source paths; input is bundled/generated deterministic audio and expected normalized shape/hash, not transcript content. Probe output is capability evidence, not performance claim.
- Implementation slices:
  1. Define command/input/output contract: `--probe-transcription-gpu` validates argument/version/asset compatibility, creates private temp input/output, activates selected provider, runs bounded inference/normalization sanity, returns machine-readable safe code/elapsed/runtime+asset id, cleans verified temp. Timeout/cancel/child crash/nonzero/invalid output leaves no product state mutation and redacts command/environment/error.
  2. Add readiness resolver/cache keyed by candidate runtime/model/hash/app version/OS capability/config policy. States include never-probed, scheduled, probing, ready, unavailable, blocked, failed, suppressed, stale; TTL/backoff/jitter and failure circuit break prevent repeated startup work. Asset/update/config change invalidates prior success; clock/time errors cannot create permanent readiness.
  3. Schedule only idle-safe conditions: no live recording, worker/queue critical stage, responsive-mode pause, low battery/thermal/power policy, shutdown/update transition, or existing probe lease. Manual Advanced request validates same gates/rate limit/one lease and gives queued/blocked reason; it cannot run concurrent or foreground-block UI.
  4. Persist bounded local status (attempt id/time/code/provider/tuple/elapsed), with atomic write/migration/corruption recovery. UI sees last result and CPU fallback route only; no full hardware/driver/path/raw output and no implication future job will accelerate.
- Tests and rendered checks: worker CLI argument/minimal-config/temp cleanup/synthetic input/redaction/timeout/cancel/crash; cache key/invalidation/backoff/clock/corrupt file; scheduler gate/concurrency/manual rate/no startup block; CPU product unaffected by probe. Render Advanced never/queued/ready/stale/blocked/failure states, keyboard/screen-reader, no raw diagnostics.
- Documentation / installer / release work: document probe uses synthetic local input, does not guarantee speed, respects policy, and CPU fallback; bundled input/worker option require package integrity tests later. Planning only no package build.
- Evidence and date: 2026-09-27 audit found analogous DirectML diarization probe/status but no transcription probe, minimal config/synthetic contract, cache invalidation/backoff, or recording/queue/power scheduling proof.
- Remaining gap or next action: write probe protocol/result schema and synthetic fixture before adding worker command.

Goal: learn GPU capability without disrupting the app.

Workstream 1 - Worker probe:

- Add `MeetingRecorder.ProcessingWorker --probe-transcription-gpu --config
  <path>`.
- Use synthetic or bundled test input only; never use meeting content.
- Validate runtime load, model load, provider activation, output sanity, and
  elapsed time.

Workstream 2 - Probe scheduling:

- Do not run probes during live recording.
- Do not run probes while responsive-mode background work is paused.
- Cache failures with backoff so startup does not repeatedly probe a blocked
  machine.
- Allow manual probe from Advanced settings.

Workstream 3 - Status:

- Persist last probe status, timestamp, provider, runtime id, model asset id,
  elapsed time, and safe fallback reason.

Sprint 5 acceptance criteria:

- GPU readiness can be proved without blocking startup, recording, setup, or
  queue processing.

## Sprint 6: GPU Provider MVP

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires selected provider, verified assets, probe evidence, and factory policy from blocked Sprints 1–5).
- User outcome: eligible backlog work may accelerate; every incompatible/unstable outcome still yields normal CPU transcript without action or data loss.
- Scope / non-goals: implement selected S1 provider after S2–S5 gates. Do not run GPU during recording, publish partial candidate output, accept runtime-internal CPU as GPU success, retry user cancellation, or change transcript/ready contract.
- Dependencies and decisions: S4 factory owns selection/S5 fresh ready cache gates; provider consumes prepared WAV only and returns isolated candidate plus S3 metadata. Candidate never writes snapshot/artifacts. Arbitration is provider-neutral and happens before final persistence; quality failure/init/run/diagnostic internal fallback results in at most one CPU attempt for same immutable input/attempt id. Cancellation/preemption is terminal cancellation, not CPU fallback.
- Implementation slices:
  1. Implement provider with verified tuple, bounded GPU resources/timeouts/progress/cancellation and normalized `TranscriptSegment` mapping. Validate input/options/asset revision before start and clean provider temp/resource handles on every exit; no ambient user config/model discovery.
  2. Run quality arbitration: schema/timestamp/order/active-audio completeness/sparse/empty/language policy and optional CPU comparison only within defined budget. Choose final candidate deterministically; do not merge candidate/CPU text. GPU accepted only when actual GPU execution and quality gate pass; internal CPU path is recorded as fallback.
  3. Execute one CPU fallback with same prepared input and source revision for recoverable GPU outcomes, preserving first failure reason and final effective provider. If CPU also fails, retain normal failure semantics; no double snapshot/ready/publish or indefinite retry. Persist snapshot only after final result/attempt revision wins CAS.
  4. Preserve shutdown/user interruption: propagate cancellation through GPU/provider child work, avoid retry/suppression mutation on cancellation, and release lease so normal queue recovery can resume. Crash handling delegates S7 circuit breaker.
- Tests and rendered checks: provider input/normalization/progress/cancel/cleanup; gate matrix, init/run/internal-CPU/empty/sparse/quality failure exactly-one fallback; final result/snapshot/ready CAS/race/retry; CPU failure; no GPU during recording; metadata/redaction. Render status later from S3 fields; no MVP UI redesign.
- Documentation / installer / release work: document actual-versus-requested provider/fallback only after measured MVP; implementation requires asset/package integrity and full release gates later.
- Evidence and date: 2026-09-27 audit found no GPU transcription provider; existing CPU and diarization fallback behavior is insufficient proof of transcript result arbitration/cancellation/snapshot safety.
- Remaining gap or next action: add fake provider/factory arbitration tests before loading real runtime.

Goal: run GPU transcription only after readiness gates pass.

Workstream 1 - Provider:

- Implement the chosen GPU provider.
- Reuse the existing prepared WAV format.
- Normalize provider output into existing `TranscriptSegment` objects.
- Record provider metadata for the accepted final transcript.

Workstream 2 - Fallback:

- Retry CPU if GPU init fails, provider output is empty, output is implausibly
  sparse for active audio, runtime throws, or provider diagnostics indicate CPU
  fallback inside the GPU runtime.
- Keep the richer transcript when CPU fallback improves a sparse GPU result.
- Save `transcription.snapshot.json` only after the final accepted transcript is
  selected.

Workstream 3 - Cancellability:

- Preserve cancellation semantics for shutdown, worker preemption, and user
  interruption.

Sprint 6 acceptance criteria:

- Compatible machines can publish GPU transcripts.
- Incompatible or unstable machines publish CPU transcripts without user action.

## Sprint 7: Worker-Crash Recovery

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: GPU-attributable recovery requires the blocked provider and per-attempt contract from Sprints 3–6).
- User outcome: a hard GPU worker failure becomes one normal CPU recovery attempt, not a stranded meeting or crash loop.
- Scope / non-goals: recover crashes causally attributable to a GPU transcription attempt. Do not retry arbitrary worker/app crashes as CPU, restart recording, delete models/sources/published artifacts, hide failure evidence, or make suppression global across unrelated runtime/model versions.
- Dependencies and decisions: S3 attempt metadata/S4 factory/S5 probe/S6 immutable job and final snapshot contracts are authority. Persist launch + in-flight checkpoint before GPU work: job/input/revision/attempt id, requested/selected provider, tuple hash, stage, start/heartbeat, worker PID/exit disposition. A crash is GPU-attributable only when current uncompleted checkpoint/exit timing proves that attempt; otherwise normal worker recovery applies.
- Implementation slices:
  1. Extend launch snapshot/worker protocol with signed/validated local acceleration override and checkpoint acknowledgements. Parent detects exit/timeout/heartbeat loss once, claims recovery lease using CAS, validates input/artifacts/no completed final receipt, then writes a recoverable `GpuWorkerLost` result—not a guessed transcription failure.
  2. Spawn exactly one CPU-only retry for same immutable input/options/revision with new attempt id and explicit parent receipt; it cannot reenter GPU/factory Auto. CPU success atomically publishes normal result plus fallback metadata; CPU failure/cancel follows existing failed-session/published-WAV semantics and preserves both failure codes. Existing valid transcript/ready always wins and prevents retry.
  3. Implement tuple-scoped circuit breaker: rolling crash count/window per runtime+model+app version/device-capability class, exponential cooldown and suppression reason/time. Suppression is local, bounded, atomic/migratable and affects Auto only; CPU assets/settings untouched. Successful manual probe or verified changed tuple/app version clears appropriate scope; one GPU success does not erase unrelated evidence.
  4. Protect recovery: no retry during shutdown/recording/explicit CPU/cancel/lease conflict; crash-loop max is one CPU retry per logical processing revision. Corrupt/missing checkpoint/status fails safe to CPU/normal recovery, never recurring GPU. Logs/status use opaque attempt/reason/count only.
- Tests and rendered checks: checkpoint/exit attribution versus unrelated crash; duplicate parent/restart/race/lease; CPU override/once-only retry/CPU failure/cancel; already-published protection; breaker threshold/window/cooldown/version/tuple/manual probe/update invalidation/corrupt state; source/artifact no-loss. Render local status fallback/suppressed/retry/normal failure with accessible explanation.
- Documentation / installer / release work: document automatic CPU recovery/suppression/clear conditions and no model deletion. Runtime launch change requires focused worker/queue tests, full gate, installer and package smoke after implementation.
- Evidence and date: 2026-09-27 audit found worker and diarization CPU fallback patterns, but no transcription GPU launch checkpoint, crash attribution/retry, tuple breaker, or artifact-safe hard-exit evidence.
- Remaining gap or next action: add worker launch checkpoint and parent recovery classifier tests before changing process launch.

Goal: survive hard GPU failures that terminate the worker process.

Workstream 1 - Queue snapshot:

- Extend `WorkerLaunchConfigSnapshot` with transcription acceleration state,
  effective provider attempt, and local suppression state.

Workstream 2 - CPU retry:

- If the worker exits during GPU transcription, retry the same manifest once
  with temporary CPU-only transcription config.
- If CPU succeeds, publish normally and record CPU fallback after GPU worker
  failure.
- If CPU fails, preserve the existing failed-session behavior and published WAV
  recovery.

Workstream 3 - Suppression:

- After repeated GPU transcription crashes, set a local suppression flag.
- Keep future processing on CPU until a successful manual probe or app update
  invalidates the suppression.

Sprint 7 acceptance criteria:

- GPU crashes do not strand meetings that CPU can transcribe.
- Suppression prevents repeated crash loops without deleting CPU models.

## Sprint 8: Performance, Battery, And Queue UX

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: scheduler truth depends on verified probe, provider, crash, and suppression evidence from blocked Sprints 5–7).
- User outcome: acceleration helps eligible backlog only when device conditions allow; recording/interactivity/battery trust outrank a faster estimate.
- Scope / non-goals: define scheduler/ETA/status over S0–S7. Do not add hardware telemetry/upload, guarantee speed/battery gain, kill in-flight work on a policy switch, run concurrent GPU jobs, or override `CpuOnly`/user pause.
- Dependencies and decisions: existing background mode/recording/queue policy plus S5 readiness/S7 suppression are authority. Policy inputs are bounded local standard power state, app recording/queue state, user preference, current effective provider/timing and tuple health; unknown/unavailable power/thermal signal chooses conservative CPU in responsive mode. GPU decision happens at job boundary and is revalidated immediately before launch.
- Implementation slices:
  1. Build pure policy resolver: CPU-only/user pause/suppressed/not-ready/recording-responsive/battery-saver/low-power/active GPU/slow-or-failed tuple → CPU with exact code; otherwise bounded eligible backlog. Define foreground versus overnight semantics, max one GPU transcription, queue fairness/ASAP behavior and no starvation of CPU-needed jobs. Existing job continues safely; policy changes affect next dispatch.
  2. Record local aggregate performance buckets keyed by provider/effective tuple/model/profile/audio-duration band/background mode, with count, robust duration ratio, failure/fallback rate, timestamp/schema and bounded retention. Never store transcript/audio/path/device identifier. Mark tuple slow only against matched CPU baseline/minimum samples/confidence; probe timing alone cannot assert backlog benefit.
  3. Estimate ETA conservatively: use provider-specific history only for eligible effective provider and enough fresh samples; otherwise legacy CPU estimate/range or unavailable. Include queue stage/concurrency/known duration and show approximate/fallback truth; do not lower ETA merely because GPU was requested. Fallback/retry updates ETA once without flicker/negative time.
  4. Project compact status: `Processing on CPU`, `GPU acceleration`, `CPU fallback`, `GPU skipped for battery/recording/policy`, with reason/help only on explicit detail. One bounded log/receipt per provider decision/result; live status does not spam.
- Tests and rendered checks: policy truth table including unknown power/recording/pause/ASAP/concurrency/policy-change; estimator sample/age/outlier/fallback/confidence; ETA no-negative/no-double-count; redaction/retention; queue cancellation/retry. Render queue at normal/small width/125%, keyboard/status/screen reader for CPU/GPU/fallback/skipped with no misleading speed promise.
- Documentation / installer / release work: document Auto/CPU behavior, battery/recording safety, ETA approximation/local metrics and no telemetry. Runtime changes require full package gates; planning none.
- Evidence and date: 2026-09-27 audit found existing background/queue concepts but no GPU transcription policy, provider timing history, power truth model, confidence ETA or rendered queue evidence.
- Remaining gap or next action: add pure policy/estimator fixtures before touching dispatch or ETA.

Goal: make acceleration improve the backlog without making the app feel worse.

Workstream 1 - Runtime policy:

- Respect existing background processing modes.
- Skip GPU while live recording is active in responsive mode.
- Skip GPU on battery saver or after repeated slow probes.
- Honor `CpuOnly` override.

Workstream 2 - ETA:

- Track CPU and GPU transcription timing separately.
- Use provider-specific averages in queue ETA once enough observations exist.
- Fall back to the current transcription estimate when provider history is
  unavailable.

Workstream 3 - Logging:

- Log provider, elapsed time, fallback reason, and runtime status as bounded
  metadata only.

Sprint 8 acceptance criteria:

- GPU improves backlog drain without degrading live recording responsiveness or
  making queue ETA misleading.

## Sprint 9: Advanced UX

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: user-facing status cannot truthfully exist before blocked asset, probe, attempt, and policy contracts).
- User outcome: normal users never need think about GPU; advanced users can choose CPU, request a safe synthetic probe, and see exact effective result/fallback.
- Scope / non-goals: surface S2/S3/S5/S7/S8 truth in existing Advanced settings. Do not alter Setup/readiness completion, promise speed, offer drivers/models/manual path editing, expose device identifiers/paths/raw diagnostics, or force active work to switch provider.
- Dependencies and decisions: CPU is default and Settings preference applies next eligible job only. `Auto` means policy may select GPU, not enabled/guaranteed; `CpuOnly` wins all automatic/mannual launch selection except a Test probe that still obeys safety gates and never processes meeting content. Status derives one resolver from asset tuple + probe freshness + suppression + last final attempt—not independent labels.
- Implementation slices:
  1. Add minimal Advanced well: segmented/native control for Auto/CPU only, `Test GPU transcription`, current readiness, last effective provider/fallback and suppression/clear condition. Persist preference atomically/versioned; undo/close/apply timing matches Settings conventions, revalidation prevents stale save from clearing suppression.
  2. Define exact copy/state precedence: fresh verified probe/tuple `GPU transcription ready`; absent/incompatible/policy/suppressed/stale/unknown uses `CPU will be used` plus concise reason; last completed attempt uses `Last transcript used GPU` or `fell back to CPU` only with matching final receipt. Never label GPU ready after asset/update change or probe timeout, never confuse requested with actual.
  3. Test action schedules S5 probe; disabled/busy/rate-limited/recording/battery/shutdown states explain why and return focus/result once. Failure warning appears only user-requested test/transcript publish failure; background skip/fallback stays compact/nonmodal. Clear suppression requires verified documented condition, not a magic reset.
  4. Apply `DESIGN.md`: opaque nested well/1px edges/max 4px/no shadow, dense layout, Segoe interaction/Cascadia technical counts only. Publish automation names/help/disabled reasons, focus order/modal behavior, high contrast/long string/125% checks; status color/icon is supplementary.
- Tests and rendered checks: state resolver precedence/freshness/migration, preference apply/cancel/race, probe scheduling/gates, status receipts/redaction, AutomationProperties/focus/keyboard/live update. Render Auto/CPU/ready/stale/incompatible/suppressed/probing/fallback/error at 1280x800/125% and high contrast.
- Documentation / installer / release work: document optional Auto/CPU controls/probe/fallback and no extra user setup; UI/runtime changes require full package gates later.
- Evidence and date: 2026-09-27 audit found existing diarization GPU Advanced controls, but no transcription preference/probe/readiness status, status precedence, accessibility inventory, or Technical Studio render evidence.
- Remaining gap or next action: implement pure GPU status view model fixtures before XAML/config changes.

Goal: expose truth for power users without adding setup friction.

Workstream 1 - Controls:

- Keep Setup unchanged.
- Add Advanced controls for `Auto`, `CPU only`, and `Test GPU transcription`.
- Show GPU runtime/model readiness, last probe status, last provider used, last
  fallback reason, and local suppression state.

Workstream 2 - User-facing messages:

- Use calm status language:
  - `GPU transcription ready`
  - `CPU will be used on this machine`
  - `Last transcript used GPU`
  - `Last transcript fell back to CPU`
- Show warnings only when transcript publication fails or the user explicitly
  runs a failed GPU test.

Workstream 3 - Design:

- Follow `DESIGN.md` and the existing Settings surface.
- Keep status visible in the current viewport and avoid relying on hidden
  activity-log text as the only feedback.

Sprint 9 acceptance criteria:

- Normal users can ignore GPU entirely.
- Advanced users can prove exactly what happened.

## Sprint 10: Benchmark And Quality Harness

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires an approved candidate and verified provider outputs; Sprint 1 has neither).
- User outcome: packaged Auto is enabled only when local evidence shows real eligible-device gain with no protected transcript regressions.
- Scope / non-goals: create isolated metadata-only benchmark/quality gate. Do not benchmark user meetings, upload telemetry/fixtures/text, use benchmark labels at runtime, treat one machine as fleet proof, or change packaged default until reviewer-approved evidence.
- Dependencies and decisions: S0 protocol/S1 candidate/S4 normalized quality/S6 final arbitration/S8 policy are authority. Corpus has versioned opaque ids/hashes, consent/classification/owner/expiry, scenario tags and development/holdout splits; private audio/text/reference remain outside source control. Runtime receives no corpus labels/categories/expected output. CPU and actual-effective GPU runs use identical pinned input/options/model/profile/app/runtime/power mode.
- Implementation slices:
  1. Build runner with clean/repeatable environment, cold/warm repetitions, controlled concurrency, timeout/cancel, tuple/device-capability class and app/runtime/model hashes. Capture elapsed/throughput/memory where safely measurable, fallback/crash/quality codes, segment/timestamp/active-audio structural metrics and redacted reference-comparison aggregate; no text/audio/path/device id in reports.
  2. Define protected matrix: short/long/noisy/sparse/silence/English fallback, boundary timestamps, model/profile, CPU-only/no-GPU/blocked/fallback plus representative eligible hardware classes. Reference evaluation may run in restricted local harness and reports only error counts/ranges/confidence; missing/expired/disabled/failed cases make promotion insufficient, not pass.
  3. Set no-go/promote rules: hard reject schema/ready/output failure, empty active audio, material timestamp/order/quality regression, security/privacy breach, increased crash/fallback, or slower protected class. Speed requires practical improvement with sample/confidence and no protected regression; actual CPU fallback does not count as GPU benefit. Holdout runs once after candidate freeze; manual review records decision, rollout/rollback and known limitations.
  4. Separate reports/retention: baseline/candidate/holdout comparison keyed by opaque fixture and tuple; allow metadata-only local artifacts under `.artifacts`, redact console, enforce size/retention/consent cleanup. No automatic config/default mutation; report recommendation only.
- Tests and rendered checks: manifest/split/consent/schema validation, deterministic run identity, CPU/GPU/fallback classification, metric/tolerance/no-go/promote math, missing coverage/holdout contamination rejection, sentinel redaction, report retention. Render no product UI; Advanced status consumes only approved promotion state later.
- Documentation / installer / release work: document corpus governance, command, metrics/no-go, reviewer/rollback and evidence retention in dependency tracker/architecture after harness. No package change now.
- Evidence and date: 2026-09-27 audit found no GPU transcription corpus, benchmark runner, protected-quality comparator, promotion evidence or Auto-release gate.
- Remaining gap or next action: define corpus manifest and synthetic structural fixtures before acquiring candidate runtime.

Goal: prove GPU helps before broad automatic use.

Workstream 1 - Benchmark runner:

- Add a metadata-only benchmark script under `scripts`.
- Compare CPU and GPU on safe fixture audio: short, long, noisy, sparse, and
  English-fallback cases.
- Report elapsed time, segment count, character count, active-audio ratio,
  sparse retry result, provider, fallback reason, and memory peak when
  available.

Workstream 2 - Promotion rules:

- Do not require exact text equality across providers.
- Block automatic GPU use if GPU produces empty active-audio transcripts, major
  timestamp drift, worse sparse behavior, broken JSON/schema output, or slower
  representative runs than CPU.
- Require benchmark evidence before enabling GPU `Auto` in packaged builds.

Workstream 3 - Privacy:

- Keep fixture reports metadata-only.
- Do not print transcript text, raw audio snippets, private paths in published
  artifacts, prompts, keys, auth headers, or profile payloads.

Sprint 10 acceptance criteria:

- GPU automatic use is evidence-backed and quality-gated.

## Sprint 11: Packaging And Release Validation

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: no selected licensed tuple or delivery policy exists; packaging would be speculative).
- User outcome: installer/update either contains a verified optional GPU tuple or safely runs CPU; packaging cannot silently corrupt normal product.
- Scope / non-goals: package selected S1 tuple and prove install/update paths. Do not assume binary checkout/LFS strategy, publish assets, require GPU hardware on every tester, weaken CPU packaging, or conflate diarization DirectML files with transcription runtime.
- Dependencies and decisions: S1 license/provenance/S2 tuple manifest/S10 promotion and current V2 stable-apphost/bundle-integrity contracts are authority. Resolve delivery strategy after size/license/security evidence: bundled, separately signed/verified release asset, or no ship. Every route needs SBOM/license/source/hash/signature/architecture/version compatibility; no dynamic unverified runtime/model acquisition.
- Implementation slices:
  1. Extend publish/release manifest with distinct transcription tuple ownership, file list/length/hash/path/license and channel/version metadata; validate no traversal/duplicate/conflict with CPU/Sherpa assets. Preserve stable apphosts/loose DLLs/bundle layout/integrity and CPU-only bundle when tuple absent; package staging is atomic and rejects stale/partial tuple.
  2. Add install/update transition matrix: clean CPU-only, clean tuple, absent/corrupt/wrong-version/wrong-architecture tuple, update add/remove/replace tuple, downgrade/rollback and interrupted extraction. CPU transcription must start/publish in every GPU failure; suppressed/preference/cache state migrates safely and invalidates changed tuples.
  3. Build tests/smokes: source/package manifest/size/SBOM/LFS-or-delivery policy, integrity hashes, deployment extraction/update rejection, CPU-only override, installed Advanced readiness/fallback. On eligible lab hardware run installed synthetic actual-GPU/CPU comparison; where unavailable record hardware gap and prove installed CPU/asset rejection only—never claim GPU acceleration from absence of test hardware.
  4. Gate release from clean committed source: focused/provider/config/schema/queue/deployment suites, Test-All, conditional AppPlatform test, installer build, package smoke plus GPU-specific installed matrix. Capture command/commit/toolchain/tuple hashes/redacted logs; packaging failure leaves no release claim. Push/upload remains separately authorized.
- Tests and rendered checks: package layout/integrity/missing/corrupt/stale tuple, V2 apphost/update/rollback extraction, CPU startup/output fallback, installed UI status/redaction, eligible-device actual proof. Test docs/matrix at normal/unsupported device states.
- Documentation / installer / release work: update release/install/architecture/dependency tracker only with chosen delivery/requirements/rollback; behavior change requires installer build and smoke, none for planning.
- Evidence and date: 2026-09-27 audit found DirectML diarization package assertions and V2 validation, but no transcription tuple supply-chain/manifest/package/update/installed-GPU matrix or CPU compatibility proof.
- Remaining gap or next action: select delivery policy after S1/S10 evidence, then add synthetic tuple package tests before scripts.

Goal: prove the installed path, not only development builds.

Workstream 1 - Packaging:

- Update `Publish-Portable.ps1`, `Build-Installer.ps1`, `Build-Release.ps1`,
  and bundle integrity validation for GPU transcription runtime and optional
  model assets.
- Add manifest hash validation and file-size checks.
- Add Git LFS rules for any checked-in binary runtime assets.

Workstream 2 - Tests:

- Add tests for required runtime files, model manifests, hashes, missing GPU
  asset fallback, package-size guardrails, and CPU-only override.
- Update deployment tests that currently assert no DirectML redist so the
  transcription runtime is intentional and separate from Sherpa diarization.

Workstream 3 - Release gates:

- Run focused provider/config/schema/queue tests.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Run `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj
  -p:NuGetAudit=false` if deployment or update contracts change.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.
- Run `powershell -ExecutionPolicy Bypass -File
  .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Sprint 11 acceptance criteria:

- Release artifacts prove CPU compatibility and GPU acceleration from the
  packaged app.
- Missing or stale GPU assets are caught before publishing.

## Sprint 12: Update, Rollback, And Field Suppression

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: rollback semantics depend on the blocked tuple, packaging, and crash-suppression contracts).
- User outcome: any bad GPU rollout is neutralized locally and immediately by CPU behavior, without reinstalling or risking existing models/transcripts.
- Scope / non-goals: define durable local override/suppression/update/rollback safety after S7/S11. Do not add remote telemetry/kill switch, let support edit artifacts, delete CPU models, classify optional tuple asset as app update, or bypass update integrity policy.
- Dependencies and decisions: precedence is explicit: release-disabled/incompatible tuple → local safety kill switch → user CpuOnly → S7 tuple suppression → S5/S8 policy → Auto eligible GPU. Higher safety state wins; UI records which one and clear conditions. Kill switch is versioned local config/policy record with reason/source/time/expiry/review token, atomic write/backup/migration and no transcript/manifest mutation; only explicit local user/support-approved action can set/clear it.
- Implementation slices:
  1. Implement policy persistence/migration: preserve user CPU preference across all updates; preserve suppression only for same tuple/app compatibility scope; preserve local kill switch until explicit clear/expiry policy. Corrupt/unknown/future policy fails safe to CPU with recovery/export-free reset path; never silently defaults to Auto.
  2. Define rollback transaction: before tuple replace/remove retain last known-good CPU/tup state and package receipt; interrupted update/repair/cleanup validates apphosts/manifests and chooses CPU before provider load. Tuple cleanup is app-owned/ref-counted and cannot affect CPU models, user profiles, meeting work, snapshots or published artifacts.
  3. Add support flow: documented local diagnostics/policy instruction, confirmation/consequence, action receipt, status/focus refresh and test probe behavior. Kill switch blocks automatic GPU and manual GPU probe unless a clearly documented controlled verification path is authorized; it never blocks transcription CPU/retry/publish.
  4. Enforce update asset fence: only versioned `MeetingRecorder-v<version>-win-x64.zip` remains app update candidate; model/runtime/MSI/bootstrap/missing/corrupt/size-mismatch packages reject before shutdown. GPU optional asset update strategy must route through S11 verified manifest transaction, not app update selector.
- Tests and rendered checks: precedence/migration/corrupt/future policy, update/downgrade/rollback/interruption, tuple cleanup CPU/source/artifact protection, kill set/clear/expiry/probe block, update asset rejection/host safety. Render CPU enforced/suppressed/user CPU/release-disabled/recovery with keyboard/accessibility/no raw diagnostic.
- Documentation / installer / release work: document CPU recovery, local safety policy/clear semantics, no data deletion, update asset rules and support evidence. Implementation requires deployment/AppPlatform tests, installer/package smoke later.
- Evidence and date: 2026-09-27 audit found robust update-asset/V2 apphost policy and diarization settings, but no transcription CPU safety precedence, local kill switch, tuple rollback cleanup, or field-recovery proof.
- Remaining gap or next action: implement pure acceleration-policy precedence/migration tests before config/update wiring.

Goal: make a bad GPU rollout recoverable.

Workstream 1 - Config preservation:

- Preserve user `CpuOnly` override across updates.
- Preserve local GPU suppression unless a new app build invalidates the
  suppressed runtime decision.
- Never delete CPU models during GPU asset install, update, repair, or cleanup.

Workstream 2 - Kill switch:

- Add a local config kill switch support can set without editing transcripts,
  manifests, or model files.
- Ensure the app can return to CPU-only behavior after a bad GPU package without
  reinstalling.

Workstream 3 - Update safety:

- Do not make GPU assets required for in-app updates.
- Do not select GPU model packages as app update ZIPs.
- Keep release upload and update selection rules aligned with existing
  app-update asset contracts.

Sprint 12 acceptance criteria:

- A flawed GPU release can be neutralized by config or update without breaking
  transcription.

## Sprint 13: Documentation And Support Readiness

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: verified user/support guidance depends on the blocked candidate, benchmark, and packaging outcomes).
- User outcome: people and support can understand CPU/GPU outcome, recover safely, and never need hidden driver/Python/model work.
- Scope / non-goals: document only verified S1–S12 behavior at ship time. Do not advertise unsupported hardware/speed, reveal private diagnostics, instruct driver/manual runtime/model conversion, change CPU fallback, or turn docs into a release approval substitute.
- Dependencies and decisions: S1 selection/S10 promotion/S11 package evidence/S12 safety policy are required before final wording. Docs distinguish `available`, `requested`, `attempted`, `effective`, `fallback`, `suppressed`, and `CPU only`; no status means a performance claim. Auto default is conditional on approved S10/S11 evidence—until then CPU-safe default remains authoritative.
- Implementation slices:
  1. Update user docs: CPU baseline, optional acceleration, Setup independence, Advanced preference/probe, truthful state meanings, recording/battery policy, expected CPU fallback, no cloud/no manual driver/Python/CUDA/runtime/model conversion, and accessibility route. Preserve legal/consent scope without adding claims.
  2. Update architecture/release/dependency tracker: chosen tuple/license/source/hash/delivery, factory/probe/fallback/breaker/update policy, bundle size, test/installed hardware matrix, version/rollback, deferred alternatives and exact validation command. Cross-link evidence receipts, not local paths/transcripts.
  3. Publish support playbook: safe collectable version/status/reason/tuple code/redacted logs, check CPU output first, user CpuOnly/local safety policy/suppression conditions, asset update/repair rules, when to stop/revert/escalate. Never direct destructive cleanup or arbitrary driver/process manipulation; support cannot clear safety state without documented user-authorized local action.
  4. Add docs accuracy gate to release matrix: each visible copy/diagnostic/recovery link maps to current config/status code/test, no stale Auto default/feature availability claim, no secret/private path/asset URLs. Review accessible copy and localized-length/Advanced state screenshots.
- Tests and rendered checks: documentation/source status vocabulary assertions, release-matrix link check, safe diagnostics redaction, troubleshooting state fixture, Advanced high-contrast/keyboard/screen reader documentation journey. No installer build for planning; shipped behavior requires S11 gates.
- Documentation / installer / release work: README/SETUP/ARCHITECTURE/RELEASING/dependency tracker/support doc update when evidence exists. This record assigns release docs; it does not claim candidate shipping.
- Evidence and date: 2026-09-27 audit found no transcription GPU behavior/docs or support playbook; existing diarization GPU docs cannot substantiate this feature. Plan interface said Auto default before promotion evidence, so this record makes it conditional.
- Remaining gap or next action: add release-doc evidence matrix and support state fixtures after S1/S10/S11 result.

Goal: make the feature maintainable after release.

Workstream 1 - User docs:

- Update `README.md` and `SETUP.md` to state that CPU transcription is the
  baseline and GPU acceleration is opportunistic.
- Document that Setup does not require GPU and Advanced shows provider truth.

Workstream 2 - Architecture and release docs:

- Update `ARCHITECTURE.md`, `RELEASING.md`, and
  `docs/dependency-api-tracker.md` with backend choice, model format,
  package-size policy, runtime source, validation commands, and fallback
  behavior.

Workstream 3 - Troubleshooting:

- Add entries for GPU unavailable, GPU slower than CPU, repeated GPU crash
  suppression, CPU-only override, and missing optional GPU assets.

Sprint 13 acceptance criteria:

- Docs match the shipped behavior and support can explain outcomes without
  reading source.

## GPU Transcription Interfaces And Constraints

- CPU transcription remains permanent.
- `Auto` may become the default preference only after approved S10/S11 evidence;
  GPU is attempted only after readiness, quality, and performance gates pass.
- `Standard` and `Higher Accuracy` remain the user-facing transcription model
  choices.
- GPU controls live in Advanced.
- GPU assets are optional for transcription readiness.
- Published transcript JSON changes are additive and backward-compatible.
- `.ready` semantics do not change.
- No user-managed Python, CUDA, driver setup, model conversion, or manual
  runtime install is required.
- If Sprint 1 cannot prove a clean packaged backend and model path, stop the
  track before implementation.

# Adaptive Backlog Drain Acceleration Plan

## Summary

Goal: make overnight drain and spare-capacity drain accelerate the real backlog
instead of only switching to transcript-only processing. Drain order is fixed:
transcripts first, then speaker labeling, then summaries. Acceleration is
conservative by default: plugged in, not recording, sustained idle capacity, and
fast backoff when the user starts using the machine.

Current gap: `OvernightDrain` maps to `TranscriptOnlyDrain` with a fixed worker
count. That can publish transcripts quickly, but it does not drain diarization
or summary backlog after transcripts are done.

## Sprint 0: Current Behavior Audit And Safety Baseline

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: background/overnight policies, migration tests, queue status and stage config exist); `Ready` (2026-09-27 pressure test); `Done` (2026-09-27 policy/migration/window fixtures and redacted behavior ledger).
- User outcome: later backlog acceleration fixes real stalled work without changing recording safety, priority, or already-correct stage behavior by accident.
- Scope / non-goals: document/characterize current policy and create fixtures only. Do not change scheduling, worker count, profile migration, priority, stage execution, UI copy, or introduce capacity probing.
- Dependencies and decisions: `BackgroundProcessingPolicy`, `AppConfigStore`, `ProcessingQueueService`, manifest stage statuses, `SessionProcessor`, MainWindow recording/queue state and tests are authority. Current source already maps legacy `OvernightDrain` to transcript-first in a migration path while configured stages exist; audit must distinguish historical bug compatibility from current default, not assert stale premise. “Active recording” derives only app lifecycle/capture/queue state; safe acceleration never uses broad OS process inspection.
- Implementation slices:
  1. Create behavior ledger for every config/migration/profile/mode/window/DST boundary: effective strategy, worker/budget/priority, stage eligibility, pause/resume/ASAP, live-recording restriction, and persisted user override. Record source/test/observable status plus legacy versus new install state.
  2. Build synthetic manifest corpus for transcript missing/current, diarization/summary missing/succeeded/failed/skipped, publishing/retry/cancel, stale/malformed and mixed backlog. Fixture asserts exact current next action/queue/status without private artifacts.
  3. Characterize overnight limitation/revision truth: test a legacy migrated configuration, current configured-stage setting, window start/end/cross-midnight/DST/clock-invalid conditions and worker restart. If transcript-only behavior is intentional legacy compatibility, state it; if it strands later enrichment, capture reproducible contract/provenance before remedial design.
  4. Define audit metrics/no-regression boundary: backlog counts by stage, queued/active lease, stage completion/failure, user-visible ETA/status, capture responsiveness. Logs are metadata-only; no production flag/default is changed in S0.
- Tests and rendered checks: focused policy/config/migration/queue/processor fixtures; time/DST/future config/recording/ASAP/cancel/restart matrix. Capture existing queue/header state with synthetic data at normal/small view only as baseline; no UI change.
- Documentation / installer / release work: add internal audit evidence/known compatibility behavior; no installer work for audit-only change.
- Evidence and date: 2026-09-27: `docs/adaptive-backlog-drain-baseline.md` records the configuration migration, compatibility path, worker and capture safety boundary, executable synthetic stage corpus, restart evidence, and the local-clock/DST limitation. `BackgroundProcessingPolicyTests` and `AppConfigStoreTests` reproduce the legacy/current overnight behavior without changing production code.
- Remaining gap or next action: complete Sprint 1's durable stage-work/barrier model before altering scheduler behavior; this audit does not supply a DST-safe resolver or stage queue.

Goal: prove the existing queue, profile, and worker contracts before changing
scheduling.

- Document current `ProcessingSpeedProfile`, `BackgroundProcessingPolicy`,
  worker launch, manifest status, and summary/speaker-label skip behavior.
- Capture backlog examples for: transcript missing, transcript done but
  diarization missing, diarization done but summary missing, and failed/skipped
  optional stages.
- Define "active recording" and "safe to accelerate" from existing app state
  rather than process inspection.
- Add focused tests that lock the current overnight bug: overnight currently
  becomes transcript-only and does not advance later enrichment stages.

Sprint 0 acceptance criteria:

- The implementer can reproduce the current overnight drain limitation from a
  focused test or fixture.
- No production behavior changes ship in this sprint.

## Sprint 1: Staged Drain Work Model

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-27 isolated staged-work contract, persistence, and resolver tests).
- User outcome: backlog makes visible progress by safely finishing earliest missing stage before spending capacity on later enrichment.
- Scope / non-goals: add durable stage work model/ordering after S0. Do not change stage implementation yet, preempt active worker, force summary consent/provider setup, delete existing full-pass path, or treat failed/blocked items as completed.
- Dependencies and decisions: S0 stage ledger and manifest/publish revision statuses are authority. Work item is opaque id + manifest/session path token + immutable manifest/input/output revision + requested stage + origin/priority/user intent + attempt/lease/created time. Stages are `Transcript`, `Diarization`, `Summary`, `FullPass`; stage selection proves prerequisite/currentness/eligibility at dispatch, not path/name alone.
- Implementation slices:
  1. Define state/transition model: pending/leased/running/succeeded/skipped/blocked/retryable/failed/cancelled/superseded per stage, receipt/reason/retry time and currentness fingerprint. Migration maps legacy queued/full items to `FullPass` without changing behavior; unknown/future work fails safely and does not drop user work.
  2. Define barrier precisely: choose earliest stage with one or more *eligible, user-intended, current* missing jobs; blocked/failed/cancelled/consent-disabled/stale items remain visible and do not deadlock all later work, but are excluded only with reason/count. Transcript barrier before diarization before summary applies to new staged dispatch; it does not rewrite manual FullPass/explicit action priority and never silently skips a prerequisite.
  3. Coalesce/conflict rules: one active lease per session+stage+revision; FullPass and stage work share dedupe/freshness so cannot run concurrently/duplicate publication. New transcript revision supersedes pending later stages and preserves outcome/audit; manual retry/Rush intent is immutable snapshot. Round-robin/fairness within current barrier prevents one problematic item from starvation.
  4. Define summary boundary: summary work is eligible only after transcript current, configured provider/consent/mode and existing summary policy; no automatic hosted consent. Diarization optional failure/skip produces explicit downstream eligibility per S0 product contract, not fake completion.
  5. Project queue truth: total/current-barrier/stage counts, blocked/failed reasons, active stage and deferred later counts; Rush remains explicitly transcript-first and active workers finish normally.
- Tests and rendered checks: legacy migration/future/corrupt item, barrier/eligible-versus-blocked/failed, lease/race/coalesce/full-pass conflict, revision supersession, fairness/priority/Rush/cancel/restart, summary-consent/optional diarization. Render stage count/barrier/blocked status and keyboard semantics later; no scheduling behavior changes in model slice.
- Documentation / installer / release work: document staged versus full-pass/Rush behavior after implementation; worker/queue schema change needs package/release gates later.
- Evidence and date: 2026-09-27: `StagedBacklogWorkModel` and its local store define versioned state, safe future/corrupt handling, barrier, full-pass compatibility, conflict, revision, and summary-boundary contracts. `StagedBacklogWorkModelTests` passes 10 focused synthetic cases; `docs/adaptive-backlog-drain-work-model.md` records the contract. The live queue remains deliberately unchanged.
- Remaining gap or next action: Sprint 2 may add validated worker stage passes and atomic artifact handling, then a later queue-adoption slice can bind this model to live dispatch.

Goal: make backlog work explicit enough to drain one stage at a time.

- Introduce a queue work item shape with `manifest path + requested stage`.
- Supported requested stages:
  - `transcript`,
  - `diarization`,
  - `summary`,
  - existing full-pass behavior when no stage is specified.
- Stage order:
  1. Start transcript work while any eligible meetings lack published
     transcripts.
  2. Start diarization work only when transcript backlog is empty.
  3. Start summary work only when transcript and diarization backlog are empty.
- Keep `Rush Backlog` as transcript-first; do not turn it into full enrichment.
- Do not kill or preempt active workers when the focused stage changes; only
  affect newly launched work.

Sprint 1 acceptance criteria:

- Queue tests prove transcript backlog is exhausted before diarization starts.
- Queue tests prove diarization backlog is exhausted before summaries start.
- Existing full-pass processing remains available for normal/manual paths.

## Sprint 2: Stage-Specific Worker Passes

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-28 stage worker contract and artifact transactions).
- User outcome: worker can add only missing work to a meeting without wasting time or damaging already-published transcript.
- Scope / non-goals: implement S1 stage dispatch/execution for transcript, diarization, summary. Do not change full-pass semantics, bypass summary consent, synthesize missing inputs, rerun prior stages for convenience, or expose incomplete artifacts as current.
- Dependencies and decisions: S1 immutable work/lease/revision barrier and existing processor/publish contracts are authority. Worker receives validated `--stage` plus work id/revision/lease token; rejects missing/unknown/conflicting flags. Stage preconditions are current artifact fingerprints and allowed input identities, rechecked immediately before write. No stage invokes an earlier provider; unavailable prerequisite returns blocked/recovery receipt.
- Implementation slices:
  1. Split orchestration from stage implementations behind shared transaction/provenance helpers. Transcript uses current prepare/transcribe/publish path and marks later enrichment pending without provider calls. Diarization needs exact current transcript + allowed audio/prepared input; Summary needs exact current structured transcript + enabled/consented provider. Each run has a stage attempt receipt/status/progress/cancel.
  2. Make enrichment atomic/current: write new JSON/Markdown/metadata to staging, validate schema/fingerprint, promote matching revision together; preserve previous readable artifacts on fail/cancel/race. Transcript `.ready` contract stays stable: enrichment cannot create duplicate completion signal or expose half-updated sidecars. Existing consumer sees a coherent prior or new revision.
  3. Propagate freshness: diarization label update changes attribution/summary freshness according to speaker roadmap; new transcript supersedes labels/summary. Summary stage never reruns diarization/transcription and respects stale/current/provider privacy boundaries. Optional diarization failure/skip is a stage result, not transcript failure.
  4. Preserve recovery: stage retries idempotent by work/input/output revision; worker crash/cancel/no-audio/asset/model/provider/storage outcomes leave lease/resume truth. FullPass shares same stage code/transaction but retains existing order/results.
- Tests and rendered checks: CLI validation; provider spies prove no-rerun; missing/stale/corrupt prerequisite; atomic JSON/Markdown/ready failures/races; optional label and summary consent/failure; freshness/supersession/retry/cancel/crash/full-pass parity. Render queue/detail stage status on synthetic fixture later; no visual redesign.
- Documentation / installer / release work: document stages/recovery/currentness after implemented; worker argument/output behavior requires full tests/package gates later.
- Evidence and date: 2026-09-28: `SessionProcessingStage` validates the worker contract; `SessionProcessor` runs transcript-only, label-only, or summary-only passes from current published artifacts. `StageArtifactFingerprint` guards enrichment currentness, and `FilePublishService.PublishEnrichmentAsync` preserves/rolls back sidecars while retaining the existing ready marker. Focused provider-spy/parser/fingerprint/publish coverage passes 43 cases; `Test-All.ps1` passes 1,475 core and 8 integration tests; the ZIP and MSI installer assets rebuilt. Packaged startup smoke is not run because the user-owned installed app remains active.
- Remaining gap or next action: Sprint 3 may bind the S1 barrier model to a timezone-safe overnight policy. A later queue-adoption slice must supply durable work id/revision/lease arguments to the worker rather than relying only on its local pre-write fingerprint.

Goal: let one worker run exactly the stage requested without redoing earlier
work.

- Add worker stage argument:
  - `--stage transcript`,
  - `--stage diarization`,
  - `--stage summary`.
- Transcript stage:
  - merge/prep audio as today,
  - run transcription,
  - publish audio, transcript JSON, and transcript Markdown,
  - skip speaker labels and summaries.
- Diarization stage:
  - require existing transcript artifacts plus prepared or merged audio,
  - run speaker labeling only,
  - republish JSON/Markdown with labels,
  - record skipped/unsupported/timeout state without failing transcript output.
- Summary stage:
  - require existing transcript JSON,
  - run summary only when summaries are enabled and configured,
  - republish JSON/Markdown with summary.
- Keep output schemas backward-compatible and keep enrichment metadata optional.

Sprint 2 acceptance criteria:

- Transcript-stage tests prove no diarization or summary provider is invoked.
- Diarization-stage tests prove transcription is not rerun.
- Summary-stage tests prove transcription and diarization are not rerun.

## Sprint 2A: Staged Queue Adoption And Lease Wiring

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-28 queue-adoption dependency discovered while closing Sprint 2); `Done` (2026-09-28).
- User outcome: the real local queue drains the earliest eligible missing stage, invokes the worker with an immutable stage request, and recovers safely after restart without duplicating or losing meeting work.
- Scope / non-goals: bind the S1 durable staged-work model and S2 worker contract to `ProcessingQueueService`. Preserve legacy full-pass/manual flows and existing user queue entries. Do not introduce overnight or idle-capacity acceleration, change the current one-worker policy, preempt a running worker, bypass summary consent, or redesign queue UI.
- Dependencies and decisions: S1's work-store/barrier/lease/currentness model and S2's `--stage`/artifact transaction contract are authority. Queue receipts must carry durable work id, work revision, and lease token to the worker; a local artifact fingerprint is an additional race fence, never the durable queue identity. Unknown/future/corrupt persisted work remains recoverable and cannot be silently discarded.
- Implementation slices:
  1. Give the app queue a versioned staged-work store location and load/migration boundary. Convert only compatible legacy queued items to immutable `FullPass` work, retain their order/intent, and preserve unknown/corrupt records for recovery rather than replacing the user's queue.
  2. Project existing enqueue, ASAP, retry, cancel, pause/resume, completion, and restart paths through staged work. Use the S1 resolver for the current eligible barrier, fairness, duplicate/full-pass conflict, revision supersession, blocked/retryable visibility, and one active lease per session/stage/revision.
  3. Launch S2 workers with validated `--stage`, work id, immutable revision, and lease token; update attempts/leases/receipts only when the completing worker still owns that lease. Crash, cancellation, stale input, malformed receipt, or a worker restart must retain explicit retry/recovery truth and must not publish a second active work item.
  4. Keep current user-visible queue contracts accurate while exposing compact staged counts/reasons internally for S3's later Settings/status projection. Existing explicit FullPass/manual actions retain their priority and are not silently rewritten as enrichment work.
- Tests and rendered checks: store migration/future/corrupt preservation; enqueue and barrier order; fairness/ASAP/full-pass conflict; lease ownership/race/restart/cancel/retry; worker command argument and receipt validation; consent/asset/currentness blocks; legacy queue compatibility. Rendered status work is deferred to S3/S6; verify current queue text does not claim completed enrichment until its receipt succeeds.
- Documentation / installer / release work: update staged-work documentation with the live queue/worker lease boundary. This app/worker runtime change requires focused tests, full tests, installer rebuild, and packaged startup smoke when no user-owned app is active.
- Evidence and date: 2026-09-28: implemented durable `staged-backlog.json` loading/recovery, barrier-aware transcript → diarization → summary dispatch, opaque-id/revision/lease worker arguments, receipt ownership fencing, malformed-receipt retry, and legacy full-pass fallback. 69 focused queue/stage/model tests and 8 integration tests pass. Full validation built every project and the installer ZIP/MSI with PowerShell 7; one catalog dry-run fixture cannot run under the host's Windows PowerShell because `Get-FileHash` is unavailable, and six unrelated portable-install tests currently fail at a pre-existing move conflict. Packaged smoke remains deferred because the user-owned installed app (PID 22188) is active.
- Remaining gap or next action: Sprint 3 can now apply its pure window policy to the live staged queue.

Goal: make the staged model drive the real queue before policy accelerates it.

- Persist staged work beside the existing local queue state.
- Dispatch the resolver's earliest eligible barrier stage to the worker with its
  immutable work id, revision, and lease token.
- Preserve legacy full-pass/manual work and recover safely after interruption.
- Keep one active lease per stage revision and never lose corrupt/future work.

Sprint 2A acceptance criteria:

- Queue tests prove transcripts drain before labels, and labels before summaries.
- Worker-launch tests prove each staged invocation receives its stage, work id,
  revision, and lease token.
- Restart/race tests prove stale completion cannot overwrite a newer lease or
  create duplicate active work.
- Existing full-pass/manual queue behavior remains available.

## Sprint 3: Overnight Acceleration Semantics

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-28 until Sprint 2A makes staged dispatch live); `Ready` (2026-09-28 after Sprint 2A completion); `Partial` (2026-09-28 verification blocked by host apphost deletion); `Done` (2026-09-28 hash-verified package fallback, legacy MSI recovery, integrity and installed-startup smoke).
- User outcome: Overnight acceleration finishes missing transcripts, then eligible labels, then eligible summaries—without surprise background work or lost recording responsiveness.
- Scope / non-goals: replace legacy overnight transcript-first selection with S1 staged dispatch policy/copy. Do not force all enrichment, change manual emergency transcript-only mode, preempt active worker at boundary, alter user consent, or assume clock/power state is always valid.
- Dependencies and decisions: S0 baseline/S1 barriers/S2 stage passes/S2A live staged dispatch/current background policy are authority. Local configured window is evaluated timezone/DST-safe with explicit invalid/ambiguous/missing-time fallback to normal conservative profile. Caps are per current barrier stage, not simultaneous transcript+diarization+summary concurrency; current worker finishes on window/recording/config transition, next launch re-evaluates.
- Implementation slices:
  1. Define effective policy resolver for legacy/new config/migration: `Overnight acceleration` only inside valid window; outside retains prior mode/budgets. Preserve `TranscriptOnlyDrain` as explicit emergency strategy and migrate legacy values/copy once without overwriting user choice. Record effective policy/window version/reason in local queue snapshot.
  2. Map staged caps from measured S0 hardware/worker evidence (initial conservative transcript ≤3, diarization ≤1, summary ≤1 subject to user/CPU policy) with no shared-resource oversubscription. Live recording/responsive pause/CPU or storage safety gate blocks new accelerated launch regardless of window; Fastest/manual priority rules remain explicit.
  3. Enforce barrier/consent: transcript eligible barrier first; then diarization only eligible prerequisites/assets; summary only existing provider consent/mode. Blocked/failed/stale later work shows count/recovery, does not falsely call complete or auto-enable hosted summaries. At window end, active run completes/cancels only existing safety path; no forced kill.
  4. Update Settings/queue copy from resolver: “Overnight acceleration,” current focus (`Transcripts`, `Speaker labels`, `Summaries`), window/blocked/fallback reason and deferred count. Copy never says enrichment will run where consent/assets/policy prevent it; preserve keyboard/accessibility.
- Tests and rendered checks: legacy/new config migration, windows/cross-midnight/DST/invalid clock, transition with active worker/recording/pause, stage barrier/caps/fairness/manual emergency, consent/asset blocks, settings/queue resolver copy. Render window active/outside/recording-blocked/transcript/label/summary/blocked at 125% and high contrast.
- Documentation / installer / release work: update Settings/help and docs with staged/overnight/manual-emergency/safety/consent behavior after implementation; scheduler/UI change requires full package gates.
- Evidence and date: 2026-09-27 audit found legacy profile logic, configurable window and migrated strategy, but no staged cap/window resolver, DST/transition proof, consent-safe later-stage policy, or updated UI wording. 2026-09-28 implemented a pure timezone-safe resolver (invalid/empty/outside/DST-ambiguous time is conservative), transcript ≤3 / labels ≤1 / summaries ≤1 staged caps, recording gate for new accelerated work, legacy migration, and queue/settings status copy. 225 policy/config/UI tests plus direct cap, recording-gate, and staged-order queue tests pass; 72 installer/package-source checks pass; integration passes 8. Generated publish apphosts continue to disappear from every tested staging root, but installed WPF/CLI/worker apphosts are byte-identical to current intermediates and survive copying. Explicit hash-verified fallback restores those three files and `Build-Installer.ps1` passes with zero warnings/errors. A legacy MSI registration was backed up and removed with its explicit known install root, then `AppPlatform.Deployment.Cli install-bundle` restored the verified bundle. The full release smoke then passed portable startup, clean MSI install, all required installed-file hashes, and installed-app startup. Full validation separately retains six executable-fixture retention failures from host apphost deletion and one Windows PowerShell diarization-fixture hash failure; neither exercises Sprint 3 behavior.
- Next sequential sprint: Sprint 4 — Idle CPU Capacity Acceleration.

Goal: make overnight mode accelerate the staged drain queue, not force
transcript-only forever.

- Change `OvernightDrain` label/help text to "Overnight acceleration."
- During the configured overnight window, use staged acceleration worker caps:
  - up to 3 transcript workers,
  - up to 1 diarization worker,
  - up to 1 summary worker.
- Outside the window, return to the prior normal processing profile.
- Keep `TranscriptOnlyDrain` as a separate manual emergency mode.
- Show queue status with active focus: `Transcripts`, `Speaker labels`, or
  `Summaries`.

Sprint 3 acceptance criteria:

- Overnight tests prove transcripts drain first, then speaker labels, then
  summaries.
- UI tests prove labels no longer describe overnight as transcript-only.
- Live recording still blocks new accelerated work.

## Sprint 4: Idle CPU Capacity Acceleration

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-28 CPU hysteresis, native local sampler, conservative staged-dispatch integration, and status projection implemented; dedicated capacity dispatch coverage and package gates remain); `Done` (2026-09-28 focused queue/UI/package verification).
- User outcome: idle plugged-in machines can finish backlog sooner while recording, battery use, and foreground responsiveness stay protected.
- Scope / non-goals: add bounded in-app CPU capacity policy after S0–S3. Do not create service/process monitoring, inspect other process trees, alter active worker priority, use capacity when unplugged/recording/unknown, or promise exact system utilization.
- Dependencies and decisions: staged barrier/window policy/background modes are authority. `GetSystemTimes` gives system-wide CPU delta, not app CPU; sampler lives in app lifetime, is advisory and uses no external telemetry. Unknown/error/clock regression/power unknown = conservative no acceleration. Overnight and manual policy precedence is explicit; capacity can only lower/add bounded next-launch capacity within current stage barrier.
- Implementation slices:
  1. Implement pure sample/state resolver: monotonic timestamp/idle/kernel/user deltas, validity/outlier checks, rolling three-sample idle entry (<55% initially) and two-sample high-use exit (>70% initially), hysteresis/debounce/cooldown. Thresholds are named/testable/tunable only with evidence; no divide-by-zero/negative/wrap misleading idle.
  2. Gate by standard power/recording/app policy: plugged-in verified, no capture/stop transition, responsive permission, no shutdown/update/queue conflict. Sample approximately 30s only while backlog needs decision; stop/dispose cleanly with app. Manual/overnight/emergency states win documented precedence.
  3. Map state to non-preemptive stage caps: current barrier transcript ≤2, label ≤1, summary ≤1 within existing global budgets/leases. High use or gate loss prevents *new* accelerated worker; active runs finish normal path. Fairness/ASAP/full-pass and CPU resource estimates remain truthful.
  4. Persist only bounded status/count/timestamps/reason for diagnostics; no user activity/process/device data. UI reports “idle capacity available” or CPU-safe fallback, no live sampling noise.
- Tests and rendered checks: delta/smoothing/hysteresis/outlier/clock/power unknown, gate precedence/window/recording, cap/no-preemption/lease/fairness, app lifecycle disposal/redaction. Render queue safe/idle/backoff/unplugged/recording/unknown at 125% with accessible status.
- Documentation / installer / release work: document local in-app sampling/plugged-in/recording behavior and no monitoring/telemetry. Runtime scheduler change requires package gates later.
- Evidence and date: 2026-09-27 audit found existing policy/queue but no capacity sampler, power/uncertainty precedence, hysteresis proof, lifecycle cleanup, or queue truth UI. 2026-09-28 added pure aggregate-system CPU delta policy with three-sample idle entry (<55%), two-sample high-use backoff (>70%), invalid/clock/power/recording/backlog conservative states, a bounded two-minute cooldown, and barrier-safe next-launch caps (transcript ≤2; labels/summaries ≤1). Added a disposable local 30-second `GetSystemTimes`/verified-AC sampler with no telemetry or process/device/user-activity inspection; native/probe failures are unknown and conservative. Queue integration applies its cap only to daytime staged work, leaves overnight/manual precedence intact, never preempts active workers, and projects the safe reason through the existing status timer. A stale staged-order fixture was corrected to emit its already-supported ownership receipt. Focused policy, capacity dispatch, and transcript/label/summary lease tests pass 6/6; full focused queue/policy band passes 51/51 and UI source band passes 214/214. Installer packaging tests pass 18/18, including the upgrade-uninstall folder guard. `Build-Installer.ps1` completed with hash-verified apphost recovery; final ZIP is 90,963,216 bytes (`3137B1E2A06F7DA5932EB98026CF87E61FE97C74E50C6D9CAA1D695CB30463B7`) and MSI is 76,148,736 bytes (`177DF736FC6EE68ECEF5BFEB148524DDA93CB4100F488809A7C2684130398B12`). Portable and clean-MSI smoke passed, including installed integrity and an 8-second installed-app survival window.
- Remaining gap or next action: Sprint 5 — Best-Effort GPU Capacity Acceleration.

Goal: use idle CPU safely without adding dependencies.

- Add an app-process `ResourceCapacityMonitor`.
- Measure CPU with native Windows `GetSystemTimes` deltas.
- Treat CPU as idle when average use stays below 55% for 3 samples.
- Back off when CPU use exceeds 70% for 2 samples.
- Sample every 30 seconds.
- Accelerate only when plugged in and not recording.
- Daytime idle-capacity worker caps:
  - up to 2 transcript workers,
  - up to 1 diarization worker,
  - up to 1 summary worker.

Sprint 4 acceptance criteria:

- Capacity tests prove smoothing, idle entry, and fast backoff thresholds.
- Queue tests prove backoff stops new workers but does not kill active workers.
- App remains single-worker when unplugged or recording.

## Sprint 5: Best-Effort GPU Capacity Acceleration

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-28: policy, bounded monitor, queue fence, focused tests, installer build, and portable/MSI smoke all passed).
- User outcome: GPU-idle signal can help an already-approved GPU stage, but missing/unreliable Windows counters never harms normal CPU processing.
- Scope / non-goals: add optional capacity input after GPU transcription/Speaker DirectML readiness contracts are implemented. Do not select/enable GPU provider, inspect process trees/other applications, make counters prerequisite, increase CPU-only caps, run multiple GPU jobs by default, or collect/upload GPU telemetry.
- Dependencies and decisions: GPU transcription S5/S8 and diarization readiness plus S1–S4 staged policy are prerequisites. Counter adapter returns opaque aggregate availability/utilization/validity only; no adapter/device/process names. Missing/access-denied/unsupported/multi-adapter/unknown mapping is `Unavailable` and CPU-safe. GPU idleness is advisory and cannot override user CPU/suppression/power/recording/background policy.
- Implementation slices:
  1. Create testable Windows counter adapter with bounded sampling/lifetime/exception classification, no startup retry storm, and optional one local diagnostic reason per app session. Validate delta/time/outlier/multi-engine aggregation; never equate graphics-engine activity to selected compute device without an explicit safe mapping—unknown means no raise.
  2. Define hysteresis resolver: three valid samples <35% to idle, two >65% to backoff, cooldown/stale/clock/counter error → unavailable. Thresholds remain policy constants pending S10-like evidence; sampler starts only for eligible backlog and disposes with app.
  3. Fence eligibility at dispatch: effective provider must be actual GPU-capable/readiness-probed selected stage (`DirectML` label only when enabled, approved external/GPU transcription only after metadata) and all CPU/power/recording/S7 suppression gates pass. CPU Whisper.NET and requested-but-fallback provider never receive GPU capacity. Keep max one GPU stage unless later measured policy changes.
  4. Apply non-preemptively: idle may allow next eligible GPU stage within stage/global budgets; high/unknown stops new GPU launch and falls through CPU policy. Track decision/reason/count locally; status says optional capacity unavailable rather than “GPU busy” speculation.
- Tests and rendered checks: adapter unavailable/access/exception/multi-engine/stale/outlier; hysteresis/cooldown; provider-stage/readiness/user/power/recording precedence; no CPU cap impact/no preemption/concurrency/queue fairness/redaction. Render only compact optional capacity detail with accessible unknown/fallback truth.
- Documentation / installer / release work: document optional local counter behavior/no telemetry/no effect on CPU; Windows adapter/runtime changes require full package gates later.
- Evidence and date: 2026-09-27 audit found DirectML diarization and potential external transcription references but no GPU counter adapter, trusted device mapping, hysteresis, stage eligibility fence, or graceful CPU-only evidence. 2026-09-28 added a pure opaque aggregate GPU policy: three samples below 35% enter availability, two above 65% enter two-minute backoff, and missing/invalid/clock-regressed samples fail closed. Its launch permit additionally requires separately-proven GPU-capable provider and readiness, so a DirectML preference or a CPU-only provider cannot raise a cap alone. A bounded `pdh.dll` monitor now requests only the Windows GPU Engine utilization counter, never materializes wildcard instance names, retains no adapter/engine/process identity, and uses the highest numeric engine value so any busy engine blocks acceleration. Query/status/format failures remain unavailable. Queue integration preserves overnight/manual precedence and only admits a second daytime diarization worker when the existing CPU gate, GPU policy, and independently-proven DirectML readiness all pass; the first worker retains the Auto route while the companion receives a per-worker CPU-only config, so two DirectML jobs cannot start. Loss of an eligibility gate leaves the normal single-worker path, and active jobs are never preempted. Fresh-root focused policy/monitor/queue tests pass 9/9. `Build-Installer.ps1` completed with hash-verified stable apphost fallback: ZIP is 90,968,204 bytes (`1CB93399CE82584DC062922F1064A0EDAAB38A79F423AF87F1975E931743EB2A`) and MSI is 76,156,928 bytes (`4AC6075119440C4F66CDBDF93F02557D6E197C1BD1848CE3519F9716DEDB9C75`). Fresh portable smoke, clean MSI install, installed integrity, and installed-app 8-second smoke all passed; no app process remained afterward.
- Remaining gap or next action: Sprint 6 — UI, Documentation, Packaging, And Release.

Goal: use idle GPU only for GPU-capable stages, with safe CPU fallback.

- Read Windows GPU Engine performance counters when available.
- If counters are missing or fail, log once and continue CPU-only.
- Treat GPU as idle when use stays below 35% for 3 samples.
- Back off when GPU use exceeds 65% for 2 samples.
- GPU capacity may only raise caps for:
  - DirectML diarization when enabled and probed,
  - external transcription CLI when probe/runtime metadata marks it
    GPU-capable.
- CPU Whisper.NET never consumes GPU merely because GPU is idle.

Sprint 5 acceptance criteria:

- Tests prove missing GPU counters degrade to CPU-only acceleration.
- Tests prove GPU idle does not affect CPU-only providers.
- DirectML/external-provider tests prove GPU capacity is considered only after
  provider readiness is known.

## Sprint 6: UI, Documentation, Packaging, And Release

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29: profile/settings/status/docs, focused tests, Test-All with one unrelated fixture failure, installer build, and portable/MSI smoke recorded; desktop visual review remains a release-approval gate).
- User outcome: people can choose conservative backlog behavior, understand current stage/reason, and receive a packaged feature that never silently changes recording or transcript safety.
- Scope / non-goals: ship S0–S5 only after stage/output safety proven. Do not make acceleration default, expose raw hardware/paths/debug data, imply exact speed, add sensing dependency, publish automatically, or ship automatic enrichment if atomic artifact/currentness proof is incomplete.
- Dependencies and decisions: S0–S5 acceptance matrix plus GPU/Speaker/Summary consent contracts are required. Settings profiles are versioned projection of explicit strategy/window/idle policy—not a second scheduler; migration preserves existing profile/user overrides and maps unknown/future safely to conservative normal. Default remains current normal responsive behavior.
- Implementation slices:
  1. Define profile catalog/compatibility: Normal, explicit Transcript-only emergency, Overnight acceleration, Idle-capacity acceleration, combined policy with explicit precedence. Apply/cancel/reset/migration/change-during-work semantics preserve snapshot jobs and affect next dispatch only; profile/copy never grants summary provider consent or bypasses CPU/GPU safety.
  2. Build one compact queue status projection: selected/effective mode, barrier stage, active/current cap, deferred/blocked count, truthful reason (`recording active`, `unplugged`, `capacity unknown`, etc.) and recovery. Status is revision-safe/non-spam/accessibly named; UI follows `DESIGN.md` dense Technical Studio rules and uses color/icon only supplemental.
  3. Create release matrix: S0 baseline, S1 work/lease migration, S2 no-rerun/atomic ready, S3 window/DST/recording, S4 CPU capacity, S5 GPU optional failure, profile UI/keyboard/high contrast/docs. Run focused test bands/Test-All, conditional AppPlatform tests if worker/update packaging changes, installer build and package startup plus synthetic staged backlog smoke on clean/upgrade CPU-only and optional-GPU devices. No active user app/worker is force-closed.
  4. Package/document only verified behavior: worker stage argument, config migration, installer layout/integrity, no new sensor dependency, local/privacy/consent/fallback/capability limits. If stage transaction/ready/currentness fails, do not narrow by silently skipping enrichments; retain existing behavior and record blocked release condition for explicit decision.
- Tests and rendered checks: profile/migration/apply race, queue status resolver/reason/count, keyboard/focus/AutomationProperties/long/high-contrast/125% layouts; synthetic package staged/migration/restart/window/capacity/recording/outcome. Docs/copy trace to tests/config.
- Documentation / installer / release work: README/SETUP/ARCHITECTURE/release docs describe exact profiles/safety; implementation requires Test-All/installer/package smoke and authorized release path. Planning only no build.
- Evidence and date: 2026-09-27 audit found existing processing profile/window settings and queue controls, but no new profile catalog/status resolver, staged package smoke/migration matrix, UI accessibility evidence, or installer contract for worker stage arguments. 2026-09-29 added a versioned `Normal`, `Transcript-only drain`, `Overnight acceleration`, `Idle-capacity acceleration`, and `Overnight + idle capacity` profile catalog. New config starts Normal; missing legacy profile state migrates once to combined behavior so an installed queue is never silently slowed. Selecting a profile applies only to future queue admission, preserves active leases/workers, and projects the explicit transcript strategy where applicable. The Processing settings surface saves/restores the profile, accessible name, and help text. Existing queue status pairs profile label with truthful policy reason, cap, and active-worker count, without exposing hardware identities. README/SETUP/ARCHITECTURE and `docs/adaptive-backlog-drain-release-matrix.md` now state this bounded behavior. Focused profile/overnight/settings tests pass 16/16. `Test-All.ps1` passes 1511/1512 cases; its sole remaining `DiarizationFixtureReplayTests.TestDiarizationFixtureCatalogScript_Runs_MetadataOnly_DryRun_Without_App_Worker_Or_Private_Text` failure is unrelated to this sprint. `Build-Installer.ps1` completed through explicit hash-verified stable WPF/CLI/worker apphosts: ZIP 90,971,668 bytes (`75800D8F01BC45B35B17E2BE2A4FD3FD80156FAB90B2E4EC18C8BA35FDD51D4E`) and MSI 76,165,120 bytes (`42419C6DE0A0256976E2EF8E68C956B0331C650F6028F96C7903B5D51B0FDF41`). Portable and MSI-installed startup smoke passed with an 8-second survival interval; no app process remained afterward.
- Remaining gap or next action: none for Sprint 6; keep the independent diarization fixture replay failure tracked outside this sprint and perform a desktop visual accessibility review before external release approval.

Goal: ship acceleration as understandable product behavior.

- Add profile options:
  - `Normal`,
  - `Transcript-only drain`,
  - `Overnight acceleration`,
  - `Idle-capacity acceleration`,
  - `Overnight + idle acceleration`.
- Show compact runtime status near the queue:
  - active acceleration mode,
  - current stage focus,
  - active worker count,
  - reason such as `CPU idle, plugged in` or `recording active`.
- Update `README.md`, `SETUP.md`, and `ARCHITECTURE.md` with staged drain,
  capacity sensing, guardrails, and fallback behavior.
- Run focused tests, then
  `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Rebuild installer assets with
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`.

Sprint 6 acceptance criteria:

- Users can tell whether acceleration is active and why.
- Documentation matches the shipped settings and queue behavior.
- Installer assets include the new runtime behavior.

## Adaptive Drain Interfaces And Constraints

- Add `ProcessingSpeedProfile` values for idle-capacity acceleration and
  combined overnight-plus-idle acceleration.
- Add a minimal stage request contract for worker launch.
- Keep transcript JSON/Markdown schema changes additive only.
- Keep default behavior conservative: plugged in, not recording, sustained
  spare capacity, fast backoff.
- No new third-party dependency for CPU sensing.
- GPU sensing is best-effort and must not block CPU-only processing.
- If stage-specific worker passes require risky artifact rewrites, stop and
  narrow the implementation before shipping automatic enrichment drain.

# Meeting Continuity And Split-Healing Reliability Plan

## Summary

Goal: stop the app from swinging between CyberArk-safe detector hardening and
meeting-fragmentation regressions. The root problem is not a single bad Teams
heuristic or a single crash; it is that Meeting Recorder currently has several
different places that answer "is this the same meeting?" differently:

- live detection and auto-stop continuity,
- recent auto-stop recovery,
- startup interrupted-session recovery,
- post-publish cleanup and merge logic,
- one-time historical repair logic.

That fragmentation creates the pendulum. One tweak makes runtime continuity
stricter and prevents a CyberArk-sensitive detector path from being used, then
another tweak tries to recover the lost continuity with title exceptions or
repair merges, and the app keeps alternating between false splits and
overfitted exceptions.

The target architecture is a single continuity engine that owns
continue/stop/roll-over/recover/merge decisions. Platform-specific heuristics
are still allowed, but only as evidence producers. They must no longer make
split decisions directly.

This plan is only successful if it solves both recent failure families:

- false auto-stop and re-auto-start splits like `GES Focus Groups: Principals`,
- crash/restart or recovery-boundary splits like `Americas Virtual AI Co-Lab`.

It must also preserve the current corporate constraint: do not depend on
process-memory inspection or other CyberArk-sensitive escalation to stay
accurate.

## Program Invariants

- Only one continuity authority may decide whether work is the same meeting.
- Heuristics such as title normalization, Teams shell handling, Google Meet
  code handling, and audio attribution may contribute evidence, but they may
  not directly split or merge meetings.
- The continuity engine returns only `SameMeeting`, `DifferentMeeting`, or
  `Unknown`.
- `Unknown` may extend a bounded grace period, but it may not create a new
  meeting row by itself.
- Auto-heal must require stronger proof than keep-alive grace because a false
  merge is more damaging than a short recording tail.
- Every automatic merge must preserve lineage to the original stems/session IDs
  and leave a durable audit trail in manifests and archives.
- Historical one-time repair remains historical; future correctness comes from
  the ongoing runtime, recovery, and publish flows.
- Every new continuity exception must add a replay fixture and a negative test
  case before it can ship.

## Success Criteria

- `GES Focus Groups: Principals`-style false auto-stop/restart scenarios remain
  one meeting.
- `Americas Virtual AI Co-Lab`-style crash/restart boundaries do not surface as
  duplicate visible meetings when strong continuity evidence exists.
- Same-title but different meetings do not auto-merge.
- Generic Teams shell windows do not create durable meeting rows.
- Continuity decisions remain explainable from logs and manifests without WER
  dumps or protected-process access.
- The codebase contains fewer direct continuity branches after the work, not
  more.

## Sprint 0: Failure Corpus And Decision Contract

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: continuity changes are judged against repeatable safe evidence, reducing false merges first and false splits second.
- Scope / non-goals: establish corpus/contract/metrics before changing policy or auto-heal. Do not commit real meeting content/titles/attendees/audio, make a merge decision, change capture timing, or infer identity from generic title alone.
- Dependencies and decisions: existing `AutoRecordingContinuityPolicy`, MainWindow rollover/recovery path, session manifests, and extensive continuity tests are baseline. Corpus uses opaque incident ids plus synthetic public fixtures; restricted local incident evidence lives outside source control with consent/classification/owner/expiry/hash and replay redaction. Replace private incident titles in plan/docs with opaque ids; report only scenario/decision metrics.
- Implementation slices:
  1. Define replay snapshot schema: normalized timestamps/window/platform/meeting identity evidence tier, detection signals/confidence, capture/audio continuity markers, stop/restart/worker/publish outcomes and expected decision/rationale—no transcript/audio/raw window title/attendee/path. Validate version/hash/consent and separate synthetic, restricted, regression, negative and holdout splits.
  2. Write decision contract/state machine: `SameMeeting`, `DifferentMeeting`, `Unknown/Grace`, `ManualReview`, with evidence precedence/conflicts/expiry. Strong stable meeting identity may support continuation; ambiguous/generic/endpoint-only/title-only evidence has bounded role; contradictory strong identity selects Different; unavailable evidence stays Unknown. Explicitly define when grace captures tail, maximum duration/resource, user stop authority and no automatic merge.
  3. Define severity/metrics: false merge highest, false split next, bounded tail capture acceptable only under privacy/storage/capture caps. Denominators, labels/review source, confidence/unknown/abstain, split per logical published meeting, auto-heal offer/commit/reversal/manual correction, crash-recovery healing, grace duration and protected negative false-merge rate are versioned; missing labels are not success.
  4. Add characterization/replay fixtures for auto-stop/restart/generic false start/quiet continuation/same-title-different/ambiguous identity plus negative cases. Test deterministically with clock/DST/cancel/manual stop/restart and assert existing behavior before any new engine.
- Tests and rendered checks: corpus schema/redaction/consent/hash/split, replay deterministic policy result, evidence conflict/unknown/grace/manual stop, metric arithmetic/unknown exclusion/negative-case protection. No UI change; record synthetic trace visualization only after S1.
- Documentation / installer / release work: add continuity decision/corpus governance docs; no installer work for audit-only sprint. Remove private incident names from tracked planning references.
- Evidence and date: 2026-09-27 audit found broad `AutoRecordingContinuityPolicyTests` and rollover paths, but no governed replay corpus, explicit tri-state evidence contract/metrics, or privacy-safe incident representation. Plan contained private incident titles, now replaced by opaque IDs. 2026-09-29 added `ContinuityReplayContracts`: a metadata-only versioned snapshot with SHA-256 integrity, consent/split validation, opaque scenario-id enforcement, and no raw title/window/attendee/path/audio/transcript fields. Five public synthetic fixtures cover auto-stop continuation, crash recovery, generic false start, quiet continuation, and protected same-title/different-meeting negative. The pure contract prioritizes manual stop, contradictory strong identity, bounded five-minute Unknown/Grace, then Manual Review; it never authorizes automatic merge. Metrics count exact decisions, false merge, false split, grace, and review. `docs/meeting-continuity-decision-contract.md` records governance and severity. Focused `ContinuityReplayContractsTests` pass 5/5 from a fresh build root.
- Remaining gap or next action: Sprint 1 — observability and decision trace infrastructure; keep S0 corpus isolated from runtime policy until S1/S2 proofs exist.

Goal: define the exact problem and prevent future "felt right" continuity
changes.

Workstream 1 - Incident corpus:

- Capture replayable fixtures for the recent split families:
  - false auto-stop then re-auto-start (`incident-autostop-continuation-a`),
  - crash/restart recovery split (`incident-crash-recovery-split-a`),
  - generic Teams false starts,
  - quiet same-meeting continuation cases,
  - same-title but actually different meeting negative cases.
- Store enough manifest, timing, title, and detection evidence to replay the
  continuity decision path without live repro.

Workstream 2 - Decision contract:

- Write the continuity decision contract in product terms:
  - what counts as `SameMeeting`,
  - what counts as `DifferentMeeting`,
  - what must stay `Unknown`.
- State the severity order explicitly:
  - false merge is worst,
  - false split is next,
  - bounded extra tail capture is acceptable only to avoid the false split.

Workstream 3 - Program metrics:

- Add baseline metrics for:
  - split rate per published meeting,
  - auto-heal count,
  - auto-heal reversal/manual-correction count,
  - percent of `Unknown` decisions that enter grace,
  - percent of crash-recovered sessions that heal into one row.

Sprint 0 acceptance criteria:

- The recent split failures are represented as deterministic replay fixtures.
- The team has a written continuity decision contract.
- Future changes can be judged against stable metrics instead of intuition.

## Sprint 1: Observability And Decision Trace Infrastructure

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: support and tests can explain continuity outcome without collecting meeting content or reproducing live Teams behavior.
- Scope / non-goals: add trace/replay observability before decision-engine changes. Do not change decisions/capture/merge, persist transcript/audio/window tree/attendee/raw title/path, upload telemetry, or make trace a runtime control channel.
- Dependencies and decisions: S0 corpus/contract and existing policy lifecycle are authority. Trace event has schema/version, session/decision/correlation opaque ids, monotonic sequence/time, event kind, normalized evidence tier/reason code/confidence band, prior/new state, bounded numeric timing/count and outcome. Event order is deterministic even wall clock changes; user-visible values are redacted/classed, not raw strings.
- Implementation slices:
  1. Instrument lifecycle at detection/identity/verdict/grace/auto-stop/rollover/reclassify/recovery/publish-heal boundaries through one append API. Trace errors must never alter decision/capture; sampling/backpressure is bounded ring plus terminal summary, with explicit overflow/drop count.
  2. Persist local sidecar/manifest reference atomically under app-owned session work, separate from published transcript/summary/export. Retention/version/migration/corrupt/future behavior preserves core session; support view is explicit local action. Allowlist serializer and log guard reject content/path/credential/profile/embedding/attendee/title data.
  3. Build pure replay runner from S0 normalized snapshot/events/config/version; no UI/windows/audio/worker/process calls, controlled clock/randomness and stable trace digest. Replay emits comparable redacted result/trace differences and detects unknown input/schema drift.
  4. Add readable synthetic explanation formatter mapping reason codes/evidence/state to concise support text; no raw exception dump. Corpus regressions demonstrate current undesired trace, not merely result.
- Tests and rendered checks: event ordering/overflow/failure isolation, sidecar atomic/migration/corrupt/retention, serializer sentinel redaction, replay determinism/no live dependencies/trace digest, incident synthetic explanation. Render support detail only with synthetic statuses and accessible disclosure after UI sprint.
- Documentation / installer / release work: document local metadata-only trace/replay/retention/access boundary; no installer impact for instrumentation design.
- Evidence and date: 2026-09-27 audit found policy tests but no continuity trace, sidecar contract, pure replay runner, redaction proof, or readable corpus explanations. 2026-09-29 added `ContinuityDecisionTrace`: a 1–256 event bounded ring containing only opaque identifiers, monotonic sequence/time, event/state enums, evidence tier, normalized reason code, and bounded numbers. `ContinuityDecisionTraceStore` atomically writes a supplied session-work sidecar and rejects every non-allowlisted JSON property; missing, corrupt, and future schemas remain non-fatal status results. `ContinuityReplayRunner` uses only S0 snapshot/trace input and emits a deterministic SHA-256 digest; the explanation formatter returns safe reason-code copy. No policy, capture, rollover, recovery, merge, UI, worker, process, or network path is changed by this sprint. Fresh-root `ContinuityReplayContractsTests` and `ContinuityDecisionTraceTests` pass 10/10, covering ordering/overflow, atomic persistence, corrupt/future/disallowed sidecars, deterministic replay, and content-free explanation.
- Remaining gap or next action: Sprint 2 — build the canonical identity snapshot/matcher, then later sprints may explicitly call this trace append API at lifecycle boundaries.

Goal: make every continuity decision inspectable without CyberArk-sensitive
debugging.

Workstream 1 - Bounded breadcrumb stream:

- Add a bounded `ContinuationDecisionTrace` or equivalent breadcrumb pipeline
  for:
  - detection result,
  - identity extraction result,
  - continuity verdict,
  - grace entry/exit,
  - auto-stop countdown transitions,
  - roll-over/reclassify decisions,
  - recent auto-stop recovery decisions,
  - startup recovery decisions,
  - post-publish auto-heal decisions.

Workstream 2 - Persisted explanation:

- Persist enough metadata in manifests or adjacent sidecars to explain why a
  session stopped, resumed, rolled over, or merged.
- Keep these logs bounded and metadata-only; do not add transcript text or
  sensitive payloads.

Workstream 3 - Replay harness:

- Add a deterministic replay surface that feeds stored detection/manifests
  through continuity policy code.
- Make replay cheap enough that every new continuity fix can run against the
  corpus.

Sprint 1 acceptance criteria:

- June 12 fixtures produce readable traces that explain the current bad
  behavior.
- Replay can run without launching the UI or requiring live Teams windows.

## Sprint 2: Shared Meeting Identity And Evidence Ladder

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: continuity uses one cautious identity judgment across runtime, recovery, and historical repair rather than scattered title heuristics.
- Scope / non-goals: introduce snapshot/matcher/evidence rules after S0/S1. Do not auto-merge based on title/fingerprint, rewrite all manifests, export identity data, use raw audio/transcript/attendee as identity evidence, or weaken explicit user stop.
- Dependencies and decisions: S0 severity/tri-state contract is authority. Snapshot is local sensitive metadata: version, normalized evidence tokens/classification, captured time/expiry, provenance/quality, stable local keyed fingerprint, and reason codes—not raw private titles/window tree/path/audio. Fingerprint is an optimization/correlation check only; collision/missing/key rotation yields `Unknown`, never Same.
- Implementation slices:
  1. Define canonical `MeetingIdentitySnapshot` builder from runtime/manifest evidence with deterministic normalization/version and local key/salt management. Separate display-safe generic/specific classification, durable meeting code/attributed source/host context and short-lived shell hints; strip generic terms, cap length, reject ambiguous/multi-candidate evidence.
  2. Implement one pure matcher returning `SameMeeting`, `DifferentMeeting`, `Unknown` plus evidence tiers/reason/conflict/expiry. Strong proof requires compatible durable identity plus temporal/capture provenance; medium may permit grace/shadow only; weak shell/title never permits auto merge. Contradictory strong evidence wins Different; equal/insufficient signals remain Unknown. Centralize Teams/Google normalization with explicit platform/version validity.
  3. Define comparison modes/runtime-manifest-manifest with same canonical inputs and no hidden live state. Add time proximity/grace bounds separately from identity; matcher never controls capture itself.
  4. Backfill lazily: older manifest derives ephemeral snapshot from safe saved evidence, records derivation/version/confidence, and persists only on normal next atomic write. Missing/corrupt/unsupported snapshot stays Unknown/readable; no bulk rewrite or title recovery from external services.
- Tests and rendered checks: normalization/version/key rotation/fingerprint collision, tier/conflict/generic/specific/platform/expiry/time modes, runtime-manifest parity, legacy/corrupt/future lazy backfill/no rewrite, redaction allowlist. Render reason disclosure only synthetic/explicit local support action.
- Documentation / installer / release work: document local identity metadata/evidence limits/Unknown safety and migration; schema/runtime change needs release gates later.
- Evidence and date: 2026-09-27 audit found scattered policy matching and generic-title protections, but no shared snapshot/matcher, collision/privacy contract, comparison parity or lazy compatibility proof.
- Evidence and date: 2026-09-29 added a local keyed `MeetingIdentitySnapshot` to manifests, a pure parity matcher, generic-title rejection, bounded expiry/proximity checks, atomic normal-save backfill, key-rotation/fingerprint safety, and legacy no-rewrite tests. Raw title/window/audio/path/attendee values are never retained in the snapshot.
- Remaining gap or next action: use the pure matcher beside the legacy policy in Sprint 3; it does not yet control capture or merging.

Goal: create the single identity model all continuity code must use.

Workstream 1 - Persisted identity snapshot:

- Add `MeetingIdentitySnapshot` to `MeetingSessionManifest`.
- Persist:
  - platform,
  - normalized durable meeting title,
  - normalized durable window title,
  - detected audio-source app/window identity,
  - evidence sources used,
  - identity confidence,
  - captured-at timestamp,
  - deterministic fingerprint.

Workstream 2 - Shared matcher:

- Add `MeetingContinuityMatcher` or equivalent in core.
- Return only:
  - `SameMeeting`,
  - `DifferentMeeting`,
  - `Unknown`.
- Centralize useful normalization already scattered in the codebase:
  - punctuation-insensitive title matching,
  - Teams suppressed-title continuation,
  - Teams sharing-surface continuation,
  - Google Meet code continuity where still valid.

Workstream 3 - Evidence ladder:

- Define strong, medium, and weak evidence tiers.
- Strong evidence should include a specific non-generic title plus attributed
  audio or durable manifest continuity.
- Medium evidence should include specific title plus platform-specific
  host/window evidence.
- Weak evidence should include shell/navigation/browser context that is useful
  for grace but not safe for merge.
- Generic titles such as `Microsoft Teams`, `Teams`, `Sharing control bar`, and
  equivalent browser shells must never become strong identity by default.

Workstream 4 - Historical compatibility:

- Add lazy backfill so older manifests can derive a `MeetingIdentitySnapshot`
  from saved evidence at read time.
- Avoid a giant mandatory manifest rewrite migration.

Sprint 2 acceptance criteria:

- Runtime-to-runtime, runtime-to-manifest, and manifest-to-manifest comparison
  all use the same matcher.
- Older manifests still participate in continuity and healing without a one-off
  bulk rewrite.

## Sprint 3: Shadow Continuity Engine

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: new continuity logic earns control through visible, privacy-safe comparisons—not a risky switch.
- Scope / non-goals: evaluate S2 matcher beside current policy only. Do not change capture/rollover/auto-stop/recovery/merge behavior, write production artifacts, submit recommendations, or make shadow output user-visible as a decision.
- Dependencies and decisions: S0 corpus/S1 trace/S2 matcher and legacy policy version are pinned input. Shadow invocation is pure, receives same normalized snapshot/clock/config and returns verdict/reason/evidence tier/confidence/engine version; exceptions/timeouts/overload are recorded as shadow-unavailable and never affect legacy path. “Parallel” means decision-boundary side evaluation, no duplicate I/O/worker/capture action.
- Implementation slices:
  1. Instrument continuation, rollover/reclassify, recent auto-stop, startup recovery and post-publish-heal candidate with correlation/revision and one legacy/shadow receipt. Sample only after legacy choice commits; bound CPU/latency/memory and drop shadow safely with count.
  2. Define divergence taxonomy: same verdict/reason drift, conservative Unknown/grace, shadow prevents split, shadow would create split, potential false merge, input/schema/version mismatch, unavailable. Classify against S0 truth/reviewer decision; Unknown/missing labels are not agreement. No raw title/audio/content in records.
  3. Build review report by opaque scenario/platform/version/evidence tier with protected negatives/incident outcomes, disagreement/unknown/unavailable/drop rate and label coverage. Reviewer marks legacy bug/new bug/intentional risk/needs corpus; decisions are auditable/local and retention-bound.
  4. Set pre-registered cutover gates: all opaque protected incidents correct, zero unreviewed/potential false merge divergence on labeled negatives, bounded shadow latency/unavailability, stable matched version/window, representative coverage and reviewer sign-off. Failure extends shadow/rejects cutover, never relaxes criteria silently.
- Tests and rendered checks: legacy isolation/no side effect, identical input/version/clock, timeout/exception/drop, taxonomy/meter/redaction, protected/negative/unknown/reviewer fixtures, report determinism. Render local review only with synthetic tokens/accessibility after action UX sprint.
- Documentation / installer / release work: record shadow protocol/cutover evidence/retention; no package change until cutover.
- Evidence and date: 2026-09-27 audit found no shadow evaluator/divergence receipt/review gate. Existing policy tests do not prove field agreement; private incident-date terminology replaced with opaque corpus reference.
- Evidence and date: 2026-09-29 added the pure, bounded `ContinuityShadowEngine`/meter/report/gate with protected-label and divergence tests; rollover/reclassify now records the legacy result only after it commits. The in-memory receipt cannot affect lifecycle behavior.
- Remaining gap or next action: Sprint 4 may consume the shadow report only after its gate. Current source has no standalone startup-recovery or post-publish-heal continuity candidate (startup seals open manifests; publish repair has no identity decision), so no synthetic hook was added; add one only if such a decision boundary is introduced.

Goal: prove the new engine before giving it control.

Workstream 1 - Parallel evaluation:

- Run the new continuity engine in parallel with the legacy policy.
- Keep legacy behavior active for now.
- Log, for every meaningful decision:
  - legacy verdict,
  - new verdict,
  - divergence reason,
  - whether the divergence would prevent or cause a split.

Workstream 2 - Divergence review:

- Review divergences across:
  - active recording continuation,
  - roll-over/reclassify,
  - recent auto-stop recovery,
  - startup recovery,
  - merge recommendation generation.
- Classify each divergence as a bug in legacy logic, a bug in the new matcher,
  or intentional risk reduction.

Workstream 3 - Cutover gate:

- Define required cutover conditions:
  - opaque incident split fixtures are corrected by the new engine,
  - severe false-merge divergences are absent on negative fixtures,
  - ordinary recordings do not show alarming divergence volume.

Sprint 3 acceptance criteria:

- The new engine has evidence-backed agreement or justified disagreement with
  the old engine.
- Cutover does not require blind trust.

## Sprint 4: Live Continuity Cutover

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: targeted continuity policy/tests and rollover paths exist); `Ready` (2026-09-27 pressure test); `Done` (2026-09-29).
- User outcome: ambiguous same meeting stays coherent without opening generic-shell false starts or endless tail capture.
- Scope / non-goals: integrate S2 matcher only after S3 cutover gate. Do not auto-merge published meetings, override manual stop/consent/capture safety, extend grace indefinitely, or cut over without rollback evidence.
- Dependencies and decisions: S0–S3 required. Feature/config gate selects legacy or matcher path per decision snapshot; rollback affects next decision only. `Same` continues only if active-session/capture revision/manual-stop state valid; `Different` uses safe rollover/reclassify; `Unknown` never starts/new row/merge and may enter one bounded grace.
- Implementation slices:
  1. Route lifecycle decisions through one adapter with immutable snapshot/revision/trace. Recheck state after async transition; stale result cannot affect wrong session.
  2. Define grace receipt: reason/tier/start/deadline/max tail/new-evidence token. Repeated scans with no new evidence are idempotent; end evidence/manual stop/deadline transitions normally. Unknown never becomes Same by timeout.
  3. Extend recent-auto-stop context with safe snapshot/fingerprint/revision; resume only verified Same plus capture safety, not platform/timing/title alone. Crash/startup uses same contract and never resurrects user stop.
  4. Preserve generic/browser/same-title-recurring negatives. Roll back feature on protected negative/trace anomaly; no silent decision drift.
- Tests and rendered checks: gate/rollback, tri-state action, grace deadline/idempotence/race/manual stop, auto-stop/crash/restart, generic/same-title negatives and opaque protected split fixture. Render user-visible grace only if needed with accessible status.
- Documentation / installer / release work: document continuity/grace/rollback after verified cutover; runtime change requires full package gates.
- Evidence and date: source audit found rich policy tests but no shared matcher cutover, bounded grace receipt, snapshot recovery, rollback gate or negative-case proof.
- Evidence and date: 2026-09-29 added a disabled-by-default `MeetingIdentityContinuityEnabled` gate, tri-state adapter, bounded idempotent grace, stopped identity snapshot/revision, and transition cleanup. Matcher mode only controls an existing managed session; it cannot auto-start or merge. Focused continuity/config/lifecycle checks passed 172/172. Installer package rebuilt with ZIP SHA-256 `5FDA8AC343352A2AE7140490DDAFD203B598E76088808E585CB2E4F646EB6297` and MSI SHA-256 `EC715B78F40FE9969DA29436B7AF8F2912D4BFEDDF8E13BB873C37439995D44D`; installed and bundle apphosts match, and no app process remained after smoke.
- Remaining gap or next action: keep the matcher gate disabled until a labeled shadow report passes its pre-registered cutover gate; Sprint 5 may use the same safe snapshots for recovery and publish-time healing.

Goal: stop false splits during recording without creating endless over-recording.

Workstream 1 - Runtime integration:

- Refactor `AutoRecordingContinuityPolicy` and
  `MainWindowInteractionLogic` to consume the shared matcher.
- Behavior contract:
  - `SameMeeting` continues and refreshes positive signal,
  - `DifferentMeeting` may roll over or reclassify,
  - `Unknown` enters bounded continuity grace.

Workstream 2 - Bounded uncertainty handling:

- Replace title-driven split behavior with identity-aware grace.
- Keep the current stop responsiveness for clearly-ended meetings.
- Add one bounded grace path for ambiguous same-platform continuity.
- Make grace idempotent so repeated scans do not extend forever without new
  evidence.

Workstream 3 - Recent auto-stop recovery:

- Extend `RecentAutoStopContext` to include stopped meeting title and identity
  snapshot/fingerprint rather than only platform and timestamp.
- Resume after recent auto-stop only when the matcher returns `SameMeeting`.
- Do not resume solely because the platform matches inside a short time window.

Workstream 4 - Negative-case preservation:

- Preserve non-start behavior for generic Teams shells, weak browser-only
  noise, and other non-specific windows.
- Ensure that same-title recurring meetings still roll over when timing and
  identity evidence indicate a truly different meeting.

Sprint 4 acceptance criteria:

- The GES-style split fixture stays one session.
- Generic shell false starts remain suppressed.
- `Unknown` no longer creates new rows by itself.

## Sprint 5: Recovery, Publish-Time Stitching, And Ongoing Auto-Heal

### Implementation Record

- Status: `Done`
- Status history: `Partial` (2026-09-27 source audit: cleanup merge recommendations and merge path exist); `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29 current-work safety slice); `Done` (2026-09-29 implementation).
- User outcome: a proven crash/recovery split can become one understandable meeting without risking unrelated meetings or user edits.
- Scope / non-goals: add current-work healer after S0–S4. Do not run bulk historical auto-merge, merge Unknown/title-only pairs, touch active capture/processing, delete source/published evidence, or override explicit user metadata/archive/delete decisions.
- Dependencies and decisions: S2 matcher/S4 cutover/S1 trace/current merge executor are authority. Historical `PublishedMeetingRepairService` remains explicitly user/release-governed; ongoing healer triggers only sealed current sessions after startup recovery/publish/recovered work and uses immutable candidate revisions/leases. Opaque protected crash-split fixture replaces private incident label.
- Implementation slices:
  1. Define candidate/eligibility resolver: Same verdict at current snapshot/version, tight measured temporal adjacency, monotonic compatible artifact order, no conflict/user lock/active lease/partial output/lineage cycle, safe source/published availability and one deterministic predecessor/successor. Weak/Unknown/ineligible yields recommendation/trace only.
  2. Execute one shared merge transaction for healer/manual safe cleanup with preflight snapshot, locks, new logical session/lineage receipt, atomic artifacts/catalog promotion, original archive/reference (not source deletion), and rollback/reversal receipt. Failure/cancel/stale candidate leaves both prior meetings visible/current; no duplicate `.ready` or automation confusion.
  3. Preserve/reconcile user value: explicit title/project/notes/speaker/summary edits follow documented conflict rules or block auto-heal; transcript/audio ordering/provenance/gaps are retained. Visible history says healed reason/time/source ids via safe local detail, not hidden rewrite.
  4. Make passes idempotent/concurrent-safe: merged lineage suppresses candidate repeat, startup/publish/queue events coalesce under lease, chain length/repair rate bounded and circuit-breaker creates recommendation rather than mass action.
- Tests and rendered checks: Same/Unknown/Different/user-edit/active/lease/ordering/lineage-cycle eligibility, transaction/cancel/crash/retry/reversal/no-source-delete/no-double-ready, startup/publish/recovery coalescence/idempotence, protected crash split and same-title negative fixture. Render healed/recommendation/blocked/reversal provenance with keyboard/accessibility using synthetic data.
- Documentation / installer / release work: document ongoing vs historical repair, auto-heal qualifications/history/reversal and automation behavior; runtime merge change requires package/release gates.
- Evidence and date: source audit found cleanup recommendations/merge UI but no current-event healer, strict eligibility transaction/reversal, user-edit preservation, idempotence/circuit breaker, or safe automation receipt. 2026-09-29 added a disabled-by-default current-work pass after publish with strict strong-identity/adjacency/artifact/order/user-metadata admission, a local lease, receipt idempotence, archive provenance, source-audio preservation, local reversal, and transcript-visible continuity history. Focused eligibility, receipt, transaction, current-pass, cancellation, reversal, and publish tests passed 15/15. The installer package rebuilt using hash-verified apphosts from `C:\Users\psharm04\MeetingRecorder`; ZIP SHA-256 `8E9199E9477A7CEEA1A0D32B96E8198E153790299695D1B694CDD33DC2CDB7BE`, MSI SHA-256 `0114D30FE0FA90804334692D49580331F9023313BE07EA21ECEAA8E77F0C6DCC`; portable, MSI-install, installed-app, and integrity smoke checks passed under PowerShell 7.
- Remaining gap or next action: disabled-default operation remains until field evidence clears an explicit rollout decision; startup/recovered work reaches the pass after its normal publish completes, and a separate broad historical sweep remains intentionally absent.

Goal: stop crash or recovery boundaries from becoming permanent meeting
boundaries.

Workstream 1 - Separate historical from ongoing repair:

- Keep `PublishedMeetingRepairService` for legacy historical migrations only.
- Introduce a new ongoing healing service that runs on current work:
  - after startup seals interrupted sessions,
  - after publish completes,
  - after recovered queued sessions finish.

Workstream 2 - Strict auto-heal rules:

- Auto-heal only when:
  - the matcher says `SameMeeting`,
  - temporal adjacency is within a tight merge window,
  - artifact ordering is monotonic,
  - no conflicting evidence exists,
  - the merge path can preserve lineage safely.
- Keep weaker cases as visible cleanup recommendations rather than automatic
  rewrites.

Workstream 3 - Lineage and audit:

- Preserve:
  - source stems/session IDs,
  - auto-heal reason,
  - healed-at timestamp,
  - archive location for originals.
- Ensure future users can understand why the visible row history changed.

Workstream 4 - Shared merge implementation:

- Reuse one merge execution path for:
  - startup healing,
  - publish-time healing,
  - safe cleanup merges,
  - any future deterministic split-chain repair.

Sprint 5 acceptance criteria:

- The Americas-style crash split heals into one visible meeting.
- Same-title but different-meeting negative fixtures do not auto-merge.
- Repeated healer passes are idempotent.

## Sprint 6: Crash Root Cause And Callback Topology Hardening

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29); `Done` (2026-09-29).
- User outcome: refresh/transition cascades cannot silently blow stack or corrupt capture state; any future crash leaves recoverable evidence and session truth.
- Scope / non-goals: diagnose/harden callback topology without tying continuity correctness to crash elimination. Do not swallow fatal UI exceptions, weaken crash logging, suppress valid state transition, recursively retry after crash, or collect private meeting content.
- Dependencies and decisions: S1 bounded trace/S4 transition adapter/S5 heal triggers and existing fatal dispatcher handling are authority. Stack overflow may terminate process before catch/finally; evidence must be pre-crash bounded breadcrumb/heartbeat/callback-depth graph, not exception-only. Every callback/event request gets correlation/causal parent/transition revision and allowed edge.
- Implementation slices:
  1. Map and instrument refresh/detection/title/startup/recommendation/heal callback graph with local metadata-only rolling trace: request source, coalesced key, state/revision, dispatcher depth, queue length/elapsed. Capture last N safely before process loss; no title/transcript/window payload/error dump.
  2. Replace nested reentry with serialized transition dispatcher/intent queue: explicit permitted states/edges, dedupe/coalesce keys, max depth/queue/work per tick and scheduling boundary. Reentrant request becomes coalesced/declined with reason—not direct recursive call. State mutation is single owner/CAS and post-action refresh is queued after commit.
  3. Add cycle/overload policy: detect same correlation/edge loop, set bounded diagnostic/recovery state, preserve capture/manual stop, and schedule safe next pass only when valid. Do not hide bug behind permanent guard; trace/report requires root-cause review. Startup/heal must not synchronously invoke their own initiators.
  4. Harden failure recovery: atomic session checkpoints/trace flush before risky boundaries; restart classifier resumes/seals safely and lets S5 healer evaluate later. Unrelated crash cannot invent Same/merge/restart or disregard user stop.
- Tests and rendered checks: callback graph/source guard, reentrant/cycle/coalescing/order/depth/overload, state transition legality/race/cancel/manual stop, simulated abrupt loss/checkpoint/restart and no private trace fields. Render only compact recovery status on synthetic data if exposed.
- Documentation / installer / release work: document diagnostic/recovery behavior and crash evidence collection; app runtime change requires full package/smoke gates.
- Evidence and date: 2026-09-27 audit found potential interacting refresh/transition paths and fatal crash policy, but no causal callback trace, dispatcher/cycle contract, stack-overflow pre-crash evidence, or restart proof. 2026-09-29 added a bounded metadata-only callback-intent dispatcher, atomic trace sidecar, queue/cycle/overload evidence, keyed lanes, and persisted pre-shutdown/cycle evidence. Deferred meeting refresh and manual Start/Stop now pass through separate keyed lanes while retaining their existing UI guards. Automatic start, auto-stop, and rollover now acquire the same serialized transition lane before mutation; startup warmup, deferred maintenance, and post-repair resume acquire dedicated bounded lanes. Transition trace flushes before and after risky boundaries; corrupted/non-normalized sidecars are rejected, and a simulated abrupt-loss/restart test proves bounded metadata survives without becoming recovery authority. Focused callback, XAML, recording coordinator, startup, healing, and recovery checks pass 59/59. Portable ZIP SHA-256 `DD19997E685ED54C6469BAFF908188A14FFF640FA9CF811C87AC32C3CBA78165`, MSI SHA-256 `50CC0799557B64A1638BB0AF22F998E36E1811E2B3F3E6845C25FDE5F1BCFB4E`; portable, MSI-install, installed-app, and integrity smoke pass under PowerShell 7. Full Core suite recorded 1,554 passing and 9 pre-existing build-output/fixture/queue failures outside this sprint.
- Remaining gap or next action: Sprint 7 — catalog and retire/demote remaining direct continuity branches behind explicit rollout and rollback controls.

Goal: remove the crash path that creates some split boundaries, without making
split correctness depend on total crash elimination.

Workstream 1 - Stack-overflow investigation:

- Use breadcrumbs plus code inspection to isolate the `0xc00000fd`
  stack-overflow path.
- Focus on detection refresh, meeting refresh requests, title promotion,
  startup maintenance, recommendation rebuilds, and any healing-triggered
  refresh cascades.

Workstream 2 - Reentrancy guards:

- Add guards around:
  - refresh requests,
  - transition handlers,
  - startup recovery to refresh loops,
  - heal-to-refresh-to-recompute cycles.
- Coalesce repeated refresh requests where possible instead of nesting them.

Workstream 3 - State-machine boundaries:

- Make recording transition state more explicit so stop/start/reclassify/recover
  paths cannot recursively trigger each other.
- Ensure crash recovery remains safe even if some other future crash appears.

Sprint 6 acceptance criteria:

- The known stack-overflow path is either eliminated or reduced to a bounded,
  diagnosable failure path.
- Continuity correctness still holds even if an unrelated future crash occurs.

## Sprint 7: Heuristic Retirement And Rollout Controls

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29); `Done` (2026-09-29).
- User outcome: future urgent continuity fixes strengthen one cautious engine instead of recreating conflicting title heuristics.
- Scope / non-goals: retire/demote branches only after S3–S6 evidence, add local rollout controls and contribution gate. Do not delete useful platform evidence extraction, remotely toggle users, silently re-enable unsafe merge, or remove legacy reference before migration/rollback proof.
- Dependencies and decisions: matcher is sole verdict authority after cutover; platform-specific code may emit normalized evidence only. Maintain retirement inventory: old branch, owner, extracted evidence/replacement matcher rule, shadow/cutover test, deletion criteria. Unknown/unmapped legacy behavior blocks deletion rather than hidden rewrite.
- Implementation slices:
  1. Route every direct continuation/split decision through matcher adapter; static/source tests reject new verdict branches outside authority. Preserve normalization/evidence extractors with bounded reason/version, remove duplicate policy only after protected corpus/shadow/cutover/recovery tests pass.
  2. Define versioned local flag policy/preference: matcher shadow/live, ongoing-heal off/review-only/live, emergency legacy fallback where still retained. Safety precedence chooses no auto-heal/review-only on corrupt/unknown config; change affects future decision snapshots only, has receipt/expiry/clear condition and no remote feed.
  3. Review-only healer produces S5 eligibility/provenance recommendation without merge; unexpected potential merge triggers circuit breaker/review-only, preserving candidate/trace. Rollback cannot resurrect stale legacy code edit or change active capture/session.
  4. Add repo continuity-change template/check: new exception must state contract/evidence tier, add synthetic replay plus adversarial negative fixture, expected shadow divergence/metric, privacy classification and removal condition. Corpus budget/version avoids unbounded fixture sprawl; reviewers reject title-only special case.
- Tests and rendered checks: retirement inventory/static authority violations, extractor parity, flag migration/precedence/corrupt/rollback/race, review-only/no-write/circuit breaker, fixture-template validation. Render Advanced/local support flag state only if exposed, accessible and nontechnical default path unchanged.
- Documentation / installer / release work: update architecture/contribution/continuity guide and support rollback instruction; runtime flags require package gate later.
- Evidence and date: audit found direct policy helpers and no retirement inventory/flag precedence/review-only transaction/fixture contribution gate. 2026-09-29 added versioned local `ContinuityEngineRolloutMode` and `OngoingMeetingHealRolloutMode` settings. Existing boolean opt-ins migrate once to Matcher/Live; new installs use Matcher/Off; invalid values resolve to Legacy/Off, so malformed local configuration cannot enable matching or mutation. Matcher mode now gates active-session reclassification, rollover, continuation, and recent-auto-stop recovery before legacy action mechanics; only confirmed `DifferentMeeting` can continue to transition mechanics, while Same/Unknown remain in the current session/grace. Review-only healing evaluates the same strict current-work candidate, emits one expiring metadata-only local recommendation, and returns before transaction, lease, transaction receipt, archive, or merge code. The continuity contract now documents retained legacy branch ownership/deletion criteria, rollback, review-only circuit-breaker response, and fixture requirements. Focused config/cutover/source-contract/continuity-policy/healer checks passed 186/186; portable ZIP SHA-256 `83E6D6818189DB7808688F2F969F452DF37AEADF6EE754DC6E4306E4A56F3CED`, MSI SHA-256 `0928FB1CF00A548C7E47DA2D8A0F47F7F5EE35578CC736A25D8D8F4D808EF45E`; PowerShell 7 portable, MSI-install, integrity, and installed-app smoke passed from `C:\Users\psharm04\MeetingRecorder`.
- Remaining gap or next action: Sprint 8 — build the S0-S7 acceptance matrix and installed synthetic continuity journey.

Goal: prevent the old pendulum from reappearing through future hotfixes.

Workstream 1 - Retire direct continuity heuristics:

- Delete or demote legacy direct split/continuation branches once the matcher
  cutover is proven.
- Keep platform-specific heuristics only as evidence extractors.

Workstream 2 - Rollout controls:

- Add feature flags for:
  - new continuity engine,
  - ongoing auto-heal,
  - review-only healing fallback if unexpected merges appear.
- Make rollback possible without reintroducing old code edits.

Workstream 3 - Fixture-gated future work:

- Add a durable rule to the repo workflow:
  - any new continuity exception must include a replay fixture and a negative
    fixture.
- Keep the fixture corpus small enough to maintain, but broad enough to block
  the next swing.

Sprint 7 acceptance criteria:

- The continuity engine is the only decision authority left.
- Future urgency fixes have a controlled place to land without forking the
  logic again.

## Sprint 8: Packaging, Validation, And Support Readiness

### Implementation Record

- Status: `Done`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29: validation matrix and package parity harness); `Done` (2026-09-29).
- User outcome: continuity ships only with installed recovery proof and support evidence, not source-test optimism.
- Scope / non-goals: verify S0–S7; do not use private incidents/live meetings, force-close user app/worker, expose trace data, or publish/upload without authority.
- Dependencies and decisions: S0 corpus/S1 trace/S4 cutover/S5 healer/S6 recovery/S7 flags and `docs/auto-detection-cyberark-decision-log.md` are authority. Evidence separates source/test/package/installed/live-machine states; source fixture success does not prove live behavior.
- Implementation slices:
  1. Create S0–S7 acceptance matrix for corpus/replay, unit/integration/UI, rendered status, privacy scan, docs, installer/package and installed smoke; include protected false-merge, crash/restart, user stop, generic shell, Unknown and review-only cases.
  2. Run focused suites/Test-All/conditional AppPlatform tests, clean installer build and layout/integrity/apphost/worker-option/trace-exclusion validation. Preserve command/commit/hash/redacted output.
  3. Run package startup plus installed synthetic continuity journey for controlled trace/replay, auto-stop/restart, negative no-merge, user stop, rollback/review-only and crash events. Block/report active user app—never terminate it.
  4. Update README/SETUP/ARCHITECTURE/RELEASING/support and append cyberark decision log with source/test/package/installed/live state, evidence/date/limits. Commit/push/release needs separate authority.
- Tests and rendered checks: matrix completeness, corpus/trace redaction, cutover/heal/rollback/callback/recovery, package/install smoke and high-contrast keyboard status.
- Documentation / installer / release work: implementation requires all release docs/decision log and package gates; plan-only work requires none.
- Evidence and date: audit found no full continuity release matrix, installed synthetic journey, separated-state decision log, support playbook, or package trace exclusion proof. 2026-09-29 added the S0–S8 synthetic evidence matrix, support interpretation, and `Test-Continuity-Release.ps1`: it refuses a running user app, rejects trace payloads, runs the Release continuity journey against the exact bundled Core DLL, and requires it to match the installed bundle. Source guard checks passed 55/55; the bundled-Core journey passed 187/187; portable, MSI-install, integrity, and installed-app smoke passed under PowerShell 7. ZIP SHA-256 `4B05A671028595D44B535014432ACAA6BD8398868AC32B4DDEDADCC521C0F2CF`; MSI SHA-256 `AE67A1312859EAFECCAE05BD416F61561FC167C212FB4A5CE9CF3CE6451C506D`. `Test-All.ps1` built all projects then reported 1,561/1,568 Core tests passing with seven pre-existing debug-apphost/diarization fixture failures; direct integration passed 8/8 and AppPlatform passed 7/7. README, SETUP, ARCHITECTURE, RELEASING, support contract, release matrix, and decision log now distinguish source/package/installed/live evidence. Native high-contrast/keyboard and a consented live meeting are recorded as operational follow-ups, not claimed as package proof.
- Remaining gap or next action: Meeting Continuity plan complete; proceed to the next chronological `Ready` sprint in `plan.md`.

Goal: ship the reliability model as a supported product behavior, not only a
development refactor.

Workstream 1 - Durable documentation:

- Update `ARCHITECTURE.md` with the new continuity engine, identity model,
  grace semantics, recovery semantics, and ongoing healing flow.
- Update relevant troubleshooting and release docs so support can explain split
  prevention and healing behavior.

Workstream 2 - Validation:

- Run the fixture corpus and focused continuity tests.
- Run `powershell -ExecutionPolicy Bypass -File .\scripts\Test-All.ps1`.
- Run `dotnet test .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj
  -p:NuGetAudit=false` for deployment or manifest-contract changes.
- If packaging or deployed behavior changes, run
  `powershell -ExecutionPolicy Bypass -File .\scripts\Build-Installer.ps1`,
  `powershell -ExecutionPolicy Bypass -File .\scripts\Deploy-Local.ps1`, and
  `powershell -ExecutionPolicy Bypass -File
  .\scripts\Smoke-Test-Release.ps1 -Runtime win-x64`.

Workstream 3 - Operational readiness:

- Add support-facing guidance for:
  - why a meeting was kept alive through grace,
  - why two rows auto-healed into one,
  - why a weak same-title case remained review-only,
  - how to diagnose crash-recovered sessions without protected-process tools.

Sprint 8 acceptance criteria:

- Docs describe the shipped behavior rather than the old heuristic sprawl.
- Validation proves both runtime behavior and packaged behavior when relevant.

## Continuity Interfaces And Constraints

- Add `MeetingIdentitySnapshot` to `MeetingSessionManifest`.
- Extend `RecentAutoStopContext` to include stopped title and stopped identity.
- Introduce a shared continuity matcher that returns `SameMeeting`,
  `DifferentMeeting`, or `Unknown`.
- Keep published lineage metadata additive and backward-compatible.
- Preserve existing artifact formats and `.ready` semantics unless a change is
  explicitly required.
- Do not depend on process-memory access, protected-process inspection, or
  other CyberArk-sensitive escalation to maintain continuity accuracy.
- If Sprint 2 cannot produce a trustworthy identity model from current allowed
  evidence, stop and reassess rather than adding another layer of special-case
  split rules.

# Production Capture, Quality, Cleanup, and History Recovery

## Summary

Root causes are systemic, not isolated:

- 355 of 1,021 published WAVs are under five minutes; 209 adjacent same-title/platform pairs form 239 likely fragment members.
- Auto-start accepts quiet/probe/recent-stop paths, creates a manifest before capture proof, and is blocked by model readiness.
- Base WhisperNet is active despite installed `ggml-large-v3-turbo-q8_0.bin`; provider/model fallback and base auto-download remain possible.
- Startup deletes raw capture sources. Cleanup has 190 failed jobs plus one stuck `Processing` job that exhausts daytime capacity.
- Publishing exposes partial artifacts; 68 published WAVs lack manifest references and 1,096 manifest audio references lack an output. Manifest-only rebuild is unsafe.
- No local WER corpus, promotion journal, or durable cleanup lease exists.

Defaults are locked: hybrid provisional capture, private local 25-clip corpus, 60-day source retention, in-place rebuild without long transcript rollback, and one live-first overnight history worker.

## Sprint 0: Contain Damage and Establish Authority

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29: automatic archive, merge, and transcript regeneration are fail-closed behind a manual-review containment fence).
- User outcome: existing recordings and published meetings remain visible and recoverable while the team establishes a trustworthy, privacy-safe picture of capture, source, cleanup, and promotion failures.
- Scope / non-goals: add containment, evidence, and contract boundaries before remediation. Do not delete, merge, reprocess, upload, or rewrite user artifacts; do not treat historical count claims as proof without a reproducible local inventory; do not expand capture authority or collect real transcript content in diagnostics.
- Dependencies and decisions: existing artifact, retention, cleanup, manifest, and `.ready` contracts are authority. Freeze automatic destructive actions behind an explicit reversible policy; inventory only source metadata/hashes and provenance; maintain a local, consent-governed quality corpus separate from user artifacts. Each new additive contract must identify owning writer, reader, schema version, retention, redaction, and migration behavior.
- Implementation slices:
  1. Add an auditable containment policy that blocks automatic deletion, merge, archive, and reprocess while preserving visible recommendations and manual routes; record each suppressed action and safe recovery route without payload data.
  2. Produce a reproducible `HistoryInventorySnapshot` from hashes, paths classes, manifest references, source availability, and lineage, with an atomic timestamped write and no transcript/audio export.
  3. Define additive, read-compatible capture, source, profile, lease, promotion-journal, and historical-job schemas plus validation fixtures; do not wire them into mutation paths yet.
  4. Establish the private local corpus protocol, consent/provenance register, aggregate-only metrics, access/revocation rules, and a safe dashboard that cannot leak identifiers, paths, text, or audio.
- Tests and rendered checks: test containment precedence/restart persistence/manual-route availability, snapshot determinism/corruption/retry/redaction, schema backward reads/unknown fields, no automatic side effect, and corpus/dashboard privacy guards. Review synthetic status states and support wording at normal and high-DPI layouts.
- Documentation / installer / release work: document containment scope, evidence classes, corpus governance, and the authority/migration map. If any shipped cleanup/capture default changes, update release notes and rebuild/smoke installer assets in that implementation task.
- Evidence and date: 2026-09-29 added a fail-closed automatic-mutation containment fence. Automatic archive, merge, and transcript regeneration no longer dispatch; each suppressed automatic recommendation now persists one deduplicated local `ManualReview` receipt with its action and safe manual recovery copy, without source paths or payload stems. Non-destructive incremental speaker-label and summary work retain their existing controls. A metadata-only `HistoryInventorySnapshotService` derives hashed identifiers, artifact/manifest/ready presence, source recoverability, and audio hashes from catalog facts, then writes atomically without persisting paths or transcript text. Additive versioned capture, source, transcription-profile, processing-block, lease, promotion-journal, and historical-job contracts now deserialize future fields safely and are not yet wired into mutation paths. `docs/recovery-corpus-protocol.md` defines the local-only consent, provenance, revocation, aggregate-report, and removal contract; `RecoveryQualityCorpusRegistry` accepts only opaque ids, hashes, category, expiry and revocation state, and emits aggregate counts. Focused policy, source-wiring, ledger, inventory privacy, schema, and corpus aggregate tests are required. Governed corpus evidence remains open.
- Remaining gap or next action: run the first inventory against an authorized data root and create a local governed corpus register/aggregate report before accepting any historical-count claim.

- Disable automatic destructive cleanup and published-session raw pruning. Keep recommendations visible; do not automatically archive, delete, merge, or reprocess until later gates pass.
- Replace hard-coded history counts with a timestamped `HistoryInventorySnapshot`: hash and classify published audio, manifest references, retained raw tracks, and lineage-proven archive sources. Never inspect or export transcript content.
- Add additive contracts: `CaptureCandidate`, `CaptureProof`, `CaptureHealthSnapshot`, `SourceAudioSet`, `ApprovedTranscriptionProfile`, `ProcessingBlockReason`, `CleanupJobLease`, `ArtifactPromotionJournal`, and `HistoricalReprocessingJob`.
- Build a private 25-clip human-reference corpus locally: long calls, short calls, split chains, low-speech audio, microphone-heavy calls, and endpoint switches. Store source hash, reference transcript, category, and consent/provenance locally only.
- Add local aggregate dashboards: false-start rate, fragment-chain rate, capture-health failures, transcript blocks, cleanup lease age, promotion recovery, retention coverage, and rebuild status.

## Sprint 1: Capture Admission and Continuity

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires completed Sprint 0 authorized inventory and governed corpus evidence before capture behavior changes).
- User outcome: recording starts only from clear, consent-safe evidence, remains one coherent meeting through short endpoint failures, and preserves captured audio even when transcription cannot run.
- Scope / non-goals: replace admission/continuity decisions and retention safety only. Do not silently start recording, inspect protected processes, infer consent, rely on title/probe heuristics as session creators, discard a viable source because a model is unavailable, or merge distinct meetings automatically.
- Dependencies and decisions: complete Sprint 0 containment/inventory first. Before changing meeting detection, Windows audio probing, launch paths, or executable naming, read and append `docs/auto-detection-cyberark-decision-log.md`, recording source/test/package/installed/live-machine state separately. `CaptureAdmissionController` owns provisional-to-visible promotion; explicit consent and capture proof are separate from transcription readiness; identity evidence is tri-state and bounded by expiry/suppression rules.
- Implementation slices:
  1. Define versioned candidate, proof, health, endpoint, suppression, and end-state records with monotonic clocks, stable session identity, restart semantics, and no raw-audio/log payload.
  2. Write provisional same-volume spool capture and atomically promote only after configurable structural-frame and meeting-evidence thresholds; reject headers, zero-duration, quiet/probe, stale, and recent-stop conditions as creators.
  3. Implement endpoint-health/failover/grace transitions that keep one session only while viable capture or matching identity persists; surface degraded state and a direct user remedy without guessing a new meeting.
  4. Decouple model/worker readiness from admission, retain all required source tracks under an explicit capacity/retention policy, and publish an honest blocked-transcription state rather than losing capture.
- Tests and rendered checks: deterministic timer/frame fixtures for promotion, stale/suppressed evidence, restart, endpoint switch/silence, explicit stop, duplicate prevention, low-storage policy, blocked model, and retention exclusions. Replay synthetic capture traces; inspect consent, degraded, blocked, and recovery UI with keyboard/Narrator and high-DPI states.
- Documentation / installer / release work: update capture consent, degraded-capture, source-retention, and recovery docs; append the decision log before source changes. Rebuild installer assets and run packaged smoke when capture/runtime behavior ships.
- Evidence and date: no complete provenance-rich admission trace, installed endpoint-failover result, or CyberArk decision-log entry proving this contract was found as of 2026-09-27.
- Remaining gap or next action: first record the decision-log baseline and write pure admission-state tests before changing live audio collection.

- Introduce `CaptureAdmissionController`. Candidate audio writes only to a provisional spool on the same volume as work data; it creates no visible meeting or manifest.
- Promote only after three seconds of structurally valid PCM from an identified endpoint plus either explicit active-meeting state or attributed active meeting audio. Valid frames need not contain speech; header-only or zero-duration data never qualifies.
- Weak evidence prompts `Start recording` / `Dismiss` after 45 seconds, expires after five minutes, and persists suppression until 60 seconds of observed inactivity followed by fresh proof.
- Remove quiet-title, probe-timeout, and recent-auto-stop paths as session creators. They may inform an active session, never create a follow-on one.
- Decouple recording from transcription readiness. Storage and capture proof gate recording; unavailable approved transcription enters `ProcessingBlockReason.BlockedModel` without launching a worker.
- Record capture health every five seconds. Ten seconds without valid frames triggers endpoint recovery; 30 seconds without a viable endpoint shows degraded capture but keeps the same session. Stop only on explicit end evidence or five minutes with neither matching identity nor viable capture.
- Retain loopback, microphone, and canonical mix for 60 days. Below 20 GiB free, purge only eligible published sources until 25 GiB is free; never purge active, queued, leased, promoting, or under-seven-day sources. Block a new recording start if reserve cannot be restored.

## Sprint 2: Approved Transcription and Source Quality

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires Sprint 0 governance and Sprint 1 source-track evidence).
- User outcome: transcripts disclose the approved engine/profile and any source limitation; a missing, invalid, or poor-quality model blocks processing safely instead of producing a silent downgrade or losing a recording.
- Scope / non-goals: establish approved transcription profiles, source-health assessment, and quality gates. Do not auto-download/fallback to an unapproved model, change language or provider silently, export corpus data, fabricate quality scores, or make low-speech audio disappear from the user’s library.
- Dependencies and decisions: Sprint 0 corpus/provenance governance and Sprint 1 source-track records are required. `ApprovedTranscriptionProfile` is immutable and release-pinned by provider/model/hash/size/language/worker-load/benchmark evidence. Define a quality-result schema that separates usable low-speech input, unsupported/corrupt input, model availability, and transcription failure; any policy change needs a reproducible candidate-versus-baseline decision.
- Implementation slices:
  1. Create the profile catalog/validator and worker-load probe, with explicit fail-closed selection and user-readable blocked reasons; migrate legacy configuration to an unapproved/needs-review state rather than selecting a fallback.
  2. Build local corpus benchmark harnesses that calculate WER/timestamp/performance/memory measures from controlled reference data, hold out promotion samples, and persist only redacted aggregate results plus profile provenance.
  3. Add preflight/source-health analysis for duration, speech/silence, clipping, discontinuities, channel/format, and language-policy eligibility; publish usable sparse recordings with an intelligible review warning.
  4. Persist actual execution/profile/source-health provenance atomically with transcript results, preserve backward readers, and route hash/probe/benchmark failure to recovery without launching a substitute worker.
- Tests and rendered checks: test profile hash/size/language mismatch, stale catalog/config migration, worker probe failure, source-health classification boundaries, low-speech publish, corrupt input rejection, provenance compatibility, candidate regression/no-go gates, and cancellation/retry. Render blocked, review-warning, and profile-provenance states from synthetic fixtures.
- Documentation / installer / release work: document approved-profile selection, source-quality warnings, benchmark governance, and exact failure recovery; packaging must prove profile/model integrity before it carries an approved claim.
- Evidence and date: no release-pinned approved profile, governed benchmark report, or source-health/provenance acceptance evidence is linked as of 2026-09-27.
- Remaining gap or next action: define the profile manifest and metadata-only benchmark fixture protocol before removing any legacy fallback.

- Make `ApprovedTranscriptionProfile` immutable: provider, model filename, SHA-256, minimum size, language policy, worker-load result, benchmark revision, approval time, and profile ID.
- Benchmark `ggml-large-v3-turbo-q8_0.bin` against base on the corpus. Approve only with at least 15% aggregate relative WER improvement, no clip regression above five WER points, valid timestamps, memory below 6 GiB, and P95 runtime no slower than 1.5× source duration.
- Remove configured-model auto-switching, CLI-to-WhisperNet fallback, and WhisperNet base-model download. Hash mismatch, failed probe, missing file, or failed benchmark blocks transcription honestly.
- Force English for approved `.en` models; use auto language plus sparse-output English retry only for approved multilingual profiles.
- Persist actual provider, profile ID, model hash, language, audio duration, speech density, silence ratio, clipping, discontinuities, and warning state. Reject unusable inputs; publish valid low-speech meetings with a review warning.

## Sprint 3: Durable Cleanup and Atomic Publishing

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires completed Sprint 0 inventory and Sprint 1–2 provenance and quality evidence).
- User outcome: cleanup and rebuilt output either completes as one coherent meeting or remains safely recoverable; users never see a partial, duplicated, or silently merged result.
- Scope / non-goals: replace cleanup ownership, promotion, and conservative derived-merge behavior. Do not automatically merge weakly related meetings, concatenate legacy Markdown, delete source before validation, expose an incomplete destination, or treat a file copy as a completed publication.
- Dependencies and decisions: Sprints 0-2 establish source inventory, capture provenance, profile/quality acceptance, and retention. A lease is the only scheduler execution authority; `ArtifactPromotionJournal` owns staged/verified/promoted/ready/committed/recovered transitions; `.ready` is written last. Define stable fingerprints, source/destination volume capacity, idempotency keys, failure taxonomy, retry/backoff limits, and a catalog projection that hides journal-locked stems behind truthful updating/recovery state.
- Implementation slices:
  1. Migrate legacy cleanup rows into a versioned lease model with owner, expiry, heartbeat, attempt, retry, failure, and manual-review fields; reconcile leases/retries once at startup and at bounded scheduler intervals.
  2. Build journaled, same-volume-aware promotion with temporary staging, source/output hashes, fsync/error handling where supported, atomic replacement, `.ready` last, and crash recovery/rollback before catalog load.
  3. Define a conservative merge planner over source lineage, normalized identity, duration/gap, revision, and quality prerequisites; output a preview/review-only result unless every deterministic automatic eligibility rule passes.
  4. Enforce multi-volume capacity reservations, cancellation boundaries, source-retention handoff, and per-item receipts so retries cannot publish twice or delete usable input.
- Tests and rendered checks: inject failures at each journal phase, restart/lease expiry races, duplicate dispatch, destination conflicts, low storage, file locks/hydration, hash mismatch, retry exhaustion, cancel/retry, catalog refresh, and merge eligibility/lineage. Render Updating, Manual review, failed/retryable, and mixed batch outcomes with accessible explanations.
- Documentation / installer / release work: document cleanup/merge eligibility, retention, recovery receipts, and support-safe diagnostics. Any shipping worker, storage, or publish-path change requires package rebuild and installed smoke testing.
- Evidence and date: no durable lease migration, phase-by-phase promotion recovery proof, or conservative merge lineage evidence is linked as of 2026-09-27.
- Remaining gap or next action: implement the journal state machine and fault-injection tests before replacing any direct publish/merge path.

- Replace the ledger with leased jobs: stable fingerprint/source hashes, owner, expiry, 60-second heartbeat, attempt count, retry time, failure class, and recovery reason. Reclaim expired leases after 15 minutes at startup and each scheduler pass.
- Retry locks, hydration failures, and transient I/O at 5 minutes, 30 minutes, 2 hours, and 12 hours. Rescan once, then mark `Unrecoverable`; migrate existing failed and stuck rows to honest `ManualReview` or retry states.
- Limit live cleanup to one lease daytime and five overnight. Count only live leases.
- Replace direct merge with `DerivedReprocessingJob`: only auto-merge same non-generic normalized title/platform chains, valid audio for every member, and end-to-start gaps of at most ten minutes. All lower-confidence chains remain review-only; never concatenate legacy Markdown.
- Route normal processing, merge, and history rebuild through `ArtifactPromotionService`. Stage per destination volume, hash-verify, journal each phase, preserve originals only until final validation, promote, write `.ready` last, then commit and remove temporary originals.
- On startup, recover or roll back every incomplete journal before catalog load. Catalog hides journal-locked stems behind one `Updating` row and never infers a new publish from audio plus Markdown alone.
- Keep legacy records visible as `LegacyIncomplete` when historical artifact sets lack JSON or `.ready`; exclude them from partial promotion and reprocess from valid audio where possible.
- Require capacity on every participating volume: `25 GiB + max(2 GiB, 3 × input bytes)` before merge or history promotion.

## Sprint 4: Pilot and Rebuild History

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires completed source lineage, approved profile, and promotion recovery from blocked Sprints 0–3).
- User outcome: historically damaged meetings improve only when a verified recoverable source and approved pilot evidence exist; otherwise the existing visible result is preserved with an honest limitation.
- Scope / non-goals: classify and selectively rebuild history through the new pipeline. Do not mass-reprocess by filename/title alone, mutate original artifacts without a journal/approval, promise a completion date before measured throughput, overwrite unrecoverable legacy records, or run history work while live capture/safety pressure is active.
- Dependencies and decisions: require S0 inventory, S1 source lineage, S2 approved profile/quality, and S3 leases/promotion/recovery. Define a source-precedence ladder, hash de-duplication, classification schema, pilot cohort/approval authority, job resume/cancel behavior, no-go conditions, and artifact lineage from original source to rebuilt output. Preserve `LegacyNoRecoverableAudio` as a user-visible state, not an error to hide.
- Implementation slices:
  1. Implement read-only source inventory/classification with canonical-source selection, duplicate/backups exclusion, reason codes, and a reviewable report; reconcile changes before job creation.
  2. Create a fixed, consent-safe pilot manifest that covers split, long, short, sparse, and microphone-heavy cases, with acceptance thresholds, reviewers, and explicit approval/rejection receipts.
  3. Execute each pilot as a single journaled job using current profile/quality/promotion contracts; compare source hash, duration, outputs, lineage, readiness, and user-visible transcript consistency without retaining unnecessary private payload.
  4. Gate broader work behind pilot approval and measured throughput; enforce one resumable off-hours worker, live-capture/storage/recovery preemption, pause/restart provenance, and an operator-visible no-go/rollback route.
- Tests and rendered checks: cover source-precedence conflicts, duplicates, missing/legacy sources, classification stability, pilot manifest tampering, approval/rejection, pause/resume/cancel, reprocessing idempotency, promotion recovery, capacity/preemption, and lineage display. Review library rows for rebuilt, updating, blocked, and legacy-unrecoverable examples at keyboard/high-DPI states.
- Documentation / installer / release work: document historical rebuild eligibility, what remains unchanged, pilot approval, throughput uncertainty, and recovery/support procedures. Rebuild installer assets only when the shipped worker/storage behavior changes.
- Evidence and date: existing roadmap estimates and classifications lack a reproducible source inventory, approved pilot report, or measured production-like throughput as of 2026-09-27.
- Remaining gap or next action: first produce the read-only classification report and pilot acceptance rubric; do not enqueue historical jobs yet.

- Build source inventory from current published WAVs first, current manifest paths second, and archive files only where lineage proves that archive is sole source for a visible meeting. Hash-deduplicate; exclude backups, standalone archive copies, and false starts.
- Classify each record: standalone, approved fragment chain, duplicate, invalid, blocked, unrecoverable, or `LegacyNoRecoverableAudio`. Leave the latter unchanged with its current transcript visible.
- Pilot ten records: three split chains, two long meetings, two short isolated meetings, two sparse/low-signal meetings, and one microphone-heavy meeting.
- Reprocess each canonical source once; concatenate only approved chains and run the complete new pipeline. Validate source hash, duration, approved profile metadata, quality gate, audio/Markdown/JSON consistency, lineage, and `.ready`.
- Require explicit pilot approval before full run. Run one resumable job only in the overnight window; pause immediately for live recording, storage pressure, blocked model, or promotion recovery.
- Publish pilot throughput and P50/P95 real-time factor before bulk start. Current 354.3 hours of audio can require roughly 531 worker-hours at the maximum allowed rate; do not promise a completion date before pilot measurement.

## Sprint 5: Verification and Release Gates

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: qualification depends on evidence from blocked Sprints 0–4 and cannot be self-certified).
- User outcome: a release can credibly claim safer capture, transcription, cleanup, and historical recovery only after deterministic, packaged, and explicitly authorized live evidence meets predeclared no-go thresholds.
- Scope / non-goals: assemble and enforce remediation qualification. Do not substitute a green unit suite for installed behavior, use unconsented meetings as a test corpus, self-authorize production canaries or release upload, collapse source/test/package/installed/live evidence, or waive a failed no-go criterion without a documented new decision.
- Dependencies and decisions: Sprints 0-4 supply containment, admission, quality, promotion, and pilot evidence. Define a traceability matrix from each acceptance condition to fixture, environment, owner, redacted evidence, pass threshold, defect/retest rule, and release decision. Keep deterministic synthetic tests separate from consented live canaries; channel/package verification follows repository release guidance.
- Implementation slices:
  1. Create the acceptance/evidence matrix for admission, capture health, profile quality, source retention, leases, promotion, merge, historical pilot, and user-visible recovery, including severity/no-go/rollback owners.
  2. Add deterministic fault-injection and replay suites for every admission/journal/lease/profile/source edge, with privacy-safe fixtures, reproducible seeds, and artifact-integrity assertions.
  3. Run isolated packaged/installed matrices for supported Windows versions and upgrade states, covering endpoint switch, silence, storage pressure, model block, interruption, recovery, and source/artifact preservation.
  4. Conduct any live canary only with explicit authorization and consent, bounded cohort/success criteria, reversible flags, support coverage, monitoring that excludes content, and a preapproved rollback action.
  5. Require sign-off on evidence, docs, decision-log entries, installer assets, smoke results, and residual-risk release notes before a release action.
- Tests and rendered checks: run focused unit/integration/replay tests, `scripts\Test-All.ps1`, AppPlatform tests for deployment/manifest changes, `scripts\Build-Installer.ps1`, and packaged smoke for shipping changes. Verify consent/degraded/blocked/updating/recovery UI with keyboard, Narrator, contrast, and 200% scaling; record failed gates rather than masking them.
- Documentation / installer / release work: complete architecture, setup, support, retention, recovery, CyberArk decision-log, release notes, and installer guidance in the shipping change. Build/release/publication actions need their own scoped authority.
- Evidence and date: no traceable remediation qualification matrix, packaged capture/promotion proof, or authorized canary evidence is linked as of 2026-09-27.
- Remaining gap or next action: establish the evidence matrix and deterministic fault fixtures before proposing any canary or release.

- Add focused tests for proof promotion, prompt expiry, restart suppression, stale evidence, endpoint recovery, model-unavailable capture, provider/model fail-closed behavior, retention purge, lease recovery, retry classification, promotion crash at every journal phase, legacy catalog visibility, merge lineage, and history deduplication.
- Run corpus regression and worker-load smoke before profile approval; exercise Teams endpoint switch, silent start, long call, storage pressure, OneDrive lock/hydration, and interrupted promotion journeys.
- Canary requires ten approved pilot records and five live meetings: zero duplicate automatic sessions, zero header-only publishes, zero silent model/provider downgrade, zero unreconciled lease, and zero partial artifact exposure.
- Before shipment run full product tests, AppPlatform tests, installer build, packaged-release smoke, and architecture/setup/support/retention/recovery documentation updates.

## Assumptions

- Local-first only; no cloud transcription, bots, protected-process inspection, or external writes.
- No long rollback archive for replaced transcripts. Promotion retains originals only until journal validation completes; recoverable source audio remains retained for 60 days.
- Historical rebuild replaces artifacts only where a valid canonical source exists. Records without one remain unchanged and clearly labeled.

# Unified Local Meeting Intelligence Roadmap

## Summary

This unifies the previous Granola roadmap with the strongest Fireflies and Otter
signals.
Granola supplies the workflow shape: brief before the meeting, a minimal note
canvas during it, then notes, actions, and follow-up afterward. Fireflies
sharpens the priority: frictionless capture, searchable recall, and reviewed
action outputs are the durable user value. Otter confirms the payoff: retain
the full conversation, find exact prior context quickly, and turn only
evidence-backed suggestions into reviewed follow-through.

Meeting Recorder should adopt those workflow gains while keeping its
Windows-first, local-first, explicit-recording posture. This section is the sole
roadmap for these capabilities; do not add a parallel Granola, Fireflies, or
Otter track.

## What To Preserve And What To Exclude

Users value bot-free capture, staying present instead of typing, useful summaries
and actions, rapid recall of a past decision, and a clean personal-workspace UI.
The roadmap therefore preserves raw notes plus AI enhancement, source-grounded
outputs, local people/project memory, and clear capture confidence.

Otter's strongest signals reinforce bot-free capture, searchable full
transcripts rather than summary-only recall, clear evidence for generated
claims, and action review before follow-through. Those gains belong in this
local workspace, not in a cloud collaboration layer.

Do not add meeting bots, cloud sync, account login, automatic CRM/Slack/Notion/
email writes, public sharing links, sentiment or coaching analytics, or a generic
AI-skill marketplace. Speaker labels and generated actions remain reviewable
suggestions, never asserted facts.

Live transcription, shared Channels/workspaces, and automatic external system
writes remain deferred. Calendar and detection signals may prefill and
recommend capture, but must never join calls or start recording silently.

## Unified Sprint Shape

### Sprint 1: Meeting Moment And Capture Confidence

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on the blocked capture-admission and health authority).
- User outcome: users can deliberately begin local notes/capture with truthful confidence from any app surface.
- Scope / non-goals: add meeting context/raw notes/controller over existing capture. Do not auto-start from calendar/detection, join meetings/bots, expose attendees/calendar details without existing consent, or treat detection as recording permission.
- Dependencies and decisions: S0 capture admission/health and current calendar/detection/privacy policy are authority. Context candidates are local, revisioned, freshness/provenance-rated; `Open Note + Start Recording` is explicit user action. Raw note autosave is atomic/local and separates draft from transcript/summary.
- Implementation slices: 1. Project Coming Up/candidates with stale/ambiguous/no-access states and user-confirmed context. 2. Add note session/capture state machine (`Ready`, `Capturing`, `LowSignal`, `EndpointChanged`, `Recovering`, `Failed`) tied to real health, no false success. 3. Add global mini-controller with one source of truth, focus-safe open/stop/recovery and no duplicate action. 4. Crash/restart restores draft safely with clear capture status.
- Tests and rendered checks: consent/action, stale/race/duplicate, draft atomic/crash/restart, global state/health/error, keyboard/Narrator/high-contrast/125% Technical Studio render.
- Documentation / installer / release work: document local/bot-free/explicit-start/recovery behavior; runtime UI change needs package gates later.
- Evidence and date: current local capture paths exist, but no unified note/context/controller or cross-surface health/recovery proof.
- Remaining gap or next action: define pure note/capture/context view model fixtures before UI.

- Add a `Coming Up` strip with calendar-matched meetings and detected call
  candidates, plus one-click `Open Note + Start Recording`.
- Open a focused raw-note canvas, prefill known title/attendees/project, and add
  a persistent active-recording mini-controller visible from every app surface.
- Treat bot-free local capture as a deliberate product advantage: use existing
  local device/audio paths and never add a meeting participant on the user's
  behalf.
- Add `Ready`, `Capturing`, `Low signal`, `Endpoint changed`, `Recovering`, and
  `Failed` states. Prompts may offer capture, but must never start it silently.
- Acceptance: users can start from an upcoming or detected meeting, see capture
  health throughout the app, avoid surprise capture, and recover their notes
  after stop or failure.

### Sprint 2: Evidence-Aware Notes And Reviewed Actions

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on the blocked Sprint 1 raw-note and capture contracts).
- User outcome: people can write/edit notes and action items while seeing what is raw, inferred, grounded, unverified, or reviewed.
- Scope / non-goals: add local note/action artifacts and provenance. Do not silently alter raw notes/transcript, auto-send tasks, claim model output is fact, or share/provider-send content outside existing consent.
- Dependencies and decisions: raw-note draft/capture and transcript/summary provenance are authority. Every block/action/decision has stable id, revision, source links/ranges/fingerprint, author/origin/review status and local-only draft/published boundary; link absence means ungrounded, not invented citation.
- Implementation slices: 1. Define atomic note/action schema/merge/conflict/autosave/restore. 2. Add optional generated suggestions as separate mutable drafts with evidence links/confidence/limits and explicit accept/edit/reject. 3. Build reviewed action lifecycle/assignee/due-date validation, no external dispatch. 4. Keep transcript changes/freshness and source deletion/staleness visible, preserve readable historic notes.
- Tests and rendered checks: revision/conflict/crash, provenance link/fingerprint/stale/missing, generated-vs-raw isolation, review/accept/reject/no external send, privacy/redaction; keyboard/screen-reader/rendered source/review states.
- Documentation / installer / release work: document local drafts, provenance/review/stale limits; runtime change package gates later.
- Evidence and date: no note/action provenance/review schema or grounded-output safety proof found.
- Remaining gap or next action: add pure note block/action/provenance fixtures before editor.

- Make raw notes the live surface; add optional enhanced notes, Markdown
  shortcuts, source inspection, selected-text rewrite, and
  regenerate-with-feedback without overwriting user-authored notes.
- Extend the existing published-summary pipeline with editable decisions, action
  items, owner/due-text, review state, and follow-up drafts rather than creating
  a parallel artifact store.
- Require `MeetingEvidenceRef` support for generated actions, decisions,
  enhanced-note claims, and follow-up drafts. Each result exposes local
  evidence or explicitly reports insufficient evidence.
- Acceptance: generated content is editable, traceable to local evidence, and
  optional AI failure never blocks transcript publication.

### Sprint 3: Local Recall And Action Inbox

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on blocked Sprint 2 note/action provenance).
- User outcome: users can find local meeting knowledge/actions with source links, freshness and clear scope.
- Scope / non-goals: add local rebuildable recall/action inbox. Do not upload/index hidden content, claim exhaustive/authoritative answer, auto-complete/send actions, or bypass retention/access boundaries.
- Dependencies and decisions: S2 note/action provenance and existing meeting artifacts are authority. Index is derived/local/versioned/rebuildable with per-item source revision/retention/access scope; search result/Q&A returns cited source ids/snippets only within allowed local scope, otherwise unknown/no answer.
- Implementation slices: 1. Build index lifecycle/checkpoint/rebuild/delete with no raw private logs. 2. Define query/ranking/filter/freshness/permission/result provenance and deterministic empty/error states. 3. Grounded Q&A uses explicit local approved provider/consent, bounded context/citations/no-answer for insufficient evidence. 4. Project action inbox with reviewed/open/blocked/due/stale state and no external task dispatch.
- Tests and rendered checks: rebuild/corrupt/version/delete/access/retention, query ranking/provenance/stale, Q&A citation/no-answer/consent/redaction, action lifecycle/keyboard/accessibility/render.
- Documentation / installer / release work: document local index/rebuild/limits/grounding; package gates after implementation.
- Evidence and date: no rebuildable recall/Q&A/action-inbox contracts found.
- Remaining gap or next action: define index/source envelope and deterministic query fixtures.

- Make timestamped, speaker-aware recall across local meetings, notes,
  decisions, and actions the primary Otter-derived user-value milestone.
- Make `MeetingSearchIndex` a rebuildable local cache whose source of truth is
  published artifacts. On cache failure or staleness, rebuild or use a direct
  catalog scan and show the incomplete state rather than returning stale recall.
- Add grounded Q&A over an explicit scope: one meeting, selected meetings,
  project/client, attendee, or all local meetings. Every answer links to source
  transcript or note evidence.
- Add a local action inbox that keeps owner, due-text, meeting origin, and review
  state visible without writing to an external task system; source deletion
  changes retained references to `Source unavailable`, never invented evidence.
- Acceptance: users can find a past decision or promise in seconds and inspect
  its local source before acting.

### Sprint 4: Focused Recipes And Follow-Up Workbench

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on blocked reviewed action and recall contracts).
- User outcome: users turn reviewed local meeting evidence into reusable follow-up drafts without accidental external action.
- Scope / non-goals: add local recipes/workbench/copy-export. Do not send email/messages/tasks, invoke external integrations, conceal generated content, or export unreviewed/private data by default.
- Dependencies and decisions: S2 provenance/review and S3 recall scope govern all inputs. Recipe is versioned local template with allowed source types/required review/output schema; execution produces a mutable draft with input fingerprints/citations/freshness, never an authoritative action.
- Implementation slices: 1. Define recipe registry/version/migration/permission and bounded local input selection. 2. Build workbench draft/validation/review/stale/conflict lifecycle; no background regeneration overwrite. 3. Require explicit Copy/Save/Export destination/format/preflight/confirmation with preview/redaction/atomic write and receipt; clipboard/export never triggers send. 4. Preserve failed/cancelled output safely and support delete/rebuild under S6.
- Tests and rendered checks: recipe schema/input scope, provenance/stale/review, export/clipboard cancel/path/error/redaction/atomicity, no external dispatch, keyboard/accessibility/long strings.
- Documentation / installer / release work: document local draft/export responsibility and recipe limits; package gates after behavior change.
- Evidence and date: no recipe/workbench/export safety/provenance contract found.
- Remaining gap or next action: create pure recipe/draft/export fixtures before UI.

- Add a bottom workbench with `Ask`, `Actions`, and `Transcript` modes.
- Deliver only four initial recipes: `Action list`, `Client follow-up`,
  `Internal recap`, and `Decision log`.
- Keep every result local and editable; users explicitly copy or export it. Do
  not send email, Teams, CRM, Slack, or task-system updates in v1.
- Send only explicitly scoped local content to configured providers, retain
  no-web-search behavior, and disclose when a hosted fallback would leave the
  device.
- Acceptance: a user can produce and review each output within one or two clicks
  after a meeting without a new integration setup flow.

### Sprint 5: Preparation And Relationship Memory

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on blocked meeting context, evidence, and recall contracts).
- User outcome: users can prepare using transparent local memory without false personal facts or hidden cross-meeting sharing.
- Scope / non-goals: add local people/project/vocabulary memory and briefs. Do not scrape contacts/calendar, infer sensitive traits, create organization profiles, send/share data, or present recalled inference as fact.
- Dependencies and decisions: S1 context/S2 evidence/S3 retention access scope apply. Memory entity has stable local id, user-created/approved source, revision, provenance/freshness/confidence/conflict/tombstone and allowed scope; inferred candidate stays draft until review. Vocabulary feedback changes local recognition preference only with explicit review/version/rollback.
- Implementation slices: 1. Define entity/relationship/brief schema and source-link rules. 2. Add user review/edit/delete/conflict/retention and no-auto-merge behavior. 3. Generate local brief with cited current/historic facts and explicit unknown/stale sections, no provider/external data without consent. 4. Make vocabulary change auditable, bounded and testable against recognition regression.
- Tests and rendered checks: source scopes/conflicts/deletion/tombstone, inference review, brief grounding/stale/unknown, vocabulary rollback/no runtime hint leak, accessibility/privacy states.
- Documentation / installer / release work: document local-only memory/review/delete/limits; package gates later.
- Evidence and date: no people/project memory or vocabulary review/provenance contract found.
- Remaining gap or next action: define local entity/source envelope before capture or recall integration.

- Add pre-meeting briefs, daily prep, recurring-meeting detection, and
  project/client grouping; prefer silence when local context is insufficient.
- Add local People/Companies views from attendees, key attendees, calendar
  enrichment, project/client fields, and speaker-name corrections.
- Add local vocabulary and correction feedback for names, acronyms, and domain
  terms while retaining manual project tags and speaker editing.
- Present past decisions, unresolved reviewed actions, and relevant meeting
  links; do not generate generic AI commentary when local context is weak.
- Acceptance: preparation adds useful local context without slowing recording
  startup, and relationship-scoped recall improves as the library grows.

### Sprint 6: Retention, Transparency, And Explicit Export

#### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on blocked Sprints 1–5 data graph and lifecycle contracts).
- User outcome: users can see, retain, export, and delete local intelligence data with clear consequences and source provenance.
- Scope / non-goals: complete local data lifecycle across notes/index/memory/recipes/actions. Do not promise secure erasure beyond storage reality, delete raw meeting artifacts outside chosen policy, export hidden/private source data, or send data externally.
- Dependencies and decisions: S1–S5 derived data graph and existing artifact retention are authority. Define data classes/owners/paths/derived dependencies/legal/user holds, retention default/override/expiry, provenance and deletion semantics. Derived index/memory/export cache is rebuildable; source artifact is authoritative and deletion cascades only disclosed local derived nodes.
- Implementation slices: 1. Create retention resolver/receipt with clock/version/hold/conflict handling. 2. Add transparency inventory per item/source/freshness/access/retention and local diagnostics redaction. 3. Implement explicit export preview/select/schema/redaction/atomic destination/receipt and delete/undo-or-no-undo confirmation/rebuild logic. 4. Run privacy/access/corrupt/restart/migration/export retention matrix; define support/release docs.
- Tests and rendered checks: class/dependency retention, expiry/hold/user override, deletion cascade/no-source-overreach, export validation/cancel/redaction, provenance/access UI and keyboard/high contrast.
- Documentation / installer / release work: docs disclose data classes/paths/retention/export/delete limitations; implementation package/release gates later.
- Evidence and date: no unified lifecycle/transparency/export contract across intelligence artifacts found.
- Remaining gap or next action: inventory existing artifact paths/classes before persistence work.

- Add local retention choices for audio, transcript, notes, workbench history,
  and exports. Warn before destructive actions and explain every capability that
  will degrade after deletion.
- Add capture, consent, and AI-provider disclosure plus explicit local export
  packages and documentation.
- When retained notes or actions lose a transcript source, keep them readable
  but mark their evidence `Source unavailable`; do not generate a replacement
  claim.
- Defer read-only local MCP access until source provenance and local search are
  demonstrably reliable.
- Acceptance: users can explain what was captured, where it lives, what leaves
  the machine, what is deleted, and what remains usable afterward.

## Public Interfaces And Artifact Boundaries

- Add `<stem>.notes.json`, `<stem>.actions.md`, `<stem>.decisions.md`,
  `<stem>.followup.md`, and `<stem>.brief.md` alongside existing local artifacts.
- Add `MeetingNotesStore`, `MeetingAiWorkbenchService`, `MeetingRecipeService`,
  `MeetingCaptureConfidenceService`, `MeetingSearchIndex`,
  `PreMeetingBriefService`, `MeetingVocabularyService`, and local
  People/Companies indexing services.
- Add `MeetingEvidenceRef` with stem, artifact kind, speaker, timestamp range,
  and excerpt fingerprint. `MeetingSearchIndex` remains a rebuildable local
  cache, not a cloud or vector dependency.
- Extend `MeetingOutputRecord` with local notes/action/brief paths, availability
  flags, relationship-index keys, and generated-workbench status. Extend active
  manifests with raw-note draft path, capture-confidence snapshots, consent and
  retention snapshots, and an optional vocabulary snapshot identifier.
- Rename, archive, and delete include companion artifacts. Merge/split preserves
  original companion artifacts as provenance and creates fresh editable content
  for derived meetings; user-authored content is never silently combined.
- Preserve existing `.wav`, `.md`, `.json`, manifest, summary snapshot, and
  `.ready` semantics. Optional workbench failures must not block transcript
  publication.

## Test And Acceptance Additions

- Test calendar and ad-hoc launch prompts without external-cloud availability;
  test active-recording visibility from `Home`, `Meetings`, and detail windows.
- Test flat audio, missing chunks, stale writes, endpoint changes, and failed
  transcription for explicit recovery rather than silent failure.
- Test raw-note preservation, evidence links, scoped local search/Q&A, action
  editing, search-index rebuild/direct-scan fallback, and prevention of external
  writes.
- Test retention degradation: retained notes stay usable while regeneration,
  source inspection, or chat correctly explain unavailable source material and
  mark evidence `Source unavailable`.
- Test bot-free explicit capture, no-web-search provider calls, hosted-provider
  disclosure, and companion-artifact rename/archive/delete/merge/split flows.
- For shipped code changes, run `powershell -ExecutionPolicy Bypass -File
  .\scripts\Test-All.ps1`; run `dotnet test
  .\tests\AppPlatform.Tests\AppPlatform.Tests.csproj -p:NuGetAudit=false` when
  shared AppPlatform surfaces change; rebuild installer assets and update
  applicable docs.

## Assumptions

- Custom note templates remain out of v1; focused recipes and
  regenerate-with-feedback cover the immediate need.
- All sharing and export remains explicit and local-first. No cloud
  transcription, account login, cross-device sync, workspace sharing, bots, or
  external system writes appear in these sprints.
- Live transcription remains deferred; this roadmap adopts Otter's recall and
  reviewed follow-through value, not its collaboration or automation model.
- UI follows `DESIGN.md`: dense opaque surfaces, tonal nesting, no shadows, 4px
  radius, technical wells, Segoe UI for human text, and Cascadia Mono/Consolas
  for capture state and timestamps.

## Research Sources

### Granola Workflow And Constraints

- [Granola homepage](https://www.granola.ai/)
- [Writing your own notes](https://docs.granola.ai/help-center/taking-notes/taking-notes-in-granola)
- [AI-enhanced notes](https://docs.granola.ai/help-center/taking-notes/ai-enhanced-notes)
- [Notifications](https://docs.granola.ai/help-center/taking-notes/notifications)
- [Pre-meeting briefs](https://docs.granola.ai/help-center/taking-notes/pre-meeting-briefs)
- [Follow-up emails](https://docs.granola.ai/help-center/taking-notes/follow-up-emails)
- [Chatting with your meetings](https://docs.granola.ai/help-center/getting-more-from-your-notes/chatting-with-your-meetings)
- [Recipes](https://docs.granola.ai/help-center/getting-more-from-your-notes/recipes)
- [People and Companies](https://docs.granola.ai/help-center/people-and-companies)
- [Spaces and Folders](https://docs.granola.ai/help-center/sharing/folders/spaces-and-folders)
- [Customizing transcription](https://docs.granola.ai/help-center/customising-granola/customising-transcription)
- [Transcript auto-deletion](https://docs.granola.ai/help-center/consent-security-privacy/transcript-auto-deletion)
- [Product Hunt reviews](https://www.producthunt.com/products/granola/reviews)
- [Efficient App review](https://efficient.app/apps/granola)
- [The Verge privacy critique](https://www.theverge.com/ai-artificial-intelligence/906253/granola-note-links-ai-training-psa)

### Fireflies Product And Review Signals

- [Fireflies product overview](https://fireflies.ai/)
- [Fireflies desktop capture workflow](https://guide.fireflies.ai/articles/1208704416-getting-started-with-the-fireflies-desktop-app)
- [Fireflies Live Assist](https://guide.fireflies.ai/articles/6032274417-learn-about-fireflies-live-assist-get-real-time-suggestions-answers-and-notes-live-during-the-meeting)
- [Fireflies conversation intelligence](https://fireflies.ai/conversation-intelligence)
- [Capterra reviews](https://www.capterra.com/p/197037/Fireflies/reviews/)
- [Trustpilot reviews](https://www.trustpilot.com/review/fireflies.ai)
- [Reddit discussion: searchable history and actions](https://www.reddit.com/r/AiAutomations/comments/1s1dgip/i_tracked_every_meeting_for_60_days_with_ai_heres/)

### Otter Product And Review Signals

- [Otter product](https://otter.ai/)
- [Otter AI Chat](https://help.otter.ai/hc/en-us/articles/19682180167575-Otter-AI-Chat-Overview)
- [Otter slide capture](https://help.otter.ai/hc/en-us/articles/5093321813911-Automated-Slide-Capture-Overview)
- [G2 reviews](https://www.g2.com/products/otter-ai/reviews)
- [Capterra reviews](https://www.capterra.com/p/202799/Otter/reviews/)
- [Reddit: live transcript and local-first preference](https://www.reddit.com/r/ProductivityApps/comments/1t49uzz/meeting_note_taker_ranking_google_zoom_notion/)
- [Reddit: intrusive bot concerns](https://www.reddit.com/r/PKMS/comments/1kmnmjd/is_otterai_worth_it_for_meeting_minutes/)

# Self-Serve Release Remediation Program

## Summary

This roadmap makes Meeting Recorder approachable for low-technical-skill Windows
users. It overrides earlier public-UX, setup-choice, and no-removal policies.
The Production Capture program remains authority for capture safety, data
retention, and the approved transcription profile; this program owns public
distribution, first-run setup, settings complexity, and release qualification.

Current friction to remove:

- an unsigned public MSI, plus MSI, PowerShell/CMD bootstrap, ZIP, and portable
  choices;
- the MSI reports successful installation after a quiet, post-finalize
  best-effort model download even when recording remains blocked;
- separate and contradictory model-bundling, download, and recovery claims in
  code, tests, release documentation, and this plan;
- separate `Setup` window, `Settings > Setup`, and advanced configuration;
- an 11-step first-launch guide that promotes Teams probes, model choices,
  providers, paths, GPU, and update details;
- developer and support controls presented beside normal recording controls.

Comparator target: one signed/trusted acquisition path, a short consent-led
onboarding flow, and sensible defaults.

| Product | First-use pattern | Meeting Recorder target |
| --- | --- | --- |
| Granola | Install, sign in, calendar/audio permission | No account; install, preparation, consent, record |
| Zoom | Install, sign in or join | Install, preparation, record |
| Wispr Flow | Install, sign in, microphone permission, resume setup | Install, preparation, microphone permission, resume safely |

Meeting Recorder remains local-first and account-free. Its normal path is:
install, automatic preparation, recording consent, microphone permission, then
first recording.

## Evidence Limit

This assessment is source-grounded. A runnable Meeting Recorder clean-machine
flow was not available to capture, and the local Product Design context
preflight could not run because Python is unavailable. Sprint 0 screenshots,
keyboard checks, and accessibility notes are release evidence, not optional
polish.

## Fixed Decisions And Constraints

- Consumer distribution is Microsoft Store MSIX. Microsoft Store owns Store
  updates. Store MSIX is re-signed after certification and does not require a
  separately purchased trusted code-signing certificate.
- Direct per-user MSI remains publicly available by explicit business-risk
  acceptance while it is unsigned. It must never be described as trusted,
  warning-free, or equivalent to the Store path. Self-signed certificates remain
  development/testing-only.
- MSI, MSIX, ZIP, and portable payloads exclude model assets. A packaged,
  release-pinned capability manifest supplies exactly one quality-approved
  transcription profile and one speaker-labeling bundle, with HTTPS locations,
  expected sizes, versions, and SHA-256 hashes.
- After package installation, every interactive channel opens the same branded
  `Getting Meeting Recorder ready` screen. It downloads, validates, and
  atomically promotes both required assets before Home opens. It shows progress,
  retry, help, and exit only; it never sends a normal user into Settings.
- Silent MSI installation installs the app only; the first interactive launch
  enters preparation. Do not perform network work inside an MSI transaction.
- The approved transcription profile is selected only after the Production
  Capture benchmark gate passes. Do not hard-code the current base model as the
  consumer default.
- Core promise is record, transcribe, and label speakers. AI summaries are not a
  first-run or release-critical capability.
- Publish transcript text first; run speaker labeling automatically in the
  background and visibly update the completed transcript when labels are ready.
- Keep one Advanced section for real power-user and support work; remove
  developer-oriented controls from normal Settings.
- Feature retirement uses code/test inventory, release/support history, and
  product-owner sign-off. Do not add usage telemetry.
- Preserve all existing published artifacts, local settings, custom models,
  recordings, transcripts, and readable historic summaries through migration.

## Status Ledger

| Sprint | Status | First acceptance focus |
| --- | --- | --- |
| 0 — Clean-Machine Evidence And Retirement Ledger | `Ready` | Clean-machine journey protocol, public-control inventory/retirement ledger, evidence segregation, and no-regression baseline defined below. |
| 1 — Microsoft Store Package And Channel Contract | `Ready` | Store/MSIX capability/package identity, worker/data/update boundary, channel migration/rollback, and clean-install validation plan defined below. |
| 2 — Automatic Capability Preparation | `Ready` | Signed/hashed capability catalog, resumable atomic preparation, truthful readiness/recovery, and clean-machine verification plan defined below. |
| 3 — Single First-Run Journey | `Ready` | Consent-led resumable onboarding, safe capture/transcript boundaries, direct recovery, and accessibility plan defined below. |
| 4 — Settings Reduction And Feature Retirement | `Ready` | Retirement-led settings ownership, backward-compatible migration, advanced/support routing, and accessibility plan defined below. |
| 5 — Distribution, Trust, And Public Documentation | `Ready` | Evidence-gated Store-first presentation, honest MSI/portable boundaries, provenance, and support-documentation plan defined below. |
| 6 — Release Qualification | `Ready` | Traceable clean-machine/channel/accessibility/usability qualification matrix, no-go criteria, and authorized release-evidence plan defined below. |

## Sprint 0: Clean-Machine Evidence And Retirement Ledger

### Implementation Record

- Status: `Partial`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Partial` (2026-09-29: reusable disposable-device reset and evidence checklist created; no clean-machine claim made).
- User outcome: self-serve work removes real first-run friction based on clean-machine evidence, not assumptions.
- Scope / non-goals: inventory/baseline only. Do not retire controls/change installer/store/package/settings, collect private user data, or treat developer machine behavior as clean-machine proof.
- Dependencies and decisions: current MSI/portable/update/setup/capture contracts and design guidance are authority. Define VM/physical clean-machine reset/image/version/network/permission/model state, synthetic consent-safe journey fixtures, observer checklist and source/test/package/installed/live evidence separation.
- Implementation slices: 1. Capture first install/launch/model prep/record/publish/reopen/update/uninstall/recovery journey timings/errors/controls with redacted screenshots. 2. Build public-control inventory: normal user, advanced/support, developer/deprecated; owner, dependency, telemetry-free evidence, replacement/retirement/migration/rollback. 3. Gate retirement on user task parity/accessibility/support route, not hidden control count.
- Tests and rendered checks: repeatable clean-reset checklist, journey/action inventory, viewport/keyboard/Narrator screens, installer/update evidence and no private capture.
- Documentation / installer / release work: store baseline/retirement ledger in repo docs; no package change.
- Evidence and date: 2026-09-29 added `docs/clean-machine-baseline.md`, a disposable VM/device reset record, generated-fixture journey, evidence-redaction, and control-retirement ledger checklist. No clean-machine observation or product-owner retirement approval is claimed.
- Remaining gap or next action: execute the checklist on a clean machine and complete the public-control inventory before UI changes.

Goal: replace source-only assumptions with real low-technical-user evidence
before changing public workflows.

- Capture fresh MSI and Store-candidate acquisition, install, first launch,
  permission, first recording, first transcript, first labels, update, and
  recovery states. Save ordered screenshots and keyboard/accessibility notes.
- Record completion time, user decisions, unfamiliar terms, installer warnings,
  blocked points, and exact recovery copy. Keep production data and credentials
  out of evidence.
- Inventory every visible installation, setup, Settings, Help, and update
  control: user outcome, default, config key, source/test ownership, support
  history, current documentation, and retain/relocate/retire recommendation.
- Obtain product-owner sign-off for each retirement. A code reference or test
  alone does not prove customer usage.
- Reconcile all model-delivery claims in packaging scripts, installer tests,
  `SETUP.md`, `RELEASING.md`, `ARCHITECTURE.md`, and this plan before
  release. Treat current seed-asset claims as defects until code proves them.
- Replace conflicting Whole-App UX and GPU-plan claims with cross-links to this
  roadmap; the quality plan remains the source for profile approval.

Acceptance:

- Baseline includes valid screenshots for every important step or a named
  blocker.
- Each public control has one accountable disposition and migration consequence.
- Team can state the three-step normal journey in plain language.

## Sprint 1: Microsoft Store Package And Channel Contract

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires the clean-machine baseline and public-control ledger from Sprint 0).
- User outcome: a Store install behaves as the complete product, with a clear Store-owned update path and no broken worker or data assumptions.
- Scope / non-goals: define the MSIX/Store feasibility, package, and channel contract. Do not submit a Store package, change the existing MSI channel, bypass Store policy/signing, assume unrestricted filesystem/process behavior, or replace user data without migration and rollback proof.
- Dependencies and decisions: Sprint 0 clean-machine baseline plus the product manifest, worker, launcher, updater, data-root, and Store-policy evidence are authority. Define package family identity, capabilities, entry points, worker activation, data roots, model assets, crash diagnostics, and update ownership. Store must disable or truthfully route the in-app updater; MSI/portable remain separately identified with non-destructive migration and side-by-side rules.
- Implementation slices:
  1. Produce an MSIX feasibility/capability matrix covering restricted APIs, native dependencies, worker/process launch, audio, filesystem, and model-download implications, with explicit stop gates.
  2. Define the packaging, install, update, uninstall, data-persistence, migration, and rollback contract.
  3. Build clean Store-install tests for launch, worker, capture, model preparation, update, downgrade, repair, and Store-sandbox paths using synthetic data.
  4. Separate Store/MSI copy, support provenance, and release evidence so neither channel presents a double-update path or launcher loop.
- Tests and rendered checks: verify manifest capabilities/identity, worker launch and data roots, model/cache persistence, Store update/rollback/uninstall, channel migration/side-by-side behavior, and accessible first launch. Actual Store submission remains separately authorized.
- Documentation / installer / release work: document channel ownership, limits, and recovery. Store packaging/release requires explicit account authority and validation.
- Evidence and date: no Store MSIX channel contract or clean-install worker/update proof found as of 2026-09-27.
- Remaining gap or next action: run the feasibility matrix before creating MSIX artifacts.

Goal: establish a consumer-safe installation/update path without breaking the
existing direct MSI channel.

- Add `MeetingRecorder.Store` MSIX packaging project and `Build-Store.ps1`.
  Target Windows Desktop x64; apply Partner Center package identity, full-trust
  desktop execution, application assets, and only capabilities proved necessary
  by package certification and runtime tests.
- Package the WPF app, processing worker, launcher-independent runtime files,
  product manifest, and release-pinned capability manifest. Do not package
  model assets or PowerShell/CMD bootstrapper scripts as public Store flow.
- Add `AppDistributionChannel` (`Store`, `DirectMsi`, `Portable`, `Unknown`) and
  `UpdateCapability` (`StoreManaged`, `DirectManaged`, `Unsupported`). Resolve
  channel from package identity plus existing install provenance.
- Store channel never downloads, stages, or applies GitHub updates. Its update
  UI says `Updates are managed by Microsoft Store` and optionally opens the
  product page. Direct MSI keeps its current direct-update path.
- Establish one canonical per-user data root and migrate existing direct-install
  data before Store use. Preserve recordings, transcripts, summaries, custom
  models, and voice profiles; never delete first and then attempt migration.
- Prove package behavior with Windows App Certification Kit and an actual Store
  private flight. Keep Store package identity/certification values out of source
  secrets and release logs.

Acceptance:

- Store package installs, launches, starts worker processing, updates through
  Store, and preserves user data.
- Store build exposes neither direct update action nor script/ZIP recovery path.
- Direct MSI behavior remains isolated from Store update state.

## Sprint 2: Automatic Capability Preparation

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires the blocked Store/channel contract from Sprint 1).
- User outcome: required default capabilities become ready automatically, with understandable progress and recovery, without making a normal user configure models.
- Scope / non-goals: prepare only an approved capability set through the product-owned workflow. Do not silently download unapproved content, send user data, expose source paths or provider tuning in normal setup, delete valid custom assets, or claim readiness before verification.
- Dependencies and decisions: Sprint 1 defines the package/channel/data-root contract. A signed or otherwise release-pinned approved catalog is authoritative for asset identity, version, size, hash, provenance, compatibility, and delivery policy. Define durable states `pending`, `downloading`, `verifying`, `ready`, `blocked`, `failed`, and `cancelled`, plus resumable ownership and a single visible readiness projection.
- Implementation slices:
  1. Implement catalog validation and a per-capability state machine with idempotent leases, resumable transfers, staged temporary files, hash/size verification, and atomic promotion.
  2. Add policy checks for offline, metered network, disk space, power, cancellation, retry/backoff, and concurrent launch without blocking safe capture or corrupting a cache.
  3. Present plain-language combined preparation progress with one safe next action for every blocked/failed state; retain advanced/support diagnostics behind an explicit route.
  4. Migrate/cache legacy valid assets and channel changes non-destructively, then reconcile stale partial files and duplicate work on startup.
- Tests and rendered checks: cover catalog signature/hash/version rejection, interrupted/resumed download, no-network, low disk, cancellation, retry, duplicate concurrency, atomic promotion, cache migration/update, offline first launch, keyboard/Narrator status, and clean-machine MSI/Store paths with synthetic assets.
- Documentation / installer / release work: document network, storage, privacy, data-retention, recovery, and support boundaries; release artifacts must publish capability provenance and verification evidence.
- Evidence and date: no approved catalog, resumable atomic preparation flow, or clean-machine readiness/recovery proof found as of 2026-09-27.
- Remaining gap or next action: specify the approved capability catalog and failure-state copy before replacing installer provisioning.

Goal: keep the package small while making the required capabilities ready
without configuration or false-success installation.

- Remove `ModelOptionsDlg`, model-option MSI properties, the post-
  `InstallFinalize` quiet `provision-models` action, and exit copy that says
  setup merely “tried” downloads.
- Add `CapabilityPreparationState`, `ApprovedCapabilityManifest`, and
  `FirstRunPreparationService`. Download to temporary files, validate
  size/hash, atomically promote to the writable cache, clean partial files, and
  resume safely after restart.
- Start preparation automatically after Store or MSI installation. The screen
  has one combined readiness state, plain-language progress, retry/help/exit,
  and no model names, profile choices, paths, source lists, or imports.
- Preparation waits for both approved assets. A blocked network, bad hash,
  insufficient disk, or interrupted download is an explicit recoverable state,
  never a successful install that later blocks recording.
- Preserve valid legacy custom and Higher Accuracy assets across repair, update,
  and channel migration, but keep them out of normal setup. Advanced remains the
  only expert-management location.

Acceptance:

- Fresh Store and MSI users reach `Ready to record` only after both assets
  verify, without opening Settings or choosing models.
- No package includes model seed assets; partial or mismatched downloads never
  become active capability files.

## Sprint 3: Single First-Run Journey

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires blocked capability preparation and clean-machine evidence).
- User outcome: a new user understands capture scope, grants only needed permission, records once, and finds the result without opening Settings or learning implementation vocabulary.
- Scope / non-goals: replace configuration-first setup with a short, Home-led, consent-led path. Do not start capture without explicit user action/consent, make microphone inclusion implicit, force auto-detection or launch-at-login, hide a blocked state, or send transcript/audio outside the local contract.
- Dependencies and decisions: Sprint 2 owns capability preparation; capture admission remains separate from transcription readiness. Define a versioned durable onboarding state with fulfilled consent/permission/preparation/first-record/publish checkpoints, a migration rule for existing users, and one authoritative next-action resolver so refreshes and restarts cannot loop or skip a consent boundary.
- Implementation slices:
  1. Model the first-run state machine and its idempotent persistence, migration, interruption/resume, reset/support path, and explicit consent receipt.
  2. Build a Home-led sequence: local recording notice and consent; microphone choice/Windows permission when requested; visible capability readiness/recovery; manual `Start recording`; capture, stop, processing, and published-result route.
  3. Offer meeting assistance only after the first successful recording as an optional, reversible decision; retain manual recording as the default.
  4. Map every prerequisite failure to one direct safe action (retry, Windows Settings, free space, preparation recovery, or privacy-safe diagnostics) and preserve raw capture when transcription preparation later fails.
- Tests and rendered checks: cover fresh/upgrade onboarding migration, each interrupted checkpoint, denied/revoked microphone permission, no network/low disk, duplicate window/app launches, consent revocation, safe capture while transcription is unavailable, published transcript before background labels, keyboard focus/Escape, Narrator names, high contrast, and 200% scaling with synthetic recordings.
- Documentation / installer / release work: update public first-run copy, consent/privacy wording, support recovery, and channel-specific readiness expectations; no data migration ships without rollback and compatibility documentation.
- Evidence and date: no end-to-end resumable first-run journey or clean-machine accessibility proof found as of 2026-09-27.
- Remaining gap or next action: approve the exact consent wording and onboarding-state schema before deleting Setup routes.

Goal: replace configuration-first setup with one consent-led path to a first
useful recording.

- Remove the standalone `SetupWindow` and `Settings > Setup`; replace them with
  a versioned `OnboardingState` and a Home-led first-run checklist.
- Ask only what changes recording scope: recording/consent notice, whether to
  include microphone audio, and Windows microphone permission. Capability
  preparation has already completed and needs no configuration screen.
- Make manual recording default. After first successful recording, offer one
  optional `Stay ready for meetings` decision that maps to auto-detection and
  launch-at-login. Do not ask this before first value.
- Suggest, but do not gate on, a short test recording. Home then answers only:
  `Can I record?`, `What is happening?`, and `What should I do next?`.
- Keep capture admission separate from readiness: model failure may block
  transcription, never safe audio capture or preservation.
- Give each blocking state one direct action: retry permission, open Windows
  Settings, free disk space, retry preparation, or copy safe support diagnostics.

Acceptance:

- A fresh user records without opening Settings or understanding models,
  providers, Teams, GPU, paths, or update mechanics.
- Interrupted onboarding resumes at the exact unmet consent/permission step.
- A transcript publishes before automatic background labels; later labeling
  failure never discards completed transcript text.
- Keyboard focus, accessible names, Escape behavior, 200% scaling, and error
  recovery work across the whole journey.

## Sprint 4: Settings Reduction And Feature Retirement

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: requires the Sprint 0 product-owner retirement ledger and blocked onboarding contracts).
- User outcome: normal Settings has understandable choices for recording and local data, while supported expert control remains findable and no existing data/configuration disappears unexpectedly.
- Scope / non-goals: simplify public settings using the Sprint 0 retirement ledger. Do not remove a capability solely because it is technical, strand existing deep links/support procedures, delete configuration/artifacts, present developer controls as normal choices, or convert Advanced into a second onboarding flow.
- Dependencies and decisions: Sprint 0 requires a signed-off disposition/replacement/owner for every retired or relocated control; Sprints 1-3 own channel, capability, and onboarding copy. Define canonical settings ownership, normal versus advanced/support visibility, config version/migration/round-trip behavior, deep-link aliases, permission/consent boundaries, and rollback for each retirement.
- Implementation slices:
  1. Reconcile the full control/config inventory to `Recording`, `Files & privacy`, `Advanced`, or contextual/support ownership; reject missing replacements and ambiguous dual ownership.
  2. Introduce additive config migrations and a `MeetingOutputRoot` plan with preflight, atomic/non-destructive moves, restart recovery, old-read/new-write compatibility, and explicit rollback.
  3. Move supported specialist controls behind an explicit Advanced/support route with plain outcomes and safe diagnostics, while removing raw provider, package, schedule, path, and update-feed controls from normal settings.
  4. Preserve old navigation/deep links with an explanatory redirect, disable retirement only after a compatibility release and approved evidence, and validate no consent or destructive action has been hidden.
- Tests and rendered checks: test legacy config/artifact load, migration interrupted at every file operation, directory validation/rollback, config round-trip, deep-link routing, advanced discoverability, permissions/consent, screen-reader heading/control semantics, keyboard focus, 200% layout, and normal-settings vocabulary snapshot checks.
- Documentation / installer / release work: update setting ownership, data-root migration/recovery, Advanced/support guidance, release notes, and installer/upgrade messaging in the shipping change.
- Evidence and date: no approved retirement ledger linked to exact migrations, deep-link behavior, or rendered normal-settings proof found as of 2026-09-27.
- Remaining gap or next action: turn the Sprint 0 control ledger into a signed per-control disposition matrix before any public control is removed.

Goal: preserve expert control without presenting a development sandbox as the
product.

- Reduce Settings to `Recording`, `Files & privacy`, and `Advanced`. Remove
  `Setup` and `Updates` navigation.
- `Recording` contains microphone scope and one meeting-assistance control.
  `Files & privacy` contains one meeting-output root, change/open folder actions,
  local-data explanation, and explicit speaker-name-learning consent.
- Add a backward-compatible `MeetingOutputRoot` migration. Existing audio and
  transcript paths map to subfolders under the selected root; validate before
  moving or deleting anything.
- `Advanced` retains custom/Higher Accuracy model management, DirectML
  preference, safe diagnostics, and supported troubleshooting. It is clearly
  separated from daily settings.
- Remove from public UI: external CLI providers, remote model-source lists,
  Teams Graph/third-party probes, raw update-feed override, package metadata,
  processing schedules, ModelProxy/OpenAI endpoint/model/timeout/chunk controls,
  and summary setup. Historic summaries remain readable.
- Maintain deserialization compatibility for retired settings. Stop emitting
  retired fields after migration. Delete retired runtime code only after Sprint 0
  evidence and one compatibility release establish no support commitment.

Acceptance:

- Normal Settings contains no developer vocabulary or raw infrastructure input.
- Advanced is discoverable, bounded, and does not become a second setup flow.
- Existing config and artifacts load without data loss or surprise re-consent.

## Sprint 5: Distribution, Trust, And Public Documentation

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: depends on blocked channel, onboarding, and settings evidence).
- User outcome: people can choose the appropriate distribution channel from clear, truthful guidance and receive only the support detail appropriate to that channel.
- Scope / non-goals: define acquisition, trust, release-provenance, and public/support documentation contracts. Do not claim Store availability before publication, minimize or bypass Windows security warnings, publish hashes/signatures without release evidence, expose developer-only installers/scripts to normal users, or collect telemetry to decide copy.
- Dependencies and decisions: Sprint 1 establishes channel ownership; Sprint 4 establishes public versus advanced/support terminology. Store-first wording is conditional on an actual published, certified Store listing. Define channel selector rules, release version/build provenance, signature/hash verification source, support escalation route, rollback/revocation notice, and an explicit portable/developer artifact fence.
- Implementation slices:
  1. Write a channel capability/trust matrix for Store, direct MSI, and portable/support paths: audience, update owner, signing state, install/update/recovery route, data migration, limitations, and supported lifecycle.
  2. Create short public install/permission/record/find-result guidance with a Store CTA only when publication evidence exists; otherwise provide accurate availability state without a false primary path.
  3. Separate detailed engineering/support documentation, shell commands, model/runtime diagnostics, and recovery instructions from the public journey, with channel-specific provenance and safe diagnostic collection.
  4. Gate every artifact/link/trust claim on release metadata, signature verification, checksum/source validation, and documented rollback/revocation behavior; keep unsigned-MSI risk prominent and never instructional about evasion.
- Tests and rendered checks: verify link/version/provenance consistency, Store-unavailable copy, signature/hash failure copy, MSI warning disclosure, no public shell/model/path jargon, accessible CTA/focus/keyboard behavior, support route completeness, and documentation review against a synthetic release manifest.
- Documentation / installer / release work: update README/public install guide, SETUP, RELEASING, architecture/support docs, product pages, release notes, and installer copy as applicable; external publication/upload remains explicitly authorized release work.
- Evidence and date: no certified public Store listing, channel/provenance matrix, or evidence-backed public trust copy found as of 2026-09-27.
- Remaining gap or next action: define the release-manifest fields and publication-evidence gate before editing public download CTAs.

Goal: make acquisition simple and explain residual direct-MSI risk honestly.

- Make Microsoft Store the primary public download CTA. Present direct MSI as a
  clearly secondary alternative; do not put ZIP, portable, CMD, or PowerShell
  paths in normal download or first-run guidance.
- State plainly that an unsigned MSI can trigger Windows warnings and recommend
  Store when that occurs. Do not provide copy intended to bypass or minimize a
  security warning.
- Keep portable and bootstrap scripts as support/developer artifacts only.
- Require explicit `-AllowUnsignedPublicMsi` acknowledgement for any unsigned
  public-MSI release. Release evidence records signature status and prevents any
  `signed`, `trusted`, or `warning-free` claim when status is `NotSigned`.
- Split documentation into a short public guide—install, allow microphone,
  record—and separate engineering/support material. Public guide contains no
  shell commands, model file names, raw paths, or provider configuration.

Acceptance:

- Store-first user completes install from one product page and one installer.
- Public MSI documentation accurately declares the accepted trust limitation.
- Support retains deep recovery material without burdening normal users.

## Sprint 6: Release Qualification

### Implementation Record

- Status: `Blocked`
- Status history: `Planned`; `Ready` (2026-09-27 pressure test); `Blocked` (2026-09-29: cannot qualify a release before clean-machine/channel/accessibility evidence exists).
- User outcome: a release decision is based on reproducible clean-machine, channel, accessibility, and usability evidence—not on source completeness or a developer-machine success.
- Scope / non-goals: define the release qualification matrix and evidence gates for the preceding sprints. Do not self-certify Store acceptance, treat a test VM as a live-user study, ship/publish/upload, waive a failed gate, or infer production readiness from a passing build.
- Dependencies and decisions: Sprints 0-5 provide their acceptance evidence. Define environment/image/build/artifact provenance, deterministic synthetic fixtures, channel/device/upgrade coverage, test ownership, severity/no-go rules, defect triage/retest criteria, evidence retention/redaction, and the named authority required for Store submission or external release.
- Implementation slices:
  1. Build a traceability matrix from every Sprint 0-5 acceptance criterion to focused tests, rendered/accessibility checks, clean-machine scenario, evidence owner, pass threshold, and release-gate result.
  2. Automate safe deterministic checks for channel detection/update suppression, capability preparation/recovery, onboarding, permission recovery, config/data-root migration, retired-config compatibility, and package integrity; keep private audio/transcripts out of fixtures and logs.
  3. Execute isolated Windows 10/11 clean-machine matrices for Store (when authorized/available), MSI, and supported portable paths: install/upgrade/repair/uninstall/reinstall, worker/capture/publish, offline/network/disk failures, update/rollback, and data preservation.
  4. Run rendered accessibility and observed usability protocols with explicit consent: keyboard/Narrator/high-contrast/200% scaling and representative first-record journeys. Record assistance, completion, comprehension, failures, and remediation without treating a participant as telemetry.
  5. Enforce release no-go criteria, signed evidence review, regression retest, release notes/support readiness, installer rebuild/smoke, and channel-specific authorization before any external action.
- Tests and rendered checks: run focused deterministic tests, `scripts\Test-All.ps1`, AppPlatform tests when shared platform changes, installer build and packaged smoke for shipping changes; validate Store package/private flight only with authorized account access; repeat visual/accessibility and clean-machine matrices after material fixes.
- Documentation / installer / release work: complete public/support/architecture/release documentation, installer assets, channel release notes, provenance, and rollback guidance in the shipping task. Publication, Store submission, and user studies require their own scoped authority.
- Evidence and date: no complete traceability matrix, clean-machine qualification set, Store-private-flight proof, or consented usability/accessibility evidence found as of 2026-09-27.
- Remaining gap or next action: create the release-evidence matrix and assign owners before running any qualification environment.

Goal: prove simple behavior on real clean machines before public shipment.

- Add tests for distribution-channel detection, Store-update suppression,
  preparation-state resume, partial-download cleanup, capability hash mismatch,
  model-cache migration, onboarding resume, denied-permission recovery,
  output-root migration, and retired-config loading.
- Add rendered and accessibility checks for focus order, keyboard-only actions,
  target sizes, contrast, screen-reader labels, permission states, error states,
  and 200% scaling.
- Qualify clean Windows 10/11 x64 VMs: Store install/update, direct MSI install,
  preparation interruption/network failure, upgrade from current release,
  manual recording, automatic speaker labels, uninstall/reinstall data
  preservation, and recovery paths.
- Run five observed low-technical-user usability sessions. Release only after
  each user can install, understand capture scope, complete a recording, and
  find its transcript without assistance.
- Run targeted tests, `scripts\Test-All.ps1`, AppPlatform tests,
  `scripts\Build-Installer.ps1`, Store package validation, Store private flight,
  and packaged smoke tests. Update public docs, support docs, architecture,
  release notes, and installer assets in the same release.

Acceptance:

- Store path is production-ready with clean-machine and usability evidence.
- Unsigned MSI remains explicitly documented business risk, not a production
  trust success.
- On the reference connection, preparation reaches `Ready to record` within
  five minutes. The normal journey has at most two decisions: recording consent
  and microphone permission.

## Public Interfaces And Compatibility

- Add `AppDistributionChannel`, `UpdateCapability`, `OnboardingState`,
  `CapabilityPreparationState`, `ApprovedCapabilityManifest`, and
  `FirstRunPreparationService`.
- Preserve existing `.wav`, `.md`, `.json`, manifest, summary snapshot, and
  `.ready` contracts. Use additive, read-compatible settings/artifact changes.
- Store packages use Store updates only. Direct MSI uses direct updates only.
- Do not add telemetry. Use signed support/owner evidence for retirement.

## Required Test Scenarios

- Fresh Store and MSI install completes automatic preparation without Settings,
  scripts, ZIP instructions, model selection, or a false-success installer.
- Interrupted, blocked-network, low-disk, and hash-mismatch preparation paths
  show one recovery action and safely resume.
- Denied microphone permission presents one next action and resumes safely.
- Store package neither presents nor runs direct update code.
- MSI first run presents no script, ZIP, or model-selection path.
- Transcript publishes before background speaker labels; label completion updates
  the visible transcript without user intervention.
- Existing custom models, recordings, transcripts, and readable summaries survive
  upgrade.
- Advanced custom-model path works without changing normal Settings.
- Unsigned MSI release evidence records the public trust risk.

## Research Sources

- [Microsoft Store code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft Store MSIX package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Granola integration setup checklist](https://www.granola.ai/blog/granola-integration-checklist-setup-testing-team-rollout)
- [Zoom desktop getting started](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0064516)
- [Wispr Flow first dictation setup](https://docs.wisprflow.ai/articles/3152211871)
