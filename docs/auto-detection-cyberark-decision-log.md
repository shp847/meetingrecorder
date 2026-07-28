# Auto-Detection and CyberArk Decision Log

Last updated: 2026-07-21

## Purpose

This is the source of truth for Meeting Recorder changes that trade meeting
auto-detection quality against endpoint-protection compatibility. Read it before
changing any of these areas:

- `src/MeetingRecorder.App/Services/WindowMeetingDetector.cs`
- `src/MeetingRecorder.Core/Services/AutoRecordingContinuityPolicy.cs`
- Windows audio endpoint or session probing
- Teams or Google Meet window inspection
- app launch, install, update, signing, executable naming, or install location

Append new evidence. Do not rewrite failed attempts into apparent successes.
Record source, tests, package, installed build, and live-machine validation as
separate states.

## Keep The Two Problems Separate

There are two related but distinct CyberArk surfaces:

1. Runtime detection: Windows APIs used after launch to inspect meeting windows
   and audio activity.
2. Executable policy: whether CyberArk allows the unsigned apphost to launch
   from a particular path and filename.

A launch-path experiment does not prove a Core Audio theory. A detector change
does not prove an executable allow-list theory.

## Current Baseline

As of 2026-07-20, runtime detection intentionally uses:

- visible top-level window class and title
- no visible-window owner process-name lookup
- endpoint-level render peak activity from both Windows `Multimedia` and
  `Communications` default render endpoints
- no per-app Core Audio session enumeration
- no Core Audio session-owner PID lookup
- no `Process.GetProcessById` lookup in detector code
- a 20-second debounce before a specific quiet Teams meeting window can
  auto-start
- immediate start when a supported specific meeting surface also has active
  endpoint render audio
- suppression for generic Teams shells, navigation titles, and a matching Teams
  chat/playback surface
- calendar-backed Zoom Web detection only when an overlapping Outlook event has
  positive Zoom join evidence, its subject exactly matches a visible Chromium
  window title, and endpoint render audio is active

The 2026-07-20 quiet-Teams fix is implemented, test-verified, packaged, and
locally deployed from the current dirty worktree. Installed bundle provenance is
commit `7573bc2` with `isWorktreeDirty=true`; live quiet-call validation remains
pending and must not be conflated with deployment success.

As of 2026-07-20, executable-policy facts are:

- installed apphost:
  `%LOCALAPPDATA%\Programs\Meeting Recorder\MeetingRecorder.App.exe`
- Authenticode status: `NotSigned`
- SHA-256:
  `8A0231C72FB248811C7317316492DF55513C486299E70878A62978A34A713A93`
- filename/path policy remains unclassified because the July 17 matrix was
  contaminated by an already-running singleton instance
- signing, allow-listing, or IT-managed deployment remains the likely durable
  executable-policy direction; rename/relocation is not yet proven

## Guardrails

- Do not reintroduce per-app Core Audio session enumeration, session-owner PID
  lookup, visible-window process lookup, or UI Automation without exact live
  evidence, a CyberArk impact review, and explicit regression coverage.
- Do not require a detector signal that the production probe cannot generate.
  Production endpoint snapshots currently contain an empty `Sessions` list.
- Detector tests must include production-shaped snapshots, not only fabricated
  `DetectedAudioSource` or per-session attribution objects.
- A quiet-start relaxation must retain negative tests for generic Teams,
  navigation titles, chat/playback matches, and pre-debounce observations.
- A false-positive tightening must retain a live-call test with a specific Teams
  window, silent endpoint, and no per-session attribution.
- Never mark a change complete based only on source or unit tests. Record package,
  installed build hash, and one live-machine result separately.
- Do not repeat the rename/relocation matrix until all Meeting Recorder app and
  worker processes are stopped first.

## Timeline

### 2026-03-23: Detection foundation

- Commit: `60f6d45`
- Attempt: expanded Teams and Google Meet window/audio detection and continuity.
- Benefit: established richer meeting identity and candidate ranking.
- Cost: broad cross-process and per-session inspection later proved sensitive on
  the managed laptop.
- Retained: explicit candidate signals, deterministic evaluator, and regression
  tests.

### 2026-04-01: Sustained quiet Teams start

- Commit: `2aab95b`
- Attempt: allow a specific quiet Teams meeting to auto-start after 20 seconds.
- Gate: required matched Teams audio attribution plus a silent endpoint signal.
- Benefit: handled one-sided or initially quiet calls without immediate false
  starts.
- Hidden dependency: assumed production would continue producing per-session
  Teams attribution.

### 2026-04-10: Quiet-call continuity

