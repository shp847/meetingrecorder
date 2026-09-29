using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Processing;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class GpuTranscriptionOutputFingerprintTests
{
    [Fact]
    public void Create_Retains_Only_Output_Shape_And_Never_Transcript_Text_Or_Path()
    {
        var result = new TranscriptionResult(
            [
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), null, "Private customer discussion."),
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), null, "C:\\private\\meeting.wav"),
            ],
            "en",
            "Completed.");

        var fingerprint = GpuTranscriptionOutputFingerprint.Create(result);
        var serialized = JsonSerializer.Serialize(fingerprint);

        Assert.True(fingerprint.IsSchemaValid);
        Assert.Equal("en", fingerprint.Language);
        Assert.Equal(2, fingerprint.SegmentCount);
        Assert.Equal(2, fingerprint.Segments.Count);
        Assert.DoesNotContain("Private customer discussion", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_Rejects_Invalid_Or_Unexpected_Output_Shape_And_Requires_Manual_Quality_Review()
    {
        var cpu = GpuTranscriptionOutputFingerprint.Create(new TranscriptionResult(
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), null, "CPU baseline")],
            "en",
            "Completed."));
        var candidate = GpuTranscriptionOutputFingerprint.Create(new TranscriptionResult(
            [new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null, "Invalid")],
            "en",
            "Completed."));

        var comparison = GpuTranscriptionOutputFingerprint.Compare(cpu, candidate, TimeSpan.FromSeconds(1));

        Assert.False(comparison.IsSchemaCompatible);
        Assert.False(comparison.IsTimestampCompatible);
        Assert.True(comparison.RequiresManualQualityReview);
        Assert.DoesNotContain("CPU baseline", JsonSerializer.Serialize(comparison), StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_Accepts_Matched_Shape_Without_Claiming_Text_Quality_Equivalence()
    {
        var cpu = GpuTranscriptionOutputFingerprint.Create(new TranscriptionResult(
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), null, "CPU baseline")],
            "en",
            "Completed."));
        var candidate = GpuTranscriptionOutputFingerprint.Create(new TranscriptionResult(
            [new TranscriptSegment(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2.25), null, "Candidate output")],
            "en",
            "Completed."));

        var comparison = GpuTranscriptionOutputFingerprint.Compare(cpu, candidate, TimeSpan.FromSeconds(1));

        Assert.True(comparison.IsSchemaCompatible);
        Assert.True(comparison.IsTimestampCompatible);
        Assert.True(comparison.RequiresManualQualityReview);
    }
}
