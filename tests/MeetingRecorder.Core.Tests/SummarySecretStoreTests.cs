using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class SummarySecretStoreTests
{
    [Fact]
    public async Task FileSummarySecretStore_Saves_Loads_And_Deletes_Protected_Secrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var secretPath = Path.Combine(root, "secrets", "summary-provider-secrets.json");
        var store = new FileSummarySecretStore(secretPath);

        await store.SaveAsync(SummarySecretKind.ModelProxy, "sk-modelproxy-secret");
        await store.SaveAsync(SummarySecretKind.OpenAi, "sk-openai-secret");

        Assert.True(await store.HasSecretAsync(SummarySecretKind.ModelProxy));
        Assert.True(await store.HasSecretAsync(SummarySecretKind.OpenAi));
        Assert.Equal("sk-modelproxy-secret", await store.LoadAsync(SummarySecretKind.ModelProxy));
        Assert.Equal("sk-openai-secret", await store.LoadAsync(SummarySecretKind.OpenAi));

        var persisted = await File.ReadAllTextAsync(secretPath);
        Assert.Contains("summary:modelproxy", persisted, StringComparison.Ordinal);
        Assert.Contains("summary:openai", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-modelproxy-secret", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-openai-secret", persisted, StringComparison.Ordinal);

        await store.DeleteAsync(SummarySecretKind.ModelProxy);

        Assert.False(await store.HasSecretAsync(SummarySecretKind.ModelProxy));
        Assert.Null(await store.LoadAsync(SummarySecretKind.ModelProxy));
        Assert.Equal("sk-openai-secret", await store.LoadAsync(SummarySecretKind.OpenAi));
    }

    [Fact]
    public async Task FileSummarySecretStore_Rejects_Blank_Secrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var secretPath = Path.Combine(root, "secrets", "summary-provider-secrets.json");
        var store = new FileSummarySecretStore(secretPath);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(SummarySecretKind.OpenAi, " "));

        Assert.False(File.Exists(secretPath));
    }
}
