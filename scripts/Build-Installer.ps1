param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PackageRoot = ".artifacts\installer\win-x64",
    [switch]$FrameworkDependent,
    [switch]$KeepStaging,
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $repoRoot $PackageRoot
$stagingPath = Join-Path $packagePath "staging"
$bundleRoot = Join-Path $stagingPath "MeetingRecorder"
$installerTemp = Join-Path $packagePath "installer-temp"
$versionLabel = Get-ReleaseVersionLabel -RepoRoot $repoRoot
$zipPath = Join-Path $packagePath "MeetingRecorder-$versionLabel-$Runtime.zip"
$installerExecutablePath = Join-Path $packagePath "MeetingRecorderInstaller.exe"
$installerMsiPath = Join-Path $packagePath "MeetingRecorderInstaller.msi"
$bootstrapCommandPath = Join-Path $packagePath "Install-LatestFromGitHub.cmd"
$bootstrapScriptPath = Join-Path $packagePath "Install-LatestFromGitHub.ps1"
$publishScript = Join-Path $PSScriptRoot "Publish-Portable.ps1"
$publishedAppPath = Join-Path $repoRoot ".artifacts\publish\$Runtime\MeetingRecorder"
$installerProjectPath = Join-Path $repoRoot "src\MeetingRecorder.Installer\MeetingRecorder.Installer.csproj"
$msiProjectPath = Join-Path $repoRoot "src\MeetingRecorder.Setup\MeetingRecorder.Setup.wixproj"
$msiTemp = Join-Path $packagePath "msi-temp"
$msiGeneratedAuthoringPath = Join-Path $msiTemp "generated\AppFiles.wxs"
$msiStagingPath = Join-Path $packagePath "msi-staging"
$gitHubReleaseLimitBytes = [long]2GB

function Resolve-CodeSigningConfiguration {
    param(
        [string]$ExplicitThumbprint,
        [string]$ExplicitStorePath,
        [string]$ExplicitTimestampUrl
    )

    $thumbprint = if (-not [string]::IsNullOrWhiteSpace($ExplicitThumbprint)) {
        $ExplicitThumbprint.Trim()
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:MEETINGRECORDER_SIGNING_CERT_THUMBPRINT)) {
        $env:MEETINGRECORDER_SIGNING_CERT_THUMBPRINT.Trim()
    }
    else {
        ""
    }

    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        return $null
    }

    $storePath = if (-not [string]::IsNullOrWhiteSpace($ExplicitStorePath)) {
        $ExplicitStorePath.Trim()
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:MEETINGRECORDER_SIGNING_CERT_STORE_PATH)) {
        $env:MEETINGRECORDER_SIGNING_CERT_STORE_PATH.Trim()
    }
    else {
        "Cert:\CurrentUser\My"
    }

    $timestampUrl = if (-not [string]::IsNullOrWhiteSpace($ExplicitTimestampUrl)) {
        $ExplicitTimestampUrl.Trim()
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:MEETINGRECORDER_SIGNING_TIMESTAMP_URL)) {
        $env:MEETINGRECORDER_SIGNING_TIMESTAMP_URL.Trim()
    }
    else {
        ""
    }

    return [pscustomobject]@{
        Thumbprint = $thumbprint
        StorePath = $storePath
        TimestampUrl = $timestampUrl
    }
}

function Get-CodeSigningCertificate {
    param(
        [pscustomobject]$Configuration
    )

    if ($null -eq $Configuration) {
        return $null
    }

    if (-not (Test-Path $Configuration.StorePath)) {
        throw "Configured signing certificate store path '$($Configuration.StorePath)' does not exist."
    }

    $certificate = Get-ChildItem -Path $Configuration.StorePath |
        Where-Object { $_.Thumbprint -eq $Configuration.Thumbprint } |
        Select-Object -First 1

    if ($null -eq $certificate) {
        throw "Could not find a signing certificate with thumbprint '$($Configuration.Thumbprint)' in '$($Configuration.StorePath)'."
    }

    if (-not $certificate.HasPrivateKey) {
        throw "The signing certificate '$($Configuration.Thumbprint)' does not have an accessible private key."
    }

    return $certificate
}