- Commit: `636a444`
- Attempt: keep a same-title Teams recording alive when Teams attribution and
  recent microphone activity still indicated a call.
- Benefit: reduced false session splits.
- Limitation: continuity still depended on per-session attribution.

### 2026-04-24: False-positive tightening and probe recovery

- Commit: `6d0f391`
- Attempt: reject browser help pages and generic Teams shells, prefer specific
  call titles, and reduce audio-probe backoff from two minutes to 15 seconds.
- Benefit: fewer false candidates and faster recovery after a hung audio probe.
- Retained: 1.5-second probe timeout, 15-second cooldown, and specific-title
  preference.

### 2026-05-05: Probe-timeout continuity fallback

- Commit: `93ad669`
- Attempt: preserve same-title active Teams recordings when attribution timed out
  but recent captured audio still existed.
- Benefit: protected live recordings from transient probe failures.
- Limitation: addressed continuity after recording start, not initial quiet-call
  detection.

### 2026-05-15: Filter before process lookup

- Commit: `1e8a8dd`
- Trigger: endpoint-protection prompts involving service-hosted audio sessions.
- Attempt: classify system sounds, inactive sessions, and Meeting Recorder's own
  sessions before resolving session process names.
- Benefit: reduced unnecessary process lookups.
- Result: insufficient; per-app session enumeration and owner metadata access
  still remained.

### 2026-06-02: Derive audio owner from metadata

- Commit: `4641cb1`
- Trigger: CyberArk sensitivity around process lookup.
- Attempt: stop `Process.GetProcessById` for audio sessions and derive Teams or
  browser family from session display name and identifier.
- Benefit: retained audio attribution without direct process opening.
- Result: insufficient; Core Audio session enumeration and PID access still
  remained.

### 2026-06-02: Remove visible-window process probing

- Commit: `41f51cb`
- Attempt: keep visible-window process names blank and classify from title,
  class, handle, and existing audio metadata.
- Benefit: removed another cross-process inspection surface.
- Retained: `TeamsWebView` and supported Chromium window-class recognition.

### 2026-06-09: Remove Core Audio session PID lookup

- Commit: `8775cd0`
- Attempt: stop reading `GetProcessID` and continue deriving meeting family from
  audio-session metadata.
- Benefit: removed direct session-owner PID dependency.
- Result: insufficient; enumerating the session collection itself still touched
  a CyberArk-sensitive service-hosted path.

### 2026-07-07: Remove all per-app audio-session enumeration

- Commit: `ff54b63`
- Trigger: continuing endpoint-protection friction involving service-hosted
  Windows audio paths such as `svchost.exe`.
- Attempt: replace `AudioSessionManager.Sessions` inspection with endpoint master
  peak only; production snapshots now use an empty `Sessions` list.
- Security result: removed the known live-detection session sweep.
- Functional regression: quiet Teams auto-start still required matched Teams
  session attribution, making that branch effectively unreachable in production.
- Test gap: tests continued constructing attributed sessions and therefore did
  not prove the production-shaped empty-session path.

### 2026-07-07: Restore active-audio Teams starts

- Commit: `2e7fdf9`
- Attempt: trust endpoint-level active audio for a specific non-navigation Teams
  title when no matching chat/playback surface exists.
- Benefit: restored normal Teams auto-start while people were speaking.
- Remaining gap: a real Teams call with a silent endpoint still could not start.

### 2026-07-15: New Teams surface recognition

- Commit: `7573bc2`
- Attempt: recognize newer Teams roster/name-only surfaces and prefer them over a
  generic stale Google Meet browser shell.
- Benefit: improved candidate identity when process metadata was intentionally
  unavailable.
- Remaining gap: silent named Teams surfaces still hit the unreachable
  attribution requirement.

### 2026-07-17: Executable rename and relocation matrix

- Evidence root:
  `%LOCALAPPDATA%\Temp\MeetingRecorder-CyberArkTest-20260717-112101`
- Tested: original name/profile path, original name/non-profile path, renamed
  EXE/profile path, and renamed EXE/non-profile path.
- Integrity: all four EXEs had the same SHA-256 and were `NotSigned`.
- Automated classification: `unaffected-or-inconclusive`.
- Why inconclusive: an installed Meeting Recorder instance and worker were
  already running, so direct launches could hand off to the singleton or exit
  cleanly instead of proving a fresh executable-policy decision.
- Do not infer: filename-based success, path-based success, or lack of CyberArk
  enforcement.
- Required rerun condition: stop app and worker first, then repeat the same matrix
  and capture the CyberArk dialog or fresh app process for every row.

### 2026-07-20: Live Teams call missed

