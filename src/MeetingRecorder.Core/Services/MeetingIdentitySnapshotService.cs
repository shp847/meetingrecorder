using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Services;

public sealed record MeetingIdentityEvidence(
    MeetingPlatform Platform,
    string? DurableMeetingCode,
    string? MeetingTitle,
    string? WindowTitle,
    bool HasAttributedCapture,
    bool HasHostContext,
    DateTimeOffset CapturedAtUtc);

public sealed class MeetingIdentityKeyStore
{
    private const int KeyLength = 32;
    private readonly string _path;

    public MeetingIdentityKeyStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public byte[] GetOrCreate()
    {
        if (File.Exists(_path))
        {
            var existing = File.ReadAllBytes(_path);
            if (existing.Length == KeyLength)
            {
                return existing;
            }

            throw new InvalidOperationException("Meeting identity key has an invalid length.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var generated = RandomNumberGenerator.GetBytes(KeyLength);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, generated);
            try
            {
                File.Move(temporaryPath, _path, overwrite: false);
            }
            catch (IOException) when (File.Exists(_path))
            {
                var concurrent = File.ReadAllBytes(_path);
                if (concurrent.Length == KeyLength)
                {
                    return concurrent;
                }

                throw new InvalidOperationException("Meeting identity key has an invalid length.");
            }

            return generated;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed class MeetingIdentitySnapshotBuilder
{
    private static readonly TimeSpan StrongLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan HintLifetime = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> GenericTitles = new(StringComparer.Ordinal)
    {
        "microsoft teams", "teams", "sharing control bar", "meeting", "google meet", "zoom", "zoom meeting",
    };
    private static readonly Regex GoogleMeetCode = new("\\b[a-z]{3}-[a-z]{4}-[a-z]{3}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly byte[] _key;

    public MeetingIdentitySnapshotBuilder(byte[] localKey)
    {
        ArgumentNullException.ThrowIfNull(localKey);
        if (localKey.Length < 32)
        {
            throw new ArgumentException("Meeting identity keys must be at least 256 bits.", nameof(localKey));
        }

        _key = localKey.ToArray();
    }

    public MeetingIdentitySnapshot? Create(MeetingIdentityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Platform == MeetingPlatform.Unknown || evidence.CapturedAtUtc == default)
        {
            return null;
        }

        var sources = new List<MeetingIdentityEvidenceSource>();
        var durableCode = NormalizeDurableCode(evidence.DurableMeetingCode) ??
                          NormalizeDurableCode(evidence.MeetingTitle) ??
                          NormalizeDurableCode(evidence.WindowTitle);
        if (durableCode is not null)
        {
            sources.Add(MeetingIdentityEvidenceSource.DurableMeetingCode);
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        AddSpecificCandidate(candidates, evidence.MeetingTitle, MeetingIdentityEvidenceSource.ManifestTitle, sources);
        AddSpecificCandidate(candidates, evidence.WindowTitle, MeetingIdentityEvidenceSource.AudioWindow, sources);
        if (evidence.HasAttributedCapture)
        {
            sources.Add(MeetingIdentityEvidenceSource.AudioApplication);
        }

        var isAmbiguous = candidates.Count > 1;
        var specificTitle = isAmbiguous ? null : candidates.SingleOrDefault();
        var durableToken = durableCode is null ? null : Token("durable", evidence.Platform, durableCode);
        var titleToken = specificTitle is null ? null : Token("title", evidence.Platform, specificTitle);
        var tier = ClassifyTier(durableToken, titleToken, evidence.HasAttributedCapture, evidence.HasHostContext, isAmbiguous);
        if (tier == MeetingIdentityEvidenceTier.None)
        {
            return null;
        }

        var capturedAtUtc = evidence.CapturedAtUtc.ToUniversalTime();
        var expiresAtUtc = capturedAtUtc + (tier == MeetingIdentityEvidenceTier.Strong ? StrongLifetime : HintLifetime);
        var canonical = string.Join("|", [
            MeetingIdentitySnapshot.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)evidence.Platform).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)tier).ToString(System.Globalization.CultureInfo.InvariantCulture),
            durableToken ?? string.Empty,
            titleToken ?? string.Empty,
            evidence.HasAttributedCapture ? "1" : "0",
            evidence.HasHostContext ? "1" : "0",
            isAmbiguous ? "1" : "0",
        ]);

        return new MeetingIdentitySnapshot(
            MeetingIdentitySnapshot.CurrentSchemaVersion,
            evidence.Platform,
            tier,
            durableToken,
            titleToken,
            evidence.HasAttributedCapture,
            evidence.HasHostContext,
            isAmbiguous,
            capturedAtUtc,
            expiresAtUtc,
            sources.Distinct().Order().ToArray(),
            Token("key-id", evidence.Platform, "meeting-identity"),
            Token("fingerprint", evidence.Platform, canonical));
    }

    private static MeetingIdentityEvidenceTier ClassifyTier(
        string? durableToken,
        string? titleToken,
        bool hasAttributedCapture,
        bool hasHostContext,
        bool isAmbiguous) =>
        isAmbiguous ? MeetingIdentityEvidenceTier.Weak :
        hasAttributedCapture && (durableToken is not null || titleToken is not null) ? MeetingIdentityEvidenceTier.Strong :
        titleToken is not null && hasHostContext ? MeetingIdentityEvidenceTier.Medium :
        titleToken is not null || durableToken is not null ? MeetingIdentityEvidenceTier.Weak :
        MeetingIdentityEvidenceTier.None;

    private void AddSpecificCandidate(
        ISet<string> candidates,
        string? value,
        MeetingIdentityEvidenceSource source,
        ICollection<MeetingIdentityEvidenceSource> sources)
    {
        var originalNormalized = MeetingTitleNormalizer.NormalizeForComparison(value);
        var normalized = RemovePlatformShell(originalNormalized);
        if (string.IsNullOrWhiteSpace(normalized) ||
            GenericTitles.Contains(originalNormalized) ||
            GenericTitles.Contains(normalized) ||
            normalized.Length > 160)
        {
            return;
        }

        candidates.Add(normalized);
        sources.Add(source);
    }

    private static string? NormalizeDurableCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = GoogleMeetCode.Match(value);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static string RemovePlatformShell(string value)
    {
        foreach (var suffix in new[] { " microsoft teams", " teams", " google meet", " zoom" })
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return value[..^suffix.Length].TrimEnd();
            }
        }

