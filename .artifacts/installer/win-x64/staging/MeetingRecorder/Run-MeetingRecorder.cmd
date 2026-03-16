@echo off
setlocal
cd /d "%~dp0"

if exist "MeetingRecorder.App.exe" (
    start "" "MeetingRecorder.App.exe"
    exit /b 0
)

if exist "MeetingRecorder\MeetingRecorder.App.exe" (
    pushd "MeetingRecorder"
    start "" "MeetingRecorder.App.exe"
    popd
    exit /b 0
)

echo Could not find MeetingRecorder.App.exe in the current folder or a .\MeetingRecorder subfolder.
exit /b 1
