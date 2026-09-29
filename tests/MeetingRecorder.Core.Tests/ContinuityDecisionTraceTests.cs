using System.Text.Json;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class ContinuityDecisionTraceTests
{
    [Fact]
    public void Trace_Orders_Events_And_Reports_Bounded_Overflow()
    {
        var trace = new ContinuityDecisionTrace("session-a", "decision-a", "correlation-a", capacity: 2);
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        trace.Append(ContinuityTraceEventKind.Detection, ContinuityEvidenceTier.Weak, "detected", ContinuityTraceState.Unknown, ContinuityTraceState.Unknown, now);
        trace.Append(ContinuityTraceEventKind.Identity, ContinuityEvidenceTier.Medium, "identity-observed", ContinuityTraceState.Unknown, ContinuityTraceState.Grace, now.AddSeconds(1));
        trace.Append(ContinuityTraceEventKind.Verdict, ContinuityEvidenceTier.Strong, "stable-identity-continuity", ContinuityTraceState.Grace, ContinuityTraceState.SameMeeting, now.AddSeconds(2));

        var snapshot = trace.CreateSnapshot();

        Assert.Equal(1, snapshot.DroppedEventCount);
        Assert.Equal([2, 3], snapshot.Events.Select(item => item.Sequence));
        ContinuityDecisionTrace.ValidateSnapshot(snapshot);
    }

    [Fact]
    public async Task Store_Writes_Only_Metadata_And_Loads_Atomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var sidecarPath = Path.Combine(root, "session-work", "continuity.trace.json");
        var trace = new ContinuityDecisionTrace("session-a", "decision-a", "correlation-a");
        trace.Append(ContinuityTraceEventKind.GraceEntered, ContinuityEvidenceTier.Medium, "bounded-ambiguous-grace", ContinuityTraceState.Unknown, ContinuityTraceState.Grace, DateTimeOffset.UtcNow, 1200, 1);
        var store = new ContinuityDecisionTraceStore(sidecarPath);

        await store.SaveAsync(trace.CreateSnapshot());
        var result = await store.TryLoadAsync();
        var json = await File.ReadAllTextAsync(sidecarPath);

        Assert.Equal(ContinuityTraceLoadStatus.Loaded, result.Status);
        Assert.NotNull(result.Snapshot);
        Assert.DoesNotContain("title", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attendee", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(sidecarPath)!, "*.tmp"));
    }

    [Fact]
    public async Task Store_Classifies_Corrupt_Unknown_And_Disallowed_Sidecars_Without_Throwing()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "continuity.trace.json");
        var store = new ContinuityDecisionTraceStore(path);

        Assert.Equal(ContinuityTraceLoadStatus.Missing, (await store.TryLoadAsync()).Status);
        await File.WriteAllTextAsync(path, "not-json");
        Assert.Equal(ContinuityTraceLoadStatus.Corrupt, (await store.TryLoadAsync()).Status);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":99}");
        Assert.Equal(ContinuityTraceLoadStatus.Unsupported, (await store.TryLoadAsync()).Status);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { schemaVersion = 1, title = "not-allowed" }));
        Assert.Equal(ContinuityTraceLoadStatus.Corrupt, (await store.TryLoadAsync()).Status);
    }

    [Fact]
    public void Replay_Is_Deterministic_And_Explanation_Is_Content_Free()
    {
        var fixture = ContinuityReplayCorpus.CreatePublicSyntheticFixtures().Single(item => item.ScenarioId == "quiet-continuation-a");
        var trace = new ContinuityDecisionTrace("session-a", "decision-a", "correlation-a");
        trace.Append(ContinuityTraceEventKind.Detection, ContinuityEvidenceTier.Medium, "ambiguous-signal", ContinuityTraceState.Unknown, ContinuityTraceState.Unknown, new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
        trace.Append(ContinuityTraceEventKind.GraceEntered, ContinuityEvidenceTier.Medium, "bounded-ambiguous-grace", ContinuityTraceState.Unknown, ContinuityTraceState.Grace, new DateTimeOffset(2026, 9, 29, 12, 0, 1, TimeSpan.Zero));
        var runner = new ContinuityReplayRunner();

        var first = runner.Run(fixture, trace.CreateSnapshot());
        var second = runner.Run(fixture, trace.CreateSnapshot());
        var explanation = ContinuityTraceExplanationFormatter.Format(first.Decision);

        Assert.Equal(first.TraceDigest, second.TraceDigest);
        Assert.Equal(ContinuityReplayDecision.UnknownGrace, first.Decision.Decision);
        Assert.Equal("Ambiguous evidence is retained only in a bounded continuity grace period.", explanation);
        Assert.DoesNotContain("title", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trace_Rejects_Raw_Or_NonMonotonic_Contract_Inputs()
    {
        Assert.Throws<ArgumentException>(() => new ContinuityDecisionTrace("private session", "decision-a", "correlation-a"));
        var trace = new ContinuityDecisionTrace("session-a", "decision-a", "correlation-a");
        Assert.Throws<ArgumentException>(() => trace.Append(ContinuityTraceEventKind.Detection, ContinuityEvidenceTier.Weak, "raw title value", ContinuityTraceState.Unknown, ContinuityTraceState.Unknown, DateTimeOffset.UtcNow));
    }
}
