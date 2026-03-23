param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$SkipTests,
    [switch]$UploadToGitHubLatestRelease,
    [switch]$DryRunGitHubUpload,
    [string]$GitHubToken = "",
    [string]$CodeSigningCertificateThumbprint = "",
    [string]$CodeSigningCertificateStorePath = "",
    [string]$CodeSigningTimestampUrl = ""
)

$ErrorActionPreference = "Stop"

function Get-ReleaseVersionLabel {
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
    $versionValue = if (-not [string]::IsNullOrWhiteSpace($informationalVersion)) {
        $informationalVersion.Trim()
    }
    else {
        ([string]$propertyGroup.Version).Trim()
    }

    if ([string]::IsNullOrWhiteSpace($versionValue)) {
        throw "Could not resolve a release version from Directory.Build.props"
    }

    if ($versionValue.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        return $versionValue
    }

    return "v" + $versionValue
}

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

    $versionValue = [string]$propertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($versionValue)) {
        throw "Could not resolve a repo version from Directory.Build.props"
    }

    return $versionValue.Trim()
}

function Get-GitHubRepositoryInfo {
    param(
        [string]$RepoRoot
    )

    $brandingPath = Join-Path $RepoRoot "src\MeetingRecorder.Core\Branding\AppBranding.cs"
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$testScript = Join-Path $PSScriptRoot "Test-All.ps1"
$installerScript = Join-Path $PSScriptRoot "Build-Installer.ps1"
$releaseRoot = Join-Path $repoRoot ".artifacts\installer\$Runtime"
$bundledAsrModelsPath = Join-Path $repoRoot "assets\models\asr"
$bundledDiarizationAssetsPath = Join-Path $repoRoot "assets\models\diarization"
$versionLabel = Get-ReleaseVersionLabel -RepoRoot $repoRoot
$script:RepoInformationalVersion = Get-RepoInformationalVersion -RepoRoot $repoRoot
$repositoryInfo = Get-GitHubRepositoryInfo -RepoRoot $repoRoot

function Publish-ReleasePayloadAssets {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [string[]]$AllowedExtensions
    )

    if (-not (Test-Path $SourcePath)) {
        return @()
    }

    $assetFiles = Get-ChildItem -Path $SourcePath -File |
        Where-Object { $AllowedExtensions -contains $_.Extension.ToLowerInvariant() } |
        Sort-Object Name
    foreach ($assetFile in $assetFiles) {
        $destinationFilePath = Join-Path $DestinationPath $assetFile.Name

        if (Test-Path $destinationFilePath) {
            $existingFile = Get-Item -Path $destinationFilePath -ErrorAction Stop
            if ($existingFile.Length -eq $assetFile.Length -and
                $existingFile.LastWriteTimeUtc -eq $assetFile.LastWriteTimeUtc) {
                Write-Host ("Preserved unchanged release asset: " + $existingFile.FullName)
                continue
            }
        }

        Copy-Item -Path $assetFile.FullName -Destination $destinationFilePath -Force
        Write-Host ("Copied updated release asset: " + $destinationFilePath)
    }

    return $assetFiles | ForEach-Object { Join-Path $DestinationPath $_.Name }
}

