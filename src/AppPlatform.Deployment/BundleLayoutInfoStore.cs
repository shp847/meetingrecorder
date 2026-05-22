using AppPlatform.Abstractions;
using System.Text.Json;

namespace AppPlatform.Deployment;

internal static class BundleLayoutInfoStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static BundleLayoutInfo? TryLoad(string bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot))
        {
            return null;
        }

        var path = Path.Combine(bundleRoot, BundleLayoutInfo.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<BundleLayoutInfo>(
                File.ReadAllText(path),
                SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool SupportsStableAppHostUpdates(string bundleRoot)
    {
        return TryLoad(bundleRoot)?.SupportsStableAppHostUpdates == true;
    }
}
