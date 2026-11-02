using MeetingRecorder.App.Services;
using MeetingRecorder.Core.Configuration;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SummarySchedulerApprovalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeetingRecorder.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Approval_Tracks_Only_Current_NonSecret_Configuration()
    {
        var path = Path.Combine(_root, "cache", "summary-scheduler-approval.json");
        var service = new SummarySchedulerApprovalService(path);
        var config = new AppConfig
        {
            SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled,
            SummaryProviderPreference = MeetingSummaryProviderPreference.LocalThenOpenAi,
            SummaryModelProxyBaseUrl = "http://127.0.0.1:8645",
            SummaryModelProxyModel = "model-a",
            SummaryOpenAiModel = "model-b",
            SummaryHostedRouteConsentVersion = 1,
        };

        service.RecordSuccess(config, SummaryChatProviderKind.ModelProxy, DateTimeOffset.Parse("2026-10-15T00:00:00Z"));

        Assert.True(service.IsApproved(config));
        Assert.False(service.IsApproved(config with { SummaryModelProxyModel = "model-c" }));
        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("apiKey", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_Removes_Persisted_Approval()
    {
        var path = Path.Combine(_root, "cache", "summary-scheduler-approval.json");
        var service = new SummarySchedulerApprovalService(path);
        var config = new AppConfig { SummaryGenerationMode = MeetingSummaryGenerationMode.Enabled };
        service.RecordSuccess(config, SummaryChatProviderKind.ModelProxy, DateTimeOffset.UtcNow);

        service.Clear();

        Assert.False(service.IsApproved(config));
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