- Runtime evidence: auto-detection was enabled; detector repeatedly saw a
  separate specific `TeamsWebView` meeting window and the Jabra headset endpoint,
  but endpoint render peak remained `0.000` before manual start.
- User-visible result: recording had to be started manually.
- Root cause: two stale gates survived the July 7 architecture change. Playback
  demotion cleared `ShouldKeepRecording`, and quiet-start policy required a
  per-session Teams match that production no longer generated.
- Source fix: preserve a silent specific Teams candidate when no matching
  chat/playback surface exists, then use the existing 20-second quiet-start
  debounce without per-session attribution.
- Negative boundaries retained: generic Teams, navigation titles, matching chat
  playback, and observations shorter than 20 seconds remain ineligible.
- Verification: 96 focused detector/continuity tests passed; full core suite
  passed 1,046 tests; integration suite passed 8 tests. One unrelated temporary
  file-lock failure passed on isolated retry and the full core rerun was green.
- Package status: rebuilt on 2026-07-20 with `Build-Installer.ps1`; portable
  bundle, 84 MB ZIP, and 72.2 MB MSI completed with zero build warnings/errors.
  `release-source.json` records commit `7573bc2`, build time
  `2026-07-20T19:11:39.7212458+00:00`, and `isWorktreeDirty=true`.
- Installed status: deployed through `Deploy-Local.ps1 -NoLaunch` at
  `2026-07-20T19:23:37.7099912+00:00` using channel `DirectCli`. Installer
  validated source, staging, and installed bundle integrity. Installed hashes
  match the packaged bundle: `MeetingRecorder.App.dll`
  `4264848DB282A976F5131E262CE33F96FB2E43BFEFDC8337F3D2877CB1195F50` and
  `MeetingRecorder.Core.dll`
  `9A7612EE32D78DA7AF9D7D8843A48662F9922190CB8650C1FB2DD1464AF74456`.
  Stable apphost hash remains
  `8A0231C72FB248811C7317316492DF55513C486299E70878A62978A34A713A93`
  with Authenticode status `NotSigned`.
- Live validation status: pending a future quiet Teams call on the rebuilt and
  installed version.

### 2026-07-21: Live Zoom Web call missed

- Trigger / observed symptom: Zoom Web call was visibly active in Edge while
  automatic detection was on, but recording remained ready until manual start.
- Exact runtime evidence: from 11:03 through manual start at 11:06, detector
  scans returned only a generic Teams calendar window. Live top-level enumeration
  showed the Zoom call as `Chrome_WidgetWin_1` with title
  `Quarterly Review | Partner`; no visible title contained `Zoom`.
  Outlook had an overlapping event with the exact same subject and positive Zoom
  join evidence. The redacted manual session
  then captured rolling loopback and microphone chunks normally.
- Root cause: product requirements and detector code supported automatic start
  only for Teams desktop and Google Meet. Zoom was explicitly manual-only, and
  arbitrary Zoom Web meeting titles cannot be classified from title/class alone.
- Hypothesis: existing cached Outlook calendar evidence can identify this narrow
  Zoom Web case without reintroducing browser UI Automation or PID/process lookup.
- APIs or signals added/removed: added first-class `Zoom` platform value and a
  `calendar-zoom` signal. Reused visible top-level title/class, Outlook calendar,
  and endpoint-level audio. Added no UI Automation, browser URL scraping,
  per-app audio session enumeration, or process lookup.
- Positive behavior expected: auto-start when Zoom calendar evidence is positive,
  event subject exactly matches a visible Chromium window title, and endpoint
  audio is active.
- False-positive/security boundary retained: no start for mismatched titles,
  quiet endpoints, unmatched calendar fallback, native or unscheduled Zoom, or
  generic browser playback.
- Tests added and results: live-shaped regression failed with a null candidate
  before implementation; eight focused Zoom/calendar/artifact/lifecycle tests
  now pass. Full verification passes 1,054 core tests and 8 integration tests with zero
  build warnings/errors.
- Package status: rebuilt with `Build-Installer.ps1` while leaving the live install
  untouched. `release-source.json` records build time
  `2026-07-21T15:26:24.6270036+00:00`, commit `7573bc2`, and
  `isWorktreeDirty=true`. `MeetingRecorder-v0.3-win-x64.zip` is 88,105,020
  bytes with SHA-256
  `438DA8191C6EE8F17F95589CC6EDCAC4741B64991DDFC30CB7336609E846414B`;
  `MeetingRecorderInstaller.msi` is 75,780,096 bytes with SHA-256
  `E916F29BB504BFF86A5458161C13B2099BFEA6C42DAD0B087C8259901471BCFE`.
