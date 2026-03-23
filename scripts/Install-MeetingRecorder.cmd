@echo off
set "MEETINGRECORDER_SKIP_SCRIPT_PAUSE=1"
powershell -ExecutionPolicy Bypass -File "%~dp0Install-MeetingRecorder.ps1" %*
set "EXIT_CODE=%errorlevel%"
if not "%EXIT_CODE%"=="0" (
    echo.
    echo The installer reported an error. Review the messages above, then press any key to close this window.
    pause >nul
)
exit /b %EXIT_CODE%