function Get-GitHubApiHeaders {
    param(
        [string]$Token
    )

    $headers = @{
        "Accept" = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "MeetingRecorderRelease/$script:RepoInformationalVersion"
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers["Authorization"] = "Bearer $Token"
    }

    return $headers
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

function Get-LatestGitHubRelease {
    param(
        [string]$Owner,
        [string]$Name,
        [string]$Token
    )

    $uri = "https://api.github.com/repos/$Owner/$Name/releases/latest"
    return Invoke-RestMethod -Headers (Get-GitHubApiHeaders -Token $Token) -Uri $uri -Method Get
}

function Build-ReleaseAssetMetadataLabel {
    param(
        [System.IO.FileInfo]$File
    )

    return "sourceLastWriteUtc={0};sizeBytes={1}" -f $File.LastWriteTimeUtc.ToString("O"), $File.Length
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

    $parsed = $null
    if ([DateTimeOffset]::TryParse($match.Groups[1].Value, [ref]$parsed)) {
        return $parsed
    }

    return $null
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

    $remoteSourceLastWriteUtc = Get-ReleaseAssetSourceLastWriteUtc -Asset $Asset
    if ($null -ne $remoteSourceLastWriteUtc) {
        return $remoteSourceLastWriteUtc.UtcDateTime -eq $LocalFile.LastWriteTimeUtc.UtcDateTime
    }

    return $LocalFile.Extension -ieq ".bin"
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
        [string]$Label,
        [switch]$DryRun
    )

    if ($DryRun.IsPresent) {
        Write-Host ("[dry-run] Would upload '{0}' ({1} bytes) with label '{2}'." -f $File.Name, $File.Length, $Label)
        return
    }

    $resolvedUploadUrl = $UploadUrl.Split('{')[0]
    $escapedName = [Uri]::EscapeDataString($File.Name)
    $escapedLabel = [Uri]::EscapeDataString($Label)
    $uri = "{0}?name={1}&label={2}" -f $resolvedUploadUrl, $escapedName, $escapedLabel
    $headers = @{
        "Accept" = "application/vnd.github+json"
        "Authorization" = "Bearer $Token"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent" = "MeetingRecorderRelease/$script:RepoInformationalVersion"
        "Content-Type" = "application/octet-stream"
    }

    Invoke-RestMethod -Headers $headers -Uri $uri -Method Post -InFile $File.FullName | Out-Null
}

