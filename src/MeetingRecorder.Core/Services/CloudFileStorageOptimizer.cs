namespace MeetingRecorder.Core.Services;

internal static class CloudFileStorageOptimizer
{
    private const FileAttributes WindowsFileAttributePinned = (FileAttributes)0x00080000;
    private const FileAttributes WindowsFileAttributeUnpinned = (FileAttributes)0x00100000;

    public static void MarkUnpinnedRecursive(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        TryMarkUnpinned(path);

        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in EnumerateFileSystemEntries(path))
        {
            var attributes = GetAttributesOrNull(entry);
            if (attributes is null)
            {
                continue;
            }

            TryMarkUnpinned(entry);

            if (Directory.Exists(entry) && !attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            {
                MarkUnpinnedRecursive(entry);
            }
        }
    }

    internal static FileAttributes BuildUnpinnedAttributes(FileAttributes attributes)
    {
        return attributes & ~WindowsFileAttributePinned | WindowsFileAttributeUnpinned;
    }

    private static IEnumerable<string> EnumerateFileSystemEntries(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static FileAttributes? GetAttributesOrNull(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryMarkUnpinned(string path)
    {
        try
        {
            File.SetAttributes(path, BuildUnpinnedAttributes(File.GetAttributes(path)));
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
