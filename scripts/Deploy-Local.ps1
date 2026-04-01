param(
    [switch]$BuildFirst,
    [switch]$FrameworkDependent,
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$Launch,
    [switch]$NoLaunch,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$BundleRoot = ".artifacts\\publish\\win-x64\\MeetingRecorder"
)

$ErrorActionPreference = "Stop"

$message = @"
Local repo deployments are disabled for publishing validation.

Use MeetingRecorderInstaller.msi for fresh-install testing, and use the in-app update path for upgrade testing.
If you need to validate published assets, rebuild them and install through the MSI, EXE bootstrapper, or release scripts instead of the old local deploy path.
"@

throw $message
