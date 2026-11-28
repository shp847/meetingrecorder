using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MeetingRecorder.Core.Tests;

public sealed class MsiPayloadTests
{
    [Fact]
    public async Task StagingPreservesLauncherAndRejectsMissingOrChangedRequiredFiles()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var script = Path.Combine(repoRoot, "scripts", "Build-Installer.ps1");
        var root = Path.Combine(Path.GetTempPath(), "MeetingRecorderTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(source);
        var launcher = Path.Combine(source, "Run-MeetingRecorder.cmd");
        await File.WriteAllTextAsync(launcher, "@echo off\r\necho test\r\n");
        foreach (var name in new[] { "Check-Dependencies.ps1", "Install-Dependencies.cmd", "Install-Dependencies.ps1" })
            await File.WriteAllTextAsync(Path.Combine(source, name), "test");
        await File.WriteAllTextAsync(Path.Combine(source, "bundle-integrity.json"), JsonSerializer.Serialize(new
        {
            requiredFiles = new[] { new { relativePath = "Run-MeetingRecorder.cmd", lengthBytes = new FileInfo(launcher).Length,
                sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(launcher))) } },
        }));
        var runner = Path.Combine(root, "verify.ps1");
        await File.WriteAllTextAsync(runner, """
            param($BuildScript, $Source, $Stage)
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($BuildScript, [ref]$tokens, [ref]$errors)
            foreach ($name in @('Copy-MsiPayload','Assert-MsiPayloadIntegrity')) {
                $definition = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true) | Select-Object -First 1
                if ($null -eq $definition) { throw "Missing function: $name" }
                Invoke-Expression $definition.Extent.Text
            }
            Copy-MsiPayload -SourceRoot $Source -DestinationRoot $Stage
            Assert-MsiPayloadIntegrity -SourceRoot $Source -DestinationRoot $Stage
            Set-Content -LiteralPath (Join-Path $Stage 'Run-MeetingRecorder.cmd') -Value 'changed'
            try { Assert-MsiPayloadIntegrity -SourceRoot $Source -DestinationRoot $Stage; throw 'Changed payload was accepted' }
            catch { if ($_.Exception.Message -eq 'Changed payload was accepted') { throw } }
            Remove-Item -LiteralPath (Join-Path $Stage 'Run-MeetingRecorder.cmd')
            try { Assert-MsiPayloadIntegrity -SourceRoot $Source -DestinationRoot $Stage; throw 'Missing payload was accepted' }
            catch { if ($_.Exception.Message -eq 'Missing payload was accepted') { throw } }
            """, new UTF8Encoding(false));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner, script, source, stage })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Payload validation failed: {output} {error}");
    }
}
