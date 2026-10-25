using System.Diagnostics;

namespace Hcc.Operations.Tests;

/// <summary>
/// Runs deployment\windows\tests\Test-HccDeployment.ps1: unit tests of the script helpers plus a -WhatIf run of every script.
/// It never connects to SQL Server and never installs a service, certificate or scheduled task.
/// </summary>
public sealed class DeploymentScriptTests
{
    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "deployment", "windows", "tests", "Test-HccDeployment.ps1");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    [Fact]
    public async Task Deployment_scripts_pass_their_dry_run_tests()
    {
        if (!OperatingSystem.IsWindows()) return; // the scripts target the Windows hotel PC
        var script = FindScript();
        Assert.NotNull(script);
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script! }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var ct = TestContext.Current.CancellationToken;
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var text = await output + await error;
        Assert.True(p.ExitCode == 0, text);
        Assert.Contains("Failed: 0", text, StringComparison.Ordinal);
    }
}
