param(
    [string]$PackageRoot = ".artifacts\\installer\\win-x64",
    [string]$GitHubToken = "",
    [Alias('IncludeHeavyInstallers')]
    [switch]$Installers,
    [switch]$DryRun,
    [ValidateRange(1, 8)]
    [int]$MaxParallelUploads = 3
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RepoInformationalVersion {
    param(
        [string]$RepoRoot
    )

    $propsPath = Join-Path $RepoRoot "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        throw "Could not find Directory.Build.props at $propsPath"
    }

    [xml]$props = Get-Content -Path $propsPath
    $propertyGroup = $props.Project.PropertyGroup
    $informationalVersion = [string]$propertyGroup.InformationalVersion

    if (-not [string]::IsNullOrWhiteSpace($informationalVersion)) {
        return $informationalVersion.Trim()
    }

    $version = [string]$propertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not resolve a repo version from Directory.Build.props"
    }

    return $version.Trim()
}

function Get-GitHubRepositoryInfo {
    param(
        [string]$RepoRoot
    )

    $brandingPath = Join-Path $RepoRoot "src\\MeetingRecorder.Core\\Branding\\AppBranding.cs"
    if (-not (Test-Path $brandingPath)) {
        throw "Could not find AppBranding.cs at $brandingPath"
    }

    $content = Get-Content -Path $brandingPath -Raw
    $ownerMatch = [regex]::Match($content, 'GitHubRepositoryOwner\s*=\s*"([^"]+)"')
    $nameMatch = [regex]::Match($content, 'GitHubRepositoryName\s*=\s*"([^"]+)"')

    if (-not $ownerMatch.Success -or -not $nameMatch.Success) {
        throw "Could not resolve the GitHub repository owner/name from AppBranding.cs"
    }

    return [pscustomobject]@{
        Owner = $ownerMatch.Groups[1].Value
        Name = $nameMatch.Groups[1].Value
    }
}

function Resolve-GitHubToken {
    param(
        [string]$ExplicitToken
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitToken)) {
        return $ExplicitToken
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        return $env:GITHUB_TOKEN
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        return $env:GH_TOKEN
    }

    return ""
}

function Invoke-GitQuery {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    $output = & git -C $RepoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed while validating release assets."
    }

    return @($output)
}

function Get-SourceStatePathspec {
    return @(
        ".",
        ":(exclude).artifacts",
        ":(glob,exclude)**/bin/**",
        ":(glob,exclude)**/obj/**"
    )
}

function Get-CurrentRepoSourceState {
    param(
        [string]$RepoRoot
    )

    # Validate against the live repo state from:
    # git rev-parse HEAD
    # git status --short
    $gitCommit = (Invoke-GitQuery -RepoRoot $RepoRoot -Arguments @("rev-parse", "HEAD") | Select-Object -First 1).Trim()
    $worktreeStatus = Invoke-GitQuery -RepoRoot $RepoRoot -Arguments (@("status", "--short", "--untracked-files=all", "--") + (Get-SourceStatePathspec))

    return [pscustomobject]@{
        GitCommit       = $gitCommit
        IsWorktreeDirty = ($worktreeStatus.Count -gt 0)
    }
}

function Get-ReleaseSourceMetadata {
    param(
        [string]$PackagePath
    )

    $metadataPath = Join-Path $PackagePath "release-source.json"
    if (-not (Test-Path $metadataPath)) {
        throw "Installer asset directory '$PackagePath' is missing release-source.json. Rebuild installer assets before uploading."
    }

    $metadata = Get-Content -Path $metadataPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$metadata.gitCommit)) {
        throw "Installer asset metadata at '$metadataPath' is missing gitCommit. The assets must be rebuilt from the current clean repo state."
    }

    return $metadata
}

function Assert-ReleaseAssetsMatchCurrentRepoState {
    param(
        [string]$RepoRoot,
        [string]$PackagePath
    )

    $metadata = Get-ReleaseSourceMetadata -PackagePath $PackagePath
    $currentState = Get-CurrentRepoSourceState -RepoRoot $RepoRoot

    if ([bool]$metadata.isWorktreeDirty) {
        throw "Installer assets in '$PackagePath' were built from a dirty worktree and must be rebuilt from the current clean repo state before uploading."
    }

    if ($currentState.IsWorktreeDirty) {
        throw "The current repo worktree is dirty. Commit or stash your changes, rebuild installer assets, and then upload the release."
    }

    if ([string]$metadata.gitCommit -ne $currentState.GitCommit) {
        throw "Installer assets in '$PackagePath' target commit '$($metadata.gitCommit)', but the current repo is at '$($currentState.GitCommit)'. The assets must be rebuilt from the current clean repo state before uploading."
    }
}

