using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ExistingAppWindowActivatorTests
{
    [Fact]
    public void FindCandidateForActivation_Ignores_Current_Process_And_Zero_Window_Handles()
    {
        var candidates = new[]
        {
            new AppWindowCandidate(101, "MeetingRecorder.App", (nint)1, "Meeting Recorder v0.3"),
            new AppWindowCandidate(202, "MeetingRecorder.App", nint.Zero, "Meeting Recorder v0.3"),
            new AppWindowCandidate(303, "MeetingRecorder.App", (nint)7, "Meeting Recorder v0.3")
        };

        var candidate = ExistingAppWindowActivator.FindCandidateForActivation(candidates, currentProcessId: 101);

        Assert.NotNull(candidate);
        Assert.Equal(303, candidate.Value.ProcessId);
    }

    [Fact]
    public void TryBringExistingWindowToFront_Retries_Until_A_Window_Appears()
    {
        var attempt = 0;
        var activator = new ExistingAppWindowActivator(
            enumerateCandidates: () =>
            {
                attempt++;
                return attempt switch
                {
                    1 => Array.Empty<AppWindowCandidate>(),
                    2 => new[] { new AppWindowCandidate(404, "MeetingRecorder.App", (nint)9, "Meeting Recorder") },
                    _ => Array.Empty<AppWindowCandidate>()
                };
            },
            activateWindow: handle => handle == (nint)9,
            delay: _ => { });

        var activated = activator.TryBringExistingWindowToFront(
            currentProcessId: 101,
            maxAttempts: 3,
            retryDelay: TimeSpan.FromMilliseconds(1));

        Assert.True(activated);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public void TryBringExistingWindowToFront_Returns_False_When_No_Window_Can_Be_Activated()
    {
        var activator = new ExistingAppWindowActivator(
            enumerateCandidates: () => new[]
            {
                new AppWindowCandidate(404, "MeetingRecorder.App", (nint)9, "Meeting Recorder")
            },
            activateWindow: _ => false,
            delay: _ => { });

        var activated = activator.TryBringExistingWindowToFront(
            currentProcessId: 101,
            maxAttempts: 2,
            retryDelay: TimeSpan.Zero);

        Assert.False(activated);
    }

    [Fact]
    public void FindCandidateForActivation_Ignores_Unrelated_Window_Titles()
    {
        var candidates = new[]
        {
            new AppWindowCandidate(101, string.Empty, (nint)1, "Current instance"),
            new AppWindowCandidate(202, string.Empty, (nint)5, "Inbox - Outlook"),
            new AppWindowCandidate(303, string.Empty, (nint)7, "Meeting Recorder v0.3"),
        };

        var candidate = ExistingAppWindowActivator.FindCandidateForActivation(candidates, currentProcessId: 101);

        Assert.NotNull(candidate);
        Assert.Equal(303, candidate.Value.ProcessId);
    }
}
