using MeetingRecorder.Core.Services;
using MeetingRecorder.ProcessingWorker;

namespace MeetingRecorder.Core.Tests;

public sealed class SessionProcessingStageTests
{
    [Theory]
    [InlineData("full", SessionProcessingStage.FullPass)]
    [InlineData("full-pass", SessionProcessingStage.FullPass)]
    [InlineData("transcript", SessionProcessingStage.Transcript)]
    [InlineData("diarization", SessionProcessingStage.Diarization)]
    [InlineData("summary", SessionProcessingStage.Summary)]
    public void TryParse_Accepts_Only_Explicit_Supported_Stages(string value, SessionProcessingStage expected)
    {
        Assert.True(SessionProcessingStageParser.TryParse(value, out var actual));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("all")]
    [InlineData("--stage transcript")]
    public void TryParse_Rejects_Unknown_Or_Compound_Stage_Values(string? value)
    {
        Assert.False(SessionProcessingStageParser.TryParse(value, out var stage));

        Assert.Equal(SessionProcessingStage.FullPass, stage);
    }

    [Fact]
    public void WorkerArguments_Require_A_Complete_Staged_Work_Lease()
    {
        var parsed = Program.TryParseArguments(
            [
                "--manifest", "C:\\work\\session\\manifest.json",
                "--stage", "transcript",
                "--work-id", "00000000-0000-0000-0000-000000000001",
                "--work-revision", "revision-1",
                "--lease-token", "lease-1",
            ],
            out var manifestPath,
            out var configPath,
            out var stage,
            out var lease,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("C:\\work\\session\\manifest.json", manifestPath);
        Assert.Null(configPath);
        Assert.Equal(SessionProcessingStage.Transcript, stage);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), lease!.WorkId);
        Assert.Equal("revision-1", lease.WorkRevision);
        Assert.Equal("lease-1", lease.LeaseToken);
    }

    [Theory]
    [InlineData("--work-id", "00000000-0000-0000-0000-000000000001")]
    [InlineData("--work-revision", "revision-1")]
    [InlineData("--lease-token", "lease-1")]
    public void WorkerArguments_Reject_A_Partial_Staged_Work_Lease(string option, string value)
    {
        var parsed = Program.TryParseArguments(
            ["--manifest", "C:\\work\\session\\manifest.json", option, value],
            out _,
            out _,
            out _,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("requires --work-id, --work-revision, and --lease-token together", error, StringComparison.Ordinal);
    }
}
