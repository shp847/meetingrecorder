using System.IO;

namespace MeetingRecorder.Core.Tests;

public sealed class CurrentMeetingMetadataSourceTests
{
    [Fact]
    public void Home_Optional_Metadata_Field_Edits_Are_Queued_For_Live_Save_During_Active_Recordings()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("CurrentMeetingProjectTextBox_OnTextChanged", source);
        Assert.Contains("CurrentMeetingKeyAttendeesTextBox_OnTextChanged", source);
        Assert.Contains("ScheduleCurrentMeetingOptionalMetadataSave();", source);
        Assert.Contains("private void ScheduleCurrentMeetingOptionalMetadataSave()", source);
        Assert.Contains("private async Task PersistCurrentMeetingOptionalMetadataAsync", source);
        Assert.Contains("_recordingCoordinator.UpdateActiveSessionMetadataAsync(", source);
    }

    [Fact]
    public void Home_Optional_Metadata_Fields_Share_The_Same_Enabled_State_As_The_Title_Field_After_Recording_State_Changes()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("CurrentMeetingTitleTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;", source);
        Assert.Contains("CurrentMeetingProjectTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;", source);
        Assert.Contains("CurrentMeetingKeyAttendeesTextBox.IsEnabled = _recordingCoordinator.IsRecording && !_isUpdateInstallInProgress && !_isRecordingTransitionInProgress;", source);
    }

    [Fact]
    public void Current_Meeting_Metadata_And_Stop_Flows_Attempt_Deferred_Reclassification_Before_Persisting_A_Manual_Session()
    {
        var sourcePath = GetPath("src", "MeetingRecorder.App", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private async Task<bool> TryApplyDeferredMeetingReclassificationAsync", source, StringComparison.Ordinal);
        Assert.Contains("await TryApplyDeferredMeetingReclassificationAsync(activeSession, cancellationToken);", source, StringComparison.Ordinal);
    }

    private static string GetPath(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(current, Path.Combine(parts));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException($"Unable to locate '{Path.Combine(parts)}' from '{AppContext.BaseDirectory}'.");
    }
}