- Installed hash/version/signature status: deployed through
  `Deploy-Local.ps1 -NoLaunch` at 2026-07-21 11:45 local time. Source and
  installed hashes match for `MeetingRecorder.App.dll`
  (`DBCF0B57FECFD6E0155A6FAF4E320CA842368309CA772DE8A3E83F068BACDE53`),
  `MeetingRecorder.Core.dll`
  (`10BEB45F56EF550D22CA4BB9EC1959625DE39208CD1D4A571C3674271C673451`),
  `MeetingRecorder.ProcessingWorker.dll`
  (`A88D8ED866D81EC1644F19B19D54356CDF15A82A4499C11C1C4C3FBF632EFE0B`),
  and `release-source.json`
  (`638BF8B5BDF9B18EC446EB480D15CC292DE088BF5F943A063DF61BD8196DB140`).
  The apphost remains `NotSigned`; app and worker were left stopped.
- Live-machine result: source signals verified against the active call; rebuilt
  installed auto-start validation remains pending.
- Post-call processing observation: the original session stopped cleanly at
  `2026-07-21T15:38:27.5027369+00:00`, completed with 320 transcript segments,
  and wrote its ready marker. The existing dropped-audio scanner then queued a
  second transcription of the 61,566,126-byte normalized WAV, which also
  completed successfully with 320 segments. Treat that as a separate
  queue/deduplication issue; it is not evidence for or against Zoom detection.
- Outcome: source, tests, package, and local deployment retained; a future
  scheduled Zoom Web call remains pending for installed auto-start validation.
- Follow-up / removal condition: if exact browser titles prove unstable, collect
  title-only evidence before widening matching; do not add browser UI Automation
  without a separate CyberArk review.

### 2026-07-24: Named Google Meet call missed during quiet endpoint audio
- Trigger / observed symptom: active Google Meet `Named Customer Workshop`
  remained `READY` with automatic detection on and required manual
  recording start.
- Exact runtime or CyberArk evidence: installed app started at 10:06:01. At
  `2026-07-24T14:06:09.5471963+00:00`, detector returned
  `platform=GoogleMeet`, title
  `Meet - Named Customer Workshop - Work - Microsoft Edge`,
  confidence 100%, `shouldStart=False`, `shouldKeepRecording=True`, browser
  window evidence, and endpoint `audio-silence` at peak `0.000`. The redacted
  manual session started at
  `2026-07-24T14:07:14.9367820+00:00` and captured loopback plus microphone
  normally.
- Hypothesis: confirmed policy gap. Quiet Google Meet auto-start and obscured-call
  continuity accepted only normalized 3-4-3 Meet codes. Google's joined-call
  named title normalized to `meet named customer workshop ...`, so it never entered
  the existing 20-second quiet-start debounce.
- APIs or signals added/removed: none. The shared specific Google Meet identity
  predicate now accepts both Meet codes and normalized named `Meet ...` titles.
- Positive behavior expected: a visible named `Meet - ...` browser window with
  quiet endpoint audio auto-starts after 20 seconds of stable detection and gets
  the same bounded continuity protection as a code-titled Meet.
- False-positive/security boundary retained: immediate auto-start still requires
  active audio; quiet auto-start still requires Google Meet platform, browser
  surface evidence, a non-generic named `Meet - ...` title, silent audio, and the
  existing 20-second stable fingerprint. No browser UI Automation, URL scraping,
  process lookup, or per-app audio enumeration was added.
- Tests added and results: both live-shaped regressions failed before the source
  change and pass afterward. Full verification passes 1,057 core tests and 8
  integration tests with zero failures.
- Package status: rebuilt with `Build-Installer.ps1` while leaving the live
  install untouched. `release-source.json` records build time
  `2026-07-24T14:13:47.6802596+00:00`, commit `7573bc2`, and
  `isWorktreeDirty=true`. `MeetingRecorder-v0.3-win-x64.zip` is 88,106,879
  bytes with SHA-256
  `68CB874F5AFAF332F42FBDA93A2F0F1B864D2E4A728381E2FEDC8AC315CD28F2`;
  `MeetingRecorderInstaller.msi` is 75,862,016 bytes with SHA-256
  `194E876E506908CC07B1DB5181C0F972FB074591A582611373719E4EEB05033A`.
- Installed hash/version/signature status: unchanged; installed
  `release-source.json` records build time
  `2026-07-21T23:50:00.0739661+00:00`, commit `7573bc2`, and
  `isWorktreeDirty=true`.
- Live-machine result: current call is recording safely from the old installed
  build; rebuilt installed auto-start validation remains pending.
- Outcome: source, tests, documentation, and package retained; local deployment
  and future live named-Meet auto-start validation remain pending.
