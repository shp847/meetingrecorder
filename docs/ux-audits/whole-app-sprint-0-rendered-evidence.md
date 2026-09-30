# Whole-App Sprint 0 Rendered Evidence

## Capture contract

Required states: `empty/healthy`, `setup-blocked`, `processing`,
`selection-active`, and `cleanup-recommendation`. The checked capture source is
the test-only `MeetingRecorder.WpfRenderProbe`: it constructs the real
`MainWindow` on a dedicated STA thread with a new disposable synthetic profile.
It does not start the production application entry point, acquire the
single-instance mutex, open capture devices, load external providers, or read
the installed profile.

Source images, automation traces, keyboard traces, and progress logs are
ignored under `.artifacts/ux-audits/whole-app-sprint-0/`. This committed record
contains only artifact-relative paths, SHA-256 values, and observations.

## 2026-09-30 result — captured and reviewed

Each state was captured with a 1280×800 logical WPF viewport. The 100% captures
are 1280×800 at 96 DPI; the 125% captures are the same viewport rendered as
1600×1000 pixels at 120 DPI. All fixtures use synthetic names and contained
test folders.

| State | 100% artifact and SHA-256 | 125% artifact and SHA-256 | Review |
| --- | --- | --- | --- |
| empty/healthy | `20260930-072351/7c1d989b64e241b3bb77f80ecd43e517/empty-healthy-1280x800-100.png` `15846f78ef2b7bceea4b27a1c74c50dbb5e5b6c6efdc0e3bdd323c803127f2a8` | `20260930-072400/7f2bc094843c438788ab2fac60a3c181/empty-healthy-1280x800-125.png` `2db0dc7fd59fbf7e1798327db06231d3852fe1aca5bb2d582c8591d1e51cf67b` | Ready state, primary action, and Settings/Help remain visible. |
| setup-blocked | `20260930-072353/155db1d0b452435297e36d92670c5360/setup-blocked-1280x800-100.png` `89c63adeea9fba373c17aa0b7872ef674c9149936de5df3bd4e3fde8bb347ac1` | `20260930-072402/e173592d2a3041c3afd0ccd9aae0989a/setup-blocked-1280x800-125.png` `577f80e4c94c95996f91011e54270f65e577bd7c182abf9cd02d4e7c80403187` | Setup reason and recovery action are visible. |
| processing | `20260930-072355/4fb10d618c57439288891037153c1e58/processing-1280x800-100.png` `1cdc91ec099c9f3208a45179f9ad263ff9f0f730f4380e684a69f304e170375f` | `20260930-072404/bdc3dc359cd845fb8847a616cd3fa1fd/processing-1280x800-125.png` `3b1d310816fd2ff00a619c2d8c60f9a6c952114fb9697ed80c4da24b6129520c` | Meeting controls and status remain visible; header detail trims inside its card. |
| selection-active | `20260930-072356/770ac40632914e7981de18f9728c31a9/selection-active-1280x800-100.png` `ba422dcf73a65dab658a7e9264796a36b74b8ebd0140a53b918e84726704117a` | `20260930-072405/82c570636f1940058b893c2d124de304/selection-active-1280x800-125.png` `bce26cb927d45736492071cba2473602725b728ea2d238215aa827e43aeb0184` | Synthetic selected-meeting inspector is present without overflow. |
| cleanup-recommendation | `20260930-072358/ac0a67b93a924133a56d3960eb497c40/cleanup-recommendation-1280x800-100.png` `655ca44ef38486d886f32997d8b237062c620c84d140596835979d03aae1084d` | `20260930-072407/2adec44b567c429087638ab6d1261a57/cleanup-recommendation-1280x800-125.png` `2d03628196b8c00200e8a375dd90ba1fea1e969b9548b7ecc84b0ecbb30d173f` | Contained cleanup banner and its explicit actions remain visible. |

The capture exposed two owner defects and the same source revision fixes both:
the window could not honor 800px height (`MinHeight=860`), and the queue card
expanded past the header because its width was only a minimum. `MainWindow`
now permits an 800px minimum height and reserves a fixed 176px queue-card
footprint. The reviewed processing and cleanup captures above confirm the
corrected header at both scales.

Automation traces preserve the full state text even where visual detail uses
ellipsis. Keyboard traces confirm focus on `Open Settings`, forward Tab
navigation, and primary-navigation focus for every fixture. The rendered
surfaces retain the `DESIGN.md` technical-studio constraints: opaque panels,
thin outlines, small radii, and no shadows.

## Remaining limits

This run is evidence for the five redacted baseline states only. It does not
claim a packaged render, a 1024×768 viewport, high-contrast behavior, complete
nine-journey keyboard replay, or Narrator validation. Those are tracked by
Sprint 15; Sprint 0 remains `Partial` until its documented manual journey
replay is recorded.
