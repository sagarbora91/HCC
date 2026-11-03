using System.Text;
using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Contracts;
using Hcc.Domain.Accounting;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// F5-A1 · ACC-4 year end and bank reconciliation (02 Planning/agents/ACC4-CONTRACT.md §1, §2, §4). Year: <c>GET /accounting/year</c>,
/// <c>GET /accounting/year/{fy}/preview</c>, <c>POST /accounting/year/{fy}/{carry-forward|lock|reopen|relock}</c> (PERM-ACC-014 prepare: Owner,
/// Accountant; PERM-ACC-015 lock / reopen / relock: Owner). Bank: <c>/accounting/bank/…</c> layouts (PERM-ACC-017), import / auto-match / match /
/// unmatch / ignore / voucher / settlement (PERM-ACC-016), rows / recon / recon.csv / clearing (PERM-ACC-001).
/// </summary>
public static class AccountingYearBankEndpoints
{
    public static IServiceCollection AddAccountingYearBankModule(this IServiceCollection services)
    {
        services.AddSingleton<IYearLockStore>(sp => new SqlYearLockStore(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<IAccountingStore>(), sp.GetService<IReportOpeningSource>()));
        services.AddSingleton<IBankReconStore>(sp => new SqlBankReconStore(sp.GetRequiredService<SqlPlatform>().Connections));
        return services;
    }

    private static IYearLockStore Years(IServiceProvider sp) => sp.GetService<IYearLockStore>()
        ?? new SqlYearLockStore(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<IAccountingStore>(), sp.GetService<IReportOpeningSource>());

    private static DateTime Now(IServiceProvider sp) => sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    private static BankReconService Bank(IServiceProvider sp)
    {
        var manual = sp.GetService<ManualVoucherService>();
        return new BankReconService(sp.GetService<IBankReconStore>() ?? new SqlBankReconStore(sp.GetRequiredService<SqlPlatform>().Connections), Years(sp),
            sp.GetRequiredService<IAccessPolicy>(), () => Now(sp),
            manual is null ? null : async (c, r, ct) => (await manual.SaveAsync(c, Guid.NewGuid().ToString("N"), r, ct).ConfigureAwait(false)).Value);
    }

    /// <summary>Contract roles from the permissions: PERM-ACC-015 → Owner, PERM-ACC-014 → Accountant, else none (refused by the service).</summary>
    public static async Task<string> YearRoleAsync(IAccessPolicy policy, Hcc.Application.Billing.Caller c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(c);
        if (await policy.HasAsync(c.PropertyId, c.PrincipalId, AccountingYearPermissions.Lock, ct: ct).ConfigureAwait(false)) return "Owner";
        return await policy.HasAsync(c.PropertyId, c.PrincipalId, AccountingYearPermissions.Prepare, ct: ct).ConfigureAwait(false) ? "Accountant" : "none";
    }

    public sealed record YearBody(string? CarryForwardId, string? Reason);
    public sealed record ImportBody(string? BankLedger, string? LayoutId, string? FileName, string? Text, bool KeepDuplicates);
    public sealed record LedgerBody(string BankLedger);
    public sealed record MatchBody(IReadOnlyList<string> RowIds, IReadOnlyList<BankLineRef> Refs);
    public sealed record IdBody(string Id, string? Reason);
    public sealed record VoucherBody(string RowId, string? Ledger, string? Narration);
    public sealed record SettlementBody(string RowId, string Clearing, long? Gross, long Mdr, long MdrGst, bool Itc);

    public static RouteGroupBuilder MapAccountingYearBankEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        async Task<(YearLockService Svc, string Role, Hcc.Application.Billing.Caller C, DateOnly Bd)> Y(HttpContext http, CancellationToken ct)
        {
            var sp = http.RequestServices;
            var c = http.HccCaller();
            var role = await YearRoleAsync(sp.GetRequiredService<IAccessPolicy>(), c, ct).ConfigureAwait(false);
            var bd = await sp.GetRequiredService<IAccountingStore>().BusinessDateAsync(null, c.PropertyId, ct).ConfigureAwait(false) ?? DateOnly.FromDateTime(Now(sp));
            return (new YearLockService(Years(sp), () => Now(sp)), role, c, bd);
        }