- Follow-up / removal condition: keep named-title support while Google emits
  joined-call titles as `Meet - <meeting name>`; tighten only if live evidence
  shows non-call browser pages using the same stable title shape.

### 2026-07-24: Teams organization and account suffix leaked into session title
- Trigger / observed symptom: the active Teams call displayed `Project Planning |
  Prep`, while Meeting Recorder displayed `Project Planning | Prep | Contoso |
  user@example.com`.
- Exact runtime or CyberArk evidence: the installed app repeatedly detected
  `window-title='Project Planning | Prep | Contoso |
  user@example.com | Microsoft Teams'` at 100% confidence for the redacted
  recording session. Audio alternated
  between active and silent while the title remained unchanged, proving one
  stable meeting surface rather than a meeting transition.
- Hypothesis: confirmed title-cleanup gap. The shared evaluator removed only
  the final `| Microsoft Teams` suffix, leaving Teams' organization and signed-in
  account decoration in the user-facing session title.
- APIs or signals added/removed: none. Shared Teams title cleanup now removes
  the final organization and account segments only when the last remaining
  segment is email-shaped.
- Positive behavior expected: the live-shaped caption normalizes to `Project
  Planning | Prep`; ordinary pipe-delimited titles remain unchanged.
- False-positive/security boundary retained: detection confidence, window
  enumeration, audio probing, process access, executable path/name, and
  CyberArk behavior are unchanged.
- Tests added and results: the live-shaped evaluator regression failed before
  the source change and passes afterward; the focused evaluator suite passes
  12/12. Full verification passes 1,058 core tests and 8 integration tests with
  zero failures.
- Package status: rebuilt with `Build-Installer.ps1` while leaving the live
  install untouched. `release-source.json` records build time
  `2026-07-24T19:25:22.0036948+00:00`, commit `7573bc2`, and
  `isWorktreeDirty=true`. `MeetingRecorder-v0.3-win-x64.zip` is 88,107,168
  bytes with SHA-256
  `5982C0EC5AF1849E3F5FB01A3766E95EEF75560D6771D229D007EB5701016652`;
  `MeetingRecorderInstaller.msi` is 75,870,208 bytes with SHA-256
  `28B25B635ADD108F393E5DF0EB07D6BABA7CAD320EB559B206D6787341F18297`.
- Installed hash/version/signature status: unchanged; the active installed app
  remains the prior build.
- Live-machine result: the prior installed build is still recording the current
  call safely; fixed-build validation is pending a non-recording deployment.
- Outcome: source, tests, documentation, and package retained; install and live
  validation pending.
- Follow-up / removal condition: retain while Teams appends organization plus
  email account metadata to call-window titles; revisit only with a different
  observed caption shape.

### 2026-07-26: Silent stale meeting titles created repeated false sessions
- Trigger / observed symptom: the Meetings library showed hundreds of sessions
  with the same Teams title; an earlier Google Meet call was similarly split
  into repeated sessions.
- Exact runtime or CyberArk evidence: 551 July 26 work manifests persisted the
  same Teams title. The false-start chain began at midnight UTC and produced
  roughly 91-second sessions. Each sampled manifest had a specific Teams window
  title plus `audio-silence` at peak `0.000`, no attributed audio source, and no
  captured chunks. Automatic detection was disabled after the app was stopped
  during a read-only catalog scan.
- Hypothesis: confirmed policy loop. A visible specific meeting title qualified
  for quiet auto-start after 20 seconds without attributed audio; auto-stop then
  reset the quiet debounce and allowed the same stale window to start again.
- APIs or signals added/removed: none. Quiet Teams and Google Meet auto-start
  now requires an attributed audio source or an unavailable audio-probe signal.
- Positive behavior expected: active attributed audio still starts immediately;
  a quiet but attributed meeting or an audio-probe failure can still use the
  debounce; a silent stale window alone cannot create sessions.
- False-positive/security boundary retained: no new Windows APIs, UI Automation,
  process lookup, browser inspection, or CyberArk-sensitive probes are added.
- Tests added and results: live-shaped Teams and Google Meet policy regressions
  passed with the focused detection/startup set (112/112); full verification
  passed 1,060 core tests and 8 integration tests.
- Package status: rebuilt successfully. ZIP is 88,107,521 bytes with SHA-256
  `952C6D724C5BD64B6D79EFA1E0BEF9CDEFDF31203C0F0E2C6E3306F90724C4D5`;
  MSI is 75,853,824 bytes with SHA-256
  `6EC4CE230C228417F011F0D2F835C6E1C2AC68A687526F298D971802AF119647`.
