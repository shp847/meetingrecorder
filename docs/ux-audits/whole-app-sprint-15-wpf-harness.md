# Whole-App Sprint 15: Test-Only WPF Harness

## Purpose

`WpfRenderedShellHarnessTests` renders the real `MainWindow` on a dedicated
STA thread. It never runs `MeetingRecorder.App.App`, so it does not acquire the
single-instance mutex or touch the installed application process.

The harness scopes `AppDataPaths` to a fresh disposable `%TEMP%` directory and
creates only default, synthetic configuration and empty output folders there.
It does not open capture devices, invoke external providers, load a user
profile, or use meeting artifacts.

## Run

```powershell
$env:DOTNET_ROOT='C:\Users\psharm04\.dotnet'
$env:PATH='C:\Users\psharm04\.dotnet;' + $env:PATH
dotnet test .\tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj -p:NuGetAudit=false --filter FullyQualifiedName~WpfRenderedShellHarnessTests
```

The test writes its redacted screenshot, automation trace, and keyboard trace
under `%TEMP%\MeetingRecorderWpfHarness`. Test output identifies failures;
inspect the newest directory after a passing run.

## Current Coverage And Limit

The first evidence row is Home at a 1280x800, 100% WPF test host. It verifies
the rendered shell, `Start recording` accessible name, complete setup-reason
text, primary navigation automation peer, and keyboard focus movement. It is
not a substitute for the remaining journey, DPI, theme, packaged, Narrator, or
manual visual review matrix required by Sprint 15.