        g.MapGet("/accounting/year", async (HttpContext http, CancellationToken ct) =>
        {
            var (_, role, c, _) = await Y(http, ct).ConfigureAwait(false);
            if (role == "none") await http.RequestServices.GetRequiredService<IAccessPolicy>().RequireAsync(c.PropertyId, c.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
            var s = Years(http.RequestServices);
            return Results.Ok(new { years = await s.YearsAsync(c.PropertyId, ct).ConfigureAwait(false), carryForwards = await s.CarryForwardsAsync(c.PropertyId, ct).ConfigureAwait(false) });
        });
        g.MapGet("/accounting/year/{fy}/preview", async (string fy, HttpContext http, CancellationToken ct) =>
        { var (s, r, c, bd) = await Y(http, ct).ConfigureAwait(false); return Results.Ok(await s.PreviewAsync(c.PropertyId, r, fy, bd, ct).ConfigureAwait(false)); });
        g.MapPost("/accounting/year/{fy}/carry-forward", async (string fy, HttpContext http, CancellationToken ct) =>
        { var (s, r, c, _) = await Y(http, ct).ConfigureAwait(false); return Results.Ok(await s.CarryForwardAsync(c.PropertyId, c.PrincipalId, r, fy, ct).ConfigureAwait(false)); });
        g.MapPost("/accounting/year/{fy}/lock", async (string fy, YearBody b, HttpContext http, CancellationToken ct) =>
        { var (s, r, c, bd) = await Y(http, ct).ConfigureAwait(false); return Results.Ok(await s.LockAsync(c.PropertyId, c.PrincipalId, r, fy, b.CarryForwardId, bd, ct).ConfigureAwait(false)); });
        g.MapPost("/accounting/year/{fy}/reopen", async (string fy, YearBody b, HttpContext http, CancellationToken ct) =>
        { var (s, r, c, _) = await Y(http, ct).ConfigureAwait(false); return Results.Ok(await s.ReopenAsync(c.PropertyId, c.PrincipalId, r, fy, b.Reason, ct).ConfigureAwait(false)); });
        g.MapPost("/accounting/year/{fy}/relock", async (string fy, HttpContext http, CancellationToken ct) =>
        { var (s, r, c, bd) = await Y(http, ct).ConfigureAwait(false); return Results.Ok(await s.RelockAsync(c.PropertyId, c.PrincipalId, r, fy, bd, ct).ConfigureAwait(false)); });

        g.MapGet("/accounting/bank/layouts", async (HttpContext http, CancellationToken ct) => Results.Ok(await Bank(http.RequestServices).LayoutsAsync(http.HccCaller(), ct).ConfigureAwait(false)));
        g.MapPost("/accounting/bank/layouts", async (BankStatementLayout l, HttpContext http, CancellationToken ct) =>
        { await Bank(http.RequestServices).UpsertLayoutAsync(http.HccCaller(), l, ct).ConfigureAwait(false); return Results.Ok(l); });
        g.MapPost("/accounting/bank/import", async (ImportBody b, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).ImportAsync(http.HccCaller(), b.BankLedger, b.LayoutId, b.FileName, b.Text, b.KeepDuplicates, ct).ConfigureAwait(false)));
        g.MapPost("/accounting/bank/auto-match", async (LedgerBody b, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).AutoMatchAsync(http.HccCaller(), b.BankLedger, ct).ConfigureAwait(false)));
        g.MapPost("/accounting/bank/match", async (MatchBody b, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).MatchAsync(http.HccCaller(), b.RowIds, b.Refs, ct).ConfigureAwait(false)));
        g.MapPost("/accounting/bank/unmatch", async (IdBody b, HttpContext http, CancellationToken ct) =>
        { await Bank(http.RequestServices).UnmatchAsync(http.HccCaller(), b.Id, ct).ConfigureAwait(false); return Results.NoContent(); });
        g.MapPost("/accounting/bank/ignore", async (IdBody b, HttpContext http, CancellationToken ct) =>
        { await Bank(http.RequestServices).IgnoreAsync(http.HccCaller(), b.Id, b.Reason, ct).ConfigureAwait(false); return Results.NoContent(); });
        g.MapPost("/accounting/bank/voucher", async (VoucherBody b, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).CreateVoucherAsync(http.HccCaller(), b.RowId, b.Ledger, b.Narration, ct).ConfigureAwait(false)));
        g.MapPost("/accounting/bank/settlement", async (SettlementBody b, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).SettlementAsync(http.HccCaller(), b.RowId, b.Clearing, b.Gross, b.Mdr, b.MdrGst, b.Itc, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/bank/rows", async (string ledger, HttpContext http, CancellationToken ct) =>
            Results.Ok(await Bank(http.RequestServices).RowsAsync(http.HccCaller(), ledger, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/bank/recon", async (string ledger, DateOnly asOf, HttpContext http, CancellationToken ct) =>
        {
            var r = await Bank(http.RequestServices).ReconAsync(http.HccCaller(), ledger, asOf, ct).ConfigureAwait(false);
            return Results.Ok(new { recon = r, difference = r.Difference });
        });
        g.MapGet("/accounting/bank/recon.csv", async (string ledger, DateOnly asOf, HttpContext http, CancellationToken ct) =>
            Results.Text(BankReconRules.ReconCsv(await Bank(http.RequestServices).ReconAsync(http.HccCaller(), ledger, asOf, ct).ConfigureAwait(false)), "text/csv", Encoding.UTF8));
        g.MapGet("/accounting/bank/clearing", async (string code, DateOnly asOf, HttpContext http, CancellationToken ct) =>
        {
            var sp = http.RequestServices;
            var c = http.HccCaller();
            await sp.GetRequiredService<IAccessPolicy>().RequireAsync(c.PropertyId, c.PrincipalId, AccountingPermissions.View, ct: ct).ConfigureAwait(false);
            if (code is not ("1010" or "1020")) throw HccException.Validation("Clearing ledger: 1010 or 1020.");
            var (bal, open) = BankReconRules.Clearing(await Years(sp).VouchersUpToAsync(c.PropertyId, asOf, ct).ConfigureAwait(false), code, asOf);
            return Results.Ok(new { code, balance = bal, open = open.Select(o => new { date = o.Date, open = o.Open, age = o.Age, band = o.Band }) });
        });
        return api;
    }
}
