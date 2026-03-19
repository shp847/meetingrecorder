@echo off
setlocal
cd /d "%~dp0"

set "APP_ROOT="

if exist "MeetingRecorder.App.exe" (
    for %%I in ("%~dp0.") do set "APP_ROOT=%%~fI"
    goto launch
)

if exist "MeetingRecorder\MeetingRecorder.App.exe" (
    for %%I in ("%~dp0MeetingRecorder\.") do set "APP_ROOT=%%~fI"
    goto launch
)

echo Could not find MeetingRecorder.App.exe in the current folder or a .\MeetingRecorder subfolder.
exit /b 1

:launch
if exist "%APP_ROOT%\Check-Dependencies.ps1" (
    powershell -ExecutionPolicy Bypass -File "%APP_ROOT%\Check-Dependencies.ps1" -AppRoot "%APP_ROOT%"
    if errorlevel 1 (
        echo.
        echo Dependency check failed. Run Install-Dependencies.cmd or review SETUP.md in the app folder.
        pause
        exit /b 1
    )
)

start "" "%APP_ROOT%\MeetingRecorder.App.exe"
exit /b 0
