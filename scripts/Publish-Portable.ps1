param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".artifacts\publish\win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $OutputRoot
$appTemp = Join-Path $outputPath "app-temp"
$cliTemp = Join-Path $outputPath "cli-temp"
$workerTemp = Join-Path $outputPath "worker-temp"
$finalPath = Join-Path $outputPath "MeetingRecorder"
$appAssetPath = Join-Path $repoRoot "src\MeetingRecorder.App\Assets\MeetingRecorder.ico"
$productManifestPath = Join-Path $repoRoot "src\MeetingRecorder.Product\MeetingRecorder.product.json"
$modelCatalogSourcePath = Join-Path $repoRoot "src\MeetingRecorder.Core\Assets\model-catalog.json"
$selfContained = -not $FrameworkDependent.IsPresent
$selfContainedValue = if ($selfContained) { "true" } else { "false" }
$bundleMode = if ($selfContained) { "self-contained" } else { "framework-dependent" }

function Invoke-GitQuery {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    $output = & git -C $RepoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed while generating release metadata."
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

function Get-ReleaseSourceMetadata {
    param(
        [string]$RepoRoot
    )

    # release-source.json captures the exact source snapshot using:
    # git rev-parse HEAD
    # git status --short
    $gitCommit = (Invoke-GitQuery -RepoRoot $RepoRoot -Arguments @("rev-parse", "HEAD") | Select-Object -First 1).Trim()
    $gitCommitShort = (Invoke-GitQuery -RepoRoot $RepoRoot -Arguments @("rev-parse", "--short", "HEAD") | Select-Object -First 1).Trim()
    $worktreeStatus = Invoke-GitQuery -RepoRoot $RepoRoot -Arguments (@("status", "--short", "--untracked-files=all", "--") + (Get-SourceStatePathspec))

    return [ordered]@{
        formatVersion   = 1
        builtAtUtc      = [DateTimeOffset]::UtcNow.ToString("O")
        gitCommit       = $gitCommit
        gitCommitShort  = $gitCommitShort
        isWorktreeDirty = ($worktreeStatus.Count -gt 0)
    }
}

function Invoke-DotnetPublish {
    param(
        [string]$ProjectPath,
        [string]$PublishOutput
    )

    $restoreArgs = @(
        "restore",
        $ProjectPath,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false"
    )

    if ($selfContained) {
        $restoreArgs += @("-r", $Runtime)
    }

    & dotnet @restoreArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $ProjectPath"
    }

    $publishArgs = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "--force",
        "--self-contained",
        $selfContainedValue,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false",
        "-o",
        $PublishOutput
    )

    if ($selfContained) {
        $publishArgs += @("-r", $Runtime)
    }

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

function Read-ModelCatalog {
    param(
        [string]$CatalogPath
    )

    if (-not (Test-Path $CatalogPath)) {
        throw "Required model catalog was not found at '$CatalogPath'."
    }

    return Get-Content -Path $CatalogPath -Raw | ConvertFrom-Json
}

function New-BundleIntegrityEntry {
    param(
        [string]$BundleRoot,
        [string]$RelativePath
    )

    $resolvedPath = Join-Path $BundleRoot $RelativePath
    if (-not (Test-Path $resolvedPath)) {
        throw "Portable bundle is missing required file '$RelativePath'."
    }

    $item = Get-Item -Path $resolvedPath
    return [ordered]@{
        relativePath = $RelativePath
        lengthBytes  = [int64]$item.Length
        sha256       = ((Get-FileHash -Path $resolvedPath -Algorithm SHA256).Hash).ToLowerInvariant()
    }
}

function Assert-SingleFileWpfShellPublishLayout {
    param(
        [string]$PublishRoot
    )

    $requiredAppShellPath = Join-Path $PublishRoot "MeetingRecorder.App.exe"
    if (-not (Test-Path $requiredAppShellPath)) {
        throw "Portable app publish is missing the single-file WPF shell '$requiredAppShellPath'."
    }

    $forbiddenAppPublishArtifacts = @(
        "MeetingRecorder.App.dll",
        "MeetingRecorder.App.deps.json",
        "MeetingRecorder.App.runtimeconfig.json",
        "WindowsBase.dll",
        "PresentationCore.dll",
        "PresentationFramework.dll",
        "PresentationUI.dll",
        "System.Xaml.dll",
        "WindowsFormsIntegration.dll",
        "UIAutomationClient.dll",
        "UIAutomationClientSideProviders.dll",
        "UIAutomationProvider.dll",
        "UIAutomationTypes.dll"
    )

    $unexpectedArtifacts = @(
        $forbiddenAppPublishArtifacts |
            ForEach-Object {
                $candidatePath = Join-Path $PublishRoot $_
                if (Test-Path $candidatePath) {
                    $candidatePath
                }
            }
    )

    if ($unexpectedArtifacts.Count -gt 0) {
        throw "Portable app publish regressed to a loose-file WPF shell layout. Unexpected app-shell artifacts: $($unexpectedArtifacts -join '; ')."
    }
}

