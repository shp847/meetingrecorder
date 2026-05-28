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
        Assert.True(selection.IsAutomaticSpeakerCountSupported);
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
        Assert.True(selection.IsAutomaticSpeakerCountSupported);
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
        Assert.False(selection.IsAutomaticSpeakerCountSupported);
    }

    [Fact]
    public void SelectBestCandidate_Rejects_OverSegmented_Default_And_Uses_Valid_Candidate()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            BuildSpeakerTurns(29));
        var stricterCandidate = new DiarizationClusterCandidate(
            0.65f,
            BuildSpeakerTurns(4));

        var selection = service.SelectBestCandidate([defaultCandidate, stricterCandidate]);

        Assert.Same(stricterCandidate, selection.Candidate);
        Assert.Equal(4, selection.SupportedSpeakerCount);
        Assert.True(selection.IsAutomaticSpeakerCountSupported);
        Assert.Contains("0.65", selection.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectBestCandidate_Marks_OverSegmented_Result_As_Unsupported_When_No_Valid_Candidate_Exists()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            BuildSpeakerTurns(29));
        var stricterCandidate = new DiarizationClusterCandidate(
            0.65f,
            BuildSpeakerTurns(20));

        var selection = service.SelectBestCandidate([defaultCandidate, stricterCandidate]);

        Assert.Same(defaultCandidate, selection.Candidate);
        Assert.Equal(29, selection.SupportedSpeakerCount);
        Assert.False(selection.IsAutomaticSpeakerCountSupported);
        Assert.Contains("outside the supported automatic range", selection.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectBestCandidate_Continues_Past_High_Count_Fallback_For_OverSegmented_Default()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            BuildSpeakerTurns(29));
        var highCountFallback = new DiarizationClusterCandidate(
            0.75f,
            BuildSpeakerTurns(14));
        var compactFallback = new DiarizationClusterCandidate(
            0.8f,
            BuildSpeakerTurns(4));

        var selection = service.SelectBestCandidate([defaultCandidate, highCountFallback, compactFallback]);

        Assert.Same(compactFallback, selection.Candidate);
        Assert.Equal(4, selection.SupportedSpeakerCount);
        Assert.True(selection.IsAutomaticSpeakerCountSupported);
        Assert.Contains("0.80", selection.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectBestCandidate_Keeps_High_Count_Fallback_Unsupported_When_No_Preferred_Candidate_Exists()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            BuildSpeakerTurns(29));
        var highCountFallback = new DiarizationClusterCandidate(
            0.75f,
            BuildSpeakerTurns(14));

        var selection = service.SelectBestCandidate([defaultCandidate, highCountFallback]);

        Assert.Same(defaultCandidate, selection.Candidate);
        Assert.Equal(29, selection.SupportedSpeakerCount);
        Assert.False(selection.IsAutomaticSpeakerCountSupported);
        Assert.Contains("outside the supported automatic range", selection.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectBestCandidate_Filters_Tiny_Fragment_Speakers_From_Selected_Candidate()
    {
        var service = new DiarizationClusterSelectionService();
        var defaultCandidate = new DiarizationClusterCandidate(
            0.5f,
            BuildSpeakerTurns(29));
        var compactFallback = new DiarizationClusterCandidate(
            0.8f,
            [
                new SpeakerTurn("speaker_00", TimeSpan.Zero, TimeSpan.FromSeconds(120)),
                new SpeakerTurn("speaker_01", TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240)),
                new SpeakerTurn("speaker_02", TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(360)),
                new SpeakerTurn("speaker_03", TimeSpan.FromSeconds(360), TimeSpan.FromSeconds(480)),
                new SpeakerTurn("speaker_99", TimeSpan.FromSeconds(480), TimeSpan.FromMilliseconds(480500)),
            ]);

        var selection = service.SelectBestCandidate([defaultCandidate, compactFallback]);

        Assert.NotSame(compactFallback, selection.Candidate);
        Assert.Equal(4, selection.SupportedSpeakerCount);
        Assert.Equal(
            ["speaker_00", "speaker_01", "speaker_02", "speaker_03"],
            selection.Candidate.SpeakerTurns.Select(turn => turn.SpeakerId).Distinct(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void IsAutomaticSpeakerCountSupported_Enforces_Automatic_Range(int speakerCount, bool expected)
    {
        Assert.Equal(expected, DiarizationClusterSelectionService.IsAutomaticSpeakerCountSupported(speakerCount));
    }

    private static IReadOnlyList<SpeakerTurn> BuildSpeakerTurns(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new SpeakerTurn(
                $"speaker_{index:00}",
                TimeSpan.FromSeconds(index * 20),
                TimeSpan.FromSeconds((index + 1) * 20)))
            .ToArray();
    }
}
