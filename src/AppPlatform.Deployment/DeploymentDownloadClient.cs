using AppPlatform.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AppPlatform.Deployment;

public sealed class DeploymentDownloadClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDeploymentLogger _logger;

    public DeploymentDownloadClient(
        string userAgentProductName,
        string userAgentVersion,
        IDeploymentLogger? logger = null)
    {
        _logger = logger ?? NullDeploymentLogger.Instance;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(userAgentProductName, userAgentVersion));
    }

    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        _logger.Info($"Requesting text payload from '{url}'.");
        return _httpClient.GetStringAsync(url, cancellationToken);
    }

    public async Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        _logger.Info($"Downloading '{downloadUrl}' to '{destinationPath}'.");
        using var response = await _httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);
        await responseStream.CopyToAsync(fileStream, cancellationToken);
        _logger.Info($"Finished downloading '{downloadUrl}' to '{destinationPath}'.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
