using System.Reflection;
using System.Text.Json;
using MeetingRecorder.Core.Services;

namespace MeetingRecorder.Core.Tests;

public sealed class MeetingCapabilityInventoryTests
{
    [Fact]
    public void Inventory_Covers_Each_Catalog_Action_Once_With_A_Discoverable_Future_Home()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetInventoryPath()));
        var capabilities = document.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();
        var ids = capabilities.Select(item => item.GetProperty("actionId").GetString()).ToArray();
        var expected = Enum.GetNames<MeetingActionId>();

        Assert.Equal(expected.Length, ids.Length);
        Assert.Equal(expected.OrderBy(id => id), ids.OrderBy(id => id));
        Assert.All(capabilities, capability =>
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.GetProperty("disposition").GetString()));
            Assert.NotEmpty(capability.GetProperty("surfaces").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(capability.GetProperty("recovery").GetString()));
        });
    }

    [Fact]
    public void Inventory_Confirmation_And_Disposition_Match_The_Canonical_Catalog()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetInventoryPath()));
        var inventory = document.RootElement.GetProperty("capabilities").EnumerateArray()
            .ToDictionary(item => Enum.Parse<MeetingActionId>(item.GetProperty("actionId").GetString()!), item => item);
        var catalog = new MeetingActionCatalog();

        Assert.All(catalog.All, entry =>
        {
            var item = inventory[entry.Id];
            Assert.Equal(entry.ConfirmationPolicy.ToString(), item.GetProperty("confirmation").GetString());
            var disposition = item.GetProperty("disposition").GetString();
            if (entry.ConfirmationPolicy == MeetingActionConfirmationPolicy.TypedPermanentDelete)
            {
                Assert.Equal("DestructiveExplicit", disposition);
                Assert.Equal("none", item.GetProperty("recovery").GetString());
            }
            else
            {
                Assert.NotEqual("DestructiveExplicit", disposition);
            }
        });
    }

    [Fact]
    public void Supplemental_Controls_Are_Unique_And_Still_Exist_In_The_Meetings_Xaml_Surface()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GetInventoryPath()));
        var controls = document.RootElement.GetProperty("supplementalControls").EnumerateArray().ToArray();
        var names = controls.Select(item => item.GetProperty("controlName").GetString()).ToArray();
        var xaml = File.ReadAllText(GetMeetingsXamlPath());

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(controls, control =>
        {
            var name = control.GetProperty("controlName").GetString();
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(control.GetProperty("surface").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(control.GetProperty("recovery").GetString()));
        });
    }

    private static string GetInventoryPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to locate the test assembly directory.");
        return Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", "..", "docs", "meeting-capability-inventory.json"));
    }

    private static string GetMeetingsXamlPath()
    {
        var inventoryDirectory = Path.GetDirectoryName(GetInventoryPath())
            ?? throw new InvalidOperationException("Unable to locate the docs directory.");
        return Path.GetFullPath(Path.Combine(inventoryDirectory, "..", "src", "MeetingRecorder.App", "MainWindow.xaml"));
    }
}
