using System.Diagnostics;
using System.Text;
using Hcc.Api.Endpoints;
using Hcc.Api.Hosting;
using Hcc.Application.GuestIds;
using Microsoft.Extensions.Configuration;

namespace Hcc.Operations.Tests;

/// <summary>
/// A07-03 / A09-07: New-HccGuestIdKey.ps1 -ImportOfflineCopy (Windows PowerShell 5.1, the hotel PC) writes the key exactly where and how
/// the server reads it: the server's own loader (<see cref="GuestIdEndpoints.KeyRing"/>, DPAPI) opens it and unseals a value sealed with
/// the raw key from the offline copy, and the production start-up checks accept the result. Temp files only (-SkipAcl); fictional key.
/// </summary>
public sealed class GuestIdKeyImportTests
{
    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "deployment", "windows", "New-HccGuestIdKey.ps1");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static async Task<(int Exit, string Output)> RunScriptAsync(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args)) psi.ArgumentList.Add(a);
        psi.Environment.Remove("PSModulePath");   // audit A01-01: a PowerShell 7 parent's module path breaks Windows PowerShell's own modules
        using var p = Process.Start(psi)!;
        var ct = TestContext.Current.CancellationToken;
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await output + await error);
    }

    [Fact]
    public async Task Imported_offline_copy_opens_with_the_servers_own_key_loader_and_unseals_a_value()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI and the scripts target the Windows hotel PC
        var script = FindScript();
        Assert.NotNull(script);
        var dir = Path.Combine(Path.GetTempPath(), "HccKeyImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "server"));
        Directory.CreateDirectory(Path.Combine(dir, "usb"));
        try
        {
            var raw = Enumerable.Range(41, 32).Select(i => (byte)i).ToArray();   // fictional test key
            var rawB64 = Convert.ToBase64String(raw);
            var copy = Path.Combine(dir, "usb", "HCC-guest-id-key-2026-10.txt");
            await File.WriteAllTextAsync(copy, "HCC guest ID key (OFFLINE COPY - keep in the safe, never e-mail, never on the server PC)\r\nKey id: 2026-10\r\n" +
                "Created (UTC): 2026-10-26 10:00\r\nRaw key (base64, 32 bytes): " + rawB64 + "\r\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);
            var settings = Path.Combine(dir, "server", "appsettings.Production.json");
            await File.WriteAllTextAsync(settings, """{ "Hcc": { "Sql": { "ConnectionString": "Data Source=.\\SQLEXPRESS;Initial Catalog=Hcc;Integrated Security=True;Encrypt=Optional" } } }""",
                TestContext.Current.CancellationToken);

            var (exit, output) = await RunScriptAsync(script!, "-SettingsFile", settings, "-ImportOfflineCopy", copy, "-SkipAcl");
            Assert.True(exit == 0, output);
            Assert.DoesNotContain(rawB64, output, StringComparison.Ordinal);
            Assert.DoesNotContain(rawB64, await File.ReadAllTextAsync(settings, TestContext.Current.CancellationToken), StringComparison.Ordinal);

            var config = new ConfigurationBuilder().AddJsonFile(settings).Build();
            var ring = GuestIdEndpoints.KeyRing(config);
            Assert.NotNull(ring);
            Assert.Equal("2026-10", ring!.CurrentKeyId);
            Assert.StartsWith("dpapi-machine:", config["Hcc:GuestIdKeys:Keys:2026-10"], StringComparison.Ordinal);

            // A number sealed on the OLD PC (raw key from the offline copy) opens on the new one, and the other way round.
            var oldPc = new GuestIdProtector(new StaticGuestIdKeyRing("2026-10", new Dictionary<string, byte[]> { ["2026-10"] = raw }));
            var newPc = new GuestIdProtector(ring);
            Assert.Equal("Z1234567", newPc.Open(7, 42, oldPc.Seal(7, 42, "Z1234567")));
            Assert.Equal("Z7654321", oldPc.Open(7, 43, newPc.Seal(7, 43, "Z7654321")));

            // The production start-up checks accept what the script wrote (Required, AllowMachineScope, dpapi-machine:).
            var production = new ConfigurationBuilder().AddJsonFile(settings).AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hcc:Sql:BackupDirectory"] = "D:\\HccBackups",
                ["Hcc:Hosting:CertificateThumbprint"] = new string('A', 40),
                ["AllowedHosts"] = "hotel-pc;localhost;192.168.1.20;127.0.0.1",
            }).Build();
            Assert.Empty(HccConfigValidator.Validate(production, production: true));
            Assert.Equal("true", config["Hcc:GuestIdKeys:Required"], ignoreCase: true);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
