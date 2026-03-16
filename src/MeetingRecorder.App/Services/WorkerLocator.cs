namespace MeetingRecorder.App.Services;

internal static class WorkerLocator
{
    public static WorkerLaunch Resolve()
    {
        foreach (var candidate in EnumerateCandidates())
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

    private static IEnumerable<string> EnumerateCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "MeetingRecorder.ProcessingWorker.exe");
        yield return Path.Combine(baseDirectory, "MeetingRecorder.ProcessingWorker.dll");

        var current = new DirectoryInfo(baseDirectory);
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
