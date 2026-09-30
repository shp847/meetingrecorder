# Whole-App Sprint 15 Rendered Evidence

## Scope and safety

The test-only `MeetingRecorder.WpfRenderProbe` constructed the real
`MainWindow` on a dedicated STA thread with a disposable synthetic profile. It
did not run application startup, acquire the primary-instance mutex, open
capture devices, read a user profile, invoke providers, or use real meeting
artifacts. PNGs, automation traces, keyboard traces, and progress logs are
ignored under `.artifacts/ux-audits/whole-app-sprint-15/`.

The capture contract fails when WPF changes the requested logical viewport.
This exposed the former 1280×800 minimum; the app now supports 1024×768.
The header also now reserves bounded primary-status and queue footprints, so
Settings and Help remain reachable in the narrower view.

## 2026-09-30 capture record

All paths below are relative to `.artifacts/ux-audits/whole-app-sprint-15/`.
Each state has an automation-tree and keyboard-trace sibling in the same
directory. The 100% 1280×800 images are 1280×800 at 96 DPI. The 125% 1280×800
and 1024×768 images are 1600×1000 and 1280×960 at 120 DPI respectively.

| State | 1280×800 / 100% | 1280×800 / 125% | 1024×768 / 125% |
| --- | --- | --- | --- |
| empty/healthy | `20260930-073615/a1a4afb60d954eea9e886f8abb41f503/empty-healthy-1280x800-100.png` `57ca988da2d14fe3bbe1168e9ae57b8ad5ab67d3f5e7c2955bbaf802fad426f9` | `20260930-073624/9de20b28395c4663af6e0a00946760a6/empty-healthy-1280x800-125.png` `30050d052cbd2e3b5da77bd3fdbd196afb0b3bbb3334b20b6aa25a43e22ba6c6` | `20260930-073634/40c2820d9476484c8fad464add9cbe65/empty-healthy-1024x768-125.png` `8ace359e1a045c248545427418fc22198c5898291d412b9dffb934754629191e` |
| setup-blocked | `20260930-073617/fceae501a9ae415b9c62f2b0baa4e6d5/setup-blocked-1280x800-100.png` `d8f993f614383fa5c2b93807de2171428d51891769ed1c63d00e2f41dced63a4` | `20260930-073626/61437cec349f4265a3e82234692168db/setup-blocked-1280x800-125.png` `a5212a1eb1bb82bbf813ad2149a1b6c5a1b449c702db2a8a9338f01026459fac` | `20260930-073635/ced448d60fd942b19f5845ab3bf6b1a5/setup-blocked-1024x768-125.png` `dcdb04cdfd0fb884a0628c0be021003ed6d571d90ee058370ccaaed1ae038b05` |
| processing | `20260930-073619/aedbd888fc434758a0a51b5a8998ff07/processing-1280x800-100.png` `ec7981d8b813c90e668c1af83fb9a9521762a322d3946dfbf80797495a57c266` | `20260930-073628/a6115becd3e5417faf46d3cf91e2f6ad/processing-1280x800-125.png` `fbe54302b45996c57e8fa5fcdd6617ae0fbc940ff589096d8179c0a1e079a337` | `20260930-073637/ba7167a7da3743f98c078c3057fbb269/processing-1024x768-125.png` `a885238e2dc404463a0c288c8525f455c83b218e31dca12ede5dc245f3667432` |
| selection-active | `20260930-073621/a13ca66f159f472e8069e929f568c79d/selection-active-1280x800-100.png` `6baedeaf36cf9060d891a8433276158c41e2f89953d30facfad737b7990df50b` | `20260930-073630/330f42b5074d41129c76e69816035e86/selection-active-1280x800-125.png` `edda1123dd31028501bbc5e65fdd92218e295940a455ed735e340a51d4ccf714` | `20260930-073639/3f2ced59feff462a9a7d549d59045c5f/selection-active-1024x768-125.png` `b096b19895a350bf6681ecedbe36bf3ea5d845d54b0c4a68f844d86d5cbbd3b5` |
| cleanup-recommendation | `20260930-073622/0846cdb9d1a449f49e2e511e678ef652/cleanup-recommendation-1280x800-100.png` `4d7f35d080a96ba6ecbbeaf227a510b926088e5b16e96768df8a788a59b8c371` | `20260930-073632/79e9b842a0ed40f9af1eb074201b2a7f/cleanup-recommendation-1280x800-125.png` `92ebda9a7d9f5bd8170269f277f7da77b2f67694ccc25c7cd5f7eeed94a4267a` | `20260930-073641/d6510aa4d0da4a98924fd7dba21e7f48/cleanup-recommendation-1024x768-125.png` `0f649b7baf7e570b79b0b312fced7d21a753603e7426dd71ef63b7aa2f5ce740` |

The processing state also rendered at 1280×800/200% as
`20260930-073643/b0d2366949464202a98aefd26cd4405c/processing-1280x800-200.png`
with SHA-256 `c65c84b1f825a00cf86ca83e378b220197c22fe1ee9a45be68afb5dd321736a8`.

Settings > Recording rendered at 1280×800/125% as
`20260930-075154/32c06cf14b1e4237b098ccba08d2c4fd/settings-recording-1280x800-125.png`
with SHA-256 `4107e381c89d7edae04032d3c7845e346b94115941f5e18d07a3519679b62cef`.
Its keyboard trace confirms that Escape closes Settings and returns focus to
the `Open Settings` control that opened it.

The same isolated profile toggled and saved the harmless calendar-title
fallback at 1280×800/125% as
`20260930-075838/00f253e0bc0647aebeeacc60205d4dc3/settings-recording-saved-1280x800-125.png`
with SHA-256 `465a64b4ddd1b6cc16267cb9309975caa6a3f05174e912b0dda313f48ab59eaa`.
The trace proves the edit enables Save Changes, persists through the running
test profile, reports success, and returns focus to `Open Settings` on Escape.

The synthetic permanent-delete confirmation rendered at 1280×800/125% as
`20260930-080547/64c26b59c3514575b80273187ba23b42/permanent-delete-cancelled-1280x800-125.png`
with SHA-256 `279ce4a59b3038cabf67bae8b94f9ae640b99fb4e5c87d58a4589a217e10b9b1`.
Its accessible text input and cancel action are named, the irreversible action
is disabled before exact `DELETE`, and Escape cancels with focus back on the
Meetings list. No synthetic artifact is deleted.

Visual review found no header overflow at the supported viewports. The Meetings
table deliberately retains a horizontal scroll surface at 1024px rather than
hiding columns. Automation evidence includes full setup-state text, accessible
meeting-list identity, selected-meeting title, cleanup status, `Open Settings`,
forward Tab navigation, and primary navigation focus.

## Package smoke

`Build-Installer.ps1` rebuilt the portable payload and MSI. The standard smoke
command started its portable phase and completed MSI installation before the
executor's 30-second ceiling. A follow-up deterministic check verified all 15
required installed bundle files against `bundle-integrity.json`, then started
the MSI-installed `MeetingRecorder.App.exe` for five seconds with no early exit
or qualifying Windows crash event. This is startup evidence, not packaged UI
render evidence.

## Limits

Raster DPI scales the synthetic image and is not a substitute for OS
per-monitor-DPI behavior, high-contrast rendering, or Narrator speech. The
hosted-consent, profile-delete, and detail-return journeys remain unexecuted.
Their evidence is still required before Sprint 15 can be marked `Done`.
