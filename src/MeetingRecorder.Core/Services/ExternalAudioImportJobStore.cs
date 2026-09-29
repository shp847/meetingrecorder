using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record ExternalAudioImportJobLoadResult(
    ExternalAudioImportJob? Job,
    ExternalAudioImportJobSchemaCompatibility Schema);

public sealed record ExternalAudioImportJobMigrationResult(
    ExternalAudioImportJob? Job,
    bool Migrated,
    string? RecoveryText);

public sealed class ExternalAudioImportJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ExternalAudioImportJobStore(string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            throw new ArgumentException("An import job path is required.", nameof(jobPath));
        }

        JobPath = jobPath;
    }

    public string JobPath { get; }

    public async Task<ExternalAudioImportJobLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = File.OpenRead(JobPath);
        var job = await JsonSerializer.DeserializeAsync<ExternalAudioImportJob>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("The import job record is empty.");
        var schema = ExternalAudioImportJobSchemaCompatibility.Resolve(job);
        return new ExternalAudioImportJobLoadResult(
            job with { IsReadOnly = job.IsReadOnly || !schema.CanWrite },
            schema);
    }

    public Task<ExternalAudioImportJobLoadResult> LoadOrProjectLegacyAsync(
        MeetingSessionManifest manifest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(JobPath))
        {
            return LoadAsync(cancellationToken);
        }

        var job = ExternalAudioImportJobFactory.CreateLegacyReadOnlyProjection(manifest, nowUtc);
        return Task.FromResult(new ExternalAudioImportJobLoadResult(
            job,
            ExternalAudioImportJobSchemaCompatibility.Resolve(job)));
    }

    /// <summary>
    /// Creates the companion record only for an explicit recovery or migration action.
    /// A normal read remains non-mutating so an older app's manifest is never silently
    /// rewritten merely because it was discovered at startup.
    /// </summary>
    public async Task<ExternalAudioImportJobMigrationResult> MigrateLegacyManifestAsync(
        MeetingSessionManifest manifest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(JobPath))
        {
            var existing = await LoadAsync(cancellationToken);
            return new ExternalAudioImportJobMigrationResult(
                existing.Job,
                Migrated: false,
                existing.Schema.CanWrite ? null : existing.Schema.RecoveryText);
        }

        if (manifest.ImportedSourceAudio is null)
        {
            throw new InvalidOperationException("Only an imported-session manifest can be migrated to an import job.");
        }

        var sessionRoot = Path.GetDirectoryName(JobPath)
            ?? throw new InvalidOperationException("The import job path must include a session directory.");
        var stagedWorkIdentity = GetSafeStagedWorkIdentity(manifest, sessionRoot);
        var migrated = ExternalAudioImportJobFactory.CreateLegacyReadOnlyProjection(manifest, nowUtc) with
        {
            StagedWorkIdentity = stagedWorkIdentity,
            IsReadOnly = false,
        };

        await SaveAsync(migrated, cancellationToken);
        return new ExternalAudioImportJobMigrationResult(migrated, Migrated: true, RecoveryText: null);
    }

    public async Task SaveAsync(
        ExternalAudioImportJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        var schema = ExternalAudioImportJobSchemaCompatibility.Resolve(job);
        if (!schema.CanWrite)
        {
            throw new InvalidOperationException(schema.RecoveryText);
        }

        var directory = Path.GetDirectoryName(JobPath)
            ?? throw new InvalidOperationException("The import job path must include a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = JobPath + ".tmp";
        var backupPath = JobPath + ".bak";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, job, SerializerOptions, cancellationToken);
            }

            if (File.Exists(JobPath))
            {
                File.Replace(temporaryPath, JobPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, JobPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale backup does not make a committed job unsafe.
        }
    }

    private static string GetSafeStagedWorkIdentity(
        MeetingSessionManifest manifest,
        string sessionRoot)
    {
        if (string.IsNullOrWhiteSpace(manifest.MergedAudioPath))
        {
            throw new InvalidOperationException("The imported-session manifest has no staged audio path to recover.");
        }

        var normalizedSessionRoot = Path.GetFullPath(sessionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedStagedPath = Path.GetFullPath(manifest.MergedAudioPath);
        var allowedPrefix = normalizedSessionRoot + Path.DirectorySeparatorChar + "processing" + Path.DirectorySeparatorChar;
        if (!normalizedStagedPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The imported-session staged audio path is outside the app-owned processing directory.");
        }

        if (!File.Exists(normalizedStagedPath))
        {
            throw new InvalidOperationException("The imported-session staged audio file is unavailable for safe recovery.");
        }

        var identity = Path.GetRelativePath(normalizedSessionRoot, normalizedStagedPath);
        if (Path.IsPathFullyQualified(identity) ||
            identity.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(identity, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The imported-session staged audio path cannot be recovered safely.");
        }

        return identity;
    }
}
