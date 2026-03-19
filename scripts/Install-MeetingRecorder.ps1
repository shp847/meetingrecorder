param(
    [string]$InstallRoot = "",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$NoLaunch,
    [switch]$DisableLaunchOnLogin,
    [string]$ReleaseVersion = "",
    [string]$ReleasePublishedAtUtc = "",
    [long]$ReleaseAssetSizeBytes = 0
)

$ErrorActionPreference = "Stop"

function Get-DefaultInstallRoot {
    $documentsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    return Join-Path $documentsPath "MeetingRecorder"
}

function Resolve-SourceBundleRoot {
    $nestedBundleRoot = Join-Path $PSScriptRoot "MeetingRecorder"
    if (Test-Path (Join-Path $nestedBundleRoot "MeetingRecorder.App.exe")) {
        return $nestedBundleRoot
    }

    if (Test-Path (Join-Path $PSScriptRoot "MeetingRecorder.App.exe")) {
        return $PSScriptRoot
    }

    throw "Could not find the portable Meeting Recorder bundle next to the installer."
}

function Test-InstallPathInUse {
    param(
        [string]$TargetRoot
    )

    return @(Get-InstallPathProcesses -TargetRoot $TargetRoot).Count -gt 0
}

function Get-InstallPathProcesses {
    param(
        [string]$TargetRoot
    )

    $normalizedRoot = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\') + '\'
    $processes = Get-CimInstance Win32_Process -Filter "Name = 'MeetingRecorder.App.exe' OR Name = 'MeetingRecorder.ProcessingWorker.exe'" -ErrorAction SilentlyContinue
    if ($null -eq $processes) {
        return @()
    }

    $matches = @()
    foreach ($process in $processes) {
        $executablePath = $process.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($executablePath)) {
            continue
        }

        $normalizedExecutable = [IO.Path]::GetFullPath($executablePath)
        if ($normalizedExecutable.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $matches += [pscustomobject]@{
                ProcessId = [int]$process.ProcessId
                Name = [string]$process.Name
                ExecutablePath = $normalizedExecutable
            }
        }
    }

    return $matches
}

function Stop-InstallPathProcessesGracefully {
    param(
        [string]$TargetRoot,
        [int]$TimeoutSeconds = 20
    )

    $matchingProcesses = @(Get-InstallPathProcesses -TargetRoot $TargetRoot)
    if ($matchingProcesses.Count -eq 0) {
        return
    }

    Write-Host "Meeting Recorder is already running from '$TargetRoot'. Attempting to close it gracefully..."

    foreach ($processInfo in $matchingProcesses) {
        $liveProcess = Get-Process -Id $processInfo.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $liveProcess) {
            continue
        }

        if ($liveProcess.MainWindowHandle -ne 0) {
            try {
                if ($liveProcess.CloseMainWindow()) {
                    Write-Host ("Requested shutdown for {0} (PID {1})." -f $processInfo.Name, $processInfo.ProcessId)
                }
            }
            catch {
                # Keep trying other processes and verify the final state after the wait window.
            }
        }
    }

    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $remainingProcesses = @(Get-InstallPathProcesses -TargetRoot $TargetRoot)
        if ($remainingProcesses.Count -eq 0) {
            Write-Host "Meeting Recorder closed successfully. Continuing the install..."
            return
        }
    } while ([DateTime]::UtcNow -lt $deadlineUtc)

    $remainingProcesses = @(Get-InstallPathProcesses -TargetRoot $TargetRoot)
    if ($remainingProcesses.Count -eq 0) {
        Write-Host "Meeting Recorder closed successfully. Continuing the install..."
        return
    }

    $processSummary = ($remainingProcesses | ForEach-Object { "{0} (PID {1})" -f $_.Name, $_.ProcessId }) -join ", "
    throw "Meeting Recorder is still running from '$TargetRoot' after an automatic shutdown attempt. Close these processes and try again: $processSummary"
}

