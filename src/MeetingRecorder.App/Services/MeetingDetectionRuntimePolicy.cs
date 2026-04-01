namespace MeetingRecorder.App.Services;

internal static class MeetingDetectionRuntimePolicy
{
    public static bool ShouldRun(
        bool autoDetectEnabled,
        bool isRecording,
        bool activeSessionWasAutoStarted)
    {
        if (!autoDetectEnabled)
        {
            return false;
        }

        return true;
    }
}
