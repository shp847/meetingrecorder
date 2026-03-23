using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class GitHubReleaseBootstrapServiceTests
{
    [Fact]
    public async Task GetLatestReleaseAsync_Returns_Main_Zip_And_Backup_Script_Assets()
    {
        var service = new GitHubReleaseBootstrapService(new FakeFeedClient("""
            {
              "tag_name": "alpha",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/alpha",
              "published_at": "2026-03-17T03:12:10Z",
              "assets": [
                {
                  "name": "MeetingRecorderInstaller.exe",
                  "browser_download_url": "https://example.com/MeetingRecorderInstaller.exe",
                  "size": 48123456
                },
                {
                  "name": "Install-LatestFromGitHub.cmd",
                  "browser_download_url": "https://example.com/Install-LatestFromGitHub.cmd",
                  "size": 1643
                },
                {
                  "name": "Install-LatestFromGitHub.ps1",
                  "browser_download_url": "https://example.com/Install-LatestFromGitHub.ps1",
                  "size": 13821
                },
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 74122056
                }
              ]
            }
            """));

        var release = await service.GetLatestReleaseAsync("https://api.github.com/repos/shp847/meetingrecorder/releases/latest");

        Assert.Equal("0.2", release.Version);
        Assert.Equal("https://github.com/shp847/meetingrecorder/releases/tag/alpha", release.ReleasePageUrl);
        Assert.Equal("MeetingRecorderInstaller.exe", release.InstallerExecutableAsset?.Name);
        Assert.Equal("https://example.com/MeetingRecorderInstaller.exe", release.InstallerExecutableAsset?.DownloadUrl);
        Assert.Equal("MeetingRecorder-v0.2-win-x64.zip", release.AppZipAsset.Name);
        Assert.Equal("https://example.com/MeetingRecorder-v0.2-win-x64.zip", release.AppZipAsset.DownloadUrl);
        Assert.Equal("Install-LatestFromGitHub.cmd", release.BackupCommandAsset?.Name);
        Assert.Equal("Install-LatestFromGitHub.ps1", release.BackupPowerShellAsset?.Name);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Uses_Release_Name_Version_When_Tag_Is_Not_Semver()
    {
        var service = new GitHubReleaseBootstrapService(new FakeFeedClient("""
            {
              "tag_name": "beta",
              "name": "Meeting Recorder v0.2",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/beta",
              "published_at": "2026-03-17T19:28:22Z",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.1-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.1-win-x64.zip",
                  "size": 74158317
                }
              ]
            }
            """));

        var release = await service.GetLatestReleaseAsync("https://api.github.com/repos/shp847/meetingrecorder/releases/latest");

        Assert.Equal("0.2", release.Version);
        Assert.Equal("https://github.com/shp847/meetingrecorder/releases/tag/beta", release.ReleasePageUrl);
        Assert.Equal("MeetingRecorder-v0.1-win-x64.zip", release.AppZipAsset.Name);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Uses_AppZip_UpdatedAt_When_It_Is_Newer_Than_Release_PublishedAt()
    {
        var service = new GitHubReleaseBootstrapService(new FakeFeedClient("""
            {
              "tag_name": "e82258e",
              "name": "Meeting Recorder v0.2",
              "html_url": "https://github.com/shp847/meetingrecorder/releases/tag/e82258e",
              "published_at": "2026-03-17T19:28:22Z",
              "assets": [
                {
                  "name": "MeetingRecorder-v0.2-win-x64.zip",
                  "browser_download_url": "https://example.com/MeetingRecorder-v0.2-win-x64.zip",
                  "size": 74220015,
                  "updated_at": "2026-03-19T18:35:44Z"
                }
              ]
            }
            """));

        var release = await service.GetLatestReleaseAsync("https://api.github.com/repos/shp847/meetingrecorder/releases/latest");

        Assert.Equal(DateTimeOffset.Parse("2026-03-19T18:35:44Z", null, System.Globalization.DateTimeStyles.RoundtripKind), release.PublishedAtUtc);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Throws_When_No_Installer_Zip_Asset_Exists()
    {
        var service = new GitHubReleaseBootstrapService(new FakeFeedClient("""
            {
              "tag_name": "alpha",
              "assets": [
                {
                  "name": "Install-LatestFromGitHub.cmd",
                  "browser_download_url": "https://example.com/Install-LatestFromGitHub.cmd"
                }
              ]
            }
            """));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetLatestReleaseAsync("https://api.github.com/repos/shp847/meetingrecorder/releases/latest"));
    }

    private sealed class FakeFeedClient : IAppUpdateFeedClient
    {
        private readonly string _payload;

        public FakeFeedClient(string payload)
        {
            _payload = payload;
        }

        public Task<string> GetStringAsync(string feedUrl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_payload);
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
