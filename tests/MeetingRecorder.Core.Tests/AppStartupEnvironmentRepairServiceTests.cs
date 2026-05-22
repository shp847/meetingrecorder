using MeetingRecorder.App.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class AppStartupEnvironmentRepairServiceTests
{
    [Fact]
    public void EnsureWpfFontEnvironment_Repairs_Missing_Windir_From_SystemRoot()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = @"C:\Windows",
        };
        var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var repaired = AppStartupEnvironmentRepairService.EnsureWpfFontEnvironment(
            name => values.TryGetValue(name, out var value) ? value : null,
            (name, value) => writes[name] = value,
            () => @"C:\Ignored");

        Assert.True(repaired);
        Assert.Equal(@"C:\Windows", writes["windir"]);
    }

    [Fact]
    public void EnsureWpfFontEnvironment_Repairs_Relative_Windir_From_WindowsFolder()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["windir"] = "Windows",
        };
        var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var repaired = AppStartupEnvironmentRepairService.EnsureWpfFontEnvironment(
            name => values.TryGetValue(name, out var value) ? value : null,
            (name, value) => writes[name] = value,
            () => @"C:\Windows");

        Assert.True(repaired);
        Assert.Equal(@"C:\Windows", writes["windir"]);
    }

    [Fact]
    public void EnsureWpfFontEnvironment_Leaves_Usable_Windir_Untouched()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["windir"] = @"C:\Windows",
            ["SystemRoot"] = @"D:\Windows",
        };
        var writeCount = 0;

        var repaired = AppStartupEnvironmentRepairService.EnsureWpfFontEnvironment(
            name => values.TryGetValue(name, out var value) ? value : null,
            (_, _) => writeCount++,
            () => @"E:\Windows");

        Assert.False(repaired);
        Assert.Equal(0, writeCount);
    }
}