- Installed hash/version/signature status: local v0.3 deployment completed and
  published/installed `MeetingRecorder.Core.dll` hashes match at
  `52D6685C7B32EA204702BCAE523C80E7A376371A97F457A1E98ADC9BFFFBB8F4`;
  automatic detection was re-enabled after validation.
- Live-machine result: the installed app stayed responsive and created no work
  manifests during a 60-second observation with the current Teams Calendar
  shell visible. The exact stale-specific-title surface was not available for
  a live reproduction. Incident evidence is preserved under
  `%LOCALAPPDATA%\MeetingRecorder\incident-backups`. The first recovery pass
  moved 251 header-only WAV files. A follow-up catalog pass moved 949 matching
  silent/unattributed work sessions and 323 active catalog artifacts
  reversibly under incident batch `20260726-175143`; 30 stems with transcript
  text beyond notification-only sounds were initially protected. After the
  remaining stale-title rows were confirmed to still clutter the Meetings
  view, a final reversible pass moved their 31 work sessions and 74 catalog
  artifacts under incident batch `20260726-175724`. The installed Meetings
  view then stayed responsive, showed the week count reduced from 576 to 29,
  and exposed zero elements with the stale title. No original titles were
  inferred because the manifests do not contain an authoritative replacement.
- Outcome: source, test, package, install, and current-shell live validation
  retained. Exact stale-title live validation remains pending.
- Follow-up / removal condition: retain unless live evidence proves a different
  trustworthy joined-call signal can distinguish quiet calls from stale windows.

### 2026-07-28: Generic meeting shells used unrelated endpoint audio
- Trigger / observed symptom: the Meetings library again showed repeated
  30-35 second Teams and Google Meet rows with generic titles such as
  `Microsoft Teams | Pinned window` and
  `Google Meet and 1 more page - Work - Microsoft Edge`.
- Exact runtime or CyberArk evidence: detector logs showed each session
  auto-started from a generic window plus endpoint-wide `audio-activity`, with
  no `audio-window`, `audio-process`, or `audio-browser-tab` attribution. The
  next scan returned a Teams chat or silent browser shell, and auto-stop fired
  at the 30-second timeout. No CyberArk block was observed.
- Hypothesis: confirmed policy gap. The July 26 change gated quiet debounce,
  but `MeetingDetectionEvaluator` still allowed generic shells to start
  immediately from unrelated endpoint audio.
- APIs or signals added/removed: no new Windows APIs or signal producers.
  `MeetingDetectionEvaluator` now distinguishes attributed audio signals from
  endpoint-wide `audio-activity` when the normalized title is generic.
- Positive behavior expected: generic Teams and Google Meet shells require
  attributed app, window, process, or browser-tab audio to auto-start; specific
  meeting titles retain endpoint-audio start behavior.
- False-positive/security boundary retained: endpoint-wide audio remains useful
  for specific meeting windows, while generic shells cannot claim unrelated
  playback. Existing recordings may continue across a temporary generic shell.
- Tests added and results: two live-shaped evaluator regressions failed before
  the source fix and passed afterward alongside positive attributed-audio
  cases. The focused detector/continuity set passed 153 tests; full verification
  passed 1,067 core tests and 8 integration tests.
- Package status: rebuilt successfully. ZIP is 88,107,964 bytes with SHA-256
  `EA746BFAEB11EC1DC689AE1CFF7AF43884D091697DA07E28CABD07AA6585E829`;
  MSI is 75,837,440 bytes with SHA-256
  `1D4C540B338646132A2F7A8041FE91994697F0362E38F9B45B05100D568B2ED9`.
- Installed hash/version/signature status: local deployment completed and the
  installed/published `MeetingRecorder.Core.dll` hashes match at
  `1284ADEBAE7281D01AA660373162729723AD59DC5DF7658C6F35DFE94BAFC0BE`.
- Live-machine result: the fixed app stayed responsive for 60 seconds with zero
  auto-starts and zero new work folders. The exact generic surface was not
  visible during observation. Nine confirmed 30-35 second, unattributed,
  empty-transcript phantoms were moved reversibly with 63 verified SHA-256
  entries under incident batch `20260728-041411-generic-shell-phantoms`; the
  Meetings view then showed zero generic phantom titles.
- Outcome: source, tests, package, install, bounded live observation, and
  reversible artifact cleanup retained. Exact-surface live reproduction remains
  pending.
- Follow-up / removal condition: retain until a stronger generic-shell meeting
  identity signal replaces audio attribution.

### 2026-07-28: Email-only Teams account shell split active Google Meet
- Trigger / observed symptom: one Google Meet appeared as alternating Google
  Meet recordings and 21-51 second Teams recordings titled
  `psharm04@atkearney.com`.
