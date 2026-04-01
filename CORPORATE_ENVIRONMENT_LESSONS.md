# Secure Corporate Environment Lessons

This document captures the reusable lessons learned while shipping and troubleshooting Meeting Recorder on security-constrained Windows laptops.

The intended audience is future app teams that want to reuse the same platform-level install and update stack:

- `AppPlatform.Deployment`
- `AppPlatform.Deployment.Cli`
- `AppPlatform.Deployment.WpfHost`
- `AppPlatform.Deployment.Wix`
- `Install-LatestFromGitHub.cmd/.ps1`
- `MeetingRecorderInstaller.exe`-style thin launchers
- per-user MSI plus ZIP/bootstrap release flows

The goal is to preserve the parts that worked, avoid the parts that caused friction, and give future apps a safer default architecture from day one.

If you want the shorter copy-paste version for a new repo, use `PLATFORM_DEPLOYMENT_CHECKLIST_TEMPLATE.md`.
If you want destination-specific markdown blocks for `ARCHITECTURE.md`, `RELEASING.md`, and `README.md`, use `PLATFORM_DEPLOYMENT_SNIPPETS.md`.

## 1. Start From Corporate-Laptop Assumptions

Assume all of these can be true at once:

- the user has no local admin rights
- unsigned or low-reputation executables trigger SmartScreen or endpoint prompts
- MSI installs are allowed more often than custom bootstrap EXEs, but not always
- PowerShell is allowed in some places where custom EXEs are not
- OneDrive-backed folders may be present in the user profile
- downloads can be filtered, replaced, or interrupted
- security tooling may flag cross-process inspection even when the app is not malicious
- the machine may be CPU-only and unable to tolerate heavyweight background components

Architectural consequence:

- use per-user installs
- keep writable data outside the installed binaries
- provide multiple install paths
- treat diagnostics and recovery as first-class features

## 2. Make One Component The Only Writer To The Install Root

The most important deployment lesson was to centralize install and update writes in one reusable component.

Recommended default:

- `AppPlatform.Deployment.Cli` is the only shipped writer/updater of the managed install tree

Why this matters:

- PowerShell wrappers stay thin and easier to audit
- the EXE launcher stays replaceable
- MSI, bootstrap scripts, manual ZIP installs, and in-app updates can converge on one install/apply implementation
- bundle validation, logging, provenance, shortcut repair, and data-preservation rules live in one place instead of drifting

Anti-pattern to avoid:

- giving the MSI path, EXE bootstrapper path, script path, and in-app update path separate file-copy or install-root mutation logic

## 3. Keep The Installer EXE Thin

The custom installer EXE should be a launcher, not a peer deployment engine.

Recommended EXE responsibilities:

- resolve release metadata
- download the bootstrap command and companion script
- write a diagnostic log
- launch the bootstrap path
- stop there

It should not:

- extract ZIPs itself
- copy app files into the managed install root itself
- create shortcuts directly
- implement its own update engine
- inspect or control running app processes aggressively

Why:

- thin launchers are easier to replace when endpoint policy changes
- the script path remains a stable fallback
- fewer responsibilities means fewer security-sensitive failure modes

## 4. Prefer Per-User MSI First, But Keep Script Fallbacks

The most robust install story on locked-down Windows laptops is not one format. It is a priority order.

Recommended priority:

1. per-user MSI
2. command bootstrap (`.cmd` + `.ps1`)
3. optional custom EXE launcher
4. manual ZIP fallback

Why this order worked:

- MSI is often the most corporate-friendly first-install path
- script bootstrap remains the lowest-friction fallback when MSI or custom EXEs are blocked
- ZIP/manual extraction still gives users a break-glass path

Do not assume one channel will survive every environment.

## 5. Use Single-File Self-Contained For The WPF Shell

The key packaging lesson from this incident was that the MSI was not the primary problem.

What actually happened:

- the shipped loose-file self-contained WPF shell could crash on startup with `System.IO.FileNotFoundException` for `WindowsBase`
- the resulting Windows Error Reporting flow could surface as an endpoint popup about `WerFault.exe` or restricted access
- that made the issue look like installer or endpoint-policy trouble when the root cause was the app packaging layout

Reusable rule:

- if the main desktop shell is WPF and you want a self-contained release, publish that shell as a single-file executable by default

Why:

- fewer loose apphost artifacts
- fewer chances for WindowsDesktop runtime files to be missing or mismatched at launch
- cleaner install-root contract for launchers and deployment tooling

The current reusable app-shell contract is:

- ship `MeetingRecorder.App.exe`-style apphosts for the desktop shell
- do not require loose `App.dll`, `.deps.json`, or `.runtimeconfig.json` files for the WPF shell in self-contained releases
- keep other sidecars external only when the bootstrap/update mechanisms still need them directly

## 6. Do Not Treat Endpoint Prompts As Proof Of The Root Cause

