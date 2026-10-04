# Whole-App Sprint 15 Native Validation Checklist

## Purpose and boundary

Use this checklist only on a non-user test profile with the rebuilt packaged
application. It completes the operating-system validation that the isolated WPF
harness cannot prove: installed-package presentation, per-monitor DPI,
high-contrast behavior, and Narrator speech. Do not record meetings, connect
providers, use personal artifacts, or retain screenshots containing personal
data.

## Record before testing

| Field | Value |
| --- | --- |
| Date and tester | |
| Windows edition/build | |
| Display scale(s) and monitor arrangement | |
| Package ZIP/MSI version and SHA-256 | |
| Test-profile root | |
| Narrator version and voice | |
| High-contrast theme | |

## Packaged UI checks

1. Install the current MSI to the test location and start `MeetingRecorder.App.exe`.
2. Capture Home at 1280x800/100% and 125%; record focus name for Open Settings.
3. Open Settings > Recording; use Tab once, press Escape, and record focus return.
4. Open Meetings with one redacted synthetic row; open Detail, press Escape, and
   record focus return to Meetings.
5. Open each destructive/consent dialog available from the synthetic profile;
   verify its stated scope, named choices, and Escape cancellation.
6. Attach PNGs, automation export if available, and no-secret notes to the S15
   evidence record. Fail on clipping, hidden focus, unnamed controls, or a
   dialog whose cancel path changes data.

## DPI and high-contrast checks

1. Move the packaged window between monitors at different supported scales,
   including 100%, 125%, and the highest supported test scale.
2. At each scale, inspect Home, Settings, Meetings, and Detail for clipped
   remediation/status text, overlap, invisible focus, and unreachable actions.
3. Enable a supported Windows high-contrast theme. Recheck the same four
   surfaces: text, borders, selected state, disabled reason, focus cue, and
   destructive/consent meaning must remain readable without color alone.
4. Restore the prior system theme after testing and record every failure with
   viewport, scale, screenshot, and minimal reproduction.

## Narrator checks

1. Start Narrator with the test profile and verify its focus announcement for
   Home primary action, Open Settings, Settings section, Meetings list, and
   Detail Close.
2. Verify spoken names/roles/help for disabled actions, destructive choices,
   hosted-summary disclosure, and Voice Profile deletion scope.
3. Verify polite status announcements do not repeat excessively after refresh,
   save, cancellation, and detail close.
4. Record the exact spoken phrase, expected phrase, result, and any issue in
   `whole-app-sprint-15-rendered-evidence.md`; do not mark S15 `Done` until all
   critical failures are fixed and retested.

