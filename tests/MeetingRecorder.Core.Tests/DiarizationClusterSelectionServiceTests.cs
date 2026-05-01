using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationClusterSelectionServiceTests
{
    [Fact]
    public void SelectBestCandidate_Keeps_Default_When_It_Has_Multiple_Supported_Speakers()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(20)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40)),
            ]);
        var stricterCandidate = new DiarizationClusterCandidate(
            0.35f,
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(10)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)),
                new SpeakerTurn("speaker_02", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)),
            ]);

        var selection = service.SelectBestCandidate([defaultCandidate, stricterCandidate]);

        Assert.Same(defaultCandidate, selection.Candidate);
        Assert.Equal(2, selection.SupportedSpeakerCount);
    }

    [Fact]
    public void SelectBestCandidate_Uses_Stricter_Threshold_When_Default_Collapses_To_One_Speaker()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            [new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromMinutes(3))]);
        var stricterCandidate = new DiarizationClusterCandidate(
            0.35f,
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(80)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(160)),
            ]);

        var selection = service.SelectBestCandidate([defaultCandidate, stricterCandidate]);

        Assert.Same(stricterCandidate, selection.Candidate);
        Assert.Equal(2, selection.SupportedSpeakerCount);
        Assert.Contains("0.35", selection.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectBestCandidate_Ignores_Tiny_Split_Speakers()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            [new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromMinutes(3))]);
        var noisyCandidate = new DiarizationClusterCandidate(
            0.35f,
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromMinutes(3)),
                new SpeakerTurn("speaker_01", TimeSpan.FromMinutes(3), TimeSpan.FromMilliseconds(181000)),
            ]);

        var selection = service.SelectBestCandidate([defaultCandidate, noisyCandidate]);

        Assert.Same(defaultCandidate, selection.Candidate);
        Assert.Equal(1, selection.SupportedSpeakerCount);
    }
}
