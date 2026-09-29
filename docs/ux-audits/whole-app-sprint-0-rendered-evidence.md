# Whole-App Sprint 0 Rendered Evidence

## Capture contract

Required states: `empty/healthy`, `setup-blocked`, `processing`,
`selection-active`, and `cleanup-recommendation`. Capture each at 1280×800 and
125% DPI from the redacted synthetic profile. Save source images only under
ignored `.artifacts/ux-audits/whole-app-sprint-0/`; this committed file records
relative artifact names, SHA-256 values, visual observations, and no payload.

## Current result — blocked

No screenshots were saved in this audit run. The only available Meeting Recorder
process is a long-running installed instance using its live profile. The app
enforces the global `Local\\MeetingRecorder.PrimaryInstance` mutex, so a second
copy cannot reach a separate portable or synthetic profile. Opening that live
window would violate the sprint's redaction rule. This is a named capture blocker,
not a claim that the UI has been rendered or accessibility-tested.

| Required state | Viewports | Artifact | SHA-256 | Result |
| --- | --- | --- | --- | --- |
| empty/healthy | 1280×800; 125% DPI | Not captured | — | Blocked by safe synthetic-instance gap |
| setup-blocked | 1280×800; 125% DPI | Not captured | — | Blocked by safe synthetic-instance gap |
| processing | 1280×800; 125% DPI | Not captured | — | Blocked by safe synthetic-instance gap |
| selection-active | 1280×800; 125% DPI | Not captured | — | Blocked by safe synthetic-instance gap |
| cleanup-recommendation | 1280×800; 125% DPI | Not captured | — | Blocked by safe synthetic-instance gap |

## Visual and accessibility limit

`DESIGN.md` is the standard for later inspection: opaque nested surfaces, 1px
technical lines, 4px radii, no shadows, dense information, and clear focusable
controls. This source-only run cannot establish contrast, focus order, target
size, screen-reader names, keyboard traversal, clipping, or 200% scaling.

## Unblocker

Add a reviewed test-only synthetic profile/instance identity or run on an
isolated clean Windows account. That work is outside Sprint 0 because it changes
runtime launch behavior and needs its own safety, test, packaging, and release
contract.
