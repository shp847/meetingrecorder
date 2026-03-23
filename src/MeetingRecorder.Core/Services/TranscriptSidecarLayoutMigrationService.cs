namespace MeetingRecorder.Core.Services;

public static class TranscriptSidecarLayoutMigrationService
{
    public static TranscriptSidecarLayoutMigrationResult Migrate(string transcriptOutputDir)
    {
        if (string.IsNullOrWhiteSpace(transcriptOutputDir) || !Directory.Exists(transcriptOutputDir))
        {
            return new TranscriptSidecarLayoutMigrationResult(transcriptOutputDir ?? string.Empty, 0, 0);
        }

        var sidecarDir = ArtifactPathBuilder.BuildTranscriptSidecarRoot(transcriptOutputDir);
        var movedArtifactCount = 0;
        var skippedArtifactCount = 0;

        foreach (var sourcePath in Directory.EnumerateFiles(transcriptOutputDir, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(sourcePath);
            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".ready", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(sidecarDir);
            var destinationPath = Path.Combine(sidecarDir, Path.GetFileName(sourcePath));
            if (File.Exists(destinationPath))
            {
                skippedArtifactCount++;
                continue;
            }

            File.Move(sourcePath, destinationPath);
            movedArtifactCount++;
        }

        return new TranscriptSidecarLayoutMigrationResult(sidecarDir, movedArtifactCount, skippedArtifactCount);
    }
}

public sealed record TranscriptSidecarLayoutMigrationResult(
    string SidecarDirectory,
    int MovedArtifactCount,
    int SkippedArtifactCount);
