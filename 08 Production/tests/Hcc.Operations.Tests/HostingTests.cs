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
    };

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
