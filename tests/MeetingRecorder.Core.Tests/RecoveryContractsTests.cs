using System.Text.Json;
using MeetingRecorder.Core.Domain;

namespace MeetingRecorder.Core.Tests;

public sealed class RecoveryContractsTests
{
    [Fact]
    public void Contracts_Are_ReadCompatible_And_Do_Not_Require_Source_Locators()
    {
        const string json = """{"schemaVersion":1,"jobId":"job-1","opaqueSourceId":"opaque-1","state":"Pending","attempt":0,"updatedAtUtc":"2026-09-29T00:00:00+00:00","futureField":true}""";
        var job = JsonSerializer.Deserialize<HistoricalReprocessingJob>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(job);
        Assert.Equal(1, job.SchemaVersion);
        Assert.Equal("opaque-1", job.OpaqueSourceId);
        Assert.DoesNotContain("path", string.Join('|', typeof(HistoricalReprocessingJob).GetProperties().Select(property => property.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessingBlockReason_Preserves_The_Model_Block_Contract()
    {
        Assert.Equal("BlockedModel", JsonSerializer.Serialize(ProcessingBlockReason.BlockedModel).Trim('"'));
    }
}
