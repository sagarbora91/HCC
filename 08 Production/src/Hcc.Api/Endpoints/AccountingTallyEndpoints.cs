using Hcc.Application.Access;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Application.Import;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Accounting;

namespace Hcc.Api.Endpoints;

/// <summary>
/// ACC-3 · lane acc3-tally (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §4; PERM-ACC-006 like <c>GET /accounting/export/tally</c>):
/// <c>GET /accounting/tally/masters?fy=2026</c> (or <c>2026-27</c>) — the masters file of a financial year — and
/// <c>GET /accounting/tally/vouchers?from&amp;to</c> — the vouchers file of at most 31 days with bill allocations. Both answer the XML in the
/// view (<c>xml</c>, <c>fileName</c>, <c>sha256</c>); nothing is sent to Tally. The queries are built per request over the accounting store.
/// </summary>
public static class AccountingTallyEndpoints
{
    private static TallyQueries Queries(IServiceProvider sp)
    {
        var platform = sp.GetRequiredService<SqlPlatform>();
        var tally = sp.GetService<SqlTallyStore>() ?? new SqlTallyStore(platform.Connections);
        return new TallyQueries(sp.GetRequiredService<IAccountingStore>(), sp.GetRequiredService<IAccessPolicy>(),
            sp.GetService<IReportPartyNames>(), sp.GetService<ITallyPartyGstin>() ?? tally, sp.GetService<IReportOpeningSource>(), sp.GetService<IOpeningBalanceSource>(),
            sp.GetService<ITallyFileLog>() ?? tally, platform.Commands, sp.GetService<TimeProvider>(),
            sp.GetService<Hcc.Application.Configuration.ISettingsReader>(), sp.GetService<IAgeingAllocationSource>() ?? new SqlAgeingAllocationSource(platform.Connections));
    }

    public static RouteGroupBuilder MapAccountingTallyEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var g = api.MapHccSecured();
        g.MapGet("/accounting/tally/masters", async (string fy, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).MastersAsync(http.HccCaller(), fy, ct).ConfigureAwait(false)));
        g.MapGet("/accounting/tally/vouchers", async (DateOnly from, DateOnly to, HttpContext http, CancellationToken ct)
            => Results.Ok(await Queries(http.RequestServices).VouchersAsync(http.HccCaller(), from, to, ct).ConfigureAwait(false)));
        return api;
    }
}
