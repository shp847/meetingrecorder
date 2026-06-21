using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MeetingRecorder.Core.Configuration;

namespace MeetingRecorder.ProcessingWorker;

internal static class CliProviderProcess
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool ProbeExecutable(string executablePath, string providerName, FileLogWriter logger)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            logger.Log($"{providerName} CLI provider not selected because no executable path is configured.");
            return false;
        }

        if (!File.Exists(executablePath))
        {
            logger.Log($"{providerName} CLI provider not selected because '{executablePath}' was not found.");
            return false;
        }

        logger.Log($"{providerName} CLI provider probe succeeded for '{executablePath}'.");
        return true;
    }

    public static bool HasCurrentSuccessfulProbe(
        string executablePath,
        ExternalProviderProbeSnapshot? snapshot)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            string.IsNullOrWhiteSpace(snapshot?.ExecutablePath))
        {
            return false;
        }

        try
        {
            return snapshot?.Succeeded == true &&
                   string.Equals(
                       Path.GetFullPath(executablePath),
                       Path.GetFullPath(snapshot.ExecutablePath),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ExternalProviderProbeSnapshot> ProbeAsync(
        string executablePath,
        string providerName,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : executablePath.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateProbeSnapshot(false, path, $"{providerName} CLI executable path is not configured.");
        }

        if (!File.Exists(path))
        {
            return CreateProbeSnapshot(false, path, $"{providerName} CLI executable was not found.");
        }

        try
        {
            var result = await RunAsync(path, "--probe", cancellationToken);
            if (result.ExitCode != 0)
            {
                return CreateProbeSnapshot(false, path, $"{providerName} CLI probe failed.");
            }

            var probe = JsonSerializer.Deserialize<CliProviderProbeDto>(result.StandardOutput, JsonOptions);
            if (probe is null)
            {
                return CreateProbeSnapshot(false, path, $"{providerName} CLI probe returned invalid JSON.");
            }

            return CreateProbeSnapshot(
                probe.Ok,
                path,
                string.IsNullOrWhiteSpace(probe.Message)
                    ? probe.Ok ? $"{providerName} CLI probe succeeded." : $"{providerName} CLI probe failed."
                    : probe.Message);
        }
        catch (JsonException)
        {
            return CreateProbeSnapshot(false, path, $"{providerName} CLI probe returned invalid JSON.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateProbeSnapshot(false, path, $"{providerName} CLI probe failed: {exception.GetType().Name}.");
        }
    }

    public static async Task<CliProviderProcessResult> RunAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start CLI provider '{executablePath}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new CliProviderProcessResult(process.ExitCode, standardOutput, standardError);
    }

    public static string BuildArguments(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var arguments = string.IsNullOrWhiteSpace(template)
            ? string.Join(" ", placeholders.Select(item => $"--{item.Key} {Quote(item.Value)}"))
            : template;

        foreach (var placeholder in placeholders)
        {
            arguments = arguments.Replace(
                "{" + placeholder.Key + "}",
                Quote(placeholder.Value),
                StringComparison.OrdinalIgnoreCase);
        }

        return arguments;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static ExternalProviderProbeSnapshot CreateProbeSnapshot(
        bool succeeded,
        string executablePath,
        string message)
    {
        return new ExternalProviderProbeSnapshot
        {
            Succeeded = succeeded,
            LastProbeUtc = DateTimeOffset.UtcNow,
            ExecutablePath = executablePath,
            Message = SanitizeMessage(message),
        };
    }

    private static string SanitizeMessage(string message)
    {
        var sanitized = new string((message ?? string.Empty)
            .Trim()
            .Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .ToArray());
        return sanitized.Length <= 240
            ? sanitized
            : sanitized[..240];
    }

    private sealed record CliProviderProbeDto
    {
        public bool Ok { get; init; }

        public string? Message { get; init; }
    }
}

internal sealed record CliProviderProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
