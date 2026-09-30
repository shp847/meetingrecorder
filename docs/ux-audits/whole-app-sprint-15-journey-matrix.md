# Whole-App Sprint 15 Journey Matrix

## Evidence contract

Use redacted synthetic data only. Do not touch microphone, provider, personal
profile, published artifacts, archive/delete actions, or live recording while
capturing. Required evidence for each row is an automation-tree export, a
keyboard trace, and a screenshot at the listed viewport. Store screenshots in
ignored `.artifacts/ux-audits/whole-app-sprint-15/`; record only filenames,
SHA-256, OS, DPI, theme, test date, and observations here.

| ID | Synthetic state and flow | Keyboard / spoken contract | Capture points | Status |
| --- | --- | --- | --- | --- |
| J15-01 | Ready Home; open Settings, edit one harmless setting, Escape | Header Settings has name; focused target or Recording default receives focus; Escape returns focus to opener | Home; Settings at 1280x800/100% and 125% | Rendered: Recording renders at 1280x800/125%; an isolated calendar-title fallback edit saves, reports success, and Escape returns focus to opener. |
| J15-02 | Setup-blocked Home | Start action exposes readiness remedy without color-only meaning | Home at 1280x800/125% | Rendered: setup state and named remedy captured at 1280x800 and 1024x768/125%. |
| J15-03 | Meetings preset, search, select one row | Preset, search, and meeting list have accessible names; Tab reaches each once | Meetings at 1280x800/100% and 1024x768/125% | Partial: processing and selection rendered; interactive preset/search/Tab replay remains open. |
| J15-04 | Queue/recovery and ASAP state | Status is understandable without row color; selected action keeps focus | Meetings and detail | Partial: queue/recovery state rendered; detail and focus-return path remains open. |
| J15-05 | Cleanup recommendation; archive/delete confirmation | Confirmation names durable effect and Escape cancels | Meetings and confirmation | Rendered: cleanup state plus typed permanent-delete confirmation captured. Delete is disabled before exact confirmation; Escape cancels and returns focus to Meetings. |
| J15-06 | Hosted-summary consent and blocked state | Consent boundary is stated before action; summary status has polite live announcement | Settings and detail | Source-only: consent interaction and runtime announcement remain open. |
| J15-07 | Speaker review; disable/delete profile | Profile action exposes disabled reason and preserves existing meeting display names | Settings and detail | Source-only: profile interaction remains open. |
| J15-08 | Detail read/maintain path | Summary/footer status has polite live announcement; Close has accessible name and returns focus | Detail at 1280x800/100% and 125% | Source-only: detail runtime route remains open. |

## Current source evidence

- `AccessibilityContractTests` verifies names for recording, Settings, Help,
  Meetings search/preset/list, Settings save/close, and detail close; it also
  verifies polite live status and deep-link focus fallbacks.
- `SettingsHostWindow` has `MinWidth=780` and `MinHeight=620`; `ShellTheme`
  contains contained directional navigation and local tab-navigation groups,
  with no drop shadows.
- This is source evidence only. It does not prove clipping, contrast, Narrator
  speech, focus order at runtime, or high-contrast rendering.

## Capture progress and next action

The isolated fixture path is available through
`MeetingRecorder.WpfRenderProbe`; the rendered evidence and hashes are recorded
in `whole-app-sprint-15-rendered-evidence.md`. It does not use the live profile
or primary-instance mutex. The remaining work is interactive journey replay,
high-contrast/OS-DPI validation, packaged UI rendering, and Narrator validation.
Do not substitute the source or raster evidence for those checks.
