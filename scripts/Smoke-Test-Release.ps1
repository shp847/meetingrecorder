param(
    [string]$Runtime = "win-x64",
    [string]$BundleRoot = "",
    [string]$PackageRoot = "",
    [int]$SmokeDurationSeconds = 30,
    [switch]$SkipBundleTest,
    [switch]$SkipMsiTest
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedBundleRoot = if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    Join-Path $repoRoot ".artifacts\publish\$Runtime\MeetingRecorder"
}
elseif ([System.IO.Path]::IsPathRooted($BundleRoot)) {
    $BundleRoot
}
else {
    Join-Path $repoRoot $BundleRoot
}
$resolvedPackageRoot = if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    Join-Path $repoRoot ".artifacts\installer\$Runtime"
}
elseif ([System.IO.Path]::IsPathRooted($PackageRoot)) {
    $PackageRoot
}
else {
    Join-Path $repoRoot $PackageRoot
}
$resolvedBundleRoot = [System.IO.Path]::GetFullPath($resolvedBundleRoot)
$resolvedPackageRoot = [System.IO.Path]::GetFullPath($resolvedPackageRoot)

function New-SmokeTestLogPath {
    param(
        [string]$Prefix
    )

    $logDirectory = Join-Path $env:TEMP "MeetingRecorderInstaller"
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    return Join-Path $logDirectory ("{0}-{1}.log" -f $Prefix, (Get-Date -Format "yyyyMMdd-HHmmss"))
}

function Resolve-ManagedInstallRoot {
    param(
        [string]$ManifestPath
    )

    if (-not (Test-Path $ManifestPath)) {
        throw "Could not find MeetingRecorder.product.json at '$ManifestPath'."
    }

    $manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    $installRoot = [string]$manifest.managedInstallLayout.installRoot
    if ([string]::IsNullOrWhiteSpace($installRoot)) {
        throw "The bundle manifest does not declare a managed install root."
    }

    return [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($installRoot))
}

function Assert-NoRunningMeetingRecorderInstances {
    $runningInstances = Get-Process -Name "MeetingRecorder.App" -ErrorAction SilentlyContinue
    if ($null -eq $runningInstances -or $runningInstances.Count -eq 0) {
        return
    }

    $instanceDescriptions = $runningInstances | ForEach-Object {
        try {
            if (-not [string]::IsNullOrWhiteSpace($_.Path)) {
                $_.Path
            }
            else {
                "pid=$($_.Id)"
            }
        }
        catch {
            "pid=$($_.Id)"
        }
    }

    throw "Close Meeting Recorder before running the release smoke test. Running instance(s): $($instanceDescriptions -join '; ')."
}

function Get-CrashEventsForWindow {
    param(
        [datetime]$StartTime,
        [datetime]$EndTime
    )

    return @(
        Get-WinEvent -FilterHashtable @{
            LogName = "Application"
            StartTime = $StartTime.AddSeconds(-2)
        } -ErrorAction SilentlyContinue |
            Where-Object {
                $_.TimeCreated -ge $StartTime -and
                $_.TimeCreated -le $EndTime.AddSeconds(5) -and
                $_.ProviderName -in @(".NET Runtime", "Application Error", "Windows Error Reporting") -and
                $_.Message -match "MeetingRecorder\\.App\\.exe"
            }
    )
}

function Format-CrashEventSummary {
    param(
        [System.Diagnostics.Eventing.Reader.EventRecord[]]$Events
    )

    return ($Events | Select-Object -First 3 | ForEach-Object {
        $message = [string]$_.Message
        if ($message.Length -gt 220) {
            $message = $message.Substring(0, 220) + "..."
        }

        "{0:u} [{1}] {2}" -f $_.TimeCreated, $_.ProviderName, ($message -replace '\s+', ' ').Trim()
    }) -join [Environment]::NewLine
}

function Invoke-AppSmokeRun {
    param(
        [string]$ExecutablePath,
        [string]$Label
    )

    if (-not (Test-Path $ExecutablePath)) {
        throw "$Label smoke test could not find '$ExecutablePath'."
    }

    $workingDirectory = Split-Path -Parent $ExecutablePath
    $startTime = Get-Date
    $process = Start-Process -FilePath $ExecutablePath -WorkingDirectory $workingDirectory -PassThru
    $timedOut = $false

    try {
        try {
            Wait-Process -Id $process.Id -Timeout $SmokeDurationSeconds -ErrorAction Stop
        }
        catch [System.TimeoutException] {
            $timedOut = $true
        }

        if (-not $timedOut) {
            $exitCode = $null
            try {
                $exitCode = $process.ExitCode
            }
            catch {
                $exitCode = "unknown"
            }

            throw "$Label smoke test failed because MeetingRecorder.App.exe exited before $SmokeDurationSeconds seconds. Exit code: $exitCode."
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
    }

    $endTime = Get-Date
    $crashEvents = Get-CrashEventsForWindow -StartTime $startTime -EndTime $endTime
    if ($crashEvents.Count -gt 0) {
        $eventSummary = Format-CrashEventSummary -Events $crashEvents
        throw "$Label smoke test detected crash-related Windows events:`n$eventSummary"
    }

    Write-Host "$Label smoke test passed for $ExecutablePath"
}

function Install-MsiForSmokeTest {
    param(
        [string]$MsiPath,
        [string]$InstallRoot
    )

    if (-not (Test-Path $MsiPath)) {
        throw "Could not find MeetingRecorderInstaller.msi at '$MsiPath'."
    }

    $logPath = New-SmokeTestLogPath -Prefix "smoke-test-msi-install"
    $arguments = @(
        "/i",
        ('"{0}"' -f $MsiPath),
        "/qn",
        "/norestart",
        ('INSTALLFOLDER="{0}"' -f $InstallRoot),
        "/l*v",
        ('"{0}"' -f $logPath)
    )

    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "MSI smoke-test install failed with exit code $($process.ExitCode). See '$logPath'."
    }

    Write-Host "MSI install smoke step passed. Log: $logPath"
}

Assert-NoRunningMeetingRecorderInstances

$manifestPath = Join-Path $resolvedBundleRoot "MeetingRecorder.product.json"
$managedInstallRoot = Resolve-ManagedInstallRoot -ManifestPath $manifestPath
$bundleExecutablePath = Join-Path $resolvedBundleRoot "MeetingRecorder.App.exe"
$installedExecutablePath = Join-Path $managedInstallRoot "MeetingRecorder.App.exe"
$msiPath = Join-Path $resolvedPackageRoot "MeetingRecorderInstaller.msi"

if (-not $SkipBundleTest.IsPresent) {
    Invoke-AppSmokeRun -ExecutablePath $bundleExecutablePath -Label "Portable bundle"
}

if (-not $SkipMsiTest.IsPresent) {
    Install-MsiForSmokeTest -MsiPath $msiPath -InstallRoot $managedInstallRoot
    Invoke-AppSmokeRun -ExecutablePath $installedExecutablePath -Label "MSI-installed app"
}

Write-Host "Release smoke test completed successfully."
