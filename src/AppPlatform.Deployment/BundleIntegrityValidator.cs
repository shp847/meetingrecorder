using AppPlatform.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace AppPlatform.Deployment;

internal static class BundleIntegrityValidator
{
    internal const string ManifestFileName = "bundle-integrity.json";
    private const int SupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static void ValidateBundle(string bundleRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);

        var resolvedBundleRoot = Path.GetFullPath(bundleRoot);
        var manifestPath = Path.Combine(resolvedBundleRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"The portable bundle is missing required integrity manifest '{ManifestFileName}'.");
        }

        var manifest = JsonSerializer.Deserialize<BundleIntegrityManifest>(
            File.ReadAllText(manifestPath),
            SerializerOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException(
                $"The bundle integrity manifest at '{manifestPath}' could not be parsed.");
        }

        if (manifest.FormatVersion != SupportedFormatVersion)
        {
            throw new InvalidOperationException(
                $"The bundle integrity manifest uses unsupported format version '{manifest.FormatVersion}'.");
        }

        if (manifest.RequiredFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"The bundle integrity manifest at '{manifestPath}' did not declare any required files.");
        }

        foreach (var entry in manifest.RequiredFiles)
        {
            ValidateEntry(resolvedBundleRoot, entry);
        }
    }

    private static void ValidateEntry(string bundleRoot, BundleIntegrityEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
        {
            throw new InvalidOperationException("The bundle integrity manifest contained an empty relative path.");
        }

        var entryPath = Path.GetFullPath(Path.Combine(bundleRoot, entry.RelativePath));
        var rootWithSeparator = bundleRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!entryPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The bundle integrity manifest entry '{entry.RelativePath}' resolved outside the bundle root.");
        }

        if (!File.Exists(entryPath))
        {
            throw new InvalidOperationException(
                $"The portable bundle is missing required file '{entry.RelativePath}'.");
        }

        var actualLength = new FileInfo(entryPath).Length;
        if (actualLength != entry.LengthBytes)
        {
            throw new InvalidOperationException(
                $"The portable bundle file '{entry.RelativePath}' had length {actualLength}, expected {entry.LengthBytes}.");
        }

        var actualHash = ComputeSha256(entryPath);
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The portable bundle file '{entry.RelativePath}' did not match the expected SHA-256 hash.");
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
