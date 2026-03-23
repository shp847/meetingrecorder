# Platform Deployment Checklist Template

Copy this into a future Windows app repo when the app will reuse the same corporate-safe install and update platform.
If you want shorter destination-specific markdown blocks instead, use `PLATFORM_DEPLOYMENT_SNIPPETS.md`.

Replace the placeholders:

- `<AppName>`
- `<AppExecutableName>`
- `<InstallRoot>`
- `<DataRoot>`
- `<InstallerMsiName>`
- `<InstallerExeName>`
- `<BootstrapCommandName>`
- `<BootstrapScriptName>`

## Deployment Defaults

- Per-user MSI is the preferred first-install path.
- Script bootstrap is the canonical fallback and update path.
- Optional custom EXE launcher is a thin handoff shell only.
- One deployment engine is the only writer to the managed install root.
- Writable runtime data stays outside the installed binaries.
- Self-contained desktop shells publish as single-file executables by default.
- Bundle integrity is validated before touching the managed install root.
- Installer and updater logs are written under `%TEMP%\<AppName>Installer`.

## Install Root Contract

- Managed install root: `<InstallRoot>`
- Writable data root: `<DataRoot>`
- Main desktop shell entry point: `<AppExecutableName>`
- Required external sidecars stay explicitly documented.
- The self-contained desktop shell does not require loose `.dll`, `.deps.json`, or `.runtimeconfig.json` apphost files.

## Installer Channel Rules

- `<InstallerMsiName>` installs per-user and does not require admin rights.
- `<BootstrapCommandName>` and `<BootstrapScriptName>` remain available when MSI or custom EXEs are blocked.
- `<InstallerExeName>` downloads and launches the bootstrap path, then stops.
- The EXE launcher does not extract ZIPs, copy app files directly, create shortcuts directly, or implement its own update engine.

## Security-Safe Behavior

- Avoid `Process.GetProcesses*` sweeps in installer, launcher, and updater code.
- Avoid WMI executable-path inspection and `MainModule` probing in install paths.
- Avoid `MainWindowHandle`, `CloseMainWindow`, UI Automation, or force-kill behavior as normal install/update strategy.
- Prefer cooperative shutdown, bounded waiting, and clear retry messaging.
- Treat endpoint prompts involving `WerFault.exe` as possible crash symptoms, not proof of root cause.

## Packaging Rules

- The main desktop shell stays single-file when self-contained.
- External sidecars are kept only when bootstrap, install, or worker flows truly need them.
- Bundle validation fails fast on missing or mismatched required files.
- Release packaging fails fast if the desktop shell regresses to loose apphost artifacts.

## Verification Checklist

- Run the desktop shell from the built bundle.
- Run the desktop shell again from the installed managed root.
- Keep each run alive for a defined smoke window.
- Fail on new `.NET Runtime`, `Application Error`, or `Windows Error Reporting` events mentioning `<AppExecutableName>`.
- Verify the installed executable hash matches the built bundle when using the local deploy path.
- Verify launcher shortcuts point at the canonical managed install root.

## Logging And Diagnostics

- Print the diagnostic log path early in every user-facing bootstrap/install flow.
- Preserve the real child-process exit code through wrapper scripts.
- Pause wrappers on failure so users can read the message and capture the log path.
- Enable verbose MSI logging by default when possible.

## Code Signing Expectations

- Sign the MSI, EXE, and shipped PowerShell scripts for broad distribution.
- Treat signing as trust and reputation help, not as a substitute for packaging correctness.
- Keep publisher identity stable across releases when possible.

## Questions To Answer Before Shipping

- Is MSI likely to be allowed in the target environment?
- Are scripts likely to be allowed where custom EXEs are blocked?
- Does the desktop shell need self-contained deployment?
- Which binaries must remain external sidecars?
- What install root is appropriate for a standard user?
- What data root is appropriate for writable state?
- What smoke test proves the installed app stays alive on a managed machine?
