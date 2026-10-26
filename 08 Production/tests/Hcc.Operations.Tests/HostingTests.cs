using Hcc.Api.Hosting;
using Hcc.Operations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Operations.Tests;

public sealed class HostingTests
{
    private const string GoodConn = "Data Source=.\\SQLEXPRESS;Initial Catalog=Hcc;Integrated Security=True;Encrypt=Optional";

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Good() => new()
    {
        ["Hcc:Sql:ConnectionString"] = GoodConn,
        ["Hcc:Sql:BackupDirectory"] = "D:\\HccBackups",
        ["Hcc:Hosting:CertificateThumbprint"] = new string('A', 40),
        ["AllowedHosts"] = "hotel-pc;localhost;192.168.1.20;127.0.0.1",
    };

    private static List<string> Errors(Dictionary<string, string?> c) => [.. HccConfigValidator.Validate(Config(c), production: true)];

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("hotel-pc;*")]
    public void Production_requires_explicit_allowed_hosts(string? hosts)
    {
        var c = Good();
        c["AllowedHosts"] = hosts;
        Assert.Contains(Errors(c), e => e.Contains("AllowedHosts", StringComparison.Ordinal));
    }

    [Fact]
    public void Public_listener_behind_a_loopback_proxy_needs_known_proxies_and_valid_addresses()
    {
        var c = Good();
        c["Hcc:Hosting:PublicPort"] = "5444";
        Assert.Empty(Errors(c)); // web booking off: the listener is never bound
        c["Hcc:WebBooking:Enabled"] = "true";
        Assert.Contains(Errors(c), e => e.Contains("KnownProxies must list", StringComparison.Ordinal));
        c["Hcc:Hosting:KnownProxies:0"] = "127.0.0.1";
        Assert.Empty(Errors(c));
        c["Hcc:Hosting:KnownProxies:1"] = "not-an-ip";
        c["Hcc:Hosting:KnownNetworks:0"] = "10.0.0.0/99";
        var errors = Errors(c);
        Assert.Contains(errors, e => e.Contains("'not-an-ip'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("KnownNetworks", StringComparison.Ordinal));
        c.Remove("Hcc:Hosting:KnownProxies:1");
        c.Remove("Hcc:Hosting:KnownNetworks:0");
        c["Hcc:Hosting:PublicPort"] = "5443";
        Assert.Contains(Errors(c), e => e.Contains("PublicPort must differ", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_refuses_plain_guest_id_keys_and_machine_keys_unless_allowed()
    {
        var c = Good();
        c["Hcc:GuestIdKeys:Current"] = "2026-10";
        c["Hcc:GuestIdKeys:Keys:2026-10"] = "plain:" + Convert.ToBase64String(new byte[32]);
        c["Hcc:GuestIdKeys:AllowPlainKeys"] = "true";
        var errors = Errors(c);
        Assert.Contains(errors, e => e.Contains("AllowPlainKeys", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("plain (unencrypted)", StringComparison.Ordinal));

        c.Remove("Hcc:GuestIdKeys:AllowPlainKeys");
        c["Hcc:GuestIdKeys:Keys:2026-10"] = "dpapi-machine:AAAA";
        Assert.Contains(Errors(c), e => e.Contains("AllowMachineScope", StringComparison.Ordinal));
        c["Hcc:GuestIdKeys:AllowMachineScope"] = "true";
        Assert.Empty(Errors(c));
        c["Hcc:GuestIdKeys:Keys:2026-10"] = "dpapi-user:AAAA";
        c.Remove("Hcc:GuestIdKeys:AllowMachineScope");
        Assert.Empty(Errors(c));
        c["Hcc:GuestIdKeys:Current"] = "2027-10";
        Assert.Contains(Errors(c), e => e.Contains("Keys:2027-10", StringComparison.Ordinal));
    }

    [Fact]
    public void Required_guest_id_key_must_be_present()
    {
        var c = Good();
        c["Hcc:GuestIdKeys:Required"] = "true";
        Assert.Contains(Errors(c), e => e.Contains("Required", StringComparison.Ordinal));
        Assert.Empty(HccConfigValidator.Validate(Config(new() { ["Hcc:Sql:ConnectionString"] = GoodConn, ["Hcc:GuestIdKeys:AllowPlainKeys"] = "true" }), production: false));
    }

    [Fact]
    public void Guest_id_key_file_must_be_readable_only_by_system_administrators_and_the_service()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "HccAclTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "appsettings.Production.json");
        File.WriteAllText(file, "{}");
        try
        {
            var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            var users = new System.Security.Principal.SecurityIdentifier("S-1-5-32-545");
            var fi = new FileInfo(file);
            var sec = new System.Security.AccessControl.FileSecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-5-18"), System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-5-32-544"), System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            fi.SetAccessControl(sec);
            Assert.Empty(GuestIdKeyFileCheck.Problems(fi.GetAccessControl(), GuestIdKeyFileCheck.AllowedReaders()));
            GuestIdKeyFileCheck.EnsureProtected(file);

            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(users, System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Allow));
            fi.SetAccessControl(sec);
            Assert.Single(GuestIdKeyFileCheck.Problems(fi.GetAccessControl(), GuestIdKeyFileCheck.AllowedReaders()));
            var ex = Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) GuestIdKeyFileCheck.EnsureProtected(file); });
            Assert.Contains("icacls", ex.Message, StringComparison.Ordinal);

            // A missing file (keys from the environment) is not an error.
            GuestIdKeyFileCheck.EnsureProtected(Path.Combine(dir, "missing.json"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Production_config_that_is_complete_is_valid() => Assert.Empty(HccConfigValidator.Validate(Config(Good()), production: true));

    [Fact]
    public void Development_needs_only_a_connection_string()
    {
        Assert.Empty(HccConfigValidator.Validate(Config(new() { ["Hcc:Sql:ConnectionString"] = "Data Source=.;Initial Catalog=Hcc;Integrated Security=True;TrustServerCertificate=True" }), production: false));
        Assert.Single(HccConfigValidator.Validate(Config([]), production: false));
    }

    [Fact]
    public void Production_rejects_trust_server_certificate_and_sql_logins()
    {
        var c = Good();
        c["Hcc:Sql:ConnectionString"] = "Data Source=.\\SQLEXPRESS;Initial Catalog=Hcc;User ID=sa;Password=x;TrustServerCertificate=True";
        var errors = HccConfigValidator.Validate(Config(c), production: true);
        Assert.Contains(errors, e => e.Contains("Integrated Security", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("TrustServerCertificate", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("user name or password", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unencrypted_remote_sql_server()
    {
        var c = Good();
        c["Hcc:Sql:ConnectionString"] = "Data Source=otherpc\\SQLEXPRESS;Initial Catalog=Hcc;Integrated Security=True;Encrypt=Optional";
        Assert.Contains(HccConfigValidator.Validate(Config(c), production: true), e => e.Contains("Encrypt=Optional", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NUL")]
    [InlineData("relative\\folder")]
    public void Production_requires_a_full_backup_folder(string dir)
    {
        var c = Good();
        c["Hcc:Sql:BackupDirectory"] = dir;
        Assert.Contains(HccConfigValidator.Validate(Config(c), production: true), e => e.Contains("BackupDirectory", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void Production_requires_a_certificate_thumbprint(string? thumb)
    {
        var c = Good();
        c["Hcc:Hosting:CertificateThumbprint"] = thumb;
        Assert.Contains(HccConfigValidator.Validate(Config(c), production: true), e => e.Contains("CertificateThumbprint", StringComparison.Ordinal));
    }

    [Fact]
    public void Ports_must_differ_and_be_in_range()
    {
        var c = Good();
        c["Hcc:Hosting:HttpsPort"] = "5443";
        c["Hcc:Hosting:LoopbackHttpPort"] = "5443";
        Assert.Contains(HccConfigValidator.Validate(Config(c), production: true), e => e.Contains("differ", StringComparison.Ordinal));
        c["Hcc:Hosting:HttpsPort"] = "70000";
        Assert.Contains(HccConfigValidator.Validate(Config(c), production: true), e => e.Contains("HttpsPort", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureValid_lists_every_problem()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HccConfigValidator.EnsureValid(Config(new() { ["Hcc:Sql:ConnectionString"] = "Data Source=.;Initial Catalog=Hcc;Integrated Security=True" }), production: true));
        Assert.Contains("BackupDirectory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CertificateThumbprint", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".\\SQLEXPRESS", true)]
    [InlineData("(local)", true)]
    [InlineData("tcp:localhost,1433", true)]
    [InlineData("10.0.0.5", false)]
    public void Local_data_source_detection(string source, bool expected) => Assert.Equal(expected, HccConfigValidator.IsLocalDataSource(source));

    [Fact]
    public async Task Discovery_endpoint_is_anonymous_and_has_no_secrets()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Hcc:Hosting:ServerName"] = "HOTEL-PC";
        builder.Configuration["Hcc:Sql:ConnectionString"] = "Data Source=secret-host;Password=hunter2";
        await using var app = builder.Build();
        app.MapGroup("/api/v1").MapHccDiscovery();
        await app.StartAsync(ct);
        var addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(addr) };
        var raw = await http.GetStringAsync("/api/v1/system/discovery", ct);
        var r = System.Text.Json.JsonSerializer.Deserialize<DiscoveryResult>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("HOTEL-PC", r.ServerName);
        Assert.Equal("Hotel Control Centre", r.Product);
        Assert.Equal("v1", r.ApiVersion);
        Assert.Equal("/api/v1", r.ApiBasePath);
        Assert.False(string.IsNullOrWhiteSpace(r.Version));
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", raw, StringComparison.Ordinal);
        await app.StopAsync(ct);
    }

    [Fact]
    public void Discovery_falls_back_to_the_machine_name()
        => Assert.Equal(Environment.MachineName, DiscoveryInfo.Create(Config([])).ServerName);

    [Fact]
    public void Backup_gate_builds_the_script_command()
    {
        var o = new OperationsOptions { BackupScript = "C:\\HCC\\Backup-HccDatabase.ps1", BackupDirectory = "D:\\HccBackups" };
        var (exe, args) = ScriptBackupGate.BuildCommand(o, "Hcc", 100, 400);
        Assert.Equal("powershell.exe", exe);
        Assert.Contains("premigrate_0100_to_0400", args);
        Assert.Contains("-CopyOnly", args);
        Assert.Equal("D:\\HccBackups", args[args.ToList().IndexOf("-BackupDirectory") + 1]);
    }

    private sealed class FakeRunner(int code) : IScriptRunner
    {
        public Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct) => Task.FromResult((code, "out"));
    }

    [Fact]
    public async Task Backup_gate_stops_migration_when_the_script_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = new ScriptBackupGate(new OperationsOptions { BackupScript = "x.ps1", BackupDirectory = "D:\\b" }, new FakeRunner(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunBackupScriptAsync("Hcc", 1, 2, ct));
        var gate2 = new ScriptBackupGate(new OperationsOptions { BackupScript = "x.ps1", BackupDirectory = "D:\\b" }, new FakeRunner(0));
        await gate2.RunBackupScriptAsync("Hcc", 1, 2, ct);
        Assert.Equal("D:\\b", gate2.LastBackupTarget);
    }
}
