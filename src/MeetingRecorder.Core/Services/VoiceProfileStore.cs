using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core.Services;

[JsonConverter(typeof(JsonStringEnumConverter<VoiceProfileStatus>))]
public enum VoiceProfileStatus
{
    Active = 0,
    Disabled = 1,
}

public sealed record VoiceProfile(
    string ProfileId,
    string DisplayName,
    string EmbeddingModelFileName,
    int EmbeddingDimension,
    IReadOnlyList<float> Centroid,
    int SampleCount,
    IReadOnlyList<string> ConfirmedMeetingIds,
    DateTimeOffset? LastMatchedAtUtc,
    VoiceProfileStatus Status);

public sealed record VoiceProfileStoreDocument(
    int SchemaVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<VoiceProfile> Profiles);

public sealed class VoiceProfileStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public VoiceProfileStore(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
        {
            throw new ArgumentException("A voice profile store path is required.", nameof(storePath));
        }

        StorePath = storePath;
    }

    public string StorePath { get; }

    public async Task<VoiceProfileStoreDocument> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(GetStoreDirectory());

        if (!File.Exists(StorePath))
        {
            var created = CreateEmptyDocument();
            await SaveAsync(created, cancellationToken);
            return created;
        }

        try
        {
            await using var stream = File.OpenRead(StorePath);
            var document = await JsonSerializer.DeserializeAsync<VoiceProfileStoreDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            return Normalize(document);
        }
        catch (JsonException)
        {
            TryBackupCorruptStore();
            var created = CreateEmptyDocument();
            await SaveAsync(created, cancellationToken);
            return created;
        }
    }

    public async Task SaveAsync(
        VoiceProfileStoreDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(GetStoreDirectory());
        var normalized = Normalize(document) with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        var tempPath = StorePath + ".tmp";
        var backupPath = StorePath + ".bak";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
        }

        if (File.Exists(StorePath))
        {
            File.Replace(tempPath, StorePath, backupPath, ignoreMetadataErrors: true);
            File.Delete(backupPath);
            return;
        }

        File.Move(tempPath, StorePath);
    }

    public async Task<VoiceProfileStoreDocument> UpdateAsync(
        Func<VoiceProfileStoreDocument, VoiceProfileStoreDocument> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var current = await LoadOrCreateAsync(cancellationToken);
        var updated = Normalize(update(current));
        await SaveAsync(updated, cancellationToken);
        return updated;
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(StorePath))
        {
            File.Delete(StorePath);
        }

        return Task.CompletedTask;
    }

    private string GetStoreDirectory()
    {
        return Path.GetDirectoryName(StorePath)
            ?? throw new InvalidOperationException("The voice profile store path must include a directory.");
    }

    private static VoiceProfileStoreDocument CreateEmptyDocument()
    {
        return new VoiceProfileStoreDocument(
            CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            Array.Empty<VoiceProfile>());
    }

    private static VoiceProfileStoreDocument Normalize(VoiceProfileStoreDocument? document)
    {
        if (document is null)
        {
            return CreateEmptyDocument();
        }

        var profiles = document.Profiles?
            .Where(profile =>
                !string.IsNullOrWhiteSpace(profile.ProfileId) &&
                !string.IsNullOrWhiteSpace(profile.DisplayName) &&
                !string.IsNullOrWhiteSpace(profile.EmbeddingModelFileName) &&
                profile.EmbeddingDimension > 0 &&
                profile.Centroid.Count == profile.EmbeddingDimension)
            .GroupBy(profile => profile.ProfileId.Trim(), StringComparer.Ordinal)
            .Select(group => NormalizeProfile(group.Last()))
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProfileId, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<VoiceProfile>();

        return new VoiceProfileStoreDocument(
            CurrentSchemaVersion,
            document.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : document.UpdatedAtUtc,
            profiles);
    }

    private static VoiceProfile NormalizeProfile(VoiceProfile profile)
    {
        return profile with
        {
            ProfileId = profile.ProfileId.Trim(),
            DisplayName = NormalizeDisplayName(profile.DisplayName),
            EmbeddingModelFileName = profile.EmbeddingModelFileName.Trim(),
            Centroid = NormalizeVector(profile.Centroid),
            SampleCount = Math.Max(1, profile.SampleCount),
            ConfirmedMeetingIds = profile.ConfirmedMeetingIds?
                .Where(meetingId => !string.IsNullOrWhiteSpace(meetingId))
                .Select(meetingId => meetingId.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>(),
            Status = Enum.IsDefined(profile.Status) ? profile.Status : VoiceProfileStatus.Active,
        };
    }

    private static string NormalizeDisplayName(string displayName)
    {
        return string.Join(
            " ",
            displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static float[] NormalizeVector(IReadOnlyList<float> vector)
    {
        var sumSquares = vector.Sum(value => (double)value * value);
        if (sumSquares <= 0d)
        {
            return vector.ToArray();
        }

        var magnitude = Math.Sqrt(sumSquares);
        return vector.Select(value => (float)(value / magnitude)).ToArray();
    }

    private void TryBackupCorruptStore()
    {
        try
        {
            File.Copy(
                StorePath,
                StorePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"),
                overwrite: false);
        }
        catch
        {
            // Best effort only. The app can recover with an empty local profile store.
        }
    }
}
