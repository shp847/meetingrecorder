using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public enum MeetingOriginKind
{
    Unknown = 0,
    Captured = 1,
    ImportedAudio = 2,
}

public enum MeetingOriginSourceAvailability
{
    NotApplicable = 0,
    RetainedAtImport = 1,
    Unknown = 2,
}

/// <summary>
/// Immutable, display-safe origin metadata for a published meeting. It
/// deliberately excludes local source/staging paths, file identity, hashes,
/// audio bytes, and voice-profile information.
/// </summary>
public sealed record MeetingOrigin(
    MeetingOriginKind Kind,
    string DisplayLabel,
    string? MethodLabel,
    string? SessionId,
    MeetingOriginSourceAvailability SourceAvailability)
{
    public static MeetingOrigin Captured { get; } = new(
        MeetingOriginKind.Captured,
        "Recorded in Meeting Recorder",
        MethodLabel: null,
        SessionId: null,
        MeetingOriginSourceAvailability.NotApplicable);
}

public static class MeetingOriginResolver
{
    public static MeetingOrigin Resolve(MeetingSessionManifest? manifest)
    {
        if (manifest?.ImportedSourceAudio is not { } imported)
        {
            return MeetingOrigin.Captured;
        }

        return new MeetingOrigin(
            MeetingOriginKind.ImportedAudio,
            "Imported audio",
            GetMethodLabel(imported.ImportMethod),
            NormalizeSessionId(manifest.SessionId),
            imported.SourceRetained
                ? MeetingOriginSourceAvailability.RetainedAtImport
                : MeetingOriginSourceAvailability.Unknown);
    }

    private static string? NormalizeSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();

    private static string GetMethodLabel(ExternalAudioImportMethod method) => method switch
    {
        ExternalAudioImportMethod.FilePicker => "Added file",
        ExternalAudioImportMethod.DragDrop => "Dropped file",
        ExternalAudioImportMethod.ImportInbox => "Import Inbox",
        _ => "Watched folder",
    };
}
