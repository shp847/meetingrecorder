using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SecondaryLaunchRecoveryCoordinatorTests
{
    [Fact]
    public void Recover_Returns_ActivatedExistingInstance_When_Activation_Is_Acknowledged_Immediately()
    {
        var coordinator = new SecondaryLaunchRecoveryCoordinator(
            trySignalAndWaitForAcknowledgement: _ => true,
            tryBringExistingWindowToFront: (_, _, _) => false,
            waitForPrimaryInstanceRelease: _ => false,
            delay: _ => { });

        var result = coordinator.Recover(
            currentProcessId: 101,
            initialActivationTimeout: TimeSpan.FromSeconds(2),
            recoveryWindow: TimeSpan.FromSeconds(4),
            retryDelay: TimeSpan.FromMilliseconds(100));

        Assert.Equal(SecondaryLaunchRecoveryResult.ActivatedExistingInstance, result);
    }

    [Fact]
    public void Recover_Returns_PrimaryReleased_When_Previous_Instance_Exits_During_Recovery_Window()
    {
        var releaseAttempt = 0;
        var coordinator = new SecondaryLaunchRecoveryCoordinator(
            trySignalAndWaitForAcknowledgement: _ => false,
            tryBringExistingWindowToFront: (_, _, _) => false,
            waitForPrimaryInstanceRelease: _ =>
            {
                releaseAttempt++;
                return releaseAttempt >= 3;
            },
            delay: _ => { });

        var result = coordinator.Recover(
            currentProcessId: 101,
            initialActivationTimeout: TimeSpan.FromMilliseconds(10),
            recoveryWindow: TimeSpan.FromSeconds(1),
            retryDelay: TimeSpan.FromMilliseconds(10));

        Assert.Equal(SecondaryLaunchRecoveryResult.PrimaryInstanceReleased, result);
    }

    [Fact]
    public void Recover_Returns_ActivatedExistingInstance_When_Window_Appears_During_Recovery_Window()
    {
        var windowAttempt = 0;
        var coordinator = new SecondaryLaunchRecoveryCoordinator(
            trySignalAndWaitForAcknowledgement: _ => false,
            tryBringExistingWindowToFront: (_, _, _) =>
            {
                windowAttempt++;
                return windowAttempt >= 2;
            },
            waitForPrimaryInstanceRelease: _ => false,
            delay: _ => { });

        var result = coordinator.Recover(
            currentProcessId: 101,
            initialActivationTimeout: TimeSpan.FromMilliseconds(10),
            recoveryWindow: TimeSpan.FromSeconds(1),
            retryDelay: TimeSpan.FromMilliseconds(10));

        Assert.Equal(SecondaryLaunchRecoveryResult.ActivatedExistingInstance, result);
    }

    [Fact]
    public void Recover_Returns_TimedOut_When_No_Recovery_Path_Succeeds()
    {
        var coordinator = new SecondaryLaunchRecoveryCoordinator(
            trySignalAndWaitForAcknowledgement: _ => false,
            tryBringExistingWindowToFront: (_, _, _) => false,
            waitForPrimaryInstanceRelease: _ => false,
            delay: _ => { });

        var result = coordinator.Recover(
            currentProcessId: 101,
            initialActivationTimeout: TimeSpan.FromMilliseconds(10),
            recoveryWindow: TimeSpan.FromMilliseconds(20),
            retryDelay: TimeSpan.FromMilliseconds(10));

        Assert.Equal(SecondaryLaunchRecoveryResult.TimedOut, result);
    }

    [Fact]
    public void Recover_Returns_PrimaryReleased_When_Primary_Exits_During_Final_Grace_Window()
    {
        var releaseAttempt = 0;
        var coordinator = new SecondaryLaunchRecoveryCoordinator(
            trySignalAndWaitForAcknowledgement: _ => false,
            tryBringExistingWindowToFront: (_, _, _) => false,
            waitForPrimaryInstanceRelease: timeout =>
            {
                releaseAttempt++;
                return timeout > TimeSpan.Zero && releaseAttempt >= 2;
            },
            delay: _ => { });

        var result = coordinator.Recover(
            currentProcessId: 101,
            initialActivationTimeout: TimeSpan.FromMilliseconds(10),
            recoveryWindow: TimeSpan.FromMilliseconds(20),
            retryDelay: TimeSpan.FromMilliseconds(10),
            finalPrimaryReleaseWait: TimeSpan.FromMilliseconds(50));

        Assert.Equal(SecondaryLaunchRecoveryResult.PrimaryInstanceReleased, result);
    }
}
