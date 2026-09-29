using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class GpuTranscriptionPolicyTests
{
    [Fact]
    public void Evaluate_Rejects_A_NoGo_Candidate_Without_Starting_A_Probe()
    {
        var decision = GpuTranscriptionPolicy.Evaluate(new GpuTranscriptionPolicyInput(
            IsCandidateRequested: true,
            HasValidatedCpuBaseline: true,
            HasApprovedRuntimeAndAssetContract: true,
            IsRecordingActive: false,
            IsGpuCapabilityConfirmed: true,
            CircuitBreakerOpen: false,
            NoGoReasons: GpuTranscriptionNoGoReason.RequiresAdministrator));

        Assert.Equal(GpuTranscriptionCapabilityState.Unavailable, decision.State);
        Assert.Equal("NoGo:RequiresAdministrator", decision.ReasonCode);
        Assert.True(decision.MustUseCpu);
        Assert.False(decision.MayProbe);
        Assert.False(decision.MayAccelerate);
        Assert.DoesNotContain("C:\\", decision.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_Suppresses_Acceleration_While_Recording_Or_Circuit_Breaker_Is_Open()
    {
        var recording = GpuTranscriptionPolicy.Evaluate(ReadyInput() with { IsRecordingActive = true });
        var suppressed = GpuTranscriptionPolicy.Evaluate(ReadyInput() with { CircuitBreakerOpen = true });

        Assert.Equal(GpuTranscriptionCapabilityState.Suppressed, recording.State);
        Assert.Equal("RecordingActive", recording.ReasonCode);
        Assert.Equal(GpuTranscriptionCapabilityState.Suppressed, suppressed.State);
        Assert.Equal("CircuitBreakerOpen", suppressed.ReasonCode);
        Assert.True(recording.MustUseCpu);
        Assert.True(suppressed.MustUseCpu);
    }

    [Fact]
    public void Evaluate_Allows_Only_A_CPU_Safe_Probe_After_All_Gates_Pass()
    {
        var decision = GpuTranscriptionPolicy.Evaluate(ReadyInput());

        Assert.Equal(GpuTranscriptionCapabilityState.Probing, decision.State);
        Assert.Equal("ProbeRequired", decision.ReasonCode);
        Assert.True(decision.MustUseCpu);
        Assert.True(decision.MayProbe);
        Assert.False(decision.MayAccelerate);
    }

    [Fact]
    public void Evaluate_Leaves_CPU_As_The_Product_When_No_Candidate_Is_Requested()
    {
        var decision = GpuTranscriptionPolicy.Evaluate(ReadyInput() with { IsCandidateRequested = false });

        Assert.Equal(GpuTranscriptionCapabilityState.NotRequested, decision.State);
        Assert.Equal("NotRequested", decision.ReasonCode);
        Assert.True(decision.MustUseCpu);
        Assert.False(decision.MayProbe);
    }

    private static GpuTranscriptionPolicyInput ReadyInput() => new(
        IsCandidateRequested: true,
        HasValidatedCpuBaseline: true,
        HasApprovedRuntimeAndAssetContract: true,
        IsRecordingActive: false,
        IsGpuCapabilityConfirmed: true,
        CircuitBreakerOpen: false,
        NoGoReasons: GpuTranscriptionNoGoReason.None);
}
