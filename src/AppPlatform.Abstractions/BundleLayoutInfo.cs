namespace AppPlatform.Abstractions;

public sealed record BundleLayoutInfo
{
    public const string FileName = "bundle-layout.json";
    public const int CurrentFormatVersion = 1;
    public const int StableAppHostLayoutVersion = 2;

    public static readonly string[] DefaultStableExecutableFiles =
    [
        "MeetingRecorder.App.exe",
        "AppPlatform.Deployment.Cli.exe",
        "MeetingRecorder.ProcessingWorker.exe",
    ];

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public int LayoutVersion { get; init; } = StableAppHostLayoutVersion;

    public string[] StableExecutableFiles { get; init; } = [.. DefaultStableExecutableFiles];

    public bool SupportsStableAppHostUpdates =>
        FormatVersion == CurrentFormatVersion &&
        LayoutVersion >= StableAppHostLayoutVersion &&
        StableExecutableFiles is { Length: > 0 } &&
        StableExecutableFiles.All(IsStableExecutableFileName);

    public static BundleLayoutInfo CreateCurrent()
    {
        return new BundleLayoutInfo
        {
            FormatVersion = CurrentFormatVersion,
            LayoutVersion = StableAppHostLayoutVersion,
            StableExecutableFiles = [.. DefaultStableExecutableFiles],
        };
    }

    private static bool IsStableExecutableFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
            !fileName.Contains(Path.DirectorySeparatorChar) &&
            !fileName.Contains(Path.AltDirectorySeparatorChar) &&
            string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
            fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
