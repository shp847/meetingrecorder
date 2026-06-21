# Meeting Recorder Feature Roadmaps

This file tracks implementation-ready sprint plans for major Meeting Recorder
feature work. Each roadmap is scoped independently unless it explicitly names a
dependency on another section.

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

## Sprint 1: UX Rules And Safety Policy

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

## Sprint 2: Settings Preset Engine

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

## Sprint 3: Settings Information Architecture

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

## Sprint 4: Recording And Setup Simplification

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

## Sprint 5: Home As Command Center

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

## Sprint 6: Meetings View Presets

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

## Sprint 7: Recommendation-First Meetings

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

## Sprint 8: Meetings Action Grouping

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

## Sprint 9: Meeting Detail Task Center

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

## Sprint 10: Backlog And Recovery Simplification

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

## Sprint 10A: ASAP Priority Carries Through Speaker Labeling

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

Goal: let the app remove chores without taking risky action.

Workstream 1 - Automated refreshes:

- Auto-refresh meeting catalog, setup readiness, cleanup recommendations,
  attendee backfill, update status, and provider status when safe.
- Keep refreshes bounded, non-overlapping, metadata-only, and visible in status
  text.
- Avoid background churn while recording or while the machine is under obvious
  processing pressure.

Workstream 2 - Safe automatic maintenance:

- Auto-apply only reversible, non-destructive maintenance where eligibility is
  proven and user value is clear.
- Keep archive, delete, hosted fallback, microphone enablement, reprocessing,
  and active-work interruption manual.
- Record what automation did in safe product status or activity logs.

Workstream 3 - Trust:

- Label whether a surface is showing live output, cached output, static
  expected output, fallback output, or no backend call.
- Never expose internal debug, provenance, or evidence chrome on normal
  customer-facing surfaces unless it is intentionally part of the experience.

Sprint 11 acceptance criteria:

- The app does more safe background housekeeping.
- Users are not surprised by privacy-sensitive, destructive, or interrupting
  behavior.
- Automated work is visible and bounded.

## Sprint 12: Summary And Hosted AI Trust Flow

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

- Define Meetings as a guided workbench, not a generic table.
- Document what can be assumed safely and what requires explicit user intent.
- Define copy rules for recommendations, blocked states, archive/delete, bulk
  outcomes, and all-clear states.
- Establish that no functionality is removed or merely visually hidden.

Acceptance: implementation has clear rules for automation versus control.

### Sprint 2: Meeting State Model

- Normalize meeting row state into clear user-facing categories: complete,
  needs attention, processing, blocked, archived, and unavailable.
- Derive states from existing metadata, queue state, transcript availability,
  artifacts, cleanup recommendations, summary state, and speaker-label state.
- Add complete/no-action-needed states so healthy rows do not feel empty.
- Keep internal technical state available in details/diagnostics only.

Acceptance: every row can explain its state in plain language.

### Sprint 3: Recommendation Engine

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

- Replace always-visible view/sort/direction/group controls with `Recent`,
  `Needs Attention`, `Processing`, `Archived`, and `Custom`.
- Keep `Custom` as the home for current table/group controls.
- Default to `Recent`, unless unresolved work makes `Needs Attention` more
  useful.
- Add preset summary text so users understand what they are seeing.

Acceptance: Meetings opens usefully without configuration.

### Sprint 5: Needs Attention Inbox

- Make `Needs Attention` the triage center.
- Include failed, blocked, queued-needs-decision, suspicious, missing,
  summary-failed, and cleanup-recommended meetings.
- Group by reason when helpful.
- Allow dismissing low-risk recommendations without hiding hard failures.
- Show an all-clear state.

Acceptance: the user has one obvious place for "what needs me?"

### Sprint 6: Processing View

- Make `Processing` the backlog/work-queue view.
- Show active item, queued items, paused reason, ETA, ASAP request, and rush
  backlog state.
- Convert mechanics into actions: `Process this first`,
  `Publish transcript first`, `Run speaker labels later`, and
  `Retry failed transcript`.
- Keep interrupting or priority-changing actions explicit.

Acceptance: backlog management does not require worker or manifest knowledge.

### Sprint 7: Selection Strip Redesign

- Redesign by selection state:
  - no selection: view summary and global next step,
  - one selection: recommendation plus `Open Details`,
  - multi-selection: eligible bulk actions and counts.
- Keep artifact shortcuts reachable but secondary.
- Show blocked counts before bulk actions.

Acceptance: the strip explains context instead of listing unrelated commands.

### Sprint 8: Action Grouping

- Group row/menu/detail actions into `Open`, `Fix`, `Organize`, `Processing`,
  and `Danger Zone`.
- Share labels, eligibility, and blocked reasons across row buttons, context
  menus, selection strip, cleanup review, and detail window.
- Keep delete isolated with typed confirmation.
- Keep archive clearly separate from permanent delete.

Acceptance: all power remains, but actions no longer compete visually.

### Sprint 9: Cleanup Consolidation

