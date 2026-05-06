using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MeetingRecorder.Core.Services;

internal static class AppUpdatePackageClassifier
{
    private static readonly Regex WindowsX64AppZipFileNamePattern = new(
        "^MeetingRecorder(?:-v?[^\\\\/]+)?-win-x64\\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsWindowsX64AppZipFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
            WindowsX64AppZipFileNamePattern.IsMatch(fileName);
    }

    public static bool IsWindowsX64AppZipPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            IsWindowsX64AppZipFileName(Path.GetFileName(path));
    }

    public static string ResolveDownloadFileName(string downloadUrl, string version)
    {
        var fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
        return string.IsNullOrWhiteSpace(fileName)
            ? $"MeetingRecorder-v{version}-win-x64.zip"
            : fileName;
    }

    public static void ValidateDownloadedAppZip(string packagePath, long? expectedSizeBytes)
    {
        var actualSizeBytes = new FileInfo(packagePath).Length;
        if (expectedSizeBytes is > 0 && actualSizeBytes != expectedSizeBytes.Value)
        {
            throw new InvalidOperationException(
                $"The downloaded update package is incomplete: expected {expectedSizeBytes.Value} bytes but found {actualSizeBytes} bytes.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            _ = archive.Entries.Count;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException(
                "The downloaded update package is not a readable ZIP archive. It may be the wrong release asset or an incomplete download.",
                exception);
        }
    }
}
