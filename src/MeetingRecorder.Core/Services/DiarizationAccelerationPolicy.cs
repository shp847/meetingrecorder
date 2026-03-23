using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public static class DiarizationAccelerationPolicy
{
    public static DiarizationAccelerationDecision Resolve(
        InferenceAccelerationPreference preference,
        bool directMlAvailable,
        string? directMlFailureMessage = null)
    {
        if (preference == InferenceAccelerationPreference.CpuOnly)
        {
            return new DiarizationAccelerationDecision(
                DiarizationExecutionProvider.Cpu,
                GpuAccelerationRequested: false,
                GpuAccelerationAvailable: directMlAvailable,
                DiagnosticMessage: null);
        }

        if (directMlAvailable)
        {
            return new DiarizationAccelerationDecision(
                DiarizationExecutionProvider.Directml,
                GpuAccelerationRequested: true,
                GpuAccelerationAvailable: true,
                DiagnosticMessage: null);
        }

        return new DiarizationAccelerationDecision(
            DiarizationExecutionProvider.Cpu,
            GpuAccelerationRequested: true,
            GpuAccelerationAvailable: false,
            DiagnosticMessage: string.IsNullOrWhiteSpace(directMlFailureMessage)
                ? null
                : directMlFailureMessage.Trim());
    }
}

public sealed record DiarizationAccelerationDecision(
    DiarizationExecutionProvider ExecutionProvider,
    bool GpuAccelerationRequested,
    bool GpuAccelerationAvailable,
    string? DiagnosticMessage);
