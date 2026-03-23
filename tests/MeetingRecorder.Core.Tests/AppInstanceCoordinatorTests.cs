using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppInstanceCoordinatorTests
{
    [Fact]
    public void TryAcquirePrimaryInstance_Allows_Only_One_Owner_Per_Name()
    {
        var instanceName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");

        using var first = new AppInstanceCoordinator(instanceName);
        using var second = new AppInstanceCoordinator(instanceName);

        Assert.True(first.TryAcquirePrimaryInstance());
        Assert.False(second.TryAcquirePrimaryInstance());
    }

    [Fact]
    public void TryAcquirePrimaryInstance_Succeeds_After_Previous_Owner_Disposes()
    {
        var instanceName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");

        using (var first = new AppInstanceCoordinator(instanceName))
        {
            Assert.True(first.TryAcquirePrimaryInstance());
        }

        using var second = new AppInstanceCoordinator(instanceName);
        Assert.True(second.TryAcquirePrimaryInstance());
    }

    [Fact]
    public async Task WaitForPrimaryInstanceRelease_Succeeds_When_Previous_Owner_Exits_Soon()
    {
        var instanceName = "MeetingRecorder.Tests." + Guid.NewGuid().ToString("N");

        var first = new AppInstanceCoordinator(instanceName);
        var second = new AppInstanceCoordinator(instanceName);

        try
        {
            Assert.True(first.TryAcquirePrimaryInstance());
            Assert.False(second.TryAcquirePrimaryInstance());

            var releaseTask = Task.Run(async () =>
            {
                await Task.Delay(100);
                first.Dispose();
            });

            Assert.True(second.WaitForPrimaryInstanceRelease(TimeSpan.FromSeconds(2)));
            await releaseTask;
        }
        finally
        {
            second.Dispose();
        }
    }
}
