namespace MeetingRecorder.Core.Domain;

/// <summary>
/// Local-only, privacy-safe continuity evidence. Values are keyed tokens, not
/// display strings or source payloads, so this record is safe to place in a
/// session manifest without exporting a meeting title, window, path, audio, or
/// attendee identity.
/// </summary>
public sealed record MeetingIdentitySnapshot(
    int SchemaVersion,
    MeetingPlatform Platform,
    MeetingIdentityEvidenceTier EvidenceTier,
    string? DurableIdentityToken,
    string? SpecificTitleToken,
    bool HasAttributedCapture,
    bool HasHostContext,
    bool IsAmbiguous,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<MeetingIdentityEvidenceSource> Sources,
    string KeyId,
    string Fingerprint)
{
    public const int CurrentSchemaVersion = 1;
}

public enum MeetingIdentityEvidenceTier
{
    None = 0,
    Weak = 1,
    Medium = 2,
    Strong = 3,
}

public enum MeetingIdentityEvidenceSource
{
    ManifestTitle = 0,
    AudioWindow = 1,
    AudioApplication = 2,
    DurableMeetingCode = 3,
}
