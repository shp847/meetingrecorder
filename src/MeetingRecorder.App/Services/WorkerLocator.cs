namespace MeetingRecorder.App.Services;

internal static class WorkerLocator
{
    public static WorkerLaunch Resolve()
    {
        foreach (var candidate in EnumerateCandidates(Environment.ProcessPath, AppContext.BaseDirectory))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (string.Equals(Path.GetExtension(candidate), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return new WorkerLaunch("dotnet", $"\"{candidate}\"");
            }

            return new WorkerLaunch(candidate, string.Empty);
        }

        throw new FileNotFoundException("Unable to locate the MeetingRecorder processing worker output.");
    }

    internal static string ResolveInstalledAppRoot(string? processPath, string appContextBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processDirectory = Path.GetDirectoryName(processPath.Trim());
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                return processDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return appContextBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static IEnumerable<string> EnumerateCandidates(string? processPath, string appContextBaseDirectory)
    {
        var installedRoot = ResolveInstalledAppRoot(processPath, appContextBaseDirectory);
        yield return Path.Combine(installedRoot, "MeetingRecorder.ProcessingWorker.exe");
        yield return Path.Combine(installedRoot, "MeetingRecorder.ProcessingWorker.dll");

        var current = new DirectoryInfo(appContextBaseDirectory);
        while (current is not null)
        {
            var rootCandidate = Path.Combine(
                current.FullName,
                "src",
                "MeetingRecorder.ProcessingWorker",
                "bin",
                "Debug",
                "net8.0-windows");

            yield return Path.Combine(rootCandidate, "MeetingRecorder.ProcessingWorker.exe");
            yield return Path.Combine(rootCandidate, "MeetingRecorder.ProcessingWorker.dll");
            current = current.Parent;
        }
    }
}

internal sealed record WorkerLaunch(string FileName, string ArgumentPrefix);
