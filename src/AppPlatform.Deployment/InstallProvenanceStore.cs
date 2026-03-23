using AppPlatform.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppPlatform.Deployment;

internal sealed class InstallProvenanceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public string GetPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, "install-provenance.json");
    }

    public InstallProvenance? TryLoad(string dataRoot)
    {
        var path = GetPath(dataRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        var contents = File.ReadAllText(path);
        return JsonSerializer.Deserialize<InstallProvenance>(contents, SerializerOptions);
    }

    public void Save(string dataRoot, InstallProvenance provenance)
    {
        var path = GetPath(dataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Install provenance path must have a parent directory."));
        File.WriteAllText(path, JsonSerializer.Serialize(provenance, SerializerOptions));
    }
}
