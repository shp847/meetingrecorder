using System.Text.Json;
using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.Core.Services;

public sealed class ModelProvisioningResultStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public ModelProvisioningResultStore(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("A config path is required.", nameof(configPath));
        }

        var configDirectory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("Config path must include a parent directory.");
        ResultPath = Path.Combine(configDirectory, "model-provisioning-result.json");
    }

    public string ResultPath { get; }

    public async Task SaveAsync(ModelProvisioningResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)
            ?? throw new InvalidOperationException("Result path must include a parent directory."));
        await File.WriteAllTextAsync(
            ResultPath,
            JsonSerializer.Serialize(result, SerializerOptions),
            cancellationToken);
    }

    public async Task<ModelProvisioningResult?> TryConsumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(ResultPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(ResultPath, cancellationToken);
            return JsonSerializer.Deserialize<ModelProvisioningResult>(json, SerializerOptions);
        }
        finally
        {
            try
            {
                File.Delete(ResultPath);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
