using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingOriginResolverTests
{
    [Fact]
    public void Resolve_ImportedMeeting_ProjectsOnlySafeProvenance()
    {
        var manifest = new MeetingSessionManifest
        {
            SessionId = "import-session-42",
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\private\\imports\\client-call.wav",
                1234,
                DateTimeOffset.Parse("2026-09-27T15:00:00Z"),
                "client-call.wav",
                ExternalAudioImportMethod.DragDrop,
                TimeSpan.FromMinutes(12),
                sourceRetained: true),
        };

        var origin = MeetingOriginResolver.Resolve(manifest);
        var serialized = JsonSerializer.Serialize(origin);

        Assert.Equal(MeetingOriginKind.ImportedAudio, origin.Kind);
        Assert.Equal("Imported audio", origin.DisplayLabel);
        Assert.Equal("Dropped file", origin.MethodLabel);
        Assert.Equal("import-session-42", origin.SessionId);
        Assert.Equal(MeetingOriginSourceAvailability.RetainedAtImport, origin.SourceAvailability);
        Assert.DoesNotContain(manifest.ImportedSourceAudio.OriginalPath, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1234", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CapturedOrMissingManifest_DoesNotInventImportContext()
    {
        var captured = MeetingOriginResolver.Resolve(new MeetingSessionManifest { SessionId = "captured-1" });
        var missing = MeetingOriginResolver.Resolve(null);

        Assert.Equal(MeetingOriginKind.Captured, captured.Kind);
        Assert.Equal("Recorded in Meeting Recorder", captured.DisplayLabel);
        Assert.Null(captured.SessionId);
        Assert.Equal(captured, missing);
    }

    [Theory]
    [InlineData(ExternalAudioImportMethod.FilePicker, "Added file")]
    [InlineData(ExternalAudioImportMethod.ImportInbox, "Import Inbox")]
    [InlineData(ExternalAudioImportMethod.WatchedFolder, "Watched folder")]
    public void Resolve_UsesStableMethodLabelsWithoutLiveSourceAccess(
        ExternalAudioImportMethod method,
        string expectedLabel)
    {
        var origin = MeetingOriginResolver.Resolve(new MeetingSessionManifest
        {
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                "C:\\missing\\source.wav",
                1,
                DateTimeOffset.UtcNow,
                "source.wav",
                method,
                null,
                sourceRetained: false),
        });

        Assert.Equal(expectedLabel, origin.MethodLabel);
        Assert.Equal(MeetingOriginSourceAvailability.Unknown, origin.SourceAvailability);
    }
}
