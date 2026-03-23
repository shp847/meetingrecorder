Option Explicit

Dim fileSystemObject
Dim shell
Dim scriptRoot
Dim appPath
Dim localAppDataRoot
Dim meetingRecorderAppDataRoot
Dim relaunchMarkerPath
Dim relaunchMarkerFile

Set fileSystemObject = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

scriptRoot = fileSystemObject.GetParentFolderName(WScript.ScriptFullName)
appPath = fileSystemObject.BuildPath(scriptRoot, "MeetingRecorder.App.exe")
localAppDataRoot = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%")
meetingRecorderAppDataRoot = fileSystemObject.BuildPath(localAppDataRoot, "MeetingRecorder")
relaunchMarkerPath = fileSystemObject.BuildPath(meetingRecorderAppDataRoot, "installer-relaunch.flag")

If Not fileSystemObject.FileExists(appPath) Then
    MsgBox "Meeting Recorder cannot launch because MeetingRecorder.App.exe is missing from '" & scriptRoot & "'.", vbExclamation, "Meeting Recorder"
    WScript.Quit 1
End If

If Not fileSystemObject.FolderExists(meetingRecorderAppDataRoot) Then
    fileSystemObject.CreateFolder(meetingRecorderAppDataRoot)
End If

Set relaunchMarkerFile = fileSystemObject.CreateTextFile(relaunchMarkerPath, True)
relaunchMarkerFile.WriteLine CStr(Now)
relaunchMarkerFile.Close

shell.CurrentDirectory = scriptRoot
shell.Run Chr(34) & appPath & Chr(34), 1, False
