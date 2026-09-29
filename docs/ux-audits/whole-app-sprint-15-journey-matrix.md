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
| J15-01 | Ready Home; open Settings, edit one harmless setting, Escape | Header Settings has name; focused target or Recording default receives focus; Escape returns focus to opener | Home; Settings at 1280x800/100% and 125% | Blocked: only live profile instance is running |
| J15-02 | Setup-blocked Home | Start action exposes readiness remedy without color-only meaning | Home at 1280x800/125% | Blocked: synthetic instance unavailable |
| J15-03 | Meetings preset, search, select one row | Preset, search, and meeting list have accessible names; Tab reaches each once | Meetings at 1280x800/100% and 1024x768/125% | Blocked: synthetic instance unavailable |
| J15-04 | Queue/recovery and ASAP state | Status is understandable without row color; selected action keeps focus | Meetings and detail | Blocked: synthetic instance unavailable |
| J15-05 | Cleanup recommendation; archive/delete confirmation | Confirmation names durable effect and Escape cancels | Meetings and confirmation | Blocked: synthetic instance unavailable |
| J15-06 | Hosted-summary consent and blocked state | Consent boundary is stated before action; summary status has polite live announcement | Settings and detail | Blocked: synthetic instance unavailable |
| J15-07 | Speaker review; disable/delete profile | Profile action exposes disabled reason and preserves existing meeting display names | Settings and detail | Blocked: synthetic instance unavailable |
| J15-08 | Detail read/maintain path | Summary/footer status has polite live announcement; Close has accessible name and returns focus | Detail at 1280x800/100% and 125% | Blocked: synthetic instance unavailable |

## Current source evidence

- `AccessibilityContractTests` verifies names for recording, Settings, Help,
  Meetings search/preset/list, Settings save/close, and detail close; it also
  verifies polite live status and deep-link focus fallbacks.
- `SettingsHostWindow` has `MinWidth=780` and `MinHeight=620`; `ShellTheme`
  contains contained directional navigation and local tab-navigation groups,
  with no drop shadows.
- This is source evidence only. It does not prove clipping, contrast, Narrator
  speech, focus order at runtime, or high-contrast rendering.

## Capture blocker and next action

On 2026-09-27, `MeetingRecorder.App` PID 23488 is running under a live user
profile. The app enforces a global single-instance mutex, and the available
computer-use surface does not expose native app capture. Do not open, resize,
or operate that live recording app for synthetic evidence. Add a reviewed,
test-only synthetic profile/instance path or run this matrix on an isolated
Windows account before changing this file to `Captured`.