function Get-GitHubApiHeaders {
    param(
        [string]$Token
    )

    $headers = @{
        "Accept" = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "MeetingRecorderInstallerUpload/$script:RepoInformationalVersion"
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers["Authorization"] = "Bearer $Token"
    }

    return $headers
}

function Get-LatestGitHubRelease {
    param(
        [string]$Owner,
        [string]$Name,
        [string]$Token
    )

    $uri = "https://api.github.com/repos/$Owner/$Name/releases/latest"
    return Invoke-RestMethod -Headers (Get-GitHubApiHeaders -Token $Token) -Uri $uri -Method Get
}

function Get-ReleaseAssetSourceLastWriteUtc {
    param(
        $Asset
    )

    if ($null -eq $Asset -or [string]::IsNullOrWhiteSpace([string]$Asset.label)) {
        return $null
    }

    $match = [regex]::Match([string]$Asset.label, 'sourceLastWriteUtc=([^;]+)')
    if (-not $match.Success) {
        return $null
    }

    $parsed = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse(
        $match.Groups[1].Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-ReleaseAssetSha256Hex {
    param(
        $Asset
    )

    if ($null -eq $Asset -or [string]::IsNullOrWhiteSpace([string]$Asset.digest)) {
        return ""
    }

    $match = [regex]::Match([string]$Asset.digest, '^sha256:([0-9a-fA-F]+)$')
    if (-not $match.Success) {
        return ""
    }

    return $match.Groups[1].Value.ToLowerInvariant()
}

function Get-LocalFileSha256Hex {
    param(
        [System.IO.FileInfo]$File
    )

    return (Get-FileHash -Path $File.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-GitHubReleaseAssetMatchesLocalFile {
    param(
        $Asset,
        [System.IO.FileInfo]$LocalFile
    )

    if ($null -eq $Asset) {
        return $false
    }

    if ([long]$Asset.size -ne $LocalFile.Length) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Asset.label)) {
        return $false
    }

    $remoteSha256Hex = Get-ReleaseAssetSha256Hex -Asset $Asset
    if (-not [string]::IsNullOrWhiteSpace($remoteSha256Hex)) {
        return $remoteSha256Hex -eq (Get-LocalFileSha256Hex -File $LocalFile)
    }

    $remoteSourceLastWriteUtc = Get-ReleaseAssetSourceLastWriteUtc -Asset $Asset
    if ($null -ne $remoteSourceLastWriteUtc) {
        return $remoteSourceLastWriteUtc.UtcDateTime -eq $LocalFile.LastWriteTimeUtc.UtcDateTime
    }

    return $false
}

function Build-GitHubReleaseAssetUploadUri {
    param(
        [string]$UploadUrl,
        [System.IO.FileInfo]$File
    )

    $resolvedUploadUrl = $UploadUrl.Split('{')[0]
    $escapedName = [Uri]::EscapeDataString($File.Name)
    return "{0}?name={1}" -f $resolvedUploadUrl, $escapedName
}

function Remove-GitHubReleaseAsset {
    param(
        [string]$Owner,
        [string]$Name,
        [string]$Token,
        $Asset,
        [switch]$DryRun
    )

    if ($DryRun.IsPresent) {
        Write-Host ("[dry-run] Would delete release asset '{0}' (id={1})." -f $Asset.name, $Asset.id)
        return
    }

    $uri = "https://api.github.com/repos/$Owner/$Name/releases/assets/$($Asset.id)"
    Invoke-RestMethod -Headers (Get-GitHubApiHeaders -Token $Token) -Uri $uri -Method Delete | Out-Null
}

function Publish-GitHubReleaseAsset {
    param(
        [string]$UploadUrl,
        [string]$Token,
        [System.IO.FileInfo]$File,
        [switch]$DryRun
    )

    if ($DryRun.IsPresent) {
        Write-Host ("[dry-run] Would upload '{0}' ({1} bytes) with no display label." -f $File.Name, $File.Length)
        return
    }

    $uri = Build-GitHubReleaseAssetUploadUri -UploadUrl $UploadUrl -File $File
    $headers = @{
        "Accept" = "application/vnd.github+json"
        "Authorization" = "Bearer $Token"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "MeetingRecorderInstallerUpload/$script:RepoInformationalVersion"
        "Content-Type" = "application/octet-stream"
    }

    Invoke-RestMethod -Headers $headers -Uri $uri -Method Post -InFile $File.FullName | Out-Null
}

function Start-GitHubReleaseAssetUploadJob {
    param(
        [string]$UploadUrl,
        [string]$Token,
        [System.IO.FileInfo]$File
    )

    $progressStatePath = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ("MeetingRecorder-release-upload-{0}.progress" -f ([Guid]::NewGuid().ToString("N")))

    $jobScript = {
        param(
            [string]$UploadUrl,
            [string]$Token,
            [string]$FilePath,
            [string]$FileName,
            [string]$RepoInformationalVersion,
            [string]$ProgressStatePath
        )

        $ErrorActionPreference = "Stop"
        $ProgressPreference = "SilentlyContinue"

        function Write-UploadProgressState {
            param(
                [string]$Path,
                [long]$BytesSent,
                [long]$TotalBytes
            )

            Set-Content `
                -LiteralPath $Path `
                -Value ("{0}|{1}" -f $BytesSent, $TotalBytes) `
                -NoNewline `
                -Encoding ascii
        }

        $fileInfo = Get-Item -LiteralPath $FilePath -ErrorAction Stop
        $resolvedUploadUrl = $UploadUrl.Split('{')[0]
        $escapedName = [Uri]::EscapeDataString($FileName)
        $uri = "{0}?name={1}" -f $resolvedUploadUrl, $escapedName
        $request = [System.Net.HttpWebRequest]::Create($uri)
        $request.Method = "POST"
        $request.Accept = "application/vnd.github+json"
        $request.UserAgent = "MeetingRecorderInstallerUpload/$RepoInformationalVersion"
        $request.ContentType = "application/octet-stream"
        $request.ContentLength = $fileInfo.Length
        $request.Timeout = [System.Threading.Timeout]::Infinite
        $request.ReadWriteTimeout = [System.Threading.Timeout]::Infinite
        $request.Headers["Authorization"] = "Bearer $Token"
        $request.Headers["X-GitHub-Api-Version"] = "2022-11-28"

        $buffer = New-Object byte[] (1024 * 1024)
        $reportIntervalBytes = [long]8MB
        $reportInterval = [TimeSpan]::FromSeconds(2)
        $bytesSent = 0L
        $lastReportedBytes = 0L
        $lastReportedAt = [DateTimeOffset]::UtcNow

        Write-UploadProgressState -Path $ProgressStatePath -BytesSent 0 -TotalBytes $fileInfo.Length

        $fileStream = $null
        $requestStream = $null
        $response = $null
        $responseStream = $null
        $responseReader = $null
        try {
            $fileStream = [System.IO.File]::OpenRead($FilePath)
            $requestStream = $request.GetRequestStream()
            while (($bytesRead = $fileStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $requestStream.Write($buffer, 0, $bytesRead)
                $bytesSent += $bytesRead

                $now = [DateTimeOffset]::UtcNow
                if (($bytesSent -ge $fileInfo.Length) -or
                    (($bytesSent - $lastReportedBytes) -ge $reportIntervalBytes) -or
                    (($now - $lastReportedAt) -ge $reportInterval)) {
                    Write-UploadProgressState -Path $ProgressStatePath -BytesSent $bytesSent -TotalBytes $fileInfo.Length
                    $lastReportedBytes = $bytesSent
                    $lastReportedAt = $now
                }
            }

            $requestStream.Dispose()
            $requestStream = $null

            try {
                $response = $request.GetResponse()
            }
            catch [System.Net.WebException] {
                $message = $_.Exception.Message
                if ($null -ne $_.Exception.Response) {
                    $responseStream = $_.Exception.Response.GetResponseStream()
                    if ($null -ne $responseStream) {
                        $responseReader = New-Object System.IO.StreamReader($responseStream)
                        $responseBody = $responseReader.ReadToEnd()
                        if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
                            $message = "{0} Response: {1}" -f $message, $responseBody
                        }
                    }
                }

                throw $message
            }

            $responseStream = $response.GetResponseStream()
            if ($null -ne $responseStream) {
                $responseReader = New-Object System.IO.StreamReader($responseStream)
                $responseReader.ReadToEnd() | Out-Null
            }

            Write-UploadProgressState -Path $ProgressStatePath -BytesSent $fileInfo.Length -TotalBytes $fileInfo.Length
        }
        finally {
            if ($null -ne $responseReader) {
                $responseReader.Dispose()
            }

            if ($null -ne $responseStream) {
                $responseStream.Dispose()
            }

            if ($null -ne $response) {
                $response.Dispose()
            }

            if ($null -ne $requestStream) {
                $requestStream.Dispose()
            }

            if ($null -ne $fileStream) {
                $fileStream.Dispose()
            }
        }

        return [pscustomobject]@{
            Message = "Uploaded GitHub release asset: $FileName"
        }
    }

    $job = Start-Job `
        -Name ("GitHub release asset upload - " + $File.Name) `
        -ScriptBlock $jobScript `
        -ArgumentList @(
            [string]$UploadUrl,
            [string]$Token,
            [string]$File.FullName,
            [string]$File.Name,
            [string]$script:RepoInformationalVersion,
            [string]$progressStatePath
        )

    return [pscustomobject]@{
        Job = $job
        FileName = $File.Name
        FileLength = [long]$File.Length
        ProgressStatePath = $progressStatePath
    }
}

function Get-GitHubReleaseAssetUploadProgressState {
    param(
        [string]$ProgressStatePath
    )

    if ([string]::IsNullOrWhiteSpace($ProgressStatePath) -or -not (Test-Path -LiteralPath $ProgressStatePath)) {
        return $null
    }

    $rawState = Get-Content -LiteralPath $ProgressStatePath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($rawState)) {
        return $null
    }

    $parts = $rawState.Trim() -split '\|'
    if ($parts.Count -lt 2) {
        return $null
    }

    $bytesSent = 0L
    $totalBytes = 0L
    if (-not [long]::TryParse($parts[0], [ref]$bytesSent)) {
        return $null
    }

    if (-not [long]::TryParse($parts[1], [ref]$totalBytes)) {
        return $null
    }

    return [pscustomobject]@{
        BytesSent = $bytesSent
        TotalBytes = $totalBytes
    }
}

function Write-GitHubReleaseAssetUploadProgressSnapshot {
    param(
        [System.Collections.ArrayList]$Jobs,
        [hashtable]$LastProgressBytesByFile
    )

    if ($null -eq $Jobs -or $Jobs.Count -eq 0) {
        return
    }

    foreach ($uploadState in @($Jobs.ToArray())) {
        $progressState = Get-GitHubReleaseAssetUploadProgressState -ProgressStatePath $uploadState.ProgressStatePath
        if ($null -eq $progressState) {
            continue
        }

        $bytesSent = [long]$progressState.BytesSent
        $lastBytes = if ($LastProgressBytesByFile.ContainsKey($uploadState.FileName)) {
            [long]$LastProgressBytesByFile[$uploadState.FileName]
        }
        else {
            -1L
        }

        if ($bytesSent -le 0 -or $bytesSent -eq $lastBytes) {
            continue
        }

        $totalBytes = if ([long]$progressState.TotalBytes -gt 0) {
            [long]$progressState.TotalBytes
        }
        else {
            [long]$uploadState.FileLength
        }

        $percent = if ($totalBytes -gt 0) {
            [math]::Min(100, [int][math]::Floor(($bytesSent * 100.0) / $totalBytes))
        }
        else {
            0
        }

        $uploadedMegabytes = [math]::Round($bytesSent / 1MB, 1)
        $totalMegabytes = [math]::Round($totalBytes / 1MB, 1)
        Write-Host ("Upload progress: {0} {1}% ({2}/{3} MB)" -f $uploadState.FileName, $percent, $uploadedMegabytes, $totalMegabytes)
        $LastProgressBytesByFile[$uploadState.FileName] = $bytesSent
    }
}

function Get-GitHubReleaseAssetUploadJobFailureMessage {
    param(
        [System.Management.Automation.Job]$Job
    )

    $messages = New-Object System.Collections.Generic.List[string]

    if ($null -ne $Job.JobStateInfo.Reason -and -not [string]::IsNullOrWhiteSpace($Job.JobStateInfo.Reason.Message)) {
        $messages.Add($Job.JobStateInfo.Reason.Message)
    }

    foreach ($childJob in @($Job.ChildJobs)) {
        if ($null -ne $childJob.JobStateInfo.Reason -and -not [string]::IsNullOrWhiteSpace($childJob.JobStateInfo.Reason.Message)) {
            $messages.Add($childJob.JobStateInfo.Reason.Message)
        }

        foreach ($errorRecord in @($childJob.Error)) {
            $errorText = [string]$errorRecord
            if (-not [string]::IsNullOrWhiteSpace($errorText)) {
                $messages.Add($errorText.Trim())
            }
        }
    }

    return ($messages | Select-Object -Unique) -join " "
}

function Complete-GitHubReleaseAssetUploadJob {
    param(
        $UploadState,
        [hashtable]$LastProgressBytesByFile
    )

    try {
        $job = $UploadState.Job
        $progressSnapshotJobs = New-Object System.Collections.ArrayList
        [void]$progressSnapshotJobs.Add($UploadState)
        Write-GitHubReleaseAssetUploadProgressSnapshot `
            -Jobs $progressSnapshotJobs `
            -LastProgressBytesByFile $LastProgressBytesByFile

        if ($Job.State -eq [System.Management.Automation.JobState]::Failed) {
            $failureMessage = Get-GitHubReleaseAssetUploadJobFailureMessage -Job $Job
            if ([string]::IsNullOrWhiteSpace($failureMessage)) {
                $failureMessage = "The background upload job failed without a detailed error message."
            }

            throw $failureMessage
        }

        $results = Receive-Job -Job $Job -ErrorAction Stop
        foreach ($result in @($results)) {
            if ($null -ne $result -and $result.PSObject.Properties.Name -contains "Message") {
                Write-Host $result.Message
            }
        }
    }
    catch {
        throw ("GitHub release asset upload failed for '{0}'. {1}" -f $Job.Name, $_.Exception.Message)
    }
    finally {
        Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue | Out-Null
        Remove-Item -LiteralPath $UploadState.ProgressStatePath -Force -ErrorAction SilentlyContinue
    }
}

function Wait-GitHubReleaseAssetUploadJobs {
    param(
        [System.Collections.ArrayList]$Jobs,
        [hashtable]$LastProgressBytesByFile,
        [switch]$Any
    )

    if ($null -eq $Jobs -or $Jobs.Count -eq 0) {
        return
    }

    while ($Jobs.Count -gt 0) {
        $jobsToWaitOn = @($Jobs.ToArray() | ForEach-Object { $_.Job })
        if ($jobsToWaitOn.Count -eq 0) {
            return
        }

        $completedJobs = if ($Any.IsPresent) {
            @(Wait-Job -Job $jobsToWaitOn -Any -Timeout 2)
        }
        else {
            @(Wait-Job -Job $jobsToWaitOn -Timeout 2)
        }

        Write-GitHubReleaseAssetUploadProgressSnapshot `
            -Jobs $Jobs `
            -LastProgressBytesByFile $LastProgressBytesByFile

        if ($completedJobs.Count -eq 0) {
            continue
        }

        foreach ($completedJob in $completedJobs) {
            $uploadState = @(
                $Jobs.ToArray() |
                Where-Object { $_.Job.Id -eq $completedJob.Id } |
                Select-Object -First 1
            )[0]
            if ($null -eq $uploadState) {
                continue
            }

            Complete-GitHubReleaseAssetUploadJob `
                -UploadState $uploadState `
                -LastProgressBytesByFile $LastProgressBytesByFile
            [void]$Jobs.Remove($uploadState)
        }

        if ($Any.IsPresent) {
            return
        }
    }
}

function Get-InstallerAssetFiles {
    param(
        [string]$PackagePath,
        [Alias('IncludeHeavyInstallers')]
        [switch]$Installers
    )

    if (-not (Test-Path $PackagePath)) {
        throw "Installer package directory '$PackagePath' does not exist. Run Build-Installer.ps1 first."
    }

    $assetPatterns = @(
        "MeetingRecorder-v*-win-x64.zip",
        "Install-LatestFromGitHub.cmd",
        "Install-LatestFromGitHub.ps1"
    )

    if ($Installers.IsPresent) {
        $assetPatterns = @(
            "MeetingRecorderInstaller.msi"
        ) + $assetPatterns
    }

    $files = foreach ($pattern in $assetPatterns) {
        Get-ChildItem -Path $PackagePath -File -Filter $pattern -ErrorAction SilentlyContinue
    }

    $uniqueFiles = $files |
        Sort-Object FullName -Unique

    if ($null -eq $uniqueFiles -or @($uniqueFiles).Count -eq 0) {
        throw "No installer assets were found in '$PackagePath'. Build the installer assets first."
    }

    return @($uniqueFiles)
}

function Get-DeprecatedReleaseAssetNames {
    return @(
        "MeetingRecorderInstaller.exe"
    )
}

function Sync-InstallerAssetsToGitHubLatestRelease {
    param(
        [string]$Owner,
        [string]$Name,
        [string]$Token,
        [System.IO.FileInfo[]]$Assets,
        [int]$MaxParallelUploads,
        [switch]$DryRun
    )

    if (-not $DryRun.IsPresent -and [string]::IsNullOrWhiteSpace($Token)) {
        throw "GitHub upload requires a token. Set GITHUB_TOKEN or GH_TOKEN, or pass -GitHubToken."
    }

    $release = Get-LatestGitHubRelease -Owner $Owner -Name $Name -Token $Token
    Write-Host ("Latest GitHub release: tag='{0}', url='{1}'" -f $release.tag_name, $release.html_url)

    $remoteAssetsByName = @{}
    foreach ($asset in @($release.assets)) {
        $remoteAssetsByName[[string]$asset.name] = $asset
    }

    foreach ($deprecatedAssetName in Get-DeprecatedReleaseAssetNames) {
        $deprecatedAsset = $remoteAssetsByName[$deprecatedAssetName]
        if ($null -eq $deprecatedAsset) {
            continue
        }

        Write-Host ("Removing deprecated GitHub release asset: " + $deprecatedAssetName)
        Remove-GitHubReleaseAsset -Owner $Owner -Name $Name -Token $Token -Asset $deprecatedAsset -DryRun:$DryRun
    }

    $uploadJobs = New-Object System.Collections.ArrayList
    $lastProgressBytesByFile = @{}

    foreach ($assetFile in $Assets) {
        $remoteAsset = $remoteAssetsByName[$assetFile.Name]
        if (Test-GitHubReleaseAssetMatchesLocalFile -Asset $remoteAsset -LocalFile $assetFile) {
            Write-Host ("Skipped unchanged installer asset: " + $assetFile.Name)
            continue
        }

        if ($null -ne $remoteAsset) {
            Write-Host ("Replacing GitHub release asset: " + $assetFile.Name)
            Remove-GitHubReleaseAsset -Owner $Owner -Name $Name -Token $Token -Asset $remoteAsset -DryRun:$DryRun
        }
        else {
            Write-Host ("Uploading new GitHub release asset: " + $assetFile.Name)
        }

        if ($DryRun.IsPresent) {
            Publish-GitHubReleaseAsset `
                -UploadUrl ([string]$release.upload_url) `
                -Token $Token `
                -File $assetFile `
                -DryRun:$DryRun
            continue
        }

        while ($uploadJobs.Count -ge $MaxParallelUploads) {
            Wait-GitHubReleaseAssetUploadJobs `
                -Jobs $uploadJobs `
                -LastProgressBytesByFile $lastProgressBytesByFile `
                -Any
        }

        Write-Host ("Queueing GitHub release asset upload: " + $assetFile.Name)
        $uploadJob = Start-GitHubReleaseAssetUploadJob `
            -UploadUrl ([string]$release.upload_url) `
            -Token $Token `
            -File $assetFile
        [void]$uploadJobs.Add($uploadJob)
    }

    if (-not $DryRun.IsPresent) {
        Wait-GitHubReleaseAssetUploadJobs `
            -Jobs $uploadJobs `
            -LastProgressBytesByFile $lastProgressBytesByFile
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$script:RepoInformationalVersion = Get-RepoInformationalVersion -RepoRoot $repoRoot
$packagePath = Join-Path $repoRoot $PackageRoot
$repositoryInfo = Get-GitHubRepositoryInfo -RepoRoot $repoRoot
$resolvedGitHubToken = Resolve-GitHubToken -ExplicitToken $GitHubToken
Assert-ReleaseAssetsMatchCurrentRepoState -RepoRoot $repoRoot -PackagePath $packagePath
$assetFiles = Get-InstallerAssetFiles -PackagePath $packagePath -Installers:$Installers

Write-Host "Installer assets selected for GitHub release sync:"
foreach ($assetFile in $assetFiles) {
    Write-Host ("- {0} [{1} bytes]" -f $assetFile.Name, $assetFile.Length)
}

if (-not $Installers.IsPresent) {
    Write-Host "Skipping EXE/MSI installer uploads by default. Pass -Installers to include them."
}

Sync-InstallerAssetsToGitHubLatestRelease `
    -Owner $repositoryInfo.Owner `
    -Name $repositoryInfo.Name `
    -Token $resolvedGitHubToken `
    -Assets $assetFiles `
    -MaxParallelUploads $MaxParallelUploads `
    -DryRun:$DryRun
