# Environment Restrictions And Corporate Laptop Lessons

Use this file beside `AGENTS.md` in projects that must build, install, update, or run on security-constrained Windows corporate laptops. It captures restrictions and design lessons learned while shipping a local-first Windows desktop app in a managed environment.

## 1. Default Assumptions

Assume all of these can be true at once:

- The user has no local admin rights.
- The machine is Windows and may be managed by corporate endpoint tooling.
- Unsigned, low-reputation, or newly downloaded executables can trigger SmartScreen or endpoint prompts.
- MSI installs are often allowed more readily than custom bootstrap executables, but either can be blocked.
- PowerShell may be allowed in contexts where custom executables are blocked.
- Script execution policy may require `powershell -ExecutionPolicy Bypass -File ...` for project scripts.
- Downloads can be filtered, replaced with HTML block pages, interrupted, or quarantined.
- OneDrive-backed folders may be present under the user profile and may dehydrate files.
- Network access may work in browsers but fail or behave differently in CLI tools.
- Corporate proxy, TLS inspection, or package-source restrictions may affect restores and downloads.
- The laptop may be CPU-only and unsuitable for heavyweight background models or GPU assumptions.
- Security tooling may flag cross-process inspection, window inspection, forced shutdown, or broad process enumeration.

Architectural consequence: design for per-user install, non-admin operation, user-writable data, deterministic validation, recoverable downloads, clear logs, and multiple install paths.

## 2. Install And Update Channel Strategy

Use a priority order rather than betting on one channel:

1. Per-user MSI as the preferred first-install path.
2. Command plus PowerShell bootstrap as the canonical fallback and update path.
3. Optional custom EXE launcher only as a thin handoff shell.
4. Manual ZIP extraction as the break-glass fallback.

Recommended defaults:

- Install app binaries under a standard-user writable managed install root.
- Keep runtime data outside installed binaries.
- Keep script/bootstrap wrappers thin.
- Route all real install and update writes through one deployment engine.
- Preserve user data on uninstall and reinstall unless the user explicitly chooses deletion.

## 3. Single Writer Rule

Make one component the only writer to the managed install root.

Recommended:

- MSI, bootstrap scripts, updater UI, release tools, and manual install paths should all delegate file promotion and update application to the same deployment engine.
- PowerShell and CMD wrappers should handle orchestration, logging, argument passing, and user messages, not complex file mutation.

Avoid:

- Separate copy/update implementations in MSI custom actions, EXE installers, scripts, in-app updaters, and manual repair tools.
- Ad hoc install-root mutation from UI code.
- Updating binaries directly from multiple components.

## 4. Thin Installer EXE Rule

If a custom installer EXE exists, keep it thin.

It may:

- Resolve release metadata.
- Download bootstrap assets.
- Write a diagnostic log.
- Launch the command or PowerShell bootstrap path.
- Forward explicit arguments.

It must not:

- Extract release ZIPs itself.
- Copy app binaries into the install root itself.
- Create shortcuts directly.
- Implement a separate update engine.
- Inspect or control running app processes aggressively.

Thin launchers are easier to replace when endpoint policy changes and easier to explain to IT.

## 5. Packaging Rules For Windows Desktop Apps

- For WPF or similar Windows desktop shells, prefer a single-file self-contained executable for the main shell.
- Keep external sidecars only when bootstrap, worker, plugin, model, or update flows truly need them.
- Do not rely on loose `.dll`, `.deps.json`, or `.runtimeconfig.json` files for the main self-contained desktop shell unless there is a documented reason.
- If single-file publish is used, resolve the running executable from the process path, not from a transient extraction directory.
- Include a bundle manifest such as `bundle-integrity.json` and validate it before modifying the install root.
- Fail packaging if required files, worker sidecars, scripts, or metadata are missing.

Important lesson: endpoint prompts involving crash-reporting processes can be symptoms of broken packaging or startup crashes, not proof that the installer itself is the root cause.

## 6. Process And Shutdown Restrictions

Avoid these in installer, bootstrap, updater, and normal startup paths:

- Broad `Process.GetProcesses*` sweeps.
- WMI process-path enumeration.
- `MainModule` executable-path inspection.
- `MainWindowHandle` probing.
- UI Automation against unrelated app windows.
- `CloseMainWindow` as normal update behavior.
- Force-kill behavior as a normal install/update strategy.
- Cross-process memory, module, or window inspection unless the feature absolutely requires it and has a safe fallback.

Prefer:

- Cooperative shutdown signals.
- Explicit "install when idle" behavior.
- Bounded waiting.
- Normal file-replacement retries.
- Clear retry messages when files remain locked.
- Deferring updates while recording, processing, syncing, or doing other user-visible work.

For audio or meeting-detection features, classify local session state first and resolve process names only when necessary for active non-system sessions that can affect the feature.

## 7. File-System And Data Layout Restrictions

Separate versioned binaries from writable runtime state.

Recommended split:

