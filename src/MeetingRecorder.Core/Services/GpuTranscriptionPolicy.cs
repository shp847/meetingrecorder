namespace MeetingRecorder.Core.Services;

/// <summary>
/// Product gate for an optional GPU transcription candidate. This is a policy
/// boundary only: it neither probes hardware nor selects a runtime. CPU remains
/// the required execution path until a later feasibility slice authorizes more.
/// </summary>
public enum GpuTranscriptionCapabilityState
{
    NotRequested = 0,
    Probing = 1,
    Eligible = 2,
    Accelerating = 3,
    CpuFallback = 4,
    Suppressed = 5,
    Unavailable = 6,
    Failed = 7,
}

[Flags]
public enum GpuTranscriptionNoGoReason
{
    None = 0,
    RequiresAdministrator = 1 << 0,
    RequiresDriverInstall = 1 << 1,
    RequiresUserManagedRuntime = 1 << 2,
    RequiresUnapprovedDownload = 1 << 3,
    UnsupportedLicenseOrSupplyChain = 1 << 4,
    CpuReliabilityRegression = 1 << 5,
    OutputDivergence = 1 << 6,
    PrivacyOrEndpointSecurityRisk = 1 << 7,
    BatteryOrThermalHarm = 1 << 8,
    PackageOrRollbackUnsupported = 1 << 9,
}

public sealed record GpuTranscriptionPolicyInput(
    bool IsCandidateRequested,
    bool HasValidatedCpuBaseline,
    bool HasApprovedRuntimeAndAssetContract,
    bool IsRecordingActive,
    bool IsGpuCapabilityConfirmed,
    bool CircuitBreakerOpen,
    GpuTranscriptionNoGoReason NoGoReasons);

public sealed record GpuTranscriptionPolicyDecision(
    GpuTranscriptionCapabilityState State,
    string ReasonCode,
    string StatusText,
    bool MustUseCpu,
    bool MayProbe,
    bool MayAccelerate);

public static class GpuTranscriptionPolicy
{
    public static GpuTranscriptionPolicyDecision Evaluate(GpuTranscriptionPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.IsCandidateRequested)
        {
            return CpuOnly(GpuTranscriptionCapabilityState.NotRequested, "NotRequested", "CPU transcription is in use.");
        }

        if (input.NoGoReasons != GpuTranscriptionNoGoReason.None)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Unavailable,
                $"NoGo:{GetPrimaryNoGoReason(input.NoGoReasons)}",
                "GPU transcription is not available because this candidate does not meet the product safety gate.");
        }

        if (input.IsRecordingActive)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Suppressed,
                "RecordingActive",
                "GPU transcription remains off while recording is active.");
        }

        if (input.CircuitBreakerOpen)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Suppressed,
                "CircuitBreakerOpen",
                "GPU transcription is temporarily suppressed after a prior failure.");
        }

        if (!input.HasValidatedCpuBaseline)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Unavailable,
                "CpuBaselineUnvalidated",
                "GPU transcription cannot be evaluated until the CPU baseline is verified.");
        }

        if (!input.HasApprovedRuntimeAndAssetContract)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Unavailable,
                "RuntimeContractMissing",
                "GPU transcription has no approved runtime and asset contract.");
        }

        if (!input.IsGpuCapabilityConfirmed)
        {
            return CpuOnly(
                GpuTranscriptionCapabilityState.Unavailable,
                "CapabilityUnconfirmed",
                "This device has no confirmed GPU transcription capability.");
        }

        return new GpuTranscriptionPolicyDecision(
            GpuTranscriptionCapabilityState.Probing,
            "ProbeRequired",
            "GPU transcription may be evaluated in a safe background probe; CPU transcription remains available.",
            MustUseCpu: true,
            MayProbe: true,
            MayAccelerate: false);
    }

    private static GpuTranscriptionPolicyDecision CpuOnly(
        GpuTranscriptionCapabilityState state,
        string reasonCode,
        string statusText) => new(
        state,
        reasonCode,
        statusText,
        MustUseCpu: true,
        MayProbe: false,
        MayAccelerate: false);

    private static GpuTranscriptionNoGoReason GetPrimaryNoGoReason(GpuTranscriptionNoGoReason reasons)
    {
        foreach (var reason in Enum.GetValues<GpuTranscriptionNoGoReason>())
        {
            if (reason != GpuTranscriptionNoGoReason.None && reasons.HasFlag(reason))
            {
                return reason;
            }
        }

        return GpuTranscriptionNoGoReason.None;
    }
}
