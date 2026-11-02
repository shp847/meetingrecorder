param(
    [Parameter(Mandatory)] [string]$BundleRoot,
    [Parameter(Mandatory)] [string]$OutputPath,
    [Parameter(Mandatory)] [string]$SourceRevision,
    [Parameter(Mandatory)] [string]$ConverterVersion,
    [Parameter(Mandatory)] [string]$ModelLicense,
    [Parameter(Mandatory)] [string]$TimestampContractVersion
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedBundle = (Resolve-Path -LiteralPath $BundleRoot).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if ($resolvedBundle.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedOutput.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'GPU feasibility inputs and manifests must remain outside source control.'
}
if (-not (Test-Path -LiteralPath (Join-Path $resolvedBundle 'tokenizer.json') -PathType Leaf)) {
    throw 'Bundle must contain tokenizer.json.'
}

$modelLayout = if (Test-Path -LiteralPath (Join-Path $resolvedBundle 'model.onnx') -PathType Leaf) {
    'single-model'
}
elseif ((Test-Path -LiteralPath (Join-Path $resolvedBundle 'encoder.onnx') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $resolvedBundle 'decoder.onnx') -PathType Leaf)) {
    'encoder-decoder'
}
else {
    throw 'Bundle must contain tokenizer.json plus model.onnx, or both encoder.onnx and decoder.onnx.'
}
$files = Get-ChildItem -LiteralPath $resolvedBundle -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        relativePath = $_.FullName.Substring($resolvedBundle.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace([IO.Path]::DirectorySeparatorChar, '/')
        lengthBytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    candidate = 'windows-ml-onnx-directml'
    modelLicense = $ModelLicense
    sourceRevision = $SourceRevision
    converterVersion = $ConverterVersion
    timestampContractVersion = $TimestampContractVersion
    modelLayout = $modelLayout
    files = @($files)
}
New-Item -ItemType Directory -Force -Path (Split-Path $resolvedOutput -Parent) | Out-Null
$temporary = "$resolvedOutput.tmp-$([guid]::NewGuid().ToString('N'))"
try { $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporary -Encoding UTF8; Move-Item -LiteralPath $temporary -Destination $resolvedOutput -Force }
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
Write-Output "GPU asset manifest written with $($files.Count) opaque file entries."