function Sync-ReleaseAssetsToGitHubLatestRelease {
    param(
        [string]$Owner,
        [string]$Name,
        [string]$Token,
        [System.IO.FileInfo[]]$Assets,
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

    foreach ($assetFile in $Assets) {
        $remoteAsset = $remoteAssetsByName[$assetFile.Name]
        $matches = Test-GitHubReleaseAssetMatchesLocalFile -Asset $remoteAsset -LocalFile $assetFile
        if ($matches) {
            $hasTimestampMetadata = $null -ne (Get-ReleaseAssetSourceLastWriteUtc -Asset $remoteAsset)
            if (-not $hasTimestampMetadata -and $assetFile.Extension -ieq ".bin") {
                Write-Host ("Skipped unchanged model asset by size match: " + $assetFile.Name)
            }
            else {
                Write-Host ("Skipped unchanged release asset: " + $assetFile.Name)
            }

            continue
        }

        if ($null -ne $remoteAsset) {
            Write-Host ("Replacing GitHub release asset: " + $assetFile.Name)
            Remove-GitHubReleaseAsset -Owner $Owner -Name $Name -Token $Token -Asset $remoteAsset -DryRun:$DryRun
        }
        else {
            Write-Host ("Uploading new GitHub release asset: " + $assetFile.Name)
        }

        Publish-GitHubReleaseAsset `
            -UploadUrl ([string]$release.upload_url) `
            -Token $Token `
            -File $assetFile `
            -Label (Build-ReleaseAssetMetadataLabel -File $assetFile) `
            -DryRun:$DryRun
    }
}

if (-not $SkipTests.IsPresent) {
    Write-Host "Running the release verification suite..."
    & $testScript -Configuration $Configuration

    if ($LASTEXITCODE -ne 0) {
        throw "Release verification failed."
    }
}

Write-Host "Building GitHub release assets..."
& $installerScript `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -PackageRoot ".artifacts\installer\$Runtime" `
    -FrameworkDependent:$FrameworkDependent `
    -CodeSigningCertificateThumbprint $CodeSigningCertificateThumbprint `
    -CodeSigningCertificateStorePath $CodeSigningCertificateStorePath `
    -CodeSigningTimestampUrl $CodeSigningTimestampUrl

if ($LASTEXITCODE -ne 0) {
    throw "Release packaging failed."
}

$mainZip = Get-ChildItem -Path $releaseRoot -Filter "MeetingRecorder-$versionLabel-*.zip" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notmatch "^MeetingRecorder-bootstrap-" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$installerExecutable = Get-Item -Path (Join-Path $releaseRoot "MeetingRecorderInstaller.exe") -ErrorAction SilentlyContinue
$installerMsi = Get-Item -Path (Join-Path $releaseRoot "MeetingRecorderInstaller.msi") -ErrorAction SilentlyContinue
$bootstrapCommand = Get-Item -Path (Join-Path $releaseRoot "Install-LatestFromGitHub.cmd") -ErrorAction SilentlyContinue
$bootstrapScript = Get-Item -Path (Join-Path $releaseRoot "Install-LatestFromGitHub.ps1") -ErrorAction SilentlyContinue
$modelAssetPaths = Publish-ReleasePayloadAssets -SourcePath $bundledAsrModelsPath -DestinationPath $releaseRoot -AllowedExtensions @(".bin")
$diarizationAssetPaths = Publish-ReleasePayloadAssets -SourcePath $bundledDiarizationAssetsPath -DestinationPath $releaseRoot -AllowedExtensions @(".zip", ".exe", ".onnx", ".bin", ".json", ".yaml", ".yml")
$modelAssets = $modelAssetPaths | ForEach-Object { Get-Item -Path $_ -ErrorAction SilentlyContinue } | Where-Object { $_ -ne $null }
$diarizationAssets = $diarizationAssetPaths | ForEach-Object { Get-Item -Path $_ -ErrorAction SilentlyContinue } | Where-Object { $_ -ne $null }
$localReleaseAssets = @($mainZip, $installerExecutable, $installerMsi, $bootstrapCommand, $bootstrapScript) + @($modelAssets) + @($diarizationAssets) | Where-Object { $_ -ne $null }

Write-Host ""
Write-Host "Release assets are ready to upload to GitHub Releases:"

if ($null -ne $mainZip) {
    Write-Host ("- Main installer: " + $mainZip.FullName)
}

if ($null -ne $installerExecutable) {
    Write-Host ("- Installer executable: " + $installerExecutable.FullName)
}

if ($null -ne $installerMsi) {
    Write-Host ("- Installer MSI: " + $installerMsi.FullName)
}

if ($null -ne $bootstrapCommand) {
    Write-Host ("- Bootstrap command: " + $bootstrapCommand.FullName)
}

if ($null -ne $bootstrapScript) {
    Write-Host ("- Bootstrap script: " + $bootstrapScript.FullName)
}

foreach ($modelAsset in $modelAssets) {
    Write-Host ("- Model asset: " + $modelAsset.FullName)
}

foreach ($diarizationAsset in $diarizationAssets) {
    Write-Host ("- Diarization asset: " + $diarizationAsset.FullName)
}

Write-Host ""
Write-Host "Recommended GitHub release flow:"
Write-Host "- Upload MeetingRecorderInstaller.msi as the preferred corporate-friendly install/update asset."
Write-Host "- Upload MeetingRecorderInstaller.exe only if you still want the custom bootstrapper available."
Write-Host "- Upload the main installer ZIP so manual download/install still works."
Write-Host "- Upload Install-LatestFromGitHub.cmd so end users can download one file and run it without extracting a ZIP."
Write-Host "- Upload Install-LatestFromGitHub.ps1 as the stable companion asset for the command bootstrap path."
Write-Host "- Upload any ggml *.bin model assets you want users to be able to download separately from the app bundle."
Write-Host "- Upload any diarization sidecar bundles or supporting diarization assets you want users to be able to download separately from the app bundle."

if ($UploadToGitHubLatestRelease.IsPresent -or $DryRunGitHubUpload.IsPresent) {
    Write-Host ""
    Write-Host "Syncing release assets to the latest GitHub release..."
    Assert-ReleaseAssetsMatchCurrentRepoState -RepoRoot $repoRoot -PackagePath $releaseRoot
    $resolvedGitHubToken = Resolve-GitHubToken -ExplicitToken $GitHubToken
    Sync-ReleaseAssetsToGitHubLatestRelease `
        -Owner $repositoryInfo.Owner `
        -Name $repositoryInfo.Name `
        -Token $resolvedGitHubToken `
        -Assets $localReleaseAssets `
        -DryRun:$DryRunGitHubUpload
}