A security popup can be a symptom, not the underlying defect.

Important lesson:

- if endpoint tooling reports access involving `WerFault.exe`, check whether the app is actually crashing first

Recommended first checks:

- Application log events for `.NET Runtime`
- Application log events for `Application Error`
- Application log events for `Windows Error Reporting`
- process lifetime from launch for both the published bundle and the installed copy

Do this before concluding that:

- the MSI is broken
- endpoint policy is the only blocker
- SmartScreen or Defender is the main issue

## 7. Avoid Cross-Process Inspection And Control In Install/Update Paths

Security-constrained environments are especially sensitive to installers or launchers that inspect other processes.

Avoid in installer/update/bootstrap code:

- `Process.GetProcesses*` sweeps over unrelated processes
- `MainModule`, executable-path inspection, or WMI process-path enumeration
- `MainWindowHandle` probing
- UI Automation against unrelated app windows
- `CloseMainWindow`
- force-kill behavior as a normal install strategy

Prefer instead:

- cooperative shutdown signals
- bounded waiting
- normal file-replacement retries
- a clear retry message when the app still will not release the install path

Reason:

- even legitimate process inspection can look like memory access or tampering to endpoint tooling
- cooperative paths are easier to explain to IT and to users

## 8. Keep The Managed Install Root Separate From Writable Data

Corporate update reliability improved when installed binaries and writable runtime state were clearly separated.

Recommended split:

- install root for versioned binaries and launchers
- separate data root for config, logs, work files, and downloaded assets

Why:

- updates can replace binaries without tripping over user-generated files
- data survives reinstall and channel changes more cleanly
- the deployment engine can preserve known directories intentionally instead of guessing

## 9. Validate The Bundle Before Touching The Install Root

Every install/update flow that consumes a ZIP or extracted bundle should validate the bundle before mutating the managed install tree.

Recommended pattern:

- emit a manifest like `bundle-integrity.json`
- verify required files and hashes
- abort before changing the install root if validation fails

Why:

- protects against partial downloads
- protects against bad release uploads
- makes failures deterministic and diagnosable

## 10. Log Everything User-Facing To A Stable Temp Location

Corporate environments need diagnostics that users can find without developer tools.

Recommended defaults:

- write installer/bootstrap/update logs under `%TEMP%\AppNameInstaller`
- print the log path early
- preserve the real child-process exit code
- keep user-facing wrappers paused on failure so the message is visible
- enable verbose MSI logging by default when possible

This is especially important when:

- PowerShell downloads fail
- endpoint tooling blocks a child process
- the app cannot release the install path
- the app crashes immediately after install

## 11. Verify Both The Bundle And The Installed Copy

Do not stop after a build succeeds.

Recommended packaging verification:

- run the desktop shell from the built bundle
- run the desktop shell again from the installed managed root
- keep each alive for a meaningful smoke window
- fail on new crash-related Windows events for the app executable
- verify the installed executable hash matches the built bundle when you intentionally validate a managed install through the MSI or another supported release path

This catches:

- bundle-only regressions
- MSI-only regressions
- install-root promotion issues
- startup crashes that never reach the UI

## 12. Code Signing Helps, But It Is Not The Whole Story

Trusted code signing is necessary for smooth distribution, but it does not replace architectural hardening.

Keep in mind:

- signing fixes publisher identity
- signing helps SmartScreen, AppLocker, WDAC, and script execution policy
- SmartScreen reputation still depends on certificate and download history
- signing does not fix broken packaging
- signing does not justify aggressive process inspection

Recommended default:

- sign MSI, EXE, and shipped PowerShell scripts when distributing broadly

## 13. Reusable Defaults For Future Apps

If a future Windows desktop app reuses this deployment platform, start with these defaults:

1. Per-user MSI as the preferred install path.
2. Script bootstrap as the canonical fallback and update path.
3. Optional EXE launcher that only hands off.
4. One deployment engine as the only writer to the managed install tree.
5. Self-contained desktop shell published as a single-file executable.
6. Writable runtime data outside the installed binaries.
7. Bundle integrity validation before promotion.
8. Cooperative shutdown plus bounded waiting instead of process inspection or force-close behavior.
9. Stable diagnostic logs under `%TEMP%`.
10. Release smoke tests for both the built bundle and the installed copy.

## 14. Questions Future App Teams Should Ask Early

Before adopting this install/update stack, answer these upfront:

- Is MSI likely to be allowed in the target environment?
- Is a custom EXE likely to be blocked more often than scripts?
- Does the main desktop shell need self-contained deployment?
- Which binaries truly need to remain external sidecars?
- What install root and data root are appropriate for a standard user?
- What logs will a non-developer need when install or update fails?
- What smoke test proves the app stays alive after install on a managed machine?

If those answers are decided early, future apps can inherit the stable parts of this platform without replaying the same corporate-environment failures.
