param(
    [Parameter(Mandatory)] [string]$PythonPath
)

$ErrorActionPreference = 'Stop'
$resolvedPython = (Resolve-Path -LiteralPath $PythonPath).Path
if (-not (Test-Path -LiteralPath $resolvedPython -PathType Leaf)) {
    throw 'A Python executable is required.'
}
if ($resolvedPython -like '*\\WindowsApps\\*') {
    throw 'The Microsoft Store Python alias is not a converter host. Supply a real isolated Python executable.'
}

$version = & $resolvedPython --version 2>&1
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^Python 3\.11\.') {
    throw "Converter host must be Python 3.11.x. Reported: $version"
}

$pipOutput = @(& $resolvedPython -m pip --version 2>&1)
$pipExitCode = $LASTEXITCODE
if ($pipExitCode -ne 0) {
    throw 'Converter host has no usable pip. Do not run a converter or download a model until a pinned package source is available.'
}
$pipVersion = [string]($pipOutput | Select-Object -First 1)

$pipMatch = [regex]::Match($pipVersion, '^pip\s+(?<version>[^\s]+)\s+')
if (-not $pipMatch.Success) {
    throw 'Converter host returned an unreadable pip version.'
}

Write-Output "Converter host ready: $version; pip $($pipMatch.Groups['version'].Value)"
