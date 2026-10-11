using MeetingRecorder.Core.Services;
using NAudio.Wave;

namespace MeetingRecorder.Core.Tests;

public sealed class SpeakerReviewClipServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_Writes_Bounded_Opaque_Local_Clip_Without_Changing_Source()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.wav");
            WriteSilence(source, seconds: 20);
            var before = new FileInfo(source);
            var service = new SpeakerReviewClipService(Path.Combine(root, "speaker-review"));

            var result = await service.GetOrCreateAsync(Request(source, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)));

            Assert.True(result.IsAvailable);
            Assert.NotNull(result.ClipPath);
            Assert.Equal(".wav", Path.GetExtension(result.ClipPath));
            Assert.DoesNotContain("meeting", Path.GetFileName(result.ClipPath), StringComparison.OrdinalIgnoreCase);
            Assert.InRange(result.Duration.TotalSeconds, 6, 12);
            var after = new FileInfo(source);
            Assert.Equal(before.Length, after.Length);
            Assert.Equal(before.LastWriteTimeUtc, after.LastWriteTimeUtc);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task GetOrCreateAsync_Uses_Fingerprint_Cache_Then_Invalidates_After_Source_Change()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.wav");
            WriteSilence(source, seconds: 20);
            var service = new SpeakerReviewClipService(Path.Combine(root, "speaker-review"));
            var first = await service.GetOrCreateAsync(Request(source, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)));
            var second = await service.GetOrCreateAsync(Request(source, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)));

            Assert.Equal(first.ClipPath, second.ClipPath);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(2));
            var changed = await service.GetOrCreateAsync(Request(source, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)));

            Assert.NotEqual(first.ClipPath, changed.ClipPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task GetOrCreateAsync_Returns_Unavailable_For_Short_Or_Missing_Audio()
    {
        var root = CreateRoot();
        try
        {
            var shortSource = Path.Combine(root, "short.wav");
            WriteSilence(shortSource, seconds: 2);
            var service = new SpeakerReviewClipService(Path.Combine(root, "speaker-review"));

            var shortResult = await service.GetOrCreateAsync(Request(shortSource, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            var missingResult = await service.GetOrCreateAsync(Request(Path.Combine(root, "missing.wav"), TimeSpan.Zero, TimeSpan.FromSeconds(1)));

            Assert.Equal(SpeakerReviewClipState.Unavailable, shortResult.State);
            Assert.Equal(SpeakerReviewClipState.Unavailable, missingResult.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CleanupAsync_Does_Not_Delete_Leased_Clip()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "source.wav");
            WriteSilence(source, seconds: 20);
            var service = new SpeakerReviewClipService(Path.Combine(root, "speaker-review"));
            var result = await service.GetOrCreateAsync(Request(source, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)));
            File.SetLastWriteTimeUtc(result.ClipPath!, DateTime.UtcNow.AddDays(-4));

            using (service.AcquireLease(result.ClipPath!))
            {
                await service.CleanupAsync();
                Assert.True(File.Exists(result.ClipPath));
            }

            await service.CleanupAsync();
            Assert.False(File.Exists(result.ClipPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SpeakerReviewClipRequest Request(string path, TimeSpan start, TimeSpan end) =>
        new("meeting-stable-id", "revision", "speaker-id", start, end, path);

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteSilence(string path, int seconds)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1));
        writer.Write(new byte[16_000 * 2 * seconds], 0, 16_000 * 2 * seconds);
    }
}