function Invoke-AuthenticodeSigning {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [string]$FilePath,
        [string]$TimestampUrl
    )

    if (-not (Test-Path $FilePath)) {
        throw "Cannot sign missing file '$FilePath'."
    }

    $signatureParameters = @{
        FilePath = $FilePath
        Certificate = $Certificate
        HashAlgorithm = "SHA256"
    }

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signatureParameters["TimestampServer"] = $TimestampUrl
    }

    $signature = Set-AuthenticodeSignature @signatureParameters

    if ($null -eq $signature.SignerCertificate -or $signature.Status -in @("HashMismatch", "NotSigned")) {
        throw "Authenticode signing failed for '$FilePath'. Status: $($signature.Status)"
    }

    Write-Host ("Signed artifact: {0} [{1}]" -f $FilePath, $signature.Status)
}

function Sign-ArtifactFiles {
    param(
        [pscustomobject]$SigningConfiguration,
        [string[]]$FilePaths
    )

    if ($null -eq $SigningConfiguration) {
        return
    }

    $certificate = Get-CodeSigningCertificate -Configuration $SigningConfiguration
    foreach ($filePath in $FilePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
        Invoke-AuthenticodeSigning `
            -Certificate $certificate `
            -FilePath $filePath `
            -TimestampUrl $SigningConfiguration.TimestampUrl
    }
}

function Reset-PackageOutputDirectory {
    param(
        [string]$DirectoryPath
    )

    New-Item -ItemType Directory -Force -Path $DirectoryPath | Out-Null
    Get-ChildItem -Path $DirectoryPath -Force -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not $_.PSIsContainer -and (Test-PreservedReleaseAsset -FileInfo $_)) {
            return
        }

        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction Stop
    }
}

function Test-PreservedReleaseAsset {
    param(
        [System.IO.FileInfo]$FileInfo
    )

    if ($null -eq $FileInfo -or $FileInfo.PSIsContainer) {
        return $false
    }

    if ($FileInfo.Extension -eq ".bin") {
        return $true
    }

    $diarizationExtensions = @(".zip", ".exe", ".onnx", ".json", ".yaml", ".yml")
    return $FileInfo.Name -match "diarization" -and $diarizationExtensions -contains $FileInfo.Extension.ToLowerInvariant()
}

function New-ZipAsset {
    param(
        [string]$SourceRoot,
        [string]$PreferredPath
    )

    Compress-Archive -Path (Join-Path $SourceRoot "*") -DestinationPath $PreferredPath -Force
    return $PreferredPath
}

function Publish-StableAsset {
    param(
        [string]$SourcePath,
        [string]$PreferredPath
    )

    Copy-Item -Path $SourcePath -Destination $PreferredPath -Force
    return $PreferredPath
}

function Convert-ToWixIdentifier {
    param(
        [string]$Prefix,
        [string]$Value,
        [hashtable]$UsedIdentifiers
    )

    $normalizedValue = if ([string]::IsNullOrWhiteSpace($Value)) { "value" } else { $Value }
    $base = ($normalizedValue -replace '[^A-Za-z0-9_.]', '_')
    if ($base -notmatch '^[A-Za-z_]') {
        $base = "_" + $base
    }

    $identifierHasher = [System.Security.Cryptography.MD5]::Create()
    try {
        $hash = [System.BitConverter]::ToString(
            $identifierHasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalizedValue))
        ).Replace("-", [string]::Empty).Substring(0, 8)
    }
    finally {
        $identifierHasher.Dispose()
    }

    $maxBaseLength = [Math]::Max(8, 64 - $Prefix.Length - $hash.Length - 1)
    if ($base.Length -gt $maxBaseLength) {
        $base = $base.Substring(0, $maxBaseLength)
    }

    $identifier = "{0}{1}_{2}" -f $Prefix, $base, $hash
    $suffix = 1
    while ($UsedIdentifiers.ContainsKey($identifier)) {
        $identifier = "{0}{1}_{2}_{3}" -f $Prefix, $base, $hash, $suffix
        $suffix += 1
    }

    $UsedIdentifiers[$identifier] = $true
    return $identifier
}

function New-DeterministicGuid {
    param(
        [string]$Value
    )

    $guidHasher = [System.Security.Cryptography.MD5]::Create()
    try {
        $hashBytes = $guidHasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))
        return [Guid]::new($hashBytes)
    }
    finally {
        $guidHasher.Dispose()
    }
}

function Escape-WixXml {
    param(
        [string]$Value
    )

    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\') + '\'))
    $targetUri = New-Object System.Uri($TargetPath)
    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', '\'))
}

function Copy-MsiPayload {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    Remove-Item -Recurse -Force $DestinationRoot -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
    Copy-Item -Path (Join-Path $SourceRoot "*") -Destination $DestinationRoot -Recurse -Force

    foreach ($excludedName in @(
        "portable.mode",
        "bundle-mode.txt",
        "Run-MeetingRecorder.cmd",
        "Install-LatestFromGitHub.cmd",
        "Install-LatestFromGitHub.ps1",
        "Check-Dependencies.ps1",
        "Install-Dependencies.cmd",
        "Install-Dependencies.ps1",
        "SETUP.md")) {
        Remove-Item -Path (Join-Path $DestinationRoot $excludedName) -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Write-MsiHarvestAuthoring {
    param(
        [string]$SourceRoot,
        [string]$OutputPath
    )

    $sourceDirectory = Get-Item -Path $SourceRoot -ErrorAction Stop
    $usedIdentifiers = @{}
    $componentIds = New-Object System.Collections.Generic.List[string]
    $builder = New-Object System.Text.StringBuilder

    function Add-Line {
        param([string]$Text)
        [void]$builder.AppendLine($Text)
    }

    function Write-DirectoryContent {
        param(
            [string]$CurrentPath,
            [int]$IndentLevel
        )

        $indent = "  " * $IndentLevel
        foreach ($file in Get-ChildItem -Path $CurrentPath -File | Sort-Object Name) {
            $relativePath = Get-RelativePathCompat -BasePath $sourceDirectory.FullName -TargetPath $file.FullName
            $componentId = Convert-ToWixIdentifier -Prefix "cmp_" -Value $relativePath -UsedIdentifiers $usedIdentifiers
            $fileId = if ($relativePath -ieq "MeetingRecorder.App.exe") {
                "MainExecutableFile"
            }
            else {
                Convert-ToWixIdentifier -Prefix "fil_" -Value $relativePath -UsedIdentifiers $usedIdentifiers
            }

            $componentIds.Add($componentId)
            $componentGuid = (New-DeterministicGuid -Value ("msi|" + $relativePath)).ToString().ToUpperInvariant()
            Add-Line ($indent + ('<Component Id="{0}" Guid="{1}">' -f $componentId, $componentGuid))
            Add-Line ($indent + '  ' + ('<File Id="{0}" Source="{1}" KeyPath="yes" />' -f $fileId, (Escape-WixXml $file.FullName)))
            Add-Line ($indent + '</Component>')
        }

        foreach ($directory in Get-ChildItem -Path $CurrentPath -Directory | Sort-Object Name) {
            $relativeDirectoryPath = Get-RelativePathCompat -BasePath $sourceDirectory.FullName -TargetPath $directory.FullName
            $directoryId = Convert-ToWixIdentifier -Prefix "dir_" -Value $relativeDirectoryPath -UsedIdentifiers $usedIdentifiers
            Add-Line ($indent + ('<Directory Id="{0}" Name="{1}">' -f $directoryId, (Escape-WixXml $directory.Name)))
            Write-DirectoryContent -CurrentPath $directory.FullName -IndentLevel ($IndentLevel + 1)
            Add-Line ($indent + '</Directory>')
        }
    }

    Add-Line '<?xml version="1.0" encoding="utf-8"?>'
    Add-Line '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
    Add-Line '  <Fragment>'
    Add-Line '    <DirectoryRef Id="INSTALLFOLDER">'
    Write-DirectoryContent -CurrentPath $sourceDirectory.FullName -IndentLevel 3
    Add-Line '    </DirectoryRef>'
    Add-Line '  </Fragment>'
    Add-Line '  <Fragment>'
    Add-Line '    <ComponentGroup Id="AppFiles">'
    foreach ($componentId in $componentIds) {
        Add-Line ('      <ComponentRef Id="{0}" />' -f $componentId)
    }
    Add-Line '    </ComponentGroup>'
    Add-Line '  </Fragment>'
    Add-Line '</Wix>'

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    Set-Content -Path $OutputPath -Value $builder.ToString() -Encoding UTF8
}

function Invoke-DotnetBuildMsi {
    param(
        [string]$ProjectPath,
        [string]$ProductVersion,
        [string]$GeneratedWixSource,
        [string]$OutputDirectory
    )

    $restoreArgs = @(
        "restore",
        $ProjectPath,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false"
    )

    & dotnet @restoreArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $ProjectPath"
    }

    $buildArgs = @(
        "build",
        $ProjectPath,
        "-c",
        $Configuration,
        "-p:ProductVersion=$ProductVersion",
        "-p:GeneratedWixSource=$GeneratedWixSource",
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false",
        "-o",
        $OutputDirectory
    )

    & dotnet @buildArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath"
    }

    $msi = Get-ChildItem -Path $OutputDirectory -Filter "*.msi" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $msi) {
        throw "Built MSI package was not found under $OutputDirectory"
    }

    return $msi.FullName
}

function Invoke-DotnetPublishSingleFile {
    param(
        [string]$ProjectPath,
        [string]$PublishOutput,
        [string]$ExecutableName
    )

    $restoreArgs = @(
        "restore",
        $ProjectPath,
        "-r",
        $Runtime,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false"
    )

    & dotnet @restoreArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $ProjectPath"
    }

    $publishArgs = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "-r",
        $Runtime,
        "--self-contained",
        "true",
        "-p:PublishSingleFile=true",
        "-p:EnableCompressionInSingleFile=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false",
        "-o",
        $PublishOutput
    )

    & dotnet @publishArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }

    $executablePath = Join-Path $PublishOutput $ExecutableName
    if (-not (Test-Path $executablePath)) {
        throw "Published single-file executable was not found at $executablePath"
    }

    return $executablePath
}

function Get-FileSizeSummary {
    param(
        [string]$FilePath
    )

    $file = Get-Item -Path $FilePath -ErrorAction Stop
    return [pscustomobject]@{
        Path = $file.FullName
        Bytes = [long]$file.Length
        MB = [math]::Round($file.Length / 1MB, 1)
    }
}

function Assert-WithinGitHubReleaseLimit {
    param(
        [pscustomobject]$FileSummary,
        [long]$LimitBytes
    )

    if ($FileSummary.Bytes -gt $LimitBytes) {
        $limitMB = [math]::Round($LimitBytes / 1MB, 1)
        throw "Main release ZIP exceeds GitHub's $limitMB MB asset limit. Built size: $($FileSummary.MB) MB. Remove bundled models or reduce packaged assets before publishing."
    }
}

$signingConfiguration = Resolve-CodeSigningConfiguration `
    -ExplicitThumbprint $CodeSigningCertificateThumbprint `
    -ExplicitStorePath $CodeSigningCertificateStorePath `
    -ExplicitTimestampUrl $CodeSigningTimestampUrl

Reset-PackageOutputDirectory -DirectoryPath $packagePath
Remove-Item -Recurse -Force $installerTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $msiTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $msiStagingPath -ErrorAction SilentlyContinue

& $publishScript -Configuration $Configuration -Runtime $Runtime -OutputRoot ".artifacts\publish\$Runtime" -FrameworkDependent:$FrameworkDependent

if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed while building the installer bundle."
}

$installerPublishedExecutable = Invoke-DotnetPublishSingleFile `
    -ProjectPath $installerProjectPath `
    -PublishOutput $installerTemp `
    -ExecutableName "MeetingRecorderInstaller.exe"

Copy-MsiPayload -SourceRoot $publishedAppPath -DestinationRoot $msiStagingPath
Write-MsiHarvestAuthoring -SourceRoot $msiStagingPath -OutputPath $msiGeneratedAuthoringPath
$installerMsiBuiltPath = Invoke-DotnetBuildMsi `
    -ProjectPath $msiProjectPath `
    -ProductVersion (([xml](Get-Content -Path (Join-Path $repoRoot "Directory.Build.props"))).Project.PropertyGroup.Version.Trim()) `
    -GeneratedWixSource $msiGeneratedAuthoringPath `
    -OutputDirectory $msiTemp

Sign-ArtifactFiles `
    -SigningConfiguration $signingConfiguration `
    -FilePaths @(
        (Join-Path $publishedAppPath "MeetingRecorder.App.exe"),
        (Join-Path $publishedAppPath "MeetingRecorder.ProcessingWorker.exe")
    )

$publishedPowerShellScripts = Get-ChildItem -Path $publishedAppPath -Filter "*.ps1" -File -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
Sign-ArtifactFiles -SigningConfiguration $signingConfiguration -FilePaths $publishedPowerShellScripts
Sign-ArtifactFiles -SigningConfiguration $signingConfiguration -FilePaths @($installerPublishedExecutable)

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
Copy-Item -Path (Join-Path $publishedAppPath "*") -Destination $bundleRoot -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Run-MeetingRecorder.cmd") -Destination (Join-Path $stagingPath "Run-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.ps1") -Destination (Join-Path $stagingPath "Install-LatestFromGitHub.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.cmd") -Destination (Join-Path $stagingPath "Install-LatestFromGitHub.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-MeetingRecorder.ps1") -Destination (Join-Path $stagingPath "Install-MeetingRecorder.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-MeetingRecorder.cmd") -Destination (Join-Path $stagingPath "Install-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $repoRoot "SETUP.md") -Destination (Join-Path $stagingPath "SETUP.md") -Force

$stagedPowerShellScripts = Get-ChildItem -Path $stagingPath -Filter "*.ps1" -File -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
Sign-ArtifactFiles -SigningConfiguration $signingConfiguration -FilePaths $stagedPowerShellScripts

$zipPath = New-ZipAsset -SourceRoot $stagingPath -PreferredPath $zipPath
$installerExecutablePath = Publish-StableAsset -SourcePath $installerPublishedExecutable -PreferredPath $installerExecutablePath
$installerMsiPath = Publish-StableAsset -SourcePath $installerMsiBuiltPath -PreferredPath $installerMsiPath
$bootstrapCommandPath = Publish-StableAsset -SourcePath (Join-Path $PSScriptRoot "Install-LatestFromGitHub.cmd") -PreferredPath $bootstrapCommandPath
$bootstrapScriptPath = Publish-StableAsset -SourcePath (Join-Path $PSScriptRoot "Install-LatestFromGitHub.ps1") -PreferredPath $bootstrapScriptPath

Sign-ArtifactFiles -SigningConfiguration $signingConfiguration -FilePaths @($bootstrapScriptPath)

$mainZipSummary = Get-FileSizeSummary -FilePath $zipPath
$installerSummary = Get-FileSizeSummary -FilePath $installerExecutablePath
$installerMsiSummary = Get-FileSizeSummary -FilePath $installerMsiPath
$bootstrapCommandSummary = Get-FileSizeSummary -FilePath $bootstrapCommandPath
$bootstrapScriptSummary = Get-FileSizeSummary -FilePath $bootstrapScriptPath

Assert-WithinGitHubReleaseLimit -FileSummary $mainZipSummary -LimitBytes $gitHubReleaseLimitBytes

Write-Host "Main installer bundle assembled at $($mainZipSummary.Path) [$($mainZipSummary.MB) MB]"
Write-Host "Installer executable assembled at $($installerSummary.Path) [$($installerSummary.MB) MB]"
Write-Host "Installer MSI assembled at $($installerMsiSummary.Path) [$($installerMsiSummary.MB) MB]"
Write-Host "Bootstrap command assembled at $($bootstrapCommandSummary.Path) [$($bootstrapCommandSummary.MB) MB]"
Write-Host "Bootstrap PowerShell script assembled at $($bootstrapScriptSummary.Path) [$($bootstrapScriptSummary.MB) MB]"

if ($null -ne $signingConfiguration) {
    Write-Host ("Artifacts were code signed with certificate thumbprint {0} from {1}" -f $signingConfiguration.Thumbprint, $signingConfiguration.StorePath)
}

if (-not $KeepStaging.IsPresent) {
    Remove-Item -Recurse -Force $stagingPath -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $msiStagingPath -ErrorAction SilentlyContinue
}

Remove-Item -Recurse -Force $installerTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $msiTemp -ErrorAction SilentlyContinue
