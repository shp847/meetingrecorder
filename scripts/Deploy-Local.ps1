param(
    [switch]$BuildFirst,
    [switch]$FrameworkDependent,
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$Launch,
    [switch]$NoLaunch,
    [string]$InstallRoot = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$BundleRoot = ".artifacts\publish\win-x64\MeetingRecorder"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot

function New-DeployLocalLogPath {
    $logDirectory = Join-Path $env:TEMP "MeetingRecorderInstaller"
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    return Join-Path $logDirectory ("deploy-local-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}

function Resolve-DeployLocalPath {
    param(
        [string]$CandidatePath
    )

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        return ""
    }

    $expandedPath = [Environment]::ExpandEnvironmentVariables($CandidatePath)
    $resolvedPath = if ([System.IO.Path]::IsPathRooted($expandedPath)) {
        $expandedPath
    }
    else {
        Join-Path $repoRoot $expandedPath
    }

    return [System.IO.Path]::GetFullPath($resolvedPath)
}

function ConvertTo-RepoRelativePath {
    param(
        [string]$ResolvedPath
    )

    $fullRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $fullRepoRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullRepoRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    $fullPath = [System.IO.Path]::GetFullPath($ResolvedPath)
    if (-not $fullPath.StartsWith($fullRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "-BuildFirst only supports bundle output paths under the repository root because Publish-Portable.ps1 accepts a repo-relative OutputRoot."
    }

    return $fullPath.Substring($fullRepoRoot.Length)
}

function Invoke-DeployLocalPublish {
    param(
        [string]$ResolvedBundleRoot
    )

    if ((Split-Path -Leaf $ResolvedBundleRoot) -ne "MeetingRecorder") {
        throw "-BuildFirst requires -BundleRoot to point at a MeetingRecorder bundle folder because Publish-Portable.ps1 writes '<OutputRoot>\MeetingRecorder'."
    }

    $publishScriptPath = Join-Path $PSScriptRoot "Publish-Portable.ps1"
    if (-not (Test-Path $publishScriptPath)) {
        throw "Could not find Publish-Portable.ps1 at '$publishScriptPath'."
    }

    $publishOutputRoot = Split-Path -Parent $ResolvedBundleRoot
    $publishArguments = @{
        Configuration = $Configuration
        Runtime = $Runtime
        OutputRoot = (ConvertTo-RepoRelativePath -ResolvedPath $publishOutputRoot)
    }

    if ($FrameworkDependent.IsPresent) {
        $publishArguments.FrameworkDependent = $true
    }

    Write-Host "Publishing a fresh portable bundle with '$publishScriptPath'..."
    & $publishScriptPath @publishArguments
    if (-not $?) {
        throw "Publish-Portable.ps1 failed while preparing the local deployment bundle."
    }
}

function Assert-DeployLocalBundle {
    param(
        [string]$ResolvedBundleRoot
    )

    $requiredFiles = @(
        "MeetingRecorder.App.exe",
        "AppPlatform.Deployment.Cli.exe",
        "MeetingRecorder.product.json",
        "bundle-integrity.json"
    )

    $missingFiles = @(
        $requiredFiles |
            Where-Object { -not (Test-Path (Join-Path $ResolvedBundleRoot $_)) }
    )

    if ($missingFiles.Count -gt 0) {
        throw "Could not find a complete published Meeting Recorder bundle at '$ResolvedBundleRoot'. Missing: $($missingFiles -join '; '). Run scripts\Deploy-Local.ps1 -BuildFirst or scripts\Publish-Portable.ps1 first."
    }
}

if ($Launch.IsPresent -and $NoLaunch.IsPresent) {
    throw "Use either -Launch or -NoLaunch, not both."
}

$deployLogPath = New-DeployLocalLogPath
Write-Host "Diagnostic log: $deployLogPath"

$effectiveBundleRoot = if ($PSBoundParameters.ContainsKey("BundleRoot")) {
    $BundleRoot
}
else {
    ".artifacts\publish\$Runtime\MeetingRecorder"
}

$resolvedBundleRoot = Resolve-DeployLocalPath -CandidatePath $effectiveBundleRoot
if ($BuildFirst.IsPresent) {
    Invoke-DeployLocalPublish -ResolvedBundleRoot $resolvedBundleRoot
}

Assert-DeployLocalBundle -ResolvedBundleRoot $resolvedBundleRoot

$deploymentCliPath = Join-Path $resolvedBundleRoot "AppPlatform.Deployment.Cli.exe"
$cliArguments = @(
    "install-bundle",
    "--bundle-root",
    $resolvedBundleRoot,
    "--install-channel",
    "DirectCli",
    "--log-path",
    $deployLogPath
)

if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
    $cliArguments += @("--install-root", (Resolve-DeployLocalPath -CandidatePath $InstallRoot))
}

if ($NoDesktopShortcut.IsPresent) {
    $cliArguments += "--no-desktop-shortcut"
}

if ($NoStartMenuShortcut.IsPresent) {
    $cliArguments += "--no-start-menu-shortcut"
}

if ($NoLaunch.IsPresent) {
    $cliArguments += "--no-launch"
}

Write-Host "Deploying local bundle '$resolvedBundleRoot' through AppPlatform.Deployment.Cli..."
& $deploymentCliPath @cliArguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "AppPlatform.Deployment.Cli exited with code $exitCode. See '$deployLogPath'."
}

Write-Host "Local deploy completed successfully."
