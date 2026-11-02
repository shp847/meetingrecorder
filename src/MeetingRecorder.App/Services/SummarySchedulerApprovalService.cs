using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.App.Services;

/// <summary>
/// Stores only a configuration fingerprint and successful synthetic-provider
/// check. API keys, transcript content, and provider replies never enter this
/// scheduler receipt.
/// </summary>
internal sealed record SummarySchedulerApproval(
    string ConfigurationFingerprint,
    SummaryChatProviderKind ProviderKind,
    DateTimeOffset ValidatedAtUtc);

internal sealed class SummarySchedulerApprovalService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private SummarySchedulerApproval? _approval;
    private bool _loaded;

    public SummarySchedulerApprovalService(string? path = null)
    {
        _path = path ?? Path.Combine(
            AppDataPaths.GetManagedAppRoot(),
            "cache",
            "summary-scheduler-approval-v1.json");
    }

    public bool IsApproved(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_gate)
        {
            return string.Equals(
                Load()?.ConfigurationFingerprint,
                ComputeConfigurationFingerprint(config),
                StringComparison.Ordinal);
        }
    }

    public void RecordSuccess(AppConfig config, SummaryChatProviderKind providerKind, DateTimeOffset validatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_gate)
        {
            _approval = new SummarySchedulerApproval(
                ComputeConfigurationFingerprint(config),
                providerKind,
                validatedAtUtc);
            _loaded = true;
            Save();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _approval = null;
            _loaded = true;
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    internal static string ComputeConfigurationFingerprint(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var canonical = string.Join("\n", [
            "summary-scheduler-v1",
            ((int)config.SummaryGenerationMode).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)config.SummaryProviderPreference).ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.SummaryModelProxyBaseUrl.Trim(),
            config.SummaryModelProxyModel.Trim(),
            config.SummaryOpenAiModel.Trim(),
            config.SummaryHostedRouteConsentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)config.SummaryReasoningEffort).ToString(System.Globalization.CultureInfo.InvariantCulture),
            config.SummaryRequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private SummarySchedulerApproval? Load()
    {
        if (_loaded)
        {
            return _approval;
        }

        _loaded = true;
        try
        {
            _approval = File.Exists(_path)
                ? JsonSerializer.Deserialize<SummarySchedulerApproval>(File.ReadAllText(_path), SerializerOptions)
                : null;
        }
        catch
        {
            _approval = null;
        }

        return _approval;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Summary scheduler approval path must include a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_approval, SerializerOptions));
        try
        {
            File.Move(temporaryPath, _path, overwrite: true);
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
