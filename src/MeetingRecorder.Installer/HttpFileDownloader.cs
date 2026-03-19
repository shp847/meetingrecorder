using MeetingRecorder.Core.Branding;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MeetingRecorder.Installer;

internal sealed class HttpFileDownloader : IDisposable
{
    private readonly HttpClient _httpClient;

    public HttpFileDownloader()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            CheckCertificateRevocationList = false,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(20),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MeetingRecorderInstaller", AppBranding.Version));
    }

    public async Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        Action<long, long?, TimeSpan?> reportProgress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Destination path must have a parent directory."));

        var totalBytes = response.Content.Headers.ContentLength;
        var stopwatch = Stopwatch.StartNew();
        long totalRead = 0;

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            TimeSpan? estimatedRemaining = null;
            if (totalBytes.HasValue && totalBytes.Value > 0 && totalRead > 0 && stopwatch.Elapsed > TimeSpan.Zero)
            {
                var bytesPerSecond = totalRead / stopwatch.Elapsed.TotalSeconds;
                if (bytesPerSecond > 0)
                {
                    estimatedRemaining = TimeSpan.FromSeconds((totalBytes.Value - totalRead) / bytesPerSecond);
                }
            }

            reportProgress(totalRead, totalBytes, estimatedRemaining);
        }

        reportProgress(totalRead, totalBytes, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
