using MeetingRecorder.Core.Branding;
using MeetingRecorder.Core.Services;
using System.IO.Compression;
using System.Text;

namespace MeetingRecorder.Core.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpdateAvailable_For_Newer_GitHub_Release()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.4",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.4",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.4-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/v0.4/MeetingRecorder-v0.4-win-x64.zip"
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                null,
                null),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.Equal("0.4", result.LatestVersion);
        Assert.Equal("https://github.com/shp847/meetingrecorder/releases/download/v0.4/MeetingRecorder-v0.4-win-x64.zip", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Uses_Release_Name_Version_When_GitHub_Tag_Is_Not_Semver()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "beta",
              "name": "Meeting Recorder v0.2",
              "published_at": "2026-03-17T19:28:22Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/beta",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.1-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/beta/MeetingRecorder-v0.1-win-x64.zip",
                  "size": 74158317
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.1",
                "alpha",
                DateTimeOffset.Parse("2026-03-17T03:12:10Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                74158317),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.Equal("0.2", result.LatestVersion);
        Assert.True(result.IsNewerByVersion);
        Assert.Contains("Version 0.2 is available.", result.Message);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Falls_Back_To_Publish_Date_When_GitHub_Tag_Is_Not_Semver()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "beta",
              "published_at": "2026-03-17T19:28:22Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/beta",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/beta/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 167258521
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.2",
                "alpha",
                DateTimeOffset.Parse("2026-03-17T03:12:10Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                74158317),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.Equal("0.2", result.LatestVersion);
        Assert.False(result.IsNewerByVersion);
        Assert.True(result.IsNewerByPublishedAt);
        Assert.DoesNotContain("not a supported semantic version", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpToDate_For_Current_GitHub_Release()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.2",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.2",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/v0.2/MeetingRecorder-v0.2-win-x64.zip"
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                null,
                null),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpToDate, result.Status);
        Assert.Equal("0.2", result.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpdateAvailable_When_PublishDate_Is_Newer_For_The_Same_Version()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.2",
              "published_at": "2026-03-17T14:00:00Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.2",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/v0.2/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 123456789
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                DateTimeOffset.Parse("2026-03-16T14:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                123456789),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.True(result.IsNewerByPublishedAt);
        Assert.False(result.IsNewerByAssetSize);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpToDate_For_The_Same_Version_When_Local_Package_Metadata_Is_Unavailable()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.3",
              "published_at": "2026-04-22T14:37:03Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.3",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.3-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/v0.3/MeetingRecorder-v0.3-win-x64.zip",
                  "size": 166259851
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.3",
                "0.3",
                null,
                null),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpToDate, result.Status);
        Assert.False(result.IsNewerByPublishedAt);
        Assert.False(result.IsNewerByAssetSize);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpdateAvailable_When_AssetSize_Changed_For_The_Same_Version()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.2",
              "published_at": "2026-03-16T14:00:00Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.2",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/v0.2/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 999
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                DateTimeOffset.Parse("2026-03-16T14:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                123456789),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.False(result.IsNewerByPublishedAt);
        Assert.True(result.IsNewerByAssetSize);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_UpdateAvailable_When_Same_Version_Asset_Was_Refreshed()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "e82258e",
              "name": "Meeting Recorder v0.2",
              "published_at": "2026-03-17T19:28:22Z",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/e82258e",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://github.com/shp847/meetingrecorder/releases/download/e82258e/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 74220015,
                  "updated_at": "2026-03-19T18:35:44Z"
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.2",
                "e82258e",
                DateTimeOffset.Parse("2026-03-17T19:28:22Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                74220015),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.True(result.IsNewerByPublishedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-03-19T18:35:44Z", null, System.Globalization.DateTimeStyles.RoundtripKind), result.LatestPublishedAtUtc);
        Assert.Contains("newer published build", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Prefers_WinX64_Zip_When_GitHub_Release_Has_Multiple_Assets()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.3",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.3",
              "assets": [
                {
                  "name": "Install-LatestFromGitHub.cmd",
                  "browser_download_url": "https://example.com/Install-LatestFromGitHub.cmd",
                  "size": 123
                },
                {
                  "name": "MeetingRecorder-v0.3-win-arm64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.3-win-arm64.zip",
                  "size": 456
                },
                {
                  "name": "MeetingRecorder-v0.3-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.3-win-x64.zip",
                  "size": 789
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                null,
                null),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal("https://example.com/MeetingRecorder-v0.3-win-x64.zip", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Prefers_App_Zip_When_Model_Binary_Appears_First()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.3",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.3",
              "assets": [
                {
                  "name": "ggml-medium.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-medium.en-q8_0.bin",
                  "size": 823382461
                },
                {
                  "name": "MeetingRecorder-v0.3-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.3-win-x64.zip",
                  "size": 147017922
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.2",
                "0.2",
                DateTimeOffset.Parse("2026-03-21T21:57:12Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                104047356),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.UpdateAvailable, result.Status);
        Assert.Equal("https://example.com/MeetingRecorder-v0.3-win-x64.zip", result.DownloadUrl);
        Assert.Equal(147017922L, result.LatestAssetSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Ignores_Model_Bundle_Zips_When_Selecting_The_App_Update_Asset()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.3",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.3",
              "assets": [
                {
                  "name": "meeting-recorder-diarization-bundle-accurate-win-x64.zip",
                  "browser_download_url": "https://example.com/meeting-recorder-diarization-bundle-accurate-win-x64.zip",
                  "size": 95853218
                },
                {
                  "name": "MeetingRecorder-v0.3-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.3-win-x64.zip",
                  "size": 271610979
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                AppBranding.Version,
                AppBranding.Version,
                null,
                null),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal("https://example.com/MeetingRecorder-v0.3-win-x64.zip", result.DownloadUrl);
        Assert.Equal(271610979L, result.LatestAssetSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdateAsync_Returns_Error_When_GitHub_Release_Has_Only_Model_Assets()
    {
        var service = new AppUpdateService(new FakeUpdateFeedClient("""
            {
              "tag_name": "v0.3",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/v0.3",
              "assets": [
                {
                  "name": "ggml-medium.en-q8_0.bin",
                  "browser_download_url": "https://example.com/ggml-medium.en-q8_0.bin",
                  "size": 823382461
                },
                {
                  "name": "meeting-recorder-diarization-bundle-standard-win-x64.zip",
                  "browser_download_url": "https://example.com/meeting-recorder-diarization-bundle-standard-win-x64.zip",
                  "size": 38614922
                }
              ]
            }
            """));

        var result = await service.CheckForUpdateAsync(
            new AppUpdateLocalState(
                "0.2",
                "0.2",
                DateTimeOffset.Parse("2026-03-21T21:57:12Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                104047356),
            "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
            enabled: true);

        Assert.Equal(AppUpdateStatusKind.Error, result.Status);
        Assert.Null(result.DownloadUrl);
        Assert.Contains("Meeting Recorder app ZIP", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadUpdateAsync_Rejects_NonApp_Asset_Without_Downloading()
    {
        var updatesDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderUpdateDownloadTests", Guid.NewGuid().ToString("N"));
        var client = new FakeUpdateFeedClient(
            "{}",
            (_, _, _) => throw new InvalidOperationException("The downloader should not be called for a model asset."));
        var service = new AppUpdateService(client, updatesDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(
            "https://example.com/releases/download/v0.3/ggml-medium.en-q8_0.bin",
            "0.3",
            823382461,
            CancellationToken.None));

        Assert.Contains("Meeting Recorder app ZIP", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(client.DownloadWasCalled);
        Assert.False(Directory.Exists(updatesDirectory));
    }

    [Fact]
    public async Task DownloadUpdateAsync_Deletes_Temp_And_Final_File_When_Size_Does_Not_Match()
    {
        var updatesDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderUpdateDownloadTests", Guid.NewGuid().ToString("N"));
        var payload = CreateZipBytes(("MeetingRecorder.App.exe", "app"));
        var client = new FakeUpdateFeedClient(
            "{}",
            async (_, destinationPath, cancellationToken) => await File.WriteAllBytesAsync(destinationPath, payload, cancellationToken));
        var service = new AppUpdateService(client, updatesDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(
            "https://example.com/releases/download/v0.3/MeetingRecorder-v0.3-win-x64.zip",
            "0.3",
            payload.Length + 1,
            CancellationToken.None));

        Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(updatesDirectory, "MeetingRecorder-v0.3-win-x64.zip")));
        Assert.False(File.Exists(Path.Combine(updatesDirectory, "MeetingRecorder-v0.3-win-x64.zip.download")));
    }

    [Fact]
    public async Task DownloadUpdateAsync_Deletes_Temp_And_Final_File_When_Zip_Is_Corrupt()
    {
        var updatesDirectory = Path.Combine(Path.GetTempPath(), "MeetingRecorderUpdateDownloadTests", Guid.NewGuid().ToString("N"));
        var payload = Encoding.UTF8.GetBytes("not a zip");
        var client = new FakeUpdateFeedClient(
            "{}",
            async (_, destinationPath, cancellationToken) => await File.WriteAllBytesAsync(destinationPath, payload, cancellationToken));
        var service = new AppUpdateService(client, updatesDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadUpdateAsync(
            "https://example.com/releases/download/v0.3/MeetingRecorder-v0.3-win-x64.zip",
            "0.3",
            payload.Length,
            CancellationToken.None));

        Assert.Contains("readable ZIP", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(updatesDirectory, "MeetingRecorder-v0.3-win-x64.zip")));
        Assert.False(File.Exists(Path.Combine(updatesDirectory, "MeetingRecorder-v0.3-win-x64.zip.download")));
    }

    private sealed class FakeUpdateFeedClient : IAppUpdateFeedClient
    {
        private readonly string _responseBody;
        private readonly Func<string, string, CancellationToken, Task>? _downloadHandler;

        public FakeUpdateFeedClient(
            string responseBody,
            Func<string, string, CancellationToken, Task>? downloadHandler = null)
        {
            _responseBody = responseBody;
            _downloadHandler = downloadHandler;
        }

        public bool DownloadWasCalled { get; private set; }

        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            Assert.StartsWith("https://", feedUrl, StringComparison.Ordinal);
            return Task.FromResult(_responseBody);
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            DownloadWasCalled = true;
            return _downloadHandler is null
                ? throw new NotSupportedException("Downloads are not exercised in this test.")
                : _downloadHandler(downloadUrl, destinationPath, cancellationToken);
        }
    }

    private static byte[] CreateZipBytes(params (string RelativePath, string Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (relativePath, contents) in entries)
            {
                var entry = archive.CreateEntry(relativePath);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                writer.Write(contents);
            }
        }

        return stream.ToArray();
    }
}