function Copy-DirectoryContents {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    if (-not (Test-Path $SourcePath)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null
    Get-ChildItem -Path $SourcePath -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $DestinationPath -Recurse -Force
    }
}

function Merge-DirectoryWithoutOverwriting {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    if (-not (Test-Path $SourcePath)) {
        return
    }

    $files = Get-ChildItem -Path $SourcePath -Recurse -File
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($SourcePath.Length).TrimStart('\')
        $destinationFile = Join-Path $DestinationPath $relativePath
        $destinationDirectory = Split-Path -Parent $destinationFile
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

        if (-not (Test-Path $destinationFile)) {
            Copy-Item -Path $file.FullName -Destination $destinationFile -Force
        }
    }
}

function Get-PreferredModelPath {
    param(
        [string]$AsrDirectory
    )

    $preferredNames = @(
        "ggml-base.bin",
        "ggml-small.bin",
        "ggml-small.en.bin"
    )

    foreach ($preferredName in $preferredNames) {
        $candidate = Join-Path $AsrDirectory $preferredName
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $firstAvailable = Get-ChildItem -Path $AsrDirectory -Filter "*.bin" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $firstAvailable) {
        return $firstAvailable.FullName
    }

    return (Join-Path $AsrDirectory "ggml-base.bin")
}

function Set-JsonProperty {
    param(
        [psobject]$Object,
        [string]$PropertyName,
        $Value,
        [switch]$OnlyIfMissing
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    $hasValue = $null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)

    if ($OnlyIfMissing -and $hasValue) {
        return
    }

    if ($null -eq $property) {
        $Object | Add-Member -NotePropertyName $PropertyName -NotePropertyValue $Value
        return
    }

    $property.Value = $Value
}

function Ensure-InstalledConfig {
    param(
        [string]$BundleRoot,
        [string]$FinalInstallRoot,
        [switch]$LaunchOnLoginDefault,
        [switch]$PreserveExistingConfig,
        [string]$ReleaseVersion,
        [string]$ReleasePublishedAtUtc,
        [long]$ReleaseAssetSizeBytes
    )

    $bundleDataRoot = Join-Path $BundleRoot "data"
    $dataRoot = Join-Path $FinalInstallRoot "data"
    $configDirectory = Join-Path $bundleDataRoot "config"
    $configPath = Join-Path $configDirectory "appsettings.json"
    $modelCacheDir = Join-Path $dataRoot "models"
    $bundleAsrDirectory = Join-Path $bundleDataRoot "models\asr"
    $finalAsrDirectory = Join-Path $modelCacheDir "asr"

    New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDataRoot "audio") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDataRoot "transcripts") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDataRoot "work") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDataRoot "models") | Out-Null
    New-Item -ItemType Directory -Force -Path $bundleAsrDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDataRoot "models\diarization") | Out-Null

    if (Test-Path $configPath) {
        $config = Get-Content -Raw $configPath | ConvertFrom-Json
    }
    else {
        $config = [pscustomobject]@{}
    }

    $preferredModelFileName = Split-Path -Leaf (Get-PreferredModelPath -AsrDirectory $bundleAsrDirectory)
    $preferredModelPath = Join-Path $finalAsrDirectory $preferredModelFileName

    Set-JsonProperty -Object $config -PropertyName "audioOutputDir" -Value (Join-Path $dataRoot "audio") -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "transcriptOutputDir" -Value (Join-Path $dataRoot "transcripts") -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "workDir" -Value (Join-Path $dataRoot "work") -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "modelCacheDir" -Value $modelCacheDir -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "diarizationAssetPath" -Value (Join-Path $modelCacheDir "diarization") -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "micCaptureEnabled" -Value $false -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "autoDetectEnabled" -Value $true -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "updateCheckEnabled" -Value $true -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "autoInstallUpdatesEnabled" -Value $true -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "updateFeedUrl" -Value "https://api.github.com/repos/shp847/meetingrecorder/releases/latest" -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "autoDetectAudioPeakThreshold" -Value 0.02 -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "meetingStopTimeoutSeconds" -Value 30 -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "launchOnLoginEnabled" -Value $LaunchOnLoginDefault.IsPresent -OnlyIfMissing
    Set-JsonProperty -Object $config -PropertyName "installedReleaseVersion" -Value "" -OnlyIfMissing

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        Set-JsonProperty -Object $config -PropertyName "installedReleaseVersion" -Value $ReleaseVersion
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
        Set-JsonProperty -Object $config -PropertyName "installedReleasePublishedAtUtc" -Value $ReleasePublishedAtUtc
    }

    if ($ReleaseAssetSizeBytes -gt 0) {
        Set-JsonProperty -Object $config -PropertyName "installedReleaseAssetSizeBytes" -Value $ReleaseAssetSizeBytes
    }

    $currentModelPathProperty = $config.PSObject.Properties["transcriptionModelPath"]
    $currentModelPath = if ($null -ne $currentModelPathProperty) { [string]$currentModelPathProperty.Value } else { "" }
    if (
        -not $PreserveExistingConfig.IsPresent -and
        ([string]::IsNullOrWhiteSpace([string]$currentModelPath) -or -not (Test-Path ([string]$currentModelPath)))
    ) {
        Set-JsonProperty -Object $config -PropertyName "transcriptionModelPath" -Value $preferredModelPath
    }

    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
    return $config
}

