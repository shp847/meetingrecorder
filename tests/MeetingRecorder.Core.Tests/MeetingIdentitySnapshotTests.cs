using System.Text.Json;
using MeetingRecorder.Core.Domain;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingIdentitySnapshotTests
{
    private static readonly DateTimeOffset CapturedAtUtc = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Builder_Stores_Only_Keyed_Metadata_And_Rejects_Generic_Title()
    {
        var builder = new MeetingIdentitySnapshotBuilder(TestKey(1));
        var snapshot = builder.Create(Evidence("Roadmap review", "Roadmap review | Microsoft Teams", audio: true));
        var generic = builder.Create(Evidence("Microsoft Teams", "Sharing control bar", audio: true));
        var json = JsonSerializer.Serialize(snapshot);

        Assert.NotNull(snapshot);
        Assert.Equal(MeetingIdentityEvidenceTier.Strong, snapshot.EvidenceTier);
        Assert.DoesNotContain("Roadmap", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Teams", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("windowTitle", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(generic);
    }

    [Fact]
    public void Builder_Uses_Durable_Code_And_Key_Rotation_Makes_Comparison_Unknown()
    {
        var first = new MeetingIdentitySnapshotBuilder(TestKey(1)).Create(Evidence("Meet abc-defg-hij", null, audio: true));
        var rotated = new MeetingIdentitySnapshotBuilder(TestKey(2)).Create(Evidence("Meet abc-defg-hij", null, audio: true));
        var result = new MeetingContinuityMatcher().Compare(first, rotated, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1));

        Assert.NotNull(first);
        Assert.NotNull(rotated);
        Assert.NotEqual(first.DurableIdentityToken, rotated.DurableIdentityToken);
        Assert.Equal(MeetingIdentityVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void Matcher_Uses_One_Parity_Contract_For_All_Modes()
    {
        var builder = new MeetingIdentitySnapshotBuilder(TestKey(1));
        var left = builder.Create(Evidence("Architecture review", null, audio: true));
        var right = builder.Create(Evidence("Architecture Review", null, audio: true));
        var matcher = new MeetingContinuityMatcher();

        var expected = matcher.Compare(left, right, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1));
        Assert.Equal(MeetingIdentityVerdict.SameMeeting, expected.Verdict);
        Assert.Equal(expected, matcher.Compare(left, right, MeetingIdentityComparisonMode.RuntimeToManifest, CapturedAtUtc.AddMinutes(1)));
        Assert.Equal(expected, matcher.Compare(left, right, MeetingIdentityComparisonMode.ManifestToManifest, CapturedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void Matcher_Preserves_Conflict_Expiry_And_Fingerprint_Safety()
    {
        var builder = new MeetingIdentitySnapshotBuilder(TestKey(1));
        var left = builder.Create(Evidence("Architecture review", null, audio: true));
        var conflicting = builder.Create(Evidence("Budget review", null, audio: true));
        var expired = left! with { ExpiresAtUtc = CapturedAtUtc.AddSeconds(-1) };
        var collision = conflicting! with { Fingerprint = left.Fingerprint };
        var matcher = new MeetingContinuityMatcher();

        Assert.Equal(MeetingIdentityVerdict.DifferentMeeting, matcher.Compare(left, conflicting, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1)).Verdict);
        Assert.Equal(MeetingIdentityVerdict.Unknown, matcher.Compare(left, expired, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1)).Verdict);
        Assert.Equal(MeetingIdentityVerdict.DifferentMeeting, matcher.Compare(left, collision, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1)).Verdict);
    }

    [Fact]
    public void Matcher_Leaves_Medium_And_Weak_Evidence_For_Grace_Owners()
    {
        var builder = new MeetingIdentitySnapshotBuilder(TestKey(1));
        var medium = builder.Create(Evidence("Architecture review", null, audio: false, host: true));
        var weak = builder.Create(Evidence("Architecture review", null, audio: false, host: false));
        var matcher = new MeetingContinuityMatcher();

        Assert.Equal(MeetingIdentityEvidenceTier.Medium, medium?.EvidenceTier);
        Assert.Equal(MeetingIdentityEvidenceTier.Weak, weak?.EvidenceTier);
        Assert.Equal(MeetingIdentityVerdict.Unknown, matcher.Compare(medium, medium, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1)).Verdict);
        Assert.Equal(MeetingIdentityVerdict.Unknown, matcher.Compare(weak, weak, MeetingIdentityComparisonMode.RuntimeToRuntime, CapturedAtUtc.AddMinutes(1)).Verdict);
    }

    [Fact]
    public async Task Legacy_Manifests_Backfill_Only_On_Normal_Atomic_Save()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var keyPath = Path.Combine(root, "identity.key");
        var manifestPath = Path.Combine(root, "work", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var legacy = new MeetingSessionManifest
        {
            SessionId = "session-a",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Architecture review",
            StartedAtUtc = CapturedAtUtc,
            State = SessionState.Queued,
        };
        var legacyJson = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(manifestPath, legacyJson);
        var service = new MeetingIdentitySnapshotService(new MeetingIdentityKeyStore(keyPath));
        var store = new SessionManifestStore(new ArtifactPathBuilder(), service);

        var loaded = await store.LoadAsync(manifestPath);
        Assert.Null(loaded.IdentitySnapshot);
        Assert.Equal(legacyJson, await File.ReadAllTextAsync(manifestPath));
        Assert.NotNull(store.GetIdentitySnapshotForComparison(loaded));

        await store.SaveAsync(loaded, manifestPath);
        var persisted = await store.LoadAsync(manifestPath);
        Assert.NotNull(persisted.IdentitySnapshot);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(manifestPath)!, "*.tmp"));
    }

    [Fact]
    public void Corrupt_Or_Future_Stored_Snapshot_Is_Unknown_And_Not_Replaced()
    {
        var builder = new MeetingIdentitySnapshotBuilder(TestKey(1));
        var current = builder.Create(Evidence("Architecture review", null, audio: true))!;
        var future = current with { CapturedAtUtc = CapturedAtUtc.AddHours(1), ExpiresAtUtc = CapturedAtUtc.AddHours(2) };
        var manifest = new MeetingSessionManifest
        {
            SessionId = "session-a",
            Platform = MeetingPlatform.Teams,
            DetectedTitle = "Architecture review",
            StartedAtUtc = CapturedAtUtc,
            State = SessionState.Queued,
            IdentitySnapshot = future,
        };
        var service = new MeetingIdentitySnapshotService(new MeetingIdentityKeyStore(Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"), "identity.key")));

        Assert.Same(future, service.EnsureForNormalSave(manifest).IdentitySnapshot);
        Assert.Equal(MeetingIdentityVerdict.Unknown, new MeetingContinuityMatcher().Compare(future, current, MeetingIdentityComparisonMode.ManifestToManifest, CapturedAtUtc).Verdict);
    }

    private static MeetingIdentityEvidence Evidence(string title, string? window, bool audio, bool host = true) =>
        new(MeetingPlatform.Teams, null, title, window, audio, host, CapturedAtUtc);

    private static byte[] TestKey(byte value) => Enumerable.Repeat(value, 32).ToArray();
}