function Assert-SingleFileWpfShellBundleLayout {
    param(
        [string]$BundleRoot
    )

    $requiredAppShellPath = Join-Path $BundleRoot "MeetingRecorder.App.exe"
    if (-not (Test-Path $requiredAppShellPath)) {
        throw "Portable bundle is missing the single-file WPF shell '$requiredAppShellPath'."
    }

    $forbiddenBundleArtifacts = @(
        "MeetingRecorder.App.dll",
        "MeetingRecorder.App.deps.json",
        "MeetingRecorder.App.runtimeconfig.json"
    )

    $unexpectedArtifacts = @(
        $forbiddenBundleArtifacts |
            ForEach-Object {
                $candidatePath = Join-Path $BundleRoot $_
                if (Test-Path $candidatePath) {
                    $candidatePath
                }
            }
    )

    if ($unexpectedArtifacts.Count -gt 0) {
        throw "Portable bundle regressed to a loose-file WPF shell layout. Unexpected app-shell artifacts: $($unexpectedArtifacts -join '; ')."
    }
}

Remove-Item -Recurse -Force $appTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $cliTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $workerTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $finalPath -ErrorAction SilentlyContinue

Invoke-DotnetPublish -ProjectPath (Join-Path $repoRoot "src\MeetingRecorder.App\MeetingRecorder.App.csproj") -PublishOutput $appTemp
Assert-SingleFileWpfShellPublishLayout -PublishRoot $appTemp
Invoke-DotnetPublish -ProjectPath (Join-Path $repoRoot "src\AppPlatform.Deployment.Cli\AppPlatform.Deployment.Cli.csproj") -PublishOutput $cliTemp
Invoke-DotnetPublish -ProjectPath (Join-Path $repoRoot "src\MeetingRecorder.ProcessingWorker\MeetingRecorder.ProcessingWorker.csproj") -PublishOutput $workerTemp

New-Item -ItemType Directory -Force -Path $finalPath | Out-Null
Copy-Item -Path (Join-Path $appTemp "*") -Destination $finalPath -Recurse -Force
Copy-Item -Path (Join-Path $cliTemp "*") -Destination $finalPath -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Run-MeetingRecorder.cmd") -Destination (Join-Path $finalPath "Run-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Launch-MeetingRecorder-AfterInstall.vbs") -Destination (Join-Path $finalPath "Launch-MeetingRecorder-AfterInstall.vbs") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Apply-DownloadedUpdate.ps1") -Destination (Join-Path $finalPath "Apply-DownloadedUpdate.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.ps1") -Destination (Join-Path $finalPath "Install-LatestFromGitHub.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.cmd") -Destination (Join-Path $finalPath "Install-LatestFromGitHub.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Check-Dependencies.ps1") -Destination (Join-Path $finalPath "Check-Dependencies.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-Dependencies.ps1") -Destination (Join-Path $finalPath "Install-Dependencies.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-Dependencies.cmd") -Destination (Join-Path $finalPath "Install-Dependencies.cmd") -Force
Copy-Item -Path (Join-Path $repoRoot "SETUP.md") -Destination (Join-Path $finalPath "SETUP.md") -Force
Copy-Item -Path $productManifestPath -Destination (Join-Path $finalPath "MeetingRecorder.product.json") -Force
Set-Content -Path (Join-Path $finalPath "portable.mode") -Value "portable" -NoNewline
Set-Content -Path (Join-Path $finalPath "bundle-mode.txt") -Value $bundleMode -NoNewline
Copy-Item -Path $modelCatalogSourcePath -Destination (Join-Path $finalPath "model-catalog.json") -Force

if (Test-Path $appAssetPath) {
    Copy-Item -Path $appAssetPath -Destination (Join-Path $finalPath "MeetingRecorder.ico") -Force
}

Copy-Item -Path (Join-Path $workerTemp "*") -Destination $finalPath -Recurse -Force

Assert-SingleFileWpfShellBundleLayout -BundleRoot $finalPath

$bundleIntegrityManifest = [ordered]@{
    formatVersion = 1
    requiredFiles = @(
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.App.exe"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "AppPlatform.Deployment.Cli.exe"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.ProcessingWorker.exe"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.ProcessingWorker.dll"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.ProcessingWorker.deps.json"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.ProcessingWorker.runtimeconfig.json"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.Core.dll"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "MeetingRecorder.product.json"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "model-catalog.json"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "Run-MeetingRecorder.cmd"),
        (New-BundleIntegrityEntry -BundleRoot $finalPath -RelativePath "Launch-MeetingRecorder-AfterInstall.vbs")
    )
}
$bundleIntegrityManifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -Path (Join-Path $finalPath "bundle-integrity.json") -Encoding UTF8

$releaseSourceMetadata = Get-ReleaseSourceMetadata -RepoRoot $repoRoot
$releaseSourceMetadata |
    ConvertTo-Json -Depth 4 |
    Set-Content -Path (Join-Path $finalPath "release-source.json") -Encoding UTF8

Write-Host "Portable publish assembled at $finalPath"
