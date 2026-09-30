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

When the host cuts VSTest off before it returns a result, build and run the
test-only probe directly. This is the reviewed fallback used for the Sprint 0
rendered evidence; it has the same isolated profile and fixture safety
properties as the harness tests.

```powershell
$env:DOTNET_ROOT='C:\Users\psharm04\.dotnet'
$env:PATH='C:\Users\psharm04\.dotnet;' + $env:PATH
dotnet build .\tests\MeetingRecorder.WpfRenderProbe\MeetingRecorder.WpfRenderProbe.csproj --no-restore -p:NuGetAudit=false -p:UseAppHost=false
dotnet .\tests\MeetingRecorder.WpfRenderProbe\bin\Debug\net8.0-windows\MeetingRecorder.WpfRenderProbe.dll processing 125
```

The final argument is `100` or `125`; valid states are `empty-healthy`,
`setup-blocked`, `processing`, `selection-active`, and
`cleanup-recommendation`. The probe writes artifact paths and screenshot
SHA-256 values to standard output.

## Current Coverage And Limit

Sprint 0 now has reviewed redacted captures of all five fixtures at a 1280×800
logical viewport at 100% and 125%; see
`whole-app-sprint-0-rendered-evidence.md`. The harness verifies the rendered
shell, `Start recording` accessible name, complete setup-reason text, primary
navigation automation peer, and keyboard focus movement. It is not a substitute
for the remaining journey, smaller viewport, theme, packaged, Narrator, or
manual visual review matrix required by Sprint 15.
