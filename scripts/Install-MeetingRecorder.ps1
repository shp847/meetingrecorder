param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\MeetingRecorder\app"
)

$ErrorActionPreference = "Stop"

$packageRoot = $PSScriptRoot
$sourceAppRoot = Join-Path $packageRoot "MeetingRecorder"

if (-not (Test-Path $sourceAppRoot)) {
    throw "Unable to find the packaged application folder at '$sourceAppRoot'."
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -Path (Join-Path $sourceAppRoot "*") -Destination $InstallRoot -Recurse -Force

$portableDataRoot = Join-Path $InstallRoot "data"
New-Item -ItemType Directory -Force -Path (Join-Path $portableDataRoot "logs") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $portableDataRoot "config") | Out-Null

$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Meeting Recorder.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($desktopShortcutPath)
$shortcut.TargetPath = Join-Path $InstallRoot "MeetingRecorder.App.exe"
$shortcut.WorkingDirectory = $InstallRoot
$shortcut.IconLocation = Join-Path $InstallRoot "MeetingRecorder.App.exe"
$shortcut.Description = "Meeting Recorder"
$shortcut.Save()

Write-Host "Meeting Recorder installed to $InstallRoot"
Write-Host "Portable app data will be stored in $portableDataRoot"
Write-Host "Desktop shortcut created at $desktopShortcutPath"
Write-Host "Run MeetingRecorder.App.exe to start the app."
