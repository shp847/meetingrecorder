namespace MeetingRecorder.Core.Services;

internal sealed record SetupGuideResolution(
    string? LocalPath,
    Uri Uri,
    bool UsedFallback);

internal sealed record AlternatePublicDownloadLocation(
    string Label,
    Uri Url,
    string Notes);

internal sealed record AlternatePublicDownloadLocationsState(
    string Heading,
    string EmptyStateText,
    IReadOnlyList<AlternatePublicDownloadLocation> Locations);

internal static class ModelsTabGuidance
{
    private const string SetupGuideFileName = "SETUP.md";
    private const int MaxParentTraversalCount = 5;

    public static SetupGuideResolution ResolveSpeakerLabelingSetupGuidePath(
        string appBaseDirectory,
        Uri fallbackUri)
    {
        if (!string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            var currentDirectory = new DirectoryInfo(Path.GetFullPath(appBaseDirectory));
            for (var traversed = 0; traversed <= MaxParentTraversalCount && currentDirectory is not null; traversed++)
            {
                var candidatePath = Path.Combine(currentDirectory.FullName, SetupGuideFileName);
                if (File.Exists(candidatePath))
                {
                    return new SetupGuideResolution(
                        candidatePath,
                        new Uri(candidatePath),
                        UsedFallback: false);
                }

                currentDirectory = currentDirectory.Parent;
            }
        }

        return new SetupGuideResolution(
            LocalPath: null,
            fallbackUri,
            UsedFallback: true);
    }

    public static AlternatePublicDownloadLocationsState BuildAlternatePublicDownloadLocationsState(
        IReadOnlyList<AlternatePublicDownloadLocation> locations)
    {
        return new AlternatePublicDownloadLocationsState(
            "Alternate public download locations",
            locations.Count == 0
                ? "No vetted public mirror configured yet."
                : string.Empty,
            locations);
    }

    public static IReadOnlyList<AlternatePublicDownloadLocation> GetSpeakerLabelingAlternatePublicDownloadLocations()
    {
        return Array.Empty<AlternatePublicDownloadLocation>();
    }
}
