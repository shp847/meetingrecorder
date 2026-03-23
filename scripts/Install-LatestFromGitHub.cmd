@echo off
setlocal EnableDelayedExpansion

set "MEETINGRECORDER_SKIP_SCRIPT_PAUSE=1"
set "LOCAL_SCRIPT=%~dp0Install-LatestFromGitHub.ps1"
set "TEMP_SCRIPT=%TEMP%\Install-LatestFromGitHub.ps1"
set "SCRIPT_URL=https://github.com/shp847/meetingrecorder/releases/latest/download/Install-LatestFromGitHub.ps1"

echo [1/3] Preparing Meeting Recorder installer...

if exist "%LOCAL_SCRIPT%" (
    echo [2/3] Launching bootstrap installer...
    powershell -ExecutionPolicy Bypass -File "%LOCAL_SCRIPT%" %*
    set "EXIT_CODE=!errorlevel!"
    if not "!EXIT_CODE!"=="0" (
        echo.
        echo The installer reported an error. Review the messages above, then press any key to close this window.
        pause >nul
    )
    exit /b !EXIT_CODE!
)

echo [2/3] Downloading bootstrap helper...
where curl.exe >nul 2>nul
if not errorlevel 1 (
    curl.exe -L --fail --show-error --silent --ssl-no-revoke --output "%TEMP_SCRIPT%" "%SCRIPT_URL%"
)

if not exist "%TEMP_SCRIPT%" (
    powershell -ExecutionPolicy Bypass -NoProfile -Command ^
        "$ProgressPreference='SilentlyContinue';" ^
        "try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {};" ^
        "try { [Net.ServicePointManager]::CheckCertificateRevocationList = \$false } catch {};" ^
        "Invoke-WebRequest -UseBasicParsing -Headers @{ 'User-Agent' = 'MeetingRecorderBootstrap/0.1' } -Uri '%SCRIPT_URL%' -OutFile '%TEMP_SCRIPT%'"
)

if not exist "%TEMP_SCRIPT%" (
    echo Failed to download Install-LatestFromGitHub.ps1 from GitHub Releases.
    echo If GitHub script downloads are blocked, use the full MeetingRecorder-vX.Y-win-x64.zip installer instead.
    echo Press any key to close this window.
    pause >nul
    exit /b 1
)

echo [3/3] Starting the full installer...
powershell -ExecutionPolicy Bypass -File "%TEMP_SCRIPT%" %*
set "EXIT_CODE=!errorlevel!"
del /q "%TEMP_SCRIPT%" >nul 2>nul
if not "!EXIT_CODE!"=="0" (
    echo.
    echo The installer reported an error. Review the messages above, then press any key to close this window.
    pause >nul
)
exit /b !EXIT_CODE!