- Fold cleanup recommendations into row recommendations and `Needs Attention`.
- Keep bulk cleanup review as an advanced review path.
- Keep `Apply Safe Fixes` explicit with preview counts.
- Never include permanent delete in cleanup recommendations.
- Persist dismissed recommendations without hiding failures.

Acceptance: cleanup feels like guided maintenance, not a separate mini-app.

### Sprint 10: Meeting Detail Task Center

- Rebuild detail around `Read`, `Details`, `Fix`, and `Organize`.
- Default to `Read`: transcript, summary, search, artifact shortcuts.
- Put one recommendation and reason at the top.
- Move retry, re-transcribe, speaker labels, summaries, split, and repair to
  `Fix`.
- Move rename, project, archive, and permanent delete to `Organize`.

Acceptance: users can read calmly while every maintenance action remains
reachable.

### Sprint 11: Transcript And Summary Reading

- Treat transcript and summary as one reading workflow.
- Show summary unavailable states without implying transcript failure.
- Route summary setup gaps to Settings.
- Keep summary metadata secondary.
- Hide irrelevant summary action buttons when summaries are disabled or
  unavailable.

Acceptance: reading is calm, summaries are supplemental, and setup gaps are
actionable.

### Sprint 12: Speaker Workflow Clarity

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

- Add bulk previews with eligible count, blocked count, destructive risk, and
  per-row outcome behavior.
- Keep merge, add labels, re-transcribe, archive, apply recommendations, and
  delete under bulk categories.
- Bulk permanent delete requires typed confirmation and lists affected artifact
  classes.
- After execution, show success/failure counts and keep failed rows visible.

Acceptance: bulk work stays powerful but no longer surprising.

### Sprint 14: Archive, Delete, And Recovery Trust

- Make archive visibly recoverable.
- Show archive destination and recovery expectation without unnecessary
  internal paths.
- Keep generated repair backups distinct from user-managed archive folders.
- Standardize delete confirmation across row, bulk, and detail paths.
- Keep permanent delete outside recommendations and safe automation.

Acceptance: users understand reversible versus irreversible actions.

### Sprint 15: Search And Metadata Simplification

- Search title, project, key attendees, platform, status, date, transcript
  availability, and recommendation reason.
- Add lightweight chips/counts only when they replace complexity.
- Keep attendee enrichment automatic and metadata-only where already supported.
- Show enrichment loading versus unavailable states.

Acceptance: finding meetings is simple without losing project/attendee
richness.

### Sprint 16: Safe Background Refresh

- Auto-refresh Meetings when opened, after publish, after retry/repair, and
  after safe enrichment.
- Coalesce refreshes while recording or when Home is active.
- Make manual refresh secondary, not the normal correctness path.
- Show current, refreshing, deferred, stale, and retry-needed states.
- Avoid repeated expensive cleanup or attendee scans for unchanged rows.

Acceptance: users trust the list without habitually pressing refresh.

### Sprint 17: Imported Meeting Parity

- Ensure imported meetings use the same presets, recommendations, action
  groups, detail sections, archive/delete rules, retry, summary, and
  speaker-label paths.
- Show `Source: Imported audio` in detail without full private paths.
- Keep import-specific recovery contextual.
- Preserve `.ready` and artifact contracts.

Acceptance: imported meetings do not create a second mental model.

### Sprint 18: Rendered UX Polish

- Verify redesigned Meetings surfaces against `DESIGN.md`.
- Use stable dimensions for presets, toolbar, row actions, status wells,
  grouped actions, and detail sections.
- Prevent text overflow and overlapping controls.
- Keep primary feedback in the current viewport.
- Validate normal and smaller desktop window sizes.

Acceptance: simplification is visible, dense, and polished.

### Sprint 19: Accessibility And Keyboard QA

- Validate keyboard navigation through presets, search, list, menus, selection
  strip, cleanup review, detail sections, and dialogs.
- Ensure focus returns to the relevant row after actions.
- Add accessible names for grouped controls and compact actions.
- Test no meetings, many meetings, active processing, failed meetings, and
  multi-selection.

Acceptance: the workflow is keyboard-usable and screen-reader legible.

### Sprint 20: Tests, Docs, Release

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
- `Auto` is the default preference, but GPU is attempted only after readiness,
  quality, and performance gates pass.
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

Goal: define the exact problem and prevent future "felt right" continuity
changes.

Workstream 1 - Incident corpus:

- Capture replayable fixtures for the recent split families:
  - false auto-stop then re-auto-start for `GES Focus Groups: Principals`,
  - crash/restart recovery split for `Americas Virtual AI Co-Lab`,
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
  - June 12 split fixtures are corrected by the new engine,
  - severe false-merge divergences are absent on negative fixtures,
  - ordinary recordings do not show alarming divergence volume.

Sprint 3 acceptance criteria:

- The new engine has evidence-backed agreement or justified disagreement with
  the old engine.
- Cutover does not require blind trust.

## Sprint 4: Live Continuity Cutover

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
