param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DotnetStep {
    param(
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host ("dotnet " + ($Arguments -join " "))
    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')"
    }
}

Invoke-DotnetStep -Arguments @("build-server", "shutdown")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "src\MeetingRecorder.Core\MeetingRecorder.Core.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "src\MeetingRecorder.App\MeetingRecorder.App.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "src\MeetingRecorder.ProcessingWorker\MeetingRecorder.ProcessingWorker.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "src\MeetingRecorder.Installer\MeetingRecorder.Installer.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build", (Join-Path $repoRoot "tests\MeetingRecorder.IntegrationTests\MeetingRecorder.IntegrationTests.csproj"), "-c", $Configuration, "-p:RestoreIgnoreFailedSources=true", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("test", (Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj"), "-c", $Configuration, "--no-build", "--no-restore", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("test", (Join-Path $repoRoot "tests\MeetingRecorder.IntegrationTests\MeetingRecorder.IntegrationTests.csproj"), "-c", $Configuration, "--no-build", "--no-restore", "-p:NuGetAudit=false", "-v", "minimal")
Invoke-DotnetStep -Arguments @("build-server", "shutdown")

Write-Host ""
Write-Host "Full clean build and test pass completed successfully."
