namespace MeetingRecorder.Core.Tests;

internal static class TestRepositoryPaths
{
    public static string FromRoot(params string[] segments) =>
        Path.Combine(FindRoot(), Path.Combine(segments));

    public static string FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MEETINGRECORDER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "MeetingRecorder.sln")))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MeetingRecorder.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Meeting Recorder repository root.");
    }
}
