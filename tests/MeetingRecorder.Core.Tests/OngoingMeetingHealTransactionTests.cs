using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class OngoingMeetingHealTransactionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_Merges_Once_Archives_Published_Artifacts_And_Preserves_Source()
    {
        var request = await CreateRequestAsync();
        var transaction = CreateTransaction();

        var result = await transaction.ExecuteAsync(request, Now);

        Assert.Equal(OngoingMeetingHealTransactionStatus.Healed, result.Status);
        Assert.NotNull(result.SurvivingStem);
        Assert.True(File.Exists(Path.Combine(request.AudioOutputDirectory, $"{result.SurvivingStem}.wav")));
        Assert.True(File.Exists(Path.Combine(request.TranscriptOutputDirectory, $"{result.SurvivingStem}.md")));
        Assert.True(File.Exists(Path.Combine(request.ArchiveDirectory, "merge-split-pairs", request.PredecessorOutput.Stem, Path.GetFileName(request.PredecessorOutput.AudioPath!))));
        Assert.True(File.Exists(request.Candidate.Predecessor.MergedAudioPath!));
        Assert.False(File.Exists(request.PredecessorOutput.ReadyMarkerPath!));
        Assert.False(File.Exists(request.SuccessorOutput.ReadyMarkerPath!));
        var healedMarkdown = await File.ReadAllTextAsync(Path.Combine(request.TranscriptOutputDirectory, $"{result.SurvivingStem}.md"));
        Assert.Contains("## Continuity history", healedMarkdown);
        Assert.Contains("Source session IDs: first, second", healedMarkdown);

        var repeat = await transaction.ExecuteAsync(request, Now.AddMinutes(1));
        Assert.Equal(OngoingMeetingHealTransactionStatus.AlreadyHealed, repeat.Status);
        Assert.True(File.Exists(request.ReceiptPath));
        Assert.False(File.Exists(request.ReceiptPath + ".lease"));
    }

    [Fact]
    public async Task Reversal_Restores_Archived_Artifacts_And_Marks_Receipt()
    {
        var request = await CreateRequestAsync();
        var healed = await CreateTransaction().ExecuteAsync(request, Now);
        Assert.Equal(OngoingMeetingHealTransactionStatus.Healed, healed.Status);

        var reversal = await new OngoingMeetingHealReversalService().ReverseAsync(
            new(request.ReceiptPath, request.AudioOutputDirectory, request.TranscriptOutputDirectory),
            Now.AddMinutes(1));

        Assert.Equal(OngoingMeetingHealReversalStatus.Reversed, reversal);
        Assert.True(File.Exists(request.PredecessorOutput.AudioPath!));
        Assert.True(File.Exists(request.SuccessorOutput.AudioPath!));
        Assert.True(File.Exists(request.PredecessorOutput.ReadyMarkerPath!));
        Assert.True(File.Exists(request.SuccessorOutput.ReadyMarkerPath!));
        Assert.Contains("first", await File.ReadAllTextAsync(request.PredecessorOutput.MarkdownPath!));
        var receipt = await new OngoingMeetingHealReceiptStore(request.ReceiptPath).TryLoadAsync();
        Assert.NotNull(receipt?.ReversedAtUtc);
        Assert.Equal(
            OngoingMeetingHealReversalStatus.AlreadyReversed,
            await new OngoingMeetingHealReversalService().ReverseAsync(
                new(request.ReceiptPath, request.AudioOutputDirectory, request.TranscriptOutputDirectory),
                Now.AddMinutes(2)));
    }

    [Fact]
    public async Task ExecuteAsync_Rejects_Ineligible_Pair_Without_Moving_Artifacts()
    {
        var request = await CreateRequestAsync();
        request = request with { Candidate = request.Candidate with { HasUserMetadataConflict = true } };

        var result = await CreateTransaction().ExecuteAsync(request, Now);

        Assert.Equal(OngoingMeetingHealTransactionStatus.Rejected, result.Status);
        Assert.True(File.Exists(request.PredecessorOutput.AudioPath!));
        Assert.True(File.Exists(request.PredecessorOutput.ReadyMarkerPath!));
        Assert.False(File.Exists(request.ReceiptPath));
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_Before_Admission_Leaves_Both_Publishes_Current()
    {
        var request = await CreateRequestAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateTransaction().ExecuteAsync(request, Now, cancellation.Token));

        Assert.True(File.Exists(request.PredecessorOutput.AudioPath!));
        Assert.True(File.Exists(request.SuccessorOutput.AudioPath!));
        Assert.True(File.Exists(request.PredecessorOutput.ReadyMarkerPath!));
        Assert.True(File.Exists(request.SuccessorOutput.ReadyMarkerPath!));
        Assert.False(File.Exists(request.ReceiptPath));
    }

    [Fact]
    public async Task Pass_Heals_Only_The_Current_Adjacent_Published_Pair()
    {
        var request = await CreateRequestAsync();
        var result = await OngoingMeetingHealPass.RunOnceAsync(
            new SessionManifestStore(new ArtifactPathBuilder()),
            new ArtifactPathBuilder(),
            Path.GetDirectoryName(Path.GetDirectoryName(request.PredecessorOutput.ManifestPath!)!)!,
            request.AudioOutputDirectory,
            request.TranscriptOutputDirectory,
            Now);

        Assert.NotNull(result);
        Assert.Equal(OngoingMeetingHealTransactionStatus.Healed, result!.Status);
        Assert.True(File.Exists(request.Candidate.Predecessor.MergedAudioPath!));
        Assert.True(File.Exists(request.Candidate.Successor.MergedAudioPath!));
    }

    private OngoingMeetingHealTransaction CreateTransaction()
    {
        var builder = new ArtifactPathBuilder();
        return new(new MeetingCleanupExecutionService(builder, new MeetingOutputCatalogService(builder)));
    }

    private async Task<OngoingMeetingHealTransactionRequest> CreateRequestAsync()
    {
        var audio = CreateDirectory("audio");
        var transcript = CreateDirectory("transcript");
        var work = CreateDirectory("work");
        var archive = CreateDirectory("archive");
        var first = await CreateOutputAsync("2026-09-29_115000_teams_crash-recovery", "first", Now.AddMinutes(-10), audio, transcript, work);
        var second = await CreateOutputAsync("2026-09-29_120000_teams_crash-recovery", "second", Now, audio, transcript, work);
        var identity = new MeetingIdentitySnapshot(1, MeetingPlatform.Teams, MeetingIdentityEvidenceTier.Strong, null, new string('a', 64), true, true, false, Now, Now.AddHours(1), [MeetingIdentityEvidenceSource.ManifestTitle], new string('c', 64), new string('d', 64));
        var predecessor = new MeetingSessionManifest { SessionId = "first", Platform = MeetingPlatform.Teams, DetectedTitle = "Crash recovery", IdentitySnapshot = identity, StartedAtUtc = Now.AddMinutes(-10), EndedAtUtc = Now.AddMinutes(-1), State = SessionState.Published, MergedAudioPath = Path.Combine(work, "first-source.wav") };
        var successor = predecessor with { SessionId = "second", StartedAtUtc = Now, EndedAtUtc = Now.AddMinutes(1), MergedAudioPath = Path.Combine(work, "second-source.wav") };
        await WriteWaveAsync(predecessor.MergedAudioPath!);
        await WriteWaveAsync(successor.MergedAudioPath!);
        var candidate = new OngoingMeetingHealCandidate(predecessor, successor, identity, identity, false, false);
        var manifestStore = new SessionManifestStore(new ArtifactPathBuilder());
        await manifestStore.SaveAsync(predecessor, first.ManifestPath!);
        await manifestStore.SaveAsync(successor, second.ManifestPath!);
        return new(candidate, first, second, audio, transcript, archive, Path.Combine(work, "healing", "first-second.receipt.json"));
    }

    private async Task<MeetingOutputRecord> CreateOutputAsync(string stem, string sessionId, DateTimeOffset started, string audioDirectory, string transcriptDirectory, string workDirectory)
    {
        var audioPath = Path.Combine(audioDirectory, $"{stem}.wav");
        var markdownPath = Path.Combine(transcriptDirectory, $"{stem}.md");
        var jsonPath = Path.Combine(transcriptDirectory, "json", $"{stem}.json");
        var readyPath = Path.Combine(transcriptDirectory, "json", $"{stem}.ready");
        var manifestPath = Path.Combine(workDirectory, sessionId, "manifest.json");
        await WriteWaveAsync(audioPath);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(markdownPath, $"# Crash recovery{Environment.NewLine}{Environment.NewLine}## Transcript{Environment.NewLine}{Environment.NewLine}[00:00:00 - 00:00:01] **Speaker:** {sessionId}");
        await File.WriteAllTextAsync(jsonPath, "{}");
        await File.WriteAllTextAsync(readyPath, string.Empty);
        await File.WriteAllTextAsync(manifestPath, "{}");
        return new(stem, "Crash recovery", started, MeetingPlatform.Teams, TimeSpan.FromSeconds(1), audioPath, markdownPath, jsonPath, readyPath, manifestPath, SessionState.Published, Array.Empty<MeetingAttendee>(), false, null);
    }

    private static async Task WriteWaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var format = new WaveFormat(16000, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        writer.Write(new byte[format.AverageBytesPerSecond], 0, format.AverageBytesPerSecond);
        await Task.CompletedTask;
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