- Exact runtime or CyberArk evidence: at 15:08, 15:09, and 15:52 UTC the
  installed app rolled an active Google Meet session into
  `psharm04@atkearney.com | Microsoft Teams`, then back to the same Meet title.
  Each false Teams decision used endpoint-wide `audio-activity` and had no
  attributed Teams audio signal. No CyberArk block or capture-device failure
  caused those transitions.
- Hypothesis: confirmed evaluator gap. Teams account decoration was removed
  only from captions with at least three pipe-delimited segments, so an
  email-only account shell was treated as a specific meeting and borrowed
  unrelated endpoint audio.
- APIs or signals added/removed: none. `MeetingDetectionEvaluator` now treats
  an email-only Teams title as a generic shell only when audio is not attributed
  to Teams.
- Positive behavior expected: a visible Google Meet remains the selected
  meeting when an unrelated Teams account shell is also open; a legitimate
  email-titled Teams call can still start when app, window, process, or browser
  audio is attributed to Teams.
- False-positive/security boundary retained: the guard does not suppress
  email-titled Teams calls with attributed audio and adds no process, browser,
  UI Automation, or CyberArk-sensitive probe.
- Tests added and results: the live-shaped unattributed email-shell regression
  failed before the fix and passed afterward; the opposite attributed-audio
  case also passes. Focused detection/lifecycle verification passed 126 tests;
  full verification passed 1,070 core tests and 8 integration tests.
- Package status: rebuilt successfully. ZIP is 88,108,567 bytes with SHA-256
  `D1874F8F70E4187FEEF4C812A6BDB8EC3669ABD4FEECF0E15C4155118AA6BA5A`;
  MSI is 75,837,440 bytes with SHA-256
  `4199F74FF23A93203240E443D60DC28FD626CAD2CBB460F645C018C04D50BFBA`.
- Installed hash/version/signature status: deployment deferred because the
  installed app is actively recording a Teams meeting; interrupting it would
  splinter the current call.
- Live-machine result: runtime logs conclusively reproduced the pre-fix
  cross-platform rollovers. The fixed package has not yet been installed or
  exercised against the exact email-only shell.
- Outcome: source, tests, and package retained; installed and live-fixed states
  remain pending.
- Follow-up / removal condition: deploy when capture is idle, then verify the
  email-only Teams shell cannot replace an active Google Meet without
  attributed Teams audio.

### 2026-07-28: Multiple specific Teams windows shared endpoint audio
- Trigger / observed symptom: one workday produced repeated 30-second to
  three-minute Teams recordings under several stale meeting and attendee
  titles, with occasional longer fragments under the real call title.
- Exact runtime or CyberArk evidence: installed-app logs showed title
  alternation between `Kohl's Blue Team & Client Q&A Preparation` and
  `IonQ + Kearney (External)`, plus repeated same-title stop/restart sequences
  for `Graszl, Kate, Villar, Juan Pablo`, `Harriss, Grant`, and other visible
  Teams windows. Every false start used only endpoint-wide `audio-activity`;
  none had attributed Teams window, process, browser, or official-match
  evidence. No CyberArk or capture-device failure caused the title changes.
- Hypothesis: confirmed candidate-selection gap. When several distinct
  specific Teams windows remained visible, each borrowed the same endpoint
  peak and qualified as a live meeting; scan ordering then selected an
  arbitrary title for auto-start or rollover.
- APIs or signals added/removed: added the internal
  `teams-identity-ambiguous` decision signal when multiple distinct specific
  Teams candidates share only endpoint audio.
- Positive behavior expected: ambiguity cannot auto-start or roll over a
  meeting. An already active Teams recording may continue while its captured
  loopback remains active, without assigning that audio to a competing title.
- False-positive/security boundary retained: a single specific Teams window
  can still auto-start from endpoint audio, and attributed Teams audio still
  resolves competing windows normally. No process, browser, UI Automation, or
  CyberArk-sensitive probe was added.
- Tests added and results: both live-shaped regressions failed before the fix
  and passed afterward. Focused detection/lifecycle verification passed 250
  tests; full verification passed 1,072 core tests and 8 integration tests.
- Package status: rebuilt successfully. ZIP is 88,109,282 bytes with SHA-256
  `D7CBF44A865D24F138C6FD480BE752917F968A5C5AC2705CF4CECCF462ECC778`;
  MSI is 75,862,016 bytes with SHA-256
  `4523E9963317340CE515820F0D0372187850047CC615B7ED60D25ECF58EDE426`.
- Installed hash/version/signature status: local deployment completed while
  capture was idle. Published and installed `MeetingRecorder.App.dll` hashes
  match at
  `D6251E34E362F1EC2E90F7FA7CC1F3AD44B3DFECD20DC49296F6F0CDD1A419DB`.