function Set-LaunchOnLoginRegistration {
    param(
        [bool]$Enabled,
        [string]$ExecutablePath
    )

    $runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $entryName = "MeetingRecorder"

    if ($Enabled) {
        $command = if ($ExecutablePath.StartsWith('"')) { $ExecutablePath } else { '"' + $ExecutablePath + '"' }
        New-Item -Path $runKeyPath -Force | Out-Null
        Set-ItemProperty -Path $runKeyPath -Name $entryName -Value $command
        return
    }

    Remove-ItemProperty -Path $runKeyPath -Name $entryName -ErrorAction SilentlyContinue
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$IconPath
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = "Meeting Recorder"

    if (Test-Path $IconPath) {
        $shortcut.IconLocation = $IconPath
    }

    $shortcut.Save()
}

function Try-NewShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$IconPath,
        [string]$ShortcutLabel
    )

    try {
        New-Shortcut `
            -ShortcutPath $ShortcutPath `
            -TargetPath $TargetPath `
            -WorkingDirectory $WorkingDirectory `
            -IconPath $IconPath
        return "created"
    }
    catch {
        Write-Host ("Unable to create the {0} shortcut automatically: {1}" -f $ShortcutLabel, $_.Exception.Message) -ForegroundColor Yellow
        Write-Host "The install still completed. You can launch Meeting Recorder from the app folder or create a shortcut manually." -ForegroundColor Yellow
        return "blocked"
    }
}

$sourceBundleRoot = Resolve-SourceBundleRoot

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Get-DefaultInstallRoot
}

$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$installParent = Split-Path -Parent $resolvedInstallRoot
$isUpdate = Test-Path $resolvedInstallRoot

if (Test-InstallPathInUse -TargetRoot $resolvedInstallRoot) {
    Stop-InstallPathProcessesGracefully -TargetRoot $resolvedInstallRoot
}

New-Item -ItemType Directory -Force -Path $installParent | Out-Null

$stagingRoot = Join-Path $installParent ("MeetingRecorder-install-" + [Guid]::NewGuid().ToString("N"))
$backupRoot = Join-Path $installParent ("MeetingRecorder-backup-" + [Guid]::NewGuid().ToString("N"))

