param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$BundleRoot = ".artifacts\publish\win-x64\MeetingRecorder",
    [switch]$BuildFirst,
    [switch]$FrameworkDependent,
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot

function New-DeployLogPath {
    $logDirectory = Join-Path $env:TEMP "MeetingRecorderInstaller"
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    return Join-Path $logDirectory ("deploy-local-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}

function Resolve-BundleRoot {
    param(
        [string]$CandidateBundleRoot
    )

    $resolvedRoot = if ([string]::IsNullOrWhiteSpace($CandidateBundleRoot)) {
        Join-Path $repoRoot ".artifacts\publish\$Runtime\MeetingRecorder"
    }
    elseif ([System.IO.Path]::IsPathRooted($CandidateBundleRoot)) {
        $CandidateBundleRoot
    }
    else {
        Join-Path $repoRoot $CandidateBundleRoot
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($resolvedRoot)
    if (-not (Test-Path (Join-Path $resolvedRoot "MeetingRecorder.App.exe"))) {
        throw "Could not find a published Meeting Recorder bundle at '$resolvedRoot'. Run Publish-Portable.ps1 first or use -BuildFirst."
    }

    return $resolvedRoot
}

function Resolve-ManifestPath {
    param(
        [string]$ResolvedBundleRoot
    )

    $manifestPath = Join-Path $ResolvedBundleRoot "MeetingRecorder.product.json"
    if (-not (Test-Path $manifestPath)) {
        throw "Could not find MeetingRecorder.product.json at '$manifestPath'."
    }

    return $manifestPath
}

function Read-Manifest {
    param(
        [string]$ManifestPath
    )

    return Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
}

function Resolve-InstallRoot {
    param(
        [object]$Manifest
    )

    $installRoot = [string]$Manifest.managedInstallLayout.installRoot
    if ([string]::IsNullOrWhiteSpace($installRoot)) {
        throw "The bundle manifest does not declare a managed install root."
    }

    return [Environment]::ExpandEnvironmentVariables($installRoot)
}

function Resolve-DeploymentCliPath {
    param(
        [string]$ResolvedBundleRoot
    )

    $deploymentCliPath = Join-Path $ResolvedBundleRoot "AppPlatform.Deployment.Cli.exe"
    if (-not (Test-Path $deploymentCliPath)) {
        throw "Could not find AppPlatform.Deployment.Cli.exe in '$ResolvedBundleRoot'."
    }

    return $deploymentCliPath
}

function Assert-FileHashMatches {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [string]$Label
    )

    if (-not (Test-Path $SourcePath)) {
        throw "$Label source file '$SourcePath' does not exist."
    }

    if (-not (Test-Path $DestinationPath)) {
        throw "$Label deployed file '$DestinationPath' does not exist."
    }

    $sourceHash = (Get-FileHash -Path $SourcePath -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash -Path $DestinationPath -Algorithm SHA256).Hash

    if ($sourceHash -ne $destinationHash) {
        throw "$Label hash mismatch. Source '$SourcePath' hash '$sourceHash' does not match '$DestinationPath' hash '$destinationHash'."
    }

    Write-Host "$Label hash verified: $destinationHash"
}

function Assert-ShortcutTargetsLauncher {
    param(
        [string]$ShortcutPath,
        [string]$InstallRoot,
        [string]$LauncherPath
    )

    if (-not (Test-Path $ShortcutPath)) {
        throw "Expected launcher shortcut '$ShortcutPath' was not created."
    }

    $shell = $null
    $shortcut = $null
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        if ([string]::IsNullOrWhiteSpace([string]$shortcut.WorkingDirectory) -or $shortcut.WorkingDirectory -ne $InstallRoot) {
            throw "Shortcut '$ShortcutPath' does not reference install root '$InstallRoot'."
        }

        if ([string]::IsNullOrWhiteSpace([string]$shortcut.TargetPath) -or $shortcut.TargetPath -ne $LauncherPath) {
            throw "Shortcut '$ShortcutPath' does not point to launcher '$LauncherPath'."
        }
    }
    finally {
        if ($null -ne $shortcut) {
            [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
        }

        if ($null -ne $shell) {
            [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
        }
    }

    if ($ShortcutPath -notlike "*.lnk") {
        throw "Shortcut '$ShortcutPath' is not a Windows .lnk shortcut."
    }

    Write-Host "Shortcut verified: $ShortcutPath"
}

function Assert-PathMissing {
    param(
        [string]$PathToCheck,
        [string]$Label
    )

    if (Test-Path $PathToCheck) {
        throw "$Label should have been removed, but '$PathToCheck' still exists."
    }
}

function Get-RunningMeetingRecorderProcessPaths {
    $processPaths = Get-Process -Name "MeetingRecorder.App" -ErrorAction SilentlyContinue |
        ForEach-Object {
            try {
                $_.Path
            }
            catch {
                $null
            }
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique

    return @($processPaths)
}

function Assert-OnlyCanonicalProcessRunning {
    param(
        [string]$ExpectedExecutablePath,
        [bool]$RequireExpectedProcess
    )

    $deadline = (Get-Date).AddSeconds(20)
    do {
        $runningProcessPaths = Get-RunningMeetingRecorderProcessPaths
        $unexpectedProcessPaths = @(
            $runningProcessPaths |
                Where-Object { $_ -ne $ExpectedExecutablePath }
        )

        if ($unexpectedProcessPaths.Count -gt 0) {
            throw "Unexpected running app instance(s) detected outside the canonical install: $($unexpectedProcessPaths -join '; '). Close those copies and retry the local deploy."
        }

        if (-not $RequireExpectedProcess -or $runningProcessPaths -contains $ExpectedExecutablePath) {
            if ($runningProcessPaths -contains $ExpectedExecutablePath) {
                Write-Host "Running process verified: $ExpectedExecutablePath"
            }

            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "MeetingRecorder.App did not launch from '$ExpectedExecutablePath' within the expected time."
}

$deployLogPath = New-DeployLogPath
Write-Host "Diagnostic log: $deployLogPath"

if ($BuildFirst.IsPresent) {
    $publishScriptPath = Join-Path $PSScriptRoot "Publish-Portable.ps1"
    Write-Host "Publishing a fresh portable bundle with $publishScriptPath..."

    $publishArguments = @{
        Configuration = $Configuration
        Runtime = $Runtime
    }

    if ($FrameworkDependent.IsPresent) {
        $publishArguments.FrameworkDependent = $true
    }

    & $publishScriptPath @publishArguments
}

$effectiveBundleRoot = $BundleRoot
if (-not $PSBoundParameters.ContainsKey("BundleRoot") -and $Runtime -ne "win-x64") {
    $effectiveBundleRoot = ".artifacts\publish\$Runtime\MeetingRecorder"
}

$resolvedBundleRoot = Resolve-BundleRoot -CandidateBundleRoot $effectiveBundleRoot
$manifestPath = Resolve-ManifestPath -ResolvedBundleRoot $resolvedBundleRoot
$manifest = Read-Manifest -ManifestPath $manifestPath
$resolvedInstallRoot = [System.IO.Path]::GetFullPath((Resolve-InstallRoot -Manifest $manifest))
$deploymentCliPath = Resolve-DeploymentCliPath -ResolvedBundleRoot $resolvedBundleRoot
$launcherPath = Join-Path $resolvedInstallRoot "Run-MeetingRecorder.cmd"
$expectedExecutablePath = Join-Path $resolvedInstallRoot "MeetingRecorder.App.exe"

Write-Host "Deploying '$resolvedBundleRoot' to '$resolvedInstallRoot'..."

$cliArguments = @(
    "install-bundle",
    "--bundle-root",
    $resolvedBundleRoot,
    "--install-root",
    $resolvedInstallRoot,
    "--install-channel",
    "DirectCli",
    "--log-path",
    $deployLogPath,
    "--pause-on-error"
)

if ($NoDesktopShortcut.IsPresent) {
    $cliArguments += "--no-desktop-shortcut"
}

if ($NoStartMenuShortcut.IsPresent) {
    $cliArguments += "--no-start-menu-shortcut"
}

if ($NoLaunch.IsPresent) {
    $cliArguments += "--no-launch"
}

& $deploymentCliPath @cliArguments
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "AppPlatform.Deployment.Cli exited with code $exitCode. See '$deployLogPath'."
}

$deployedManifestPath = Join-Path $resolvedInstallRoot "MeetingRecorder.product.json"
$bundleExecutablePath = Join-Path $resolvedBundleRoot "MeetingRecorder.App.exe"
$deployedExecutablePath = Join-Path $resolvedInstallRoot "MeetingRecorder.App.exe"

Assert-FileHashMatches -SourcePath $bundleExecutablePath -DestinationPath $deployedExecutablePath -Label "MeetingRecorder.App.exe"
Assert-FileHashMatches -SourcePath $manifestPath -DestinationPath $deployedManifestPath -Label "MeetingRecorder.product.json"

$deployedManifest = Read-Manifest -ManifestPath $deployedManifestPath
$deployedManifestInstallRoot = [System.IO.Path]::GetFullPath((Resolve-InstallRoot -Manifest $deployedManifest))
if ($deployedManifestInstallRoot -ne $resolvedInstallRoot) {
    throw "The deployed manifest install root '$deployedManifestInstallRoot' does not match the canonical deploy root '$resolvedInstallRoot'."
}

Write-Host "Bundled manifest install root verified: $deployedManifestInstallRoot"

$desktopRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$programsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$displayName = [string]$manifest.shortcutPolicy.displayName
$defaultLauncherFileName = "Meeting Recorder.lnk"
$desktopShortcutFileName = if ([string]::IsNullOrWhiteSpace([string]$manifest.shortcutPolicy.desktopShortcutFileName)) {
    $defaultLauncherFileName
}
else {
    [string]$manifest.shortcutPolicy.desktopShortcutFileName
}
$startMenuShortcutFileName = if ([string]::IsNullOrWhiteSpace([string]$manifest.shortcutPolicy.startMenuShortcutFileName)) {
    $defaultLauncherFileName
}
else {
    [string]$manifest.shortcutPolicy.startMenuShortcutFileName
}
$desktopShortcutPath = Join-Path $desktopRoot $desktopShortcutFileName
$startMenuShortcutPath = Join-Path $programsRoot $startMenuShortcutFileName
$desktopLegacyShortcutPath = Join-Path $desktopRoot ("{0}.lnk" -f $displayName)
$startMenuLegacyShortcutPath = Join-Path $programsRoot ("{0}.lnk" -f $displayName)
$nestedLegacyStartMenuFolder = Join-Path $programsRoot $displayName

if (-not $NoDesktopShortcut.IsPresent) {
    Assert-ShortcutTargetsLauncher -ShortcutPath $desktopShortcutPath -InstallRoot $resolvedInstallRoot -LauncherPath $launcherPath
    Assert-PathMissing -PathToCheck $desktopLegacyShortcutPath -Label "Legacy desktop shortcut"
}

if (-not $NoStartMenuShortcut.IsPresent) {
    Assert-ShortcutTargetsLauncher -ShortcutPath $startMenuShortcutPath -InstallRoot $resolvedInstallRoot -LauncherPath $launcherPath
    Assert-PathMissing -PathToCheck $startMenuLegacyShortcutPath -Label "Legacy Start Menu shortcut"
    Assert-PathMissing -PathToCheck $nestedLegacyStartMenuFolder -Label "Legacy nested Start Menu folder"
}

Assert-OnlyCanonicalProcessRunning -ExpectedExecutablePath $expectedExecutablePath -RequireExpectedProcess:(-not $NoLaunch.IsPresent)

Write-Host "Local deploy verification completed successfully."
Write-Host "Install root: $resolvedInstallRoot"