- Managed install root: user-writable app binaries and launchers.
- Data root: config, logs, work files, models, caches, downloaded assets, provenance, and user-generated data.
- Temporary installer/update logs: `%TEMP%\<AppName>Installer`.
- Per-user app state: `%LOCALAPPDATA%\<AppName>`.
- User output: a clear user-owned folder such as `Documents\<AppName>` or another explicit location.

Rules:

- Do not write mutable app data into the installed binaries folder.
- Do not assume OneDrive files are always hydrated.
- Handle long paths, spaces, parentheses, and corporate profile paths.
- Preserve user data during update, uninstall, and reinstall unless explicitly deleting.
- Treat local model or profile data as local-only unless the product explicitly says otherwise.

## 8. Download And Package Validation

Before applying an update or install package:

- Confirm the asset is the intended app package, not a model bundle, script, MSI, HTML error page, or unrelated release asset.
- Check filename shape, extension, version, expected metadata, and required files.
- Validate hashes or manifest entries.
- Reject partial downloads, corrupt ZIPs, missing pending files, size mismatches, and wrong asset types before asking the running app to exit.
- Keep downloaded packages or staging folders only when they are useful as validated repair sources.

Do not mutate the install root until validation succeeds.

## 9. Logging And Diagnostics

Corporate users need diagnostics that do not require developer tools.

Defaults:

- Write installer, bootstrap, and update logs under `%TEMP%\<AppName>Installer`.
- Print the log path early.
- Suppress noisy raw transfer progress when it obscures status.
- Preserve child-process exit codes.
- Pause user-facing console wrappers on failure so the user can read the message.
- Enable verbose MSI logging by default when possible.
- Include enough context to distinguish download failure, endpoint block, validation failure, locked files, app crash, and missing dependency.

When troubleshooting startup:

- Check `.NET Runtime`, `Application Error`, and `Windows Error Reporting` events.
- Launch the built bundle directly.
- Launch the installed copy directly.
- Compare behavior before blaming endpoint policy.

## 10. Verification Expectations

Do not stop at "the build passed" for installer or runtime changes.

Recommended verification:

- Run unit and integration tests.
- Build the release bundle.
- Build installer assets.
- Launch the desktop shell from the built bundle.
- Install through the supported installer path.
- Launch the installed copy from the managed install root.
- Keep each launch alive for a meaningful smoke window.
- Fail on new crash-related Windows events for the app executable.
- Verify shortcuts point to the canonical managed install root.
- Verify the installed executable hash matches the built bundle when validating managed install promotion.

For changes that affect reusable platform, deployment, extraction, or update code, include the focused platform test project even if the main verification script does not run it by default.

## 11. Package Restore And Build Constraints

Common corporate friction:

- `rg` may be unavailable. Fall back to PowerShell search.
- NuGet restore may need ignored failed sources or disabled audit depending on the environment.
- Package feeds can be blocked, slow, mirrored, or certificate-inspected.
- Build servers can hold locks. Shutdown of build servers may be needed before clean builds.
- Long-running scripts should be run from the repo root with explicit PowerShell commands.

Project scripts should document exact commands and flags, including any environment-specific restore flags such as:

- `RestoreIgnoreFailedSources=true`
- `NuGetAudit=false`
- `-ExecutionPolicy Bypass`

Use these only when the project has adopted them intentionally and documented why.

## 12. Code Signing And Trust

- Sign MSI, EXE, and shipped PowerShell scripts for broad distribution.
- Keep publisher identity stable across releases when possible.
- Remember that signing helps identity and reputation, but does not fix broken packaging, missing files, bad update validation, or suspicious process behavior.
- SmartScreen reputation may still lag for new certificates or rarely downloaded binaries.

## 13. Release Hygiene For Managed Machines

- Treat installer assets as part of the shipped product.
- Upload only the intended release assets for each channel.
- Keep update package selection narrow and explicit.
- Avoid same-version auto-update loops when a release is republished with the same display version.
- Record install provenance locally so the app can distinguish installed package identity from remote release identity.
- Prefer repairable staged installs over destructive replacement.
- Keep old user data and work artifacts out of the install-root replacement path.

## 14. Early Project Questions

Answer these before the first release:

- Is MSI likely to be allowed for the target users?
- Are PowerShell scripts allowed where custom EXEs are blocked?
- Does the desktop shell need self-contained deployment?
- Which sidecars must remain external?
- What install root works without admin rights?
- What data root preserves user state across uninstall and reinstall?
- What logs can a non-developer find and send?
- What smoke test proves the app stays alive after install?
- What update package shapes are valid?
- What behavior should happen if the app is busy, recording, processing, or syncing during an update?

## 15. New Project Checklist

For a new project, fill these in immediately:

- App name: `<AppName>`
- Main executable: `<AppExecutableName>`
- Managed install root: `<InstallRoot>`
- Writable data root: `<DataRoot>`
- Installer MSI: `<InstallerMsiName>`
- Bootstrap command: `<BootstrapCommandName>`
- Bootstrap script: `<BootstrapScriptName>`
- Release ZIP pattern: `<AppName>-v<version>-win-x64.zip`
- Installer log directory: `%TEMP%\<AppName>Installer`
- Main verification command: `<command>`
- Installer build command: `<command>`
- Packaged startup smoke test: `<command>`