- Live-machine result: the fixed installed app relaunched visible and
  responsive and continued five-second detection scans. The current live
  surface was a quiet Teams chat and remained suppressed; the exact
  multi-specific-window plus active-endpoint-audio condition was not present
  for direct live reproduction.
- Outcome: source, tests, package, and local install retained; exact-condition
  live validation remains pending.
- Follow-up / removal condition: during the next real call with multiple stale
  Teams windows visible, confirm logs report `teams-identity-ambiguous` without
  creating a new session or rolling over the active one.

## What We Must Not Repeat

### 2026-07-21: Meeting window disappeared during automatic rollover
- Trigger / observed symptom: Meeting Recorder disappeared while recording after the detected Teams title changed.
- Exact runtime or CyberArk evidence: no Meeting Recorder process remained; Windows Application events at 15:33 and 18:08 local time reported `0xc00000fd` stack overflow. Logs ended immediately after a different Teams title was detected and preserved raw chunks were recovered on restart.
- Hypothesis: confirmed source recursion. `TryRollOverManagedSessionAsync` called `ApplyPendingCurrentMetadataAsync`, which re-evaluated the same rollover and called `TryRollOverManagedSessionAsync` again.
- APIs or signals added/removed: none. Rollover now saves pending metadata with deferred reclassification disabled for that already-decided transition.
- Positive behavior expected: a new meeting identity closes and queues the prior session once, then starts the new session without exiting.
- False-positive/security boundary retained: meeting identity and rollover policy are unchanged.
- Tests added and results: focused lifecycle/source tests passed 226/226; full verification passed 1,055 core tests and 8 integration tests with zero failures.
- Package status: rebuilt successfully. ZIP is 88,105,077 bytes with SHA-256 `5B250F1539E4FA5CFA6F0A33D73C5A0127F66C1308873EF2DA75D25A5C831E13`; MSI is 75,771,904 bytes with SHA-256 `30662032DA03FC9089AD8E914981C1DC1E15B96E78B891C85D62DD3B8D792A7E`.
- Installed hash/version/signature status: local deploy completed while app was stopped. Published and installed `MeetingRecorder.App.dll` hashes match at `740BCB1B528D20DE2BF6B866258E747A9E45381C8943AAD3B06CC3ED8CA549FE`.
- Live-machine result: fixed installed app launched visible and responsive,
  auto-started the active Teams call, then naturally detected a title change
  from `Attendee A | Contoso | user@example.com` to
  `External Partner Review | Fabrikam | user@fabrikam.example`. It completed
  exactly one rollover, queued the prior session, started new loopback and
  microphone capture, and remained responsive with no new crash event.
- Outcome: source, test, package, install, startup/capture, and live rollover fix retained.
- Follow-up / removal condition: keep the guard while metadata persistence can invoke deferred reclassification.

- Repeatedly narrowing session inspection while retaining the same protected
  session sweep. May and June reduced risk but did not remove the trigger.
- Removing a signal producer without auditing every policy that consumes that
  signal. July 7 removed sessions while quiet-start still required them.
- Treating synthetic attributed-session tests as production proof after the
  production probe returns no sessions.
- Treating endpoint master peak as proof that a named Teams window is not a call.
  Quiet and one-sided calls can legitimately report `0.000`.
- Treating a running-singleton launch matrix as proof about executable name or
  path policy.
- Combining source, packaged, deployed, and live-validated states into one
  ambiguous word such as "fixed."

## Open Work

1. Validate one scheduled Zoom Web call with matching calendar/title evidence and
   active audio; record whether the installed build auto-starts.
2. Run one quiet Teams call test that remains silent for at least 20 seconds and
   capture detector logs proving whether the installed build auto-starts.
3. Repeat rename/relocation testing only from a fully stopped state if executable
   path/name classification is still useful.
4. Pursue code signing and an IT allow-list or managed deployment decision; do
   not treat renaming as the durable security solution without clean evidence.
5. Investigate why the post-processing normalized WAV was rediscovered as
   dropped audio and queued for a second transcription.

## Entry Template

Append future entries with all fields:

```text
### YYYY-MM-DD: Short title
- Trigger / observed symptom:
- Exact runtime or CyberArk evidence:
- Hypothesis:
- APIs or signals added/removed:
- Positive behavior expected:
- False-positive/security boundary retained:
- Tests added and results:
- Package status:
- Installed hash/version/signature status:
- Live-machine result:
- Outcome: retained, reverted, inconclusive, or superseded
- Follow-up / removal condition:
```
