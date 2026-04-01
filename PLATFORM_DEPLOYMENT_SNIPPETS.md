# Platform Deployment Snippets

Copy these snippets into future Windows app repos that reuse the same corporate-safe install and update platform.

Replace the placeholders:

- `<AppName>`
- `<AppExecutableName>`
- `<InstallerMsiName>`
- `<InstallerExeName>`
- `<BootstrapCommandName>`
- `<BootstrapScriptName>`
- `<InstallRoot>`
- `<DataRoot>`

## `ARCHITECTURE.md` Snippet

```md
## Corporate Deployment Model

This app is designed for security-constrained Windows laptops where all of the following may be true:

- users do not have local admin rights
- unsigned or low-reputation executables trigger SmartScreen or endpoint prompts
- MSI is allowed more often than custom bootstrap EXEs, but not always
- PowerShell is allowed in some environments where custom EXEs are blocked
- downloads may be filtered, replaced, or interrupted
- security tooling may treat cross-process inspection as suspicious behavior

Deployment defaults:

- per-user MSI is the preferred first-install path
- script bootstrap is the canonical fallback and update path
- the custom installer EXE is a thin handoff shell only
- one deployment engine is the only writer to the managed install root
- writable runtime data stays outside the installed binaries

Install contract:

- managed install root: `<InstallRoot>`
- writable data root: `<DataRoot>`
- main desktop shell entry point: `<AppExecutableName>`

Packaging defaults:

- self-contained desktop shells publish as single-file executables by default
- the self-contained shell does not require loose `.dll`, `.deps.json`, or `.runtimeconfig.json` apphost files
- external sidecars remain only when bootstrap, install, or worker flows still need them directly

Operational safety rules:

- avoid cross-process inspection and force-close behavior in installer, launcher, and updater code
- validate bundle integrity before mutating the managed install root
- use cooperative shutdown, bounded waiting, and clear retry messaging
```

## `RELEASING.md` Snippet

```md
## Corporate-Safe Release Rules

Preferred install and update channels:

1. `<InstallerMsiName>` as the preferred first-install path
2. `<BootstrapCommandName>` plus `<BootstrapScriptName>` as the canonical fallback and update path
3. `<InstallerExeName>` as an optional thin launcher over the bootstrap path
4. ZIP/manual extraction as the break-glass fallback

Release packaging rules:

- the desktop shell publishes as a single-file executable when self-contained
- the installer EXE remains a handoff shell and does not implement its own deployment engine
- one deployment engine remains the only shipped writer to the managed install root
- bundle integrity validation must fail before any install-root mutation if required files are missing or mismatched
- installer and updater logs must be written under `%TEMP%\<AppName>Installer`

Verification rules:

- smoke test the built bundle by launching `<AppExecutableName>` directly
- smoke test the installed copy by launching `<AppExecutableName>` from the managed install root
- fail on new `.NET Runtime`, `Application Error`, or `Windows Error Reporting` events that mention `<AppExecutableName>`
- verify the installed executable hash matches the built bundle when validating a managed install through the MSI or another supported release path

Security and trust rules:

- sign the MSI, EXE, and shipped PowerShell scripts for broad distribution
- treat code signing as a trust aid, not as a substitute for correct packaging
- avoid cross-process inspection and force-close behavior in bootstrap and update code
```

## `README.md` Install Summary Snippet

```md
## Installation Options

### Recommended for most users

- `<InstallerMsiName>`
  - preferred per-user installer for standard corporate laptops

### Other supported install paths

- `<BootstrapCommandName>`
  - script-based fallback when MSI or custom EXE installation is blocked
- `<BootstrapScriptName>`
  - direct PowerShell fallback for environments that allow scripts more readily than custom executables
- `<InstallerExeName>`
  - optional thin launcher that downloads and starts the bootstrap path, then steps aside

Current installer and update authority:

- one deployment engine is the only component that writes or updates the managed install tree
- the script bootstrap is the canonical interactive fallback and update path
- the custom installer EXE is a launcher over that same bootstrap path, not a peer deployment engine
- self-contained releases ship the desktop shell as a single-file executable by default
```
