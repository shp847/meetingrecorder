using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class VoiceProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"MeetingRecorderVoiceProfiles-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync_Creates_Versioned_Empty_Profile_Document()
    {
        var path = Path.Combine(_root, "speaker-profiles", "voice-profiles.json");
        var store = new VoiceProfileStore(path);

        var document = await store.LoadOrCreateAsync();

        Assert.Equal(1, document.SchemaVersion);
        Assert.Empty(document.Profiles);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAsync_Persists_Profiles_Atomically()
    {
        var path = Path.Combine(_root, "speaker-profiles", "voice-profiles.json");
        var store = new VoiceProfileStore(path);
        var now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
        var document = new VoiceProfileStoreDocument(
            1,
            now,
            [
                new VoiceProfile(
                    "voice_1",
                    "Pranav Sharma",
                    "embedding.onnx",
                    3,
                    [1f, 0f, 0f],
                    2,
                    ["meeting-a"],
                    now.AddMinutes(-5),
                    VoiceProfileStatus.Active),
            ]);

        await store.SaveAsync(document);
        var reloaded = await store.LoadOrCreateAsync();

        var profile = Assert.Single(reloaded.Profiles);
        Assert.Equal("voice_1", profile.ProfileId);
        Assert.Equal("Pranav Sharma", profile.DisplayName);
        Assert.Equal([1f, 0f, 0f], profile.Centroid);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task DisableProfileAsync_Disables_Selected_Profile()
    {
        var path = Path.Combine(_root, "speaker-profiles", "voice-profiles.json");
        var store = new VoiceProfileStore(path);
        var now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
        await store.SaveAsync(new VoiceProfileStoreDocument(
            1,
            now,
            [Profile("voice_1", "Pranav Sharma", now)]));

        await store.DisableProfileAsync("voice_1");

        var profile = Assert.Single((await store.LoadOrCreateAsync()).Profiles);
        Assert.Equal(VoiceProfileStatus.Disabled, profile.Status);
    }

    [Fact]
    public async Task DeleteProfileAsync_Removes_Selected_Profile()
    {
        var path = Path.Combine(_root, "speaker-profiles", "voice-profiles.json");
        var store = new VoiceProfileStore(path);
        var now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
        await store.SaveAsync(new VoiceProfileStoreDocument(
            1,
            now,
            [
                Profile("voice_1", "Pranav Sharma", now),
                Profile("voice_2", "Terry Jones", now),
            ]));

        await store.DeleteProfileAsync("voice_1");

        var profile = Assert.Single((await store.LoadOrCreateAsync()).Profiles);
        Assert.Equal("voice_2", profile.ProfileId);
    }

    [Fact]
    public async Task LoadOrCreateAsync_Recovers_From_Corrupt_Profile_Store()
    {
        var path = Path.Combine(_root, "speaker-profiles", "voice-profiles.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json");
        var store = new VoiceProfileStore(path);

        var document = await store.LoadOrCreateAsync();

        Assert.Empty(document.Profiles);
        Assert.True(File.Exists(path));
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, "voice-profiles.json.corrupt-*"));
    }

    private static VoiceProfile Profile(string profileId, string displayName, DateTimeOffset now)
    {
        return new VoiceProfile(
            profileId,
            displayName,
            "embedding.onnx",
            3,
            [1f, 0f, 0f],
            2,
            ["meeting-a"],
            now,
            VoiceProfileStatus.Active,
            []);
    }
}
