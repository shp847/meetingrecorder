using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AutoStartRegistrationServiceTests
{
    [Fact]
    public void SyncRegistration_Writes_Quoted_Executable_Path_When_Enabled()
    {
        var store = new FakeAutoStartRegistrationStore();
        var service = new AutoStartRegistrationService(store);

        service.SyncRegistration(
            enabled: true,
            executablePath: @"C:\Portable Apps\MeetingRecorder\MeetingRecorder.App.exe");

        Assert.Equal(
            "\"C:\\Portable Apps\\MeetingRecorder\\MeetingRecorder.App.exe\"",
            store.StoredCommand);
    }

    [Fact]
    public void SyncRegistration_Removes_Entry_When_Disabled()
    {
        var store = new FakeAutoStartRegistrationStore
        {
            StoredCommand = "\"C:\\Portable Apps\\MeetingRecorder\\MeetingRecorder.App.exe\"",
        };
        var service = new AutoStartRegistrationService(store);

        service.SyncRegistration(
            enabled: false,
            executablePath: @"C:\Portable Apps\MeetingRecorder\MeetingRecorder.App.exe");

        Assert.Null(store.StoredCommand);
        Assert.True(store.RemoveWasCalled);
    }

    private sealed class FakeAutoStartRegistrationStore : IAutoStartRegistrationStore
    {
        public string? StoredCommand { get; set; }

        public bool RemoveWasCalled { get; private set; }

        public string? ReadCommand(string entryName)
        {
            Assert.Equal("MeetingRecorder", entryName);
            return StoredCommand;
        }

        public void RemoveCommand(string entryName)
        {
            Assert.Equal("MeetingRecorder", entryName);
            RemoveWasCalled = true;
            StoredCommand = null;
        }

        public void WriteCommand(string entryName, string command)
        {
            Assert.Equal("MeetingRecorder", entryName);
            StoredCommand = command;
        }
    }
}
