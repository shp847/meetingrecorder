@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul
if errorlevel 1 (
    echo Failed to switch to the upload script directory.
    exit /b 1
)

set "LOCAL_ENV_SCRIPT=%SCRIPT_DIR%Upload-ReleaseAssets.local.cmd"
if exist "%LOCAL_ENV_SCRIPT%" (
    call "%LOCAL_ENV_SCRIPT%"
    if errorlevel 1 (
        echo.
        echo The local GitHub token bootstrap reported an error.
        popd >nul
        exit /b 1
    )
)

powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Upload-ReleaseAssets.ps1" %*
set "EXIT_CODE=%errorlevel%"
popd >nul
if not "%EXIT_CODE%"=="0" (
    echo.
    echo The GitHub upload script reported an error. Review the messages above, then press any key to close this window.
    pause >nul
)
exit /b %EXIT_CODE%