        return value;
    }

    private string Token(string kind, MeetingPlatform platform, string value)
    {
        var bytes = Encoding.UTF8.GetBytes($"v1|{kind}|{(int)platform}|{value}");
        return Convert.ToHexString(HMACSHA256.HashData(_key, bytes)).ToLowerInvariant();
    }
}

public sealed class MeetingIdentitySnapshotService
{
    private readonly MeetingIdentitySnapshotBuilder _builder;

    public MeetingIdentitySnapshotService(MeetingIdentityKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        _builder = new MeetingIdentitySnapshotBuilder(keyStore.GetOrCreate());
    }

    public MeetingIdentitySnapshot? GetForComparison(MeetingSessionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.IdentitySnapshot is { } stored)
        {
            return MeetingIdentitySnapshotValidator.IsReadable(stored) ? stored : null;
        }

        return CreateFromManifest(manifest);
    }

    public MeetingSessionManifest EnsureForNormalSave(MeetingSessionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.IdentitySnapshot is not null)
        {
            return manifest;
        }

        var derived = CreateFromManifest(manifest);
        return derived is null ? manifest : manifest with { IdentitySnapshot = derived };
    }

    private MeetingIdentitySnapshot? CreateFromManifest(MeetingSessionManifest manifest)
    {
        var audio = manifest.DetectedAudioSource;
        return _builder.Create(new MeetingIdentityEvidence(
            manifest.Platform,
            DurableMeetingCode: null,
            manifest.DetectedTitle,
            audio?.WindowTitle ?? audio?.BrowserTabTitle,
            HasAttributedCapture: audio is not null,
            HasHostContext: audio is not null && !string.IsNullOrWhiteSpace(audio.AppName),
            manifest.StartedAtUtc));
    }
}

public static class MeetingIdentitySnapshotValidator
{
    public static bool IsReadable(MeetingIdentitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.SchemaVersion == MeetingIdentitySnapshot.CurrentSchemaVersion &&
               snapshot.Platform != MeetingPlatform.Unknown &&
               snapshot.EvidenceTier != MeetingIdentityEvidenceTier.None &&
               snapshot.CapturedAtUtc != default &&
               snapshot.ExpiresAtUtc > snapshot.CapturedAtUtc &&
               snapshot.ExpiresAtUtc - snapshot.CapturedAtUtc <= TimeSpan.FromHours(24) &&
               snapshot.Fingerprint.Length == 64 &&
               snapshot.Fingerprint.All(char.IsAsciiHexDigit) &&
               snapshot.KeyId.Length == 64 &&
               snapshot.KeyId.All(char.IsAsciiHexDigit) &&
               snapshot.Sources.Count > 0;
    }
}