try {
    Copy-DirectoryContents -SourcePath $sourceBundleRoot -DestinationPath $stagingRoot

    if ($isUpdate) {
        $existingDataRoot = Join-Path $resolvedInstallRoot "data"
        $stagingDataRoot = Join-Path $stagingRoot "data"

        foreach ($preservedDirectory in @("config", "logs", "audio", "transcripts", "work")) {
            Copy-DirectoryContents `
                -SourcePath (Join-Path $existingDataRoot $preservedDirectory) `
                -DestinationPath (Join-Path $stagingDataRoot $preservedDirectory)
        }

        Merge-DirectoryWithoutOverwriting `
            -SourcePath (Join-Path $existingDataRoot "models") `
            -DestinationPath (Join-Path $stagingDataRoot "models")
    }

    $config = Ensure-InstalledConfig `
        -BundleRoot $stagingRoot `
        -FinalInstallRoot $resolvedInstallRoot `
        -LaunchOnLoginDefault:(-not $DisableLaunchOnLogin.IsPresent) `
        -PreserveExistingConfig:$isUpdate `
        -ReleaseVersion $ReleaseVersion `
        -ReleasePublishedAtUtc $ReleasePublishedAtUtc `
        -ReleaseAssetSizeBytes $ReleaseAssetSizeBytes

    if (Test-Path $resolvedInstallRoot) {
        Rename-Item -Path $resolvedInstallRoot -NewName (Split-Path -Leaf $backupRoot)
    }

    Move-Item -Path $stagingRoot -Destination $resolvedInstallRoot

    $launcherPath = Join-Path $resolvedInstallRoot "Run-MeetingRecorder.cmd"
    $executablePath = Join-Path $resolvedInstallRoot "MeetingRecorder.App.exe"
    $iconPath = Join-Path $resolvedInstallRoot "MeetingRecorder.ico"
    $desktopShortcutStatus = "skipped"
    $startMenuShortcutStatus = "skipped"

    if (-not $NoDesktopShortcut.IsPresent) {
        $desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "Meeting Recorder.lnk"
        $desktopShortcutStatus = Try-NewShortcut `
            -ShortcutPath $desktopShortcut `
            -TargetPath $launcherPath `
            -WorkingDirectory $resolvedInstallRoot `
            -IconPath $iconPath `
            -ShortcutLabel "Desktop"
    }

    if (-not $NoStartMenuShortcut.IsPresent) {
        $startMenuShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) "Meeting Recorder.lnk"
        $startMenuShortcutStatus = Try-NewShortcut `
            -ShortcutPath $startMenuShortcut `
            -TargetPath $launcherPath `
            -WorkingDirectory $resolvedInstallRoot `
            -IconPath $iconPath `
            -ShortcutLabel "Start menu"
    }

    $launchOnLoginProperty = $config.PSObject.Properties["launchOnLoginEnabled"]
    $launchOnLoginEnabled = if ($null -ne $launchOnLoginProperty) { [bool]$launchOnLoginProperty.Value } else { $false }
    Set-LaunchOnLoginRegistration -Enabled $launchOnLoginEnabled -ExecutablePath $executablePath

    if (Test-Path $backupRoot) {
        Remove-Item -Recurse -Force $backupRoot
    }

    Write-Host ("Meeting Recorder " + ($(if ($isUpdate) { "updated" } else { "installed" })) + " at " + $resolvedInstallRoot)
    Write-Host "Desktop shortcut: $desktopShortcutStatus"
    Write-Host "Start menu shortcut: $startMenuShortcutStatus"
    Write-Host ("Launch on login: " + $(if ($launchOnLoginEnabled) { "enabled" } else { "disabled" }))

    if (-not $NoLaunch.IsPresent) {
        Start-Process -FilePath $launcherPath -WorkingDirectory $resolvedInstallRoot
    }
}
catch {
    if (Test-Path $stagingRoot) {
        Remove-Item -Recurse -Force $stagingRoot -ErrorAction SilentlyContinue
    }

    if ((-not (Test-Path $resolvedInstallRoot)) -and (Test-Path $backupRoot)) {
        Move-Item -Path $backupRoot -Destination $resolvedInstallRoot -Force
    }

    throw
}
