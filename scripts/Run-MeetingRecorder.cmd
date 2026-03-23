@echo off
setlocal
cd /d "%~dp0"

set "APP_ROOT="
set /a WAIT_SECONDS_REMAINING=15
set "CURRENT_ROOT="
set "NESTED_ROOT="

for %%I in ("%~dp0.") do set "CURRENT_ROOT=%%~fI"
for %%I in ("%~dp0MeetingRecorder\.") do set "NESTED_ROOT=%%~fI"

if exist "MeetingRecorder.App.exe" (
    set "APP_ROOT=%CURRENT_ROOT%"
    goto launch
)

if exist "MeetingRecorder\MeetingRecorder.App.exe" (
    set "APP_ROOT=%NESTED_ROOT%"
    goto launch
)

if exist "MeetingRecorder\." (
    set "APP_ROOT=%NESTED_ROOT%"
    goto waitForAppHost
)

if exist "MeetingRecorder.product.json" (
    set "APP_ROOT=%CURRENT_ROOT%"
    goto waitForAppHost
)

if exist "Check-Dependencies.ps1" (
    set "APP_ROOT=%CURRENT_ROOT%"
    goto waitForAppHost
)

echo Could not find MeetingRecorder.App.exe in the current folder or a .\MeetingRecorder subfolder.
pause
exit /b 1

:waitForAppHost
if exist "%APP_ROOT%\MeetingRecorder.App.exe" (
    goto launch
)

if %WAIT_SECONDS_REMAINING% LEQ 0 (
    goto missingAppHost
)

timeout /t 1 /nobreak >nul
set /a WAIT_SECONDS_REMAINING-=1
goto waitForAppHost

:missingAppHost
echo.
echo Meeting Recorder cannot start because MeetingRecorder.App.exe is still missing from "%APP_ROOT%".
echo Repair or reinstall the app so the launcher can start the packaged Windows apphost.
pause
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
