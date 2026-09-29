using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingOutputOriginProjectionTests
{
    [Fact]
    public async Task ListMeetings_ProjectsPublishedImportedOriginWithoutLocalSourceLocator()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var audioDir = Path.Combine(root, "audio");
        var transcriptDir = Path.Combine(root, "transcripts");
        var workDir = Path.Combine(root, "work");
        Directory.CreateDirectory(audioDir);
        Directory.CreateDirectory(transcriptDir);
        Directory.CreateDirectory(workDir);
        var pathBuilder = new ArtifactPathBuilder();
        var store = new SessionManifestStore(pathBuilder);
        var manifest = await store.CreateAsync(workDir, MeetingPlatform.Manual, "Imported planning", Array.Empty<DetectionSignal>());
        var stem = pathBuilder.BuildImportedFileStem(MeetingPlatform.Manual, manifest.StartedAtUtc, "Imported planning", manifest.SessionId);
        var manifestPath = Path.Combine(workDir, manifest.SessionId, "manifest.json");
        var privateSource = "C:\\private\\imports\\planning.wav";
        await store.SaveAsync(manifest with
        {
            State = SessionState.Published,
            ImportedSourceAudio = new ImportedSourceAudioInfo(
                privateSource,
                12,
                DateTimeOffset.UtcNow,
                "planning.wav",
                ExternalAudioImportMethod.FilePicker,
                TimeSpan.FromMinutes(2),
                sourceRetained: true)
            {
                OutputStem = stem,
            },
        }, manifestPath);
        await File.WriteAllTextAsync(Path.Combine(audioDir, $"{stem}.wav"), "audio");

        var meeting = Assert.Single(new MeetingOutputCatalogService(pathBuilder).ListMeetings(audioDir, transcriptDir, workDir));

        Assert.NotNull(meeting.Origin);
        Assert.Equal(MeetingOriginKind.ImportedAudio, meeting.Origin!.Kind);
        Assert.Equal("Added file", meeting.Origin.MethodLabel);
        Assert.DoesNotContain(privateSource, JsonSerializer.Serialize(meeting.Origin), StringComparison.OrdinalIgnoreCase);
    }
}
