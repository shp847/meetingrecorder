param(
    [string]$AppRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$normalizedAppRoot = if ($null -ne $AppRoot) {
    $AppRoot.Trim()
}
else {
    $PSScriptRoot
}
$normalizedAppRoot = $normalizedAppRoot.Trim('"')

if ([string]::IsNullOrWhiteSpace($normalizedAppRoot)) {
    $normalizedAppRoot = $PSScriptRoot
}

$resolvedAppRoot = [System.IO.Path]::GetFullPath($normalizedAppRoot)
$setupPath = Join-Path $resolvedAppRoot "SETUP.md"
$dotnetRuntimeUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
$vcRedistUrl = "https://aka.ms/vc14/vc_redist.x64.exe"

Write-Host "Opening dependency setup guidance..."

if (Test-Path $setupPath) {
    Start-Process $setupPath
}

Start-Process $dotnetRuntimeUrl
Start-Process $vcRedistUrl

Write-Host "Opened:"
Write-Host " - SETUP.md"
Write-Host " - .NET 8 download page"
Write-Host " - Visual C++ x64 redistributable"
