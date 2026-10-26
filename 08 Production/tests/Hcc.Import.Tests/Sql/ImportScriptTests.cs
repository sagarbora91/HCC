using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Hcc.Api;
using Hcc.Api.Endpoints;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Application.Yield;
using Hcc.Contracts;
using Hcc.Contracts.Access;
using Hcc.Domain.Errors;
using Hcc.Import.Tests.Harness;
using Hcc.Infrastructure.SqlServer.Stays;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Import.Tests.Sql;

/// <summary>
/// A02b-01 end to end: deployment\windows\Import-HccMasters.ps1 run by Windows PowerShell against the real import endpoints (real access
/// policy, real module stores, disposable HccTest_* database) behind the session filter. Sign-in is a test stand-in for P1 (one Owner
/// password, bearer session token), so the script's sign-in, upload, report, dry run, commit, stop-on-rejected and sign-out are exercised.
/// </summary>
[Trait("Category", "Sql")]
public class ImportScriptTests : IAsyncLifetime
{
    private static readonly string Password = "Own-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private ImportWorld _w = null!;
    private WebApplication _app = null!;
    private string _url = null!;
    private string _folder = null!;
    private int _signOuts;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _w = await ImportWorld.CreateAsync(Ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_w.Platform);
        builder.Services.AddSingleton(_w.Platform.Connections);
        builder.Services.AddSingleton<ICommandRunner>(_w.Platform.Commands);
        builder.Services.AddSingleton<TimeProvider>(_w.Clock);
        builder.Services.AddSingleton<IYieldStore>(_w.Yield);
        builder.Services.AddSingleton<IRatePublicationPort>(_w.Rates);
        builder.Services.AddSingleton<IBillingStore>(_w.Billing);
        builder.Services.AddSingleton<IStaysStore, SqlStaysStore>();
        builder.Services.AddSingleton<Hcc.Application.Pos.IPosStore>(new Hcc.Infrastructure.SqlServer.Pos.SqlPosStore(_w.Platform.Connections));
        builder.Services.AddSingleton<Hcc.Application.Payroll.IPayrollStore>(new Hcc.Infrastructure.SqlServer.Payroll.SqlPayrollStore(_w.Platform.Connections));
        builder.Services.AddSingleton<Hcc.Application.Groups.ICompanyStore>(new Hcc.Infrastructure.SqlServer.Groups.SqlCompanyStore(_w.Platform.Connections));
        builder.Services.AddSingleton<Hcc.Application.Maintenance.IMaintenanceStore>(new Hcc.Infrastructure.SqlServer.Maintenance.SqlMaintenanceStore());
        builder.Services.AddSingleton<Hcc.Application.Stock.IStockStore>(new Hcc.Infrastructure.SqlServer.Stock.SqlStockStore());
        builder.Services.AddImportModule();
        builder.Services.AddSingleton<IAccessPolicy>(_w.Policy);
        builder.Services.AddSingleton<ISessionService>(new TestSessions(_w.PropertyId));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        _app = builder.Build();
        _app.UseMiddleware<HccErrorMiddleware>();
        var api = _app.MapGroup(ApiConventions.BasePath);
        // P1 stand-in: the Owner's password gives a bearer session token (returnToken, as the real sign-in does for scripts and the APK).
        api.MapPost("/auth/sign-in", (SignInRequest body) =>
            body.LoginName == "owner" && body.Password == Password && body.ReturnToken
                ? Results.Ok(new SignInResult(_w.Users["owner"], "Test Owner", "PASSWORD", null!, TestSessions.Token(_w.Users["owner"])))
                : throw new HccException(HccErrorCode.FORBIDDEN, "The sign-in name or password is not right."));
        api.MapPost("/auth/sign-out", (HttpContext http) =>
        {
            if (http.Request.Headers.Authorization.ToString().StartsWith("Bearer test-", StringComparison.Ordinal)) Interlocked.Increment(ref _signOuts);
            return Results.NoContent();
        });
        api.MapImportEndpoints();
        await _app.StartAsync(Ct);
        _url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _folder = Path.Combine(Path.GetTempPath(), "HccImportScript_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        await _w.DisposeAsync();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static string Script => Path.Combine(Templates.ProductionRoot().FullName, "deployment", "windows", "Import-HccMasters.ps1");

    private void CopySamples()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(Templates.Folder, "samples"), "*.csv")) File.Copy(f, Path.Combine(_folder, Path.GetFileName(f)), true);
    }

    /// <summary>Runs the script in Windows PowerShell 5.1 (the hotel PC); the password reaches it only as a SecureString built from the child's environment.</summary>
    private async Task<(int Exit, string Out)> RunAsync(string password, params string[] extra)
    {
        var args = string.Join(' ', extra);
        // A refusal is thrown by the script: print its message on one line and exit 1, as "powershell -File" would.
        var command = $"try {{ & '{Script.Replace("'", "''", StringComparison.Ordinal)}' -Folder '{_folder.Replace("'", "''", StringComparison.Ordinal)}' -ServerUrl '{_url}' -UserName owner "
            + "-Password (ConvertTo-SecureString $env:HCC_TEST_IMPORT_PASSWORD -AsPlainText -Force) " + args + "; exit $LASTEXITCODE } "
            + "catch { [Console]::Error.WriteLine('ERROR: ' + $_.Exception.Message); exit 1 }";
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(a);
        psi.Environment["HCC_TEST_IMPORT_PASSWORD"] = password;
        psi.Environment.Remove("PSModulePath");   // a PowerShell 7 parent's module path breaks Windows PowerShell's own modules (ConvertTo-SecureString)
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync(Ct);
        var error = p.StandardError.ReadToEndAsync(Ct);
        await p.WaitForExitAsync(Ct);
        return (p.ExitCode, (await output + await error).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Script_dry_runs_then_imports_every_sample_then_finds_nothing_to_change()
    {
        if (!OperatingSystem.IsWindows()) return;   // the script targets the Windows hotel PC
        CopySamples();

        var dry = await RunAsync(Password, "-OpeningBalancesDate 2026-10-25", "-DryRun");
        Assert.True(dry.Exit == 0, dry.Out);
        Assert.Contains("expected in a dry run", dry.Out, StringComparison.Ordinal);
        Assert.Contains("dry run passed", dry.Out, StringComparison.Ordinal);
        Assert.Contains("Dry run finished: nothing was kept. 0 file(s) with rejected rows.", dry.Out, StringComparison.Ordinal);
        Assert.Equal(15, await _w.CountAsync("SELECT COUNT(*) FROM imp.batch WHERE status = 'Uploaded'"));
        Assert.Equal(0, await _w.CountAsync("SELECT (SELECT COUNT(*) FROM inv.room_type) + (SELECT COUNT(*) FROM hr.department) + (SELECT COUNT(*) FROM stk.item) + (SELECT COUNT(*) FROM imp.ledger)"));

        var real = await RunAsync(Password, "-OpeningBalancesDate 2026-10-25");
        Assert.True(real.Exit == 0, real.Out);
        Assert.Contains("Finished: 15 file(s) committed, 0 file(s) with rejected rows.", real.Out, StringComparison.Ordinal);
        Assert.Contains("priya.reception (Priya Kulkarni) cannot sign in yet: set the first PIN", real.Out, StringComparison.Ordinal);
        Assert.Contains("Total debits Rs 16,20,000 = total credits Rs 16,20,000", real.Out, StringComparison.Ordinal);
        Assert.Equal(6, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));
        Assert.Equal(162000000, await _w.CountAsync("SELECT debit_paise FROM imp.opening_set"));
        Assert.Equal(1, await _w.CountAsync("SELECT COUNT(*) FROM pay.till WHERE code = 'FD-1'"));
        Assert.Equal(5, await _w.CountAsync("SELECT COUNT(*) FROM pos.menu_item"));
        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM hr.department"));
        Assert.Equal(2, await _w.CountAsync("SELECT COUNT(*) FROM grp.company_account"));
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM mnt.vendor"));
        Assert.Equal(4, await _w.CountAsync("SELECT COUNT(*) FROM stk.item"));
        Assert.Equal(15, await _w.CountAsync("SELECT COUNT(*) FROM imp.batch WHERE status = 'Committed'"));
        Assert.Equal(15, await _w.CountAsync($"SELECT COUNT(*) FROM plat.audit_event WHERE action = 'IMPORT_COMMIT' AND principal_id = {_w.Users["owner"]}"));

        var again = await RunAsync(Password, "-OpeningBalancesDate 2026-10-25");
        Assert.True(again.Exit == 0, again.Out);
        Assert.Equal(15, again.Out.Split("nothing to change: every row is already in HCC").Length - 1);
        Assert.Contains("Finished: 0 file(s) committed", again.Out, StringComparison.Ordinal);
        Assert.Equal(3, _signOuts);
        Assert.DoesNotContain(Password, dry.Out + real.Out + again.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_stops_at_a_rejected_row_and_refuses_a_wrong_password()
    {
        if (!OperatingSystem.IsWindows()) return;
        await File.WriteAllTextAsync(Path.Combine(_folder, "room-types.csv"), Templates.Sample("room-types"), Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "rooms.csv"), "roomNo,roomType\n101,STD\n102,XL\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "suppliers.csv"), Templates.Sample("suppliers"), Ct);

        var r = await RunAsync(Password);
        Assert.Equal(1, r.Exit);
        Assert.Contains("REJECTED row 3 / roomType: Room type XL is not in HCC yet", r.Out, StringComparison.Ordinal);
        Assert.Contains("STOPPED: fix the rejected rows in rooms.csv", r.Out, StringComparison.Ordinal);
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM inv.room_type"));   // committed before the rejected file
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));        // the rejected file: all or nothing
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM imp.batch WHERE master = 'suppliers'"));   // files after it are not sent

        var skip = await RunAsync(Password, "-ContinueOnErrors");
        Assert.Equal(1, skip.Exit);
        Assert.Contains("NOT committed", skip.Out, StringComparison.Ordinal);
        Assert.Equal(3, await _w.CountAsync("SELECT COUNT(*) FROM mnt.vendor"));
        Assert.Equal(0, await _w.CountAsync("SELECT COUNT(*) FROM inv.room"));

        var wrong = await RunAsync("not-the-password");
        Assert.NotEqual(0, wrong.Exit);
        // an error record may be wrapped at the console width: join its lines before matching the message
        Assert.Contains("Sign-in as 'owner' was refused: FORBIDDEN: The sign-in name or password is not right.", wrong.Out.Replace("\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", wrong.Out, StringComparison.Ordinal);
    }
}
