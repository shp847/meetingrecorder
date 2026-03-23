using Microsoft.Win32;

namespace MeetingRecorder.Core.Services;

public sealed class AutoStartRegistrationService
{
    private const string EntryName = "MeetingRecorder";
    private readonly IAutoStartRegistrationStore _store;

    public AutoStartRegistrationService()
        : this(new RegistryAutoStartRegistrationStore())
    {
    }

    public AutoStartRegistrationService(IAutoStartRegistrationStore store)
    {
        _store = store;
    }

    public bool SyncRegistration(bool enabled, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Executable path is required for launch-on-login registration.");
        }

        var normalizedCommand = BuildCommand(executablePath);
        var currentCommand = _store.ReadCommand(EntryName);

        if (!enabled)
        {
            if (string.IsNullOrWhiteSpace(currentCommand))
            {
                return false;
            }

            _store.RemoveCommand(EntryName);
            return true;
        }

        if (string.Equals(currentCommand, normalizedCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _store.WriteCommand(EntryName, normalizedCommand);
        return true;
    }

    internal static string BuildCommand(string executablePath)
    {
        var normalized = executablePath.Trim();
        var extension = Path.GetExtension(normalized);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            return $"cmd.exe /c \"\\\"{normalized}\\\"\"";
        }

        if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return $"powershell.exe -ExecutionPolicy Bypass -File \"{normalized}\"";
        }

        return normalized.StartsWith("\"", StringComparison.Ordinal)
            ? normalized
            : $"\"{normalized}\"";
    }
}

public interface IAutoStartRegistrationStore
{
    string? ReadCommand(string entryName);

    void WriteCommand(string entryName, string command);

    void RemoveCommand(string entryName);
}

internal sealed class RegistryAutoStartRegistrationStore : IAutoStartRegistrationStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadCommand(string entryName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(entryName) as string;
    }

    public void WriteCommand(string entryName, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("Unable to open the current user's startup registry key.");
        key.SetValue(entryName, command);
    }

    public void RemoveCommand(string entryName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(entryName, throwOnMissingValue: false);
    }
}
