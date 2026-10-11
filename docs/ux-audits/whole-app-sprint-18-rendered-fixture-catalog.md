# Whole-App Sprint 18 — Meetings Rendered Fixture Catalog

Date: 2026-10-10
Scope: deterministic local WPF render harness, not an installed-product or assistive-technology qualification.

## Surface contract

The Meetings workbench uses the Technical Studio hierarchy from `DESIGN.md`:

- canvas (`AppCanvasBrush`) for the application base;
- structural cards (`AppCardBrush`) for filters, command lanes, and the selected-meeting inspector;
- an outlined, inset data well (`MeetingsDataWellBorderStyle` / `AppMutedCardBrush`) for the library;
- only opaque 1px-outline surfaces with 4px-or-less corners; no drop shadows or pill chips.

## Deterministic capture catalog

| Fixture | State covered | Required observable contract |
| --- | --- | --- |
| Setup blocked | Empty library with model setup required | Shell readiness and navigation remain visible. |
| Empty healthy | Empty library with a valid local model | The ready model state is present without a queue or error signal. |
| Processing | One synthetic local manifest in progress | Queue strip, list data well, selection command lane, and row state are present. |
| Selection active | Selected synthetic processing meeting | Inspector title, library well, and selection command lane remain in the current workspace. |
| Cleanup recommendation | Synthetic deterministic cleanup recommendation | Review banner remains in the initiating Meetings viewport. |
| Delete cancel / detail close | Synthetic confirmation and detail journeys | Result/cancel behavior and focus return remain covered by the existing render harness. |

Fixture content is generated under the test app root from synthetic titles, timestamps, paths, and zero-byte artifact placeholders. It contains no production meeting data or screenshots.

## Viewport baseline

The repeatable processing fixture runs at 1280x800 (100% and 125% raster scale) and 1440x900 (100%). The WPF host used for deterministic capture clamps an off-screen 1920x1080 window to its virtual desktop, so that size is an environment limit rather than a product layout result. Installed-product feedback remains the path for display combinations outside this deterministic baseline, per product-owner direction.

## Explicitly out of scope

Manual high-contrast, Narrator, and broad exploratory visual review are not release gates for this sprint. They were deliberately retired in favor of production-feedback handling; the source-level accessibility controls remain separately covered by the existing semantic and render tests.
