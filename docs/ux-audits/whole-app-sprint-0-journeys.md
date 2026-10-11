# Whole-App Sprint 0 Journey Baseline

Source-trace baseline dated 2026-09-27. Fixture data is synthetic and redacted:
`Project Northstar`, `Practice call`, and neutral speaker labels. Counts below
include only user activations; waits and automatic refreshes are separate.

## J01 — First setup and first useful recording

- Preconditions: no transcription model configured; empty synthetic library.
- Steps: open Settings; select Setup; choose a transcription profile; return
  Home; enter `Practice call`; start recording; stop recording.
- User activations: 6. Automatic waits: model-readiness refresh and processing.
- Concepts exposed: transcription profile, model storage, readiness, microphone.
- Explicit boundary: microphone capture is an informed choice.
- Blocked/recovery branch: readiness failure routes to Settings > Setup; no
  rendered proof was captured because the only installed instance has a live
  user profile and the app's global single-instance lock prevents a safe
  synthetic instance.
- Outcome: first recording can begin only after setup reaches Ready.

## J02 — Manual recording

- Preconditions: synthetic model-ready profile; Home selected.
- Steps: enter title; optionally add project and attendees; choose microphone
  state; start recording; stop recording.
- User activations: 4 minimum, 6 with optional fields. Automatic waits: elapsed
  timer and processing transition.
- Concepts exposed: recording state, optional metadata, microphone scope.
- Explicit boundary: microphone capture remains direct.
- Blocked/recovery branch: missing readiness routes to J01; denied microphone
  needs an operating-system settings route.
- Outcome: user creates one local recording without opening advanced settings.

## J03 — Assisted auto-detected recording

- Preconditions: synthetic model-ready profile; auto-detect disabled.
- Steps: Home; enable automatic detection; review confirmation/status; wait for
  detection; stop capture when meeting ends.
- User activations: 2 before automatic detection. Automatic waits: source probe,
  detection loop, and stop timeout.
- Concepts exposed: automatic detection, source availability, timeout.
- Explicit boundary: enabling capture assistance is explicit.
- Blocked/recovery branch: unavailable source explains why assistance cannot
  start and routes to a direct setting, not a threshold field.
- Outcome: user knows whether the app is waiting, recording, or blocked.

## J04 — Recover a failed transcript

- Preconditions: synthetic meeting row with transcript failure.
- Steps: Meetings; select failed row; open details; inspect failure; choose Retry
  transcript or Re-transcribe; return to row status.
- User activations: 4. Automatic waits: worker progress and catalog refresh.
- Concepts exposed: retry versus re-transcribe, processing state.
- Explicit boundary: reprocessing is deliberate because it can replace work.
- Blocked/recovery branch: missing source audio keeps recovery disabled with a
  reason and open-folder route.
- Outcome: one recovery action is apparent without scanning all maintenance
  controls.

## J05 — Manage a processing backlog

- Preconditions: synthetic queue contains queued, processing, and failed rows.
- Steps: Meetings; choose Processing view; select a row; inspect state; use
  Process ASAP or backlog action; check final status.
- User activations: 4. Automatic waits: queue refresh and worker stage changes.
- Concepts exposed: priority, stage, queue position, worker availability.
- Explicit boundary: priority/reprocessing changes remain explicit.
- Blocked/recovery branch: unavailable worker reports stable last-known state
  and offers recovery rather than pretending the item completed.
- Outcome: user can distinguish active work from queued and failed work.

## J06 — Configure summaries

- Preconditions: synthetic completed transcript; summaries initially disabled.
- Steps: Meeting detail; choose Configure summaries; select mode/provider;
  acknowledge hosted choice when applicable; generate summary; read provenance.
- User activations: 3 local, 5 hosted. Automatic waits: provider/model refresh
  and generation.
- Concepts exposed: local versus hosted, provider, provenance, timeout.
- Explicit boundary: transcript transmission and cost are direct consent.
- Blocked/recovery branch: disabled/unavailable provider routes to summary setup
  with a reason; it does not silently use a hosted fallback.
- Outcome: user knows whether a summary is local, hosted, pending, or failed.

## J07 — Fix speaker labels

- Preconditions: synthetic diarized meeting with neutral labels.
- Steps: Meeting detail; open speaker section; edit visible names; apply names;
  inspect resulting labels.
- User activations: 3. Automatic waits: label refresh only.
- Concepts exposed: diarization, display name, profile suggestion.
- Explicit boundary: identity edits stay local and explicit.
- Blocked/recovery branch: unavailable diarization routes to Setup with a
  capability explanation.
- Outcome: a user can repair labels without confusing it with voice learning.

## J08 — Apply speaker-name suggestions

- Preconditions: synthetic meeting with one local voice-profile suggestion.
- Steps: Meeting detail; inspect suggestion; use or reject it; apply speaker
  names; optionally undo last recognition.
- User activations: 3 minimum. Automatic waits: suggestion refresh.
- Concepts exposed: suggestion confidence, local profile, undo scope.
- Explicit boundary: applying a learned identity and undoing it are explicit.
- Blocked/recovery branch: no suggestion reports absence rather than assigning a
  name automatically.
- Outcome: name learning is explainable and reversible.

## J09 — Clean up old meetings

- Preconditions: synthetic library has eligible stale/duplicate meeting records.
- Steps: Meetings; review cleanup suggestions; inspect scope; apply safe fixes
  or open a meeting; read result summary.
- User activations: 3. Automatic waits: recommendation refresh and result
  aggregation.
- Concepts exposed: recommendation, eligible scope, safe fix, archive receipt.
- Explicit boundary: cleanup and archival effects require review and truthful
  per-item results.
- Blocked/recovery branch: ineligible items retain a reason and route to details
  instead of joining the execution scope.
- Outcome: one recommendation explains what can safely happen next.

## Evidence limit

This run inspected source and did not capture live UI. The safe synthetic-profile
renderer is blocked by the production mutex; do not treat these source traces as
rendered or assistive-technology proof. Sprint 15 owns rendered and accessibility
qualification after a synthetic-profile launch path exists.
