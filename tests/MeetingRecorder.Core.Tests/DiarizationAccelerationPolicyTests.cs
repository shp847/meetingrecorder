using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class DiarizationAccelerationPolicyTests
{
    [Fact]
    public void Resolve_Uses_DirectMl_When_Auto_And_Probe_Succeeds()
    {
        var result = DiarizationAccelerationPolicy.Resolve(
            InferenceAccelerationPreference.Auto,
            directMlAvailable: true);

        Assert.Equal(DiarizationExecutionProvider.Directml, result.ExecutionProvider);
        Assert.True(result.GpuAccelerationRequested);
        Assert.True(result.GpuAccelerationAvailable);
        Assert.Null(result.DiagnosticMessage);
    }

    [Fact]
    public void Resolve_Uses_Cpu_When_Auto_And_Probe_Fails()
    {
        var result = DiarizationAccelerationPolicy.Resolve(
            InferenceAccelerationPreference.Auto,
            directMlAvailable: false,
            directMlFailureMessage: "DirectML probe failed.");

        Assert.Equal(DiarizationExecutionProvider.Cpu, result.ExecutionProvider);
        Assert.True(result.GpuAccelerationRequested);
        Assert.False(result.GpuAccelerationAvailable);
        Assert.Equal("DirectML probe failed.", result.DiagnosticMessage);
    }

    [Fact]
    public void Resolve_Uses_Cpu_When_User_Disables_Gpu_Acceleration()
    {
        var result = DiarizationAccelerationPolicy.Resolve(
            InferenceAccelerationPreference.CpuOnly,
            directMlAvailable: true);

        Assert.Equal(DiarizationExecutionProvider.Cpu, result.ExecutionProvider);
        Assert.False(result.GpuAccelerationRequested);
        Assert.True(result.GpuAccelerationAvailable);
        Assert.Null(result.DiagnosticMessage);
    }
}
