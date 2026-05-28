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
